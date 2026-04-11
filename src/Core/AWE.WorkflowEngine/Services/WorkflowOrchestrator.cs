using System.Text.Json;
using System.Text.Json.Nodes;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using AWE.Shared.Primitives;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class WorkflowOrchestrator(IUnitOfWork uow,
    IWorkflowDefinitionRepository defRepo,
    IWorkflowInstanceRepository instanceRepo,
    IExecutionPointerRepository pointerRepo,
    IWorkflowContextManager contextManager,
    ITransitionEvaluator evaluator,
    IJoinBarrierService joinService,
    IPointerDispatcher dispatcher,
    IWorkflowCompensationService compensationService,
    IPublishEndpoint publishEndpoint,
    ILogger<WorkflowOrchestrator> logger) : IWorkflowOrchestrator
{
    private readonly IUnitOfWork _uow = uow;
    private readonly IWorkflowDefinitionRepository _defRepo = defRepo;
    private readonly IWorkflowInstanceRepository _instanceRepo = instanceRepo;
    private readonly IExecutionPointerRepository _pointerRepo = pointerRepo;

    private readonly IWorkflowContextManager _contextManager = contextManager;
    private readonly ITransitionEvaluator _evaluator = evaluator;
    private readonly IJoinBarrierService _joinService = joinService;
    private readonly IPointerDispatcher _dispatcher = dispatcher;
    private readonly IWorkflowCompensationService _compensationService = compensationService;

    private readonly IPublishEndpoint _publishEndpoint = publishEndpoint;

    private readonly ILogger<WorkflowOrchestrator> _logger = logger;

    public async Task<Result<Guid>> StartWorkflowAsync(Guid definitionId, string jobName, string inputData, Guid? correlationId, bool isTest = false, string? stopAtStepId = null)
    {
        var def = await _defRepo.GetDefinitionByIdAsync(definitionId);
        if (def == null) return Result.Failure<Guid>(Error.NotFound("Definition.NotFound", ""));

        var normalizedStopAtStepId = string.IsNullOrWhiteSpace(stopAtStepId) ? null : stopAtStepId.Trim();

        if (!isTest && normalizedStopAtStepId is not null)
        {
            return Result.Failure<Guid>(Error.Validation("Workflow.StopAtStep.Invalid", "Chỉ được phép cấu hình điểm dừng (StopAtStepId) trong chế độ chạy thử (isTest = true)."));
        }

        if (normalizedStopAtStepId is not null && !HasStepDefinition(def.DefinitionJson, normalizedStopAtStepId))
        {
            return Result.Failure<Guid>(Error.Validation("Workflow.StopAtStep.NotFound", $"Không tìm thấy step '{normalizedStopAtStepId}' trong workflow definition."));
        }

        // 1. Tạo Context bằng Service
        var contextResult = _contextManager.InitializeContext(inputData, jobName, correlationId ?? Guid.NewGuid(), normalizedStopAtStepId);
        if (contextResult.IsFailure) return Result.Failure<Guid>(contextResult.Error);

        // 2. Khởi tạo Instance
        var instance = new WorkflowInstance(def.Id, def.Version, contextResult.Value, isTestInstance: isTest);
        // instance.MarkAsRunning(); // Bạn nhớ thêm hàm này vào WorkflowInstance nhé
        await _instanceRepo.AddInstanceAsync(instance);

        // support multiple start nodes, nên phải lưu DB trước để có InstanceId, phục vụ cho việc tạo ExecutionPointer và Dispatch sau này
        // 3. Khởi tạo TẤT CẢ các Start Pointers
        var startNodeIds = _evaluator.FindStartNodeIdsWithType(def.DefinitionJson);

        if (startNodeIds.Count == 0)
        {
            return Result.Failure<Guid>(Error.Validation("Workflow.NoTrigger", "Không tìm thấy Node Trigger nào (VD: ManualTrigger) để khởi động Workflow."));
        }

        // add vào List để sau này có thể dùng chung cho việc Dispatch, tránh phải query lại DB nhiều lần trong vòng lặp
        var pendingCommands = new List<ExecutePluginCommand>();
        var failedStartPointers = new List<ExecutionPointer>();

        foreach (var startNodeId in startNodeIds)
        {
            var pointerId = Guid.NewGuid();
            // Mỗi start node sẽ chạy song song như một nhánh độc lập (branchId riêng)
            var pointer = new ExecutionPointer(
                instanceId: instance.Id,
                stepId: startNodeId,
                predecessorId: null,
                scope: null,
                parentTokenId: null,
                branchId: Guid.NewGuid().ToString() // Khởi tạo nhánh song song luôn
            );

            await _pointerRepo.AddPointerAsync(pointer);

            var command = await _dispatcher.CreateDispatchCommand(instance, pointer, def.DefinitionJson);
            if (command != null)
            {
                pendingCommands.Add(command);
            }
            else if (pointer.Status == ExecutionPointerStatus.Failed)
            {
                failedStartPointers.Add(pointer);
            }
        }

        // Nếu toàn bộ Start node đều fail ở phase resolve/dispatch thì fail luôn instance.
        // Lưu ý: không gọi UpdateInstanceAsync ở đây vì instance vẫn đang ở trạng thái Added.
        if (pendingCommands.Count == 0 && failedStartPointers.Count > 0)
        {
            instance.Fail();
            instance.EndTime = DateTime.UtcNow;
        }

        // 4. Lưu DB (Atomic) chốt hạ tất cả Pointers và Messages cùng lúc
        await _uow.SaveChangesAsync();

        // 5. Ghi log bắt đầu Workflow
        _logger.LogInformation("Started Job '{Name}' (ID: {Id}) with {Count} Start Nodes.", jobName, instance.Id, startNodeIds.Count);
        await _publishEndpoint.Publish(new WriteAuditLogCommand(
            InstanceId: instance.Id,
            Event: "WorkflowStarted",
            Message: $"Bắt đầu thực thi Workflow: {jobName}",
            Level: Domain.Enums.LogLevel.Information,
            NodeId: "System" // Log chung của hệ thống
        ));

        if (pendingCommands.Count == 0 && failedStartPointers.Count > 0)
        {
            var firstFailed = failedStartPointers[0];
            await _publishEndpoint.Publish(new UiWorkflowStatusChangedEvent(
                InstanceId: instance.Id,
                Status: "Failed",
                Timestamp: DateTime.UtcNow));

            await _publishEndpoint.Publish(new WriteAuditLogCommand(
                InstanceId: instance.Id,
                Event: "WorkflowFailed",
                Message: $"Workflow thất bại tại Start node {firstFailed.StepId} do lỗi resolve/dispatch.",
                Level: Domain.Enums.LogLevel.Error,
                ExecutionPointerId: firstFailed.Id,
                NodeId: firstFailed.StepId,
                MetadataJson: firstFailed.Output?.RootElement.GetRawText()
            ));
        }

        // update: Sau khi có InstanceId rồi thì mới publish lệnh ExecutePluginCommand để Worker chạy, tránh tình trạng Worker nhận được message mà DB chưa kịp lưu nên không tìm thấy dữ liệu.
        var routingKey = $"{MessagingConstants.PatternPlugin.TrimEnd('#')}execute";
        foreach (var cmd in pendingCommands)
        {
            await _publishEndpoint.Publish(cmd, ctx => ctx.SetRoutingKey(routingKey));
        }

        //await _uow.SaveChangesAsync();

        return Result.Success(instance.Id);
    }

    public async Task<Result> HandleStepCompletionAsync(Guid instanceId, Guid executionPointerId, JsonDocument? eventOutput)
    {
        var instance = await _instanceRepo.GetInstanceByIdAsync(instanceId);
        var pointer = await _pointerRepo.GetPointerByIdAsync(executionPointerId);

        if (instance == null || pointer == null)
            return Result.Failure(Error.NotFound("Data.NotFound", ""));

        // 1. Check Idempotency chặn luồng lặp
        //if (pointer.Status == ExecutionPointerStatus.Completed || pointer.Status == ExecutionPointerStatus.Failed)
        //    return Result.Success();
        if (pointer.Routed)
        {
            _logger.LogInformation("Pointer {Id} already routed. Ignoring duplicate event.", pointer.Id);
            return Result.Success();
        }

        // Use provided eventOutput, or fallback to pointer.Output (for Resumed routing where eventOutput is null)
        var actualEventOutput = eventOutput ?? pointer.Output;

        // 2. Hoàn thành Node và Merge Data
        _contextManager.MergeStepOutput(instance, pointer.StepId, actualEventOutput);

        // Kiểm tra trạng thái Workflow Instance, nếu đang Suspended thì không tiếp tục điều hướng nữa
        if (instance.Status == WorkflowInstanceStatus.Suspended)
        {
            _logger.LogInformation("Workflow {Id} is SUSPENDED. Halting routing at Step {StepId}.", instance.Id, pointer.StepId);
            await _instanceRepo.UpdateInstanceAsync(instance);
            await _uow.SaveChangesAsync();
            return Result.Success();
        }

        var latestStatus = await _instanceRepo.GetInstanceStatusAsync(instance.Id);
        if (latestStatus == WorkflowInstanceStatus.Suspended)
        {
            _logger.LogInformation("Workflow {Id} became SUSPENDED while handling completion of Step {StepId}. Routing is halted.", instance.Id, pointer.StepId);
            await _instanceRepo.UpdateInstanceAsync(instance);
            await _uow.SaveChangesAsync();
            return Result.Success();
        }

        if (instance.Status == WorkflowInstanceStatus.Cancelled)
        {
            _logger.LogWarning("Workflow {Id} was CANCELLED. Halting routing permanently at Step {StepId}.", instance.Id, pointer.StepId);
            pointer.MarkAsRouted();
            // update status for instance and pointer to reflect cancellation
            await _instanceRepo.UpdateInstanceAsync(instance);
            await _uow.SaveChangesAsync();
            return Result.Success();
        }

        var def = await _defRepo.GetDefinitionByIdAsync(instance.DefinitionId);

        // 3. Đánh giá đường đi tiếp theo
        var nextTransitions = _evaluator.EvaluateTransitions(def!.DefinitionJson, pointer.StepId, instance.ContextData);

        var stopAtStepId = GetStopAtStepId(instance);
        if (!string.IsNullOrWhiteSpace(stopAtStepId)
            && nextTransitions.Any(t => t.IsConditionMet
                && string.Equals(t.TargetNodeId, stopAtStepId, StringComparison.OrdinalIgnoreCase)))
        {
            if (instance.Status == WorkflowInstanceStatus.Running)
            {
                instance.Suspend();
            }

            // Đánh dấu pointer hiện tại đã xử lý xong, không cho reroute lại.
            pointer.MarkAsRouted();
            ClearStopAtStepId(instance);

            _logger.LogInformation("Workflow {Id} stopped before Step '{StopAtStepId}' at predecessor '{CurrentStepId}'.",
                instance.Id,
                stopAtStepId,
                pointer.StepId);

            await _instanceRepo.UpdateInstanceAsync(instance);
            await _uow.SaveChangesAsync();

            await _publishEndpoint.Publish(new UiWorkflowStatusChangedEvent(
                InstanceId: instance.Id,
                Status: "Suspended",
                Timestamp: DateTime.UtcNow));

            await _publishEndpoint.Publish(new WriteAuditLogCommand(
                InstanceId: instance.Id,
                Event: "WorkflowSuspended",
                Message: $"Workflow đã dừng tại node trước '{stopAtStepId}' (node hiện tại: '{pointer.StepId}').",
                Level: Domain.Enums.LogLevel.Warning,
                ExecutionPointerId: pointer.Id,
                NodeId: pointer.StepId
            ));

            return Result.Success();
        }

        if (nextTransitions.Count == 0)
        {
            pointer.MarkAsRouted();
            var activePointers = await _pointerRepo.GetActivePointersByInstanceAsync(instance.Id);
            var hasOtherActivePointers = activePointers.Any(p => p.Id != pointer.Id);

            if (!hasOtherActivePointers)
            {
                // Chỉ complete khi KHÔNG còn pointer active nào khác.
                instance.Complete();
                _logger.LogInformation("Workflow {Id} Completed successfully.", instanceId);

                await _instanceRepo.UpdateInstanceAsync(instance);
                await _uow.SaveChangesAsync();

                await _publishEndpoint.Publish(new UiWorkflowStatusChangedEvent(
                    InstanceId: instance.Id,
                    Status: "Completed",
                    Timestamp: DateTime.UtcNow));

                await _publishEndpoint.Publish(new WriteAuditLogCommand(
                    InstanceId: instance.Id,
                    Event: "WorkflowCompleted",
                    Message: "Quy trình đã hoàn thành xuất sắc toàn bộ các bước.",
                    Level: Domain.Enums.LogLevel.Information,
                    NodeId: "System"
                ));
            }
            else
            {
                _logger.LogInformation(
                    "Step {StepId} reached terminal path but workflow {InstanceId} still has {ActiveCount} active pointers. Skip completing workflow.",
                    pointer.StepId,
                    instance.Id,
                    activePointers.Count(p => p.Id != pointer.Id));
            }
        }
        else
        {
            var pointersToDispatch = new List<ExecutionPointer>();
            var joinNodesToCheck = new HashSet<string>();
            var visitedDeadPathEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 4. Chuẩn bị các Pointer tiếp theo
            foreach (var transition in nextTransitions)
            {
                var branchId = nextTransitions.Count > 1 ? Guid.NewGuid().ToString() : pointer.BranchId;

                var newPointer = new ExecutionPointer(instance.Id, transition.TargetNodeId,
                    parentTokenId: pointer.Id,
                    branchId: branchId);

                if (!transition.IsConditionMet)
                {
                    await PropagateDeadPathAsync(
                        instance: instance,
                        definitionJson: def.DefinitionJson,
                        sourceStepId: pointer.StepId,
                        parentTokenId: pointer.Id,
                        branchId: branchId,
                        targetStepId: transition.TargetNodeId,
                        joinNodesToCheck: joinNodesToCheck,
                        visitedDeadPathEdges: visitedDeadPathEdges);
                }
                else
                {
                    await _pointerRepo.AddPointerAsync(newPointer);

                    if (_evaluator.IsJoinNode(def.DefinitionJson, transition.TargetNodeId))
                        joinNodesToCheck.Add(transition.TargetNodeId);
                    else
                        pointersToDispatch.Add(newPointer);
                }
            }

            // Phải Save ở đây để Database thực sự có dữ liệu, giúp hàm COUNT(*) của JoinBarrier chạy đúng,
            // và đảm bảo khi Worker nhận được message thì Data đã sẵn sàng!
            // [FIX] Chưa mark Routed tại đây để nếu Join lock bận thì có thể resume/reroute lại.
            await _uow.SaveChangesAsync();

            var pendingCommandsToPublish = new List<ExecutePluginCommand>();
            bool engineDispatchFailed = false;
            string failedStepId = string.Empty;
            string? failedMetadataJson = null;

            // 5. Giải quyết các tụ điểm Join
            foreach (var joinNodeId in joinNodesToCheck)
            {
                int edgesCount = _evaluator.GetIncomingEdgesCount(def.DefinitionJson, joinNodeId);
                var joinResult = await _joinService.EvaluateBarrierAsync(instance, joinNodeId, edgesCount);

                if (joinResult.IsBarrierBroken && joinResult.PointerToDispatch != null)
                {
                    var cmd = await _dispatcher.CreateDispatchCommand(instance, joinResult.PointerToDispatch, def.DefinitionJson);
                    if (cmd != null)
                    {
                        pendingCommandsToPublish.Add(cmd);
                    }
                    else if (joinResult.PointerToDispatch.Status == ExecutionPointerStatus.Failed)
                    {
                        engineDispatchFailed = true;
                        failedStepId = joinResult.PointerToDispatch.StepId;
                        failedMetadataJson = joinResult.PointerToDispatch.Output?.RootElement.GetRawText();
                        break;
                    }
                }
            }

            // 6. Gửi lệnh các đường thẳng/rẽ nhánh bình thường
            if (!engineDispatchFailed)
            {
                foreach (var p in pointersToDispatch)
                {
                    var cmd = await _dispatcher.CreateDispatchCommand(instance, p, def.DefinitionJson);
                    if (cmd != null)
                    {
                        pendingCommandsToPublish.Add(cmd);
                    }
                    else if (p.Status == ExecutionPointerStatus.Failed)
                    {
                        engineDispatchFailed = true;
                        failedStepId = p.StepId;
                        failedMetadataJson = p.Output?.RootElement.GetRawText();
                        break;
                    }
                }
            }

            if (engineDispatchFailed)
            {
                instance.Fail();
                instance.EndTime = DateTime.UtcNow;
                pointer.MarkAsRouted();

                await _instanceRepo.UpdateInstanceAsync(instance);
                await _uow.SaveChangesAsync();

                await _publishEndpoint.Publish(new UiWorkflowStatusChangedEvent(
                    InstanceId: instance.Id,
                    Status: "Failed",
                    Timestamp: DateTime.UtcNow));

                await _publishEndpoint.Publish(new WriteAuditLogCommand(
                    InstanceId: instance.Id,
                    Event: "WorkflowFailed",
                    Message: $"Workflow thất bại ở bước {failedStepId} do lỗi resolve/dispatch.",
                    Level: Domain.Enums.LogLevel.Error,
                    ExecutionPointerId: executionPointerId,
                    NodeId: failedStepId,
                    MetadataJson: failedMetadataJson
                ));

                return Result.Success();
            }

            // Đến đây mới xem là rẽ nhánh thành công hoàn toàn.
            pointer.MarkAsRouted();
            await _uow.SaveChangesAsync();

            // 7. BÂY GIỜ MỚI KÍCH HOẠT MASSTRANSIT PUBLISH
            latestStatus = await _instanceRepo.GetInstanceStatusAsync(instance.Id);
            if (latestStatus == WorkflowInstanceStatus.Suspended)
            {
                _logger.LogInformation("Workflow {Id} was suspended before publish. Skip dispatching next commands.", instance.Id);
                await _uow.SaveChangesAsync();
                return Result.Success();
            }

            var routingKey = $"{MessagingConstants.PatternPlugin.TrimEnd('#')}execute";
            foreach (var cmd in pendingCommandsToPublish)
            {
                await _publishEndpoint.Publish(cmd, ctx => ctx.SetRoutingKey(routingKey));
            }

        }

        // ONE SAVE TO RULE THEM ALL
        await _instanceRepo.UpdateInstanceAsync(instance);
        await _uow.SaveChangesAsync();
        return Result.Success();
    }

    public async Task<Result> HandleStepFailureAsync(Guid instanceId, Guid pointerId, string error)
    {
        try
        {
            if (pointerId == Guid.Empty)
            {
                _logger.LogWarning("Received failure for Instance {Id} but PointerId is empty.", instanceId);
                return Result.Success();
            }

            var pointer = await _pointerRepo.GetPointerByIdAsync(pointerId);
            var instance = await _instanceRepo.GetInstanceByIdAsync(instanceId);

            if (pointer == null || instance == null)
                return Result.Failure(Error.NotFound("Data.NotFound", "Instance or Pointer not found"));

            // 1. IDEMPOTENCY
            if (pointer.Routed)
                return Result.Success();

            // Tạo cục JSON chứa chi tiết lỗi để lưu DB
            var errorDoc = JsonSerializer.SerializeToDocument(new
            {
                ErrorMessage = error,
                FailedAt = DateTime.UtcNow
            });

            var def = await _defRepo.GetDefinitionByIdAsync(instance.DefinitionId);
            var stepDef = GetStepDefinition(def!.DefinitionJson, pointer.StepId);

            // 2. LOGIC RETRY CẤP ĐỘ ENGINE
            // Đọc cấu hình "MaxRetries" từ JSON của Step (Mặc định là 0 nếu không cấu hình)
            int maxRetries = 0;
            if (stepDef.TryGetProperty("MaxRetries", out var retriesElem) && retriesElem.TryGetInt32(out int parsedRetries))
            {
                maxRetries = parsedRetries;
            }

            ExecutePluginCommand? retryCommandToPublish = null;
            List<CompensatePluginCommand> compensateCommandsToPublish = new();

            if (pointer.RetryCount < maxRetries)
            {
                _logger.LogInformation("Retrying Step {StepId}...", pointer.StepId);

                pointer.ResetToPending();
                await _instanceRepo.UpdateInstanceAsync(instance);
                await _uow.SaveChangesAsync();

                await _publishEndpoint.Publish(new UiNodeStatusChangedEvent(
                    InstanceId: instance.Id,
                    StepId: pointer.StepId,
                    Status: "Retrying",
                    Timestamp: DateTime.UtcNow));

                await _publishEndpoint.Publish(new WriteAuditLogCommand(
                    InstanceId: instance.Id,
                    Event: "StepRetrying",
                    Message: $"Node {pointer.StepId} đang được retry lần {pointer.RetryCount}/{maxRetries}.",
                    Level: Domain.Enums.LogLevel.Warning,
                    ExecutionPointerId: pointer.Id,
                    NodeId: pointer.StepId));

                retryCommandToPublish = await _dispatcher.CreateDispatchCommand(instance, pointer, def.DefinitionJson);

                if (retryCommandToPublish == null && pointer.Status == ExecutionPointerStatus.Failed)
                {
                    instance.Fail();
                    await _instanceRepo.UpdateInstanceAsync(instance);
                    await _uow.SaveChangesAsync();

                    await _publishEndpoint.Publish(new UiWorkflowStatusChangedEvent(
                        InstanceId: instanceId,
                        Status: "Failed",
                        Timestamp: DateTime.UtcNow));

                    await _publishEndpoint.Publish(new WriteAuditLogCommand(
                        InstanceId: instanceId,
                        Event: "WorkflowFailed",
                        Message: $"Node {pointer.StepId} thất bại khi tạo lệnh retry (resolve/dispatch).",
                        Level: Domain.Enums.LogLevel.Error,
                        ExecutionPointerId: pointerId,
                        NodeId: pointer.StepId,
                        MetadataJson: pointer.Output?.RootElement.GetRawText()
                    ));

                    return Result.Success();
                }
            }
            else
            {
                _logger.LogError("Step {StepId} failed permanently. Triggering Compensation.", pointer.StepId);

                await _publishEndpoint.Publish(new WriteAuditLogCommand(
                    InstanceId: instanceId,
                    Event: "WorkflowFailed",
                    Message: $"Node {pointer.StepId} đã thất bại vĩnh viễn sau {maxRetries} lần thử. Đang kích hoạt Rollback.",
                    Level: Domain.Enums.LogLevel.Error,
                    ExecutionPointerId: pointerId,
                    NodeId: pointer.StepId,
                    MetadataJson: errorDoc.RootElement.GetRawText() // Bơm chi tiết lỗi vào đây để UI vẽ ra
                ));
                // Đánh dấu Node và Workflow Failed
                //pointer.MarkAsFailed("Engine", errorDoc);
                pointer.MarkAsRouted();
                instance.Status = WorkflowInstanceStatus.Compensating;
                _contextManager.MergeStepOutput(instance, pointer.StepId, errorDoc);

                // call SERVICE XỬ LÝ SAGA
                compensateCommandsToPublish = await _compensationService.TriggerCompensationAsync(instance, def!.DefinitionJson);

                if (compensateCommandsToPublish.Any())
                {
                    await _publishEndpoint.Publish(new UiWorkflowStatusChangedEvent(
                        InstanceId: instanceId,
                        Status: "Compensating",
                        Timestamp: DateTime.UtcNow));
                }
                else
                {
                    instance.Fail();
                    instance.EndTime = DateTime.UtcNow;

                    await _publishEndpoint.Publish(new UiWorkflowStatusChangedEvent(
                        InstanceId: instanceId,
                        Status: "Failed",
                        Timestamp: DateTime.UtcNow));
                }
            }

            // Tương lai: Cần thêm logic để track xem khi nào tất cả lệnh Compensate chạy xong thì mới đổi status thành Compensated.
            // Tạm thời ở mức MVP, ta fire-and-forget

            // ONE SAVE TO RULE THEM ALL
            await _instanceRepo.UpdateInstanceAsync(instance);
            await _uow.SaveChangesAsync();

            if (retryCommandToPublish != null)
            {
                var routingKey = $"{MessagingConstants.PatternPlugin.TrimEnd('#')}execute";
                await _publishEndpoint.Publish(retryCommandToPublish, ctx => ctx.SetRoutingKey(routingKey));
            }

            if (compensateCommandsToPublish.Any())
            {
                var compRoutingKey = $"{MessagingConstants.PatternPlugin.TrimEnd('#')}compensate";
                foreach (var cmd in compensateCommandsToPublish)
                {
                    await _publishEndpoint.Publish(cmd, ctx => ctx.SetRoutingKey(compRoutingKey));
                }
            }

            //await _uow.SaveChangesAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System error in HandleStepFailureAsync");
            // Nếu bản thân Engine bị lỗi DB khi ghi log, ta báo Failure để hệ thống gọi lại sau
            return Result.Failure(Error.Unexpected("System.StepFailureError", ex.Message));
        }
    }

    public async Task<Result> ResumeStepAsync(Guid pointerId, JsonDocument resumeData)
    {
        _logger.LogInformation("⏰ Attempting to RESUME Pointer {PointerId}...", pointerId);

        // Lấy Pointer từ DB
        var pointer = await _pointerRepo.GetPointerByIdAsync(pointerId);
        if (pointer == null)
            return Result.Failure(Error.NotFound("Pointer.NotFound", "Execution Pointer not found."));

        // =================================================================
        // FR-12: ATOMIC IDEMPOTENCY CHECK (Chống kích hoạt kép)
        // =================================================================
        if (pointer.Status != ExecutionPointerStatus.Suspended)
        {
            _logger.LogWarning("[IDEMPOTENCY] Pointer {Id} is in status {Status}. Resume rejected.", pointer.Id, pointer.Status);
            return Result.Success(); 
        }

        // Lấy Instance chuẩn xác qua Repository
        var instance = await _instanceRepo.GetInstanceByIdAsync(pointer.InstanceId);
        if (instance == null)
            return Result.Failure(Error.Unexpected("Instance.Invalid", "Workflow not found."));

        if (instance.Status != WorkflowInstanceStatus.Running
            && instance.Status != WorkflowInstanceStatus.Suspended)
            return Result.Failure(Error.Unexpected("Instance.Invalid", "Workflow is not resumable."));

        if (instance.Status == WorkflowInstanceStatus.Suspended)
        {
            _logger.LogInformation("Workflow {Id} is suspended. Wake-up for pointer {PointerId} will not switch status to Running.",
                instance.Id,
                pointer.Id);
        }

        // =================================================================
        // FR-11: WAKE UP & INJECT DATA (Đánh thức và Bơm dữ liệu)
        // =================================================================
        // Cập nhật Output cho chính Node Wait/Delay này
        pointer.CompleteFromWait(resumeData);

        // Cập nhật state Context Data của toàn bộ Workflow bằng hàm xịn bạn vừa viết
        _contextManager.MergeStepOutput(instance, pointer.StepId, resumeData);

        // Hãy để hàm HandleStepCompletionAsync tính toán Node tiếp theo rồi Save 1 lần duy nhất
        // Như vậy hệ thống mới đảm bảo tính Atomic .
        // Tái sử dụng logic điều hướng chuẩn của Engine
        return await HandleStepCompletionAsync(instance.Id, pointer.Id, resumeData);
    }

    private static string? GetStopAtStepId(WorkflowInstance instance)
    {
        if (instance.ContextData.RootElement.ValueKind != JsonValueKind.Object)
            return null;

        if (!instance.ContextData.RootElement.TryGetProperty("Meta", out var meta)
            || meta.ValueKind != JsonValueKind.Object)
            return null;

        if (!meta.TryGetProperty("StopAtStepId", out var stopAtStepElement)
            || stopAtStepElement.ValueKind != JsonValueKind.String)
            return null;

        var stopAtStepId = stopAtStepElement.GetString();
        return string.IsNullOrWhiteSpace(stopAtStepId) ? null : stopAtStepId;
    }

    private static void ClearStopAtStepId(WorkflowInstance instance)
    {
        var root = JsonNode.Parse(instance.ContextData.RootElement.GetRawText()) as JsonObject;
        if (root != null && root.TryGetPropertyValue("Meta", out var metaNode) && metaNode is JsonObject meta)
        {
            meta.Remove("StopAtStepId");
            instance.UpdateContext(JsonDocument.Parse(root.ToJsonString()));
        }
    }

    private static bool HasStepDefinition(JsonDocument definitionJson, string stepId)
    {
        if (!definitionJson.RootElement.TryGetProperty("Steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var step in steps.EnumerateArray())
        {
            if (!step.TryGetProperty("Id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String)
                continue;

            if (string.Equals(idElement.GetString(), stepId, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private async Task PropagateDeadPathAsync(
        WorkflowInstance instance,
        JsonDocument definitionJson,
        string sourceStepId,
        Guid parentTokenId,
        string branchId,
        string targetStepId,
        HashSet<string> joinNodesToCheck,
        HashSet<string> visitedDeadPathEdges)
    {
        var edgeKey = $"{sourceStepId}->{targetStepId}";
        if (!visitedDeadPathEdges.Add(edgeKey))
        {
            return;
        }

        var skippedPointer = new ExecutionPointer(
            instanceId: instance.Id,
            stepId: targetStepId,
            parentTokenId: parentTokenId,
            branchId: branchId);

        skippedPointer.Skip();
        await _pointerRepo.AddPointerAsync(skippedPointer);

        if (_evaluator.IsJoinNode(definitionJson, targetStepId))
        {
            joinNodesToCheck.Add(targetStepId);
            return;
        }

        var outgoingTargets = GetOutgoingTargetNodeIds(definitionJson, targetStepId);
        if (outgoingTargets.Count == 0)
        {
            return;
        }

        foreach (var nextTarget in outgoingTargets)
        {
            var nextBranchId = outgoingTargets.Count > 1 ? Guid.NewGuid().ToString() : branchId;
            await PropagateDeadPathAsync(
                instance: instance,
                definitionJson: definitionJson,
                sourceStepId: targetStepId,
                parentTokenId: skippedPointer.Id,
                branchId: nextBranchId,
                targetStepId: nextTarget,
                joinNodesToCheck: joinNodesToCheck,
                visitedDeadPathEdges: visitedDeadPathEdges);
        }
    }

    private static List<string> GetOutgoingTargetNodeIds(JsonDocument definitionJson, string sourceStepId)
    {
        var result = new List<string>();

        if (!definitionJson.RootElement.TryGetProperty("Transitions", out var transitions)
            || transitions.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var transition in transitions.EnumerateArray())
        {
            if (!transition.TryGetProperty("Source", out var source)
                || source.ValueKind != JsonValueKind.String
                || !string.Equals(source.GetString(), sourceStepId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!transition.TryGetProperty("Target", out var target)
                || target.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var targetStepId = target.GetString();
            if (!string.IsNullOrWhiteSpace(targetStepId))
            {
                result.Add(targetStepId);
            }
        }

        return result;
    }

    // Hàm tiện ích để lấy thông tin Step từ JSON Definition (để đọc cấu hình MaxRetries)
    private JsonElement GetStepDefinition(JsonDocument defJson, string stepId)
    {
        if (defJson.RootElement.TryGetProperty("Steps", out var steps))
        {
            foreach (var step in steps.EnumerateArray())
            {
                if (step.GetProperty("Id").GetString() == stepId) return step;
            }
        }
        throw new InvalidOperationException($"Step {stepId} not found in definition");
    }

    public async Task<Result> HandleStepSuspendedAsync(Guid instanceId, Guid pointerId, string? reason)
    {
        var pointer = await _pointerRepo.GetPointerByIdAsync(pointerId);
        if (pointer == null)
            return Result.Failure(Error.NotFound("Pointer.NotFound", "Execution Pointer not found."));

        var instance = await _instanceRepo.GetInstanceByIdAsync(instanceId);
        if (instance == null)
            return Result.Failure(Error.NotFound("Instance.NotFound", "Workflow instance not found."));

        // Chỉ xử lý nếu Pointer đang chạy (đề phòng duplicate message)
        if (pointer.Status != ExecutionPointerStatus.Running)
        {
            _logger.LogWarning("Pointer {Id} is not in Running state (Current: {Status}). Suspend ignored.", pointer.Id, pointer.Status);
            return Result.Success();
        }

        // Đổi trạng thái từ Running -> Suspended
        pointer.PauseForWebhook();

        if (instance.Status == WorkflowInstanceStatus.Running)
            instance.Suspend();

        _logger.LogInformation("Workflow {InstanceId} SUSPENDED at Step {StepId}. Reason: {Reason}", instanceId, pointer.StepId, reason);

        // Lưu trạng thái xuống DB
        await _instanceRepo.UpdateInstanceAsync(instance);
        await _uow.SaveChangesAsync();
        return Result.Success();
    }
}
