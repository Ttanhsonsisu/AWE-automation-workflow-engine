using System.Text.Json;
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

    public async Task<Result<Guid>> StartWorkflowAsync(Guid definitionId, string jobName, string inputData, Guid? correlationId)
    {
        var def = await _defRepo.GetDefinitionByIdAsync(definitionId);
        if (def == null) return Result.Failure<Guid>(Error.NotFound("Definition.NotFound", ""));

        // 1. Tạo Context bằng Service
        var contextResult = _contextManager.InitializeContext(inputData, jobName, correlationId ?? Guid.NewGuid());
        if (contextResult.IsFailure) return Result.Failure<Guid>(contextResult.Error);

        // 2. Khởi tạo Instance
        var instance = new WorkflowInstance(def.Id, def.Version, contextResult.Value);
        // instance.MarkAsRunning(); // Bạn nhớ thêm hàm này vào WorkflowInstance nhé
        await _instanceRepo.AddInstanceAsync(instance);

        // support multiple start nodes, nên phải lưu DB trước để có InstanceId, phục vụ cho việc tạo ExecutionPointer và Dispatch sau này
        // 3. Khởi tạo TẤT CẢ các Start Pointers
        var startNodeIds = _evaluator.FindStartNodeIds(def.DefinitionJson);

        // add vào List để sau này có thể dùng chung cho việc Dispatch, tránh phải query lại DB nhiều lần trong vòng lặp
        var pendingCommands = new List<ExecutePluginCommand>();

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

        // update: Sau khi có InstanceId rồi thì mới publish lệnh ExecutePluginCommand để Worker chạy, tránh tình trạng Worker nhận được message mà DB chưa kịp lưu nên không tìm thấy dữ liệu.
        var routingKey = $"{MessagingConstants.PatternPlugin.TrimEnd('#')}execute";
        foreach (var cmd in pendingCommands)
        {
            await _publishEndpoint.Publish(cmd, ctx => ctx.SetRoutingKey(routingKey));
        }

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

        // Kiểm tra trạng thái Workflow Instance, nếu đang Suspended thì không tiếp tục điều hướng nữa
        if (instance.Status == WorkflowInstanceStatus.Suspended)
        {
            _logger.LogInformation("Workflow {Id} is SUSPENDED. Halting routing at Step {StepId}.", instance.Id, pointer.StepId);
            await _uow.SaveChangesAsync();
            return Result.Success();
        }

        if (instance.Status == WorkflowInstanceStatus.Cancelled)
        {
            _logger.LogWarning("Workflow {Id} was CANCELLED. Halting routing permanently at Step {StepId}.", instance.Id, pointer.StepId);
            pointer.MarkAsRouted();
            await _uow.SaveChangesAsync();
            return Result.Success();
        }

        // 2. Hoàn thành Node và Merge Data
        //pointer.Complete("Engine", eventOutput); // Gọi hàm chuẩn của Entity
        _contextManager.MergeStepOutput(instance, pointer.StepId, eventOutput);


        var def = await _defRepo.GetDefinitionByIdAsync(instance.DefinitionId);

        // 3. Đánh giá đường đi tiếp theo
        var nextTransitions = _evaluator.EvaluateTransitions(def!.DefinitionJson, pointer.StepId, instance.ContextData);

        if (nextTransitions.Count == 0)
        {
            // Kết thúc Workflow
            // instance.MarkAsCompleted(); 
            _logger.LogInformation("Workflow {Id} Completed successfully.", instanceId);
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
            var pointersToDispatch = new List<ExecutionPointer>();
            var joinNodesToCheck = new HashSet<string>();

            // 4. Chuẩn bị các Pointer tiếp theo
            foreach (var transition in nextTransitions)
            {
                var newPointer = new ExecutionPointer(instance.Id, transition.TargetNodeId,
                    parentTokenId: pointer.Id,
                    branchId: nextTransitions.Count > 1 ? Guid.NewGuid().ToString() : pointer.BranchId);

                if (!transition.IsConditionMet)
                {
                    newPointer.Skip(); // Dead-path
                    await _pointerRepo.AddPointerAsync(newPointer);
                    joinNodesToCheck.Add(transition.TargetNodeId);
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
            //Đánh dấu Engine đã rẽ nhánh thành công cho Pointer này
            pointer.MarkAsRouted();

            await _uow.SaveChangesAsync();

            var pendingCommandsToPublish = new List<ExecutePluginCommand>();

            // 5. Giải quyết các tụ điểm Join
            foreach (var joinNodeId in joinNodesToCheck)
            {
                int edgesCount = _evaluator.GetIncomingEdgesCount(def.DefinitionJson, joinNodeId);
                var joinResult = await _joinService.EvaluateBarrierAsync(instance, joinNodeId, edgesCount);

                if (joinResult.IsBarrierBroken && joinResult.PointerToDispatch != null)
                {
                    var cmd = await _dispatcher.CreateDispatchCommand(instance, joinResult.PointerToDispatch, def.DefinitionJson);
                    if (cmd != null) pendingCommandsToPublish.Add(cmd);
                }
            }

            // 6. Gửi lệnh các đường thẳng/rẽ nhánh bình thường
            foreach (var p in pointersToDispatch)
            {
                var cmd = await _dispatcher.CreateDispatchCommand(instance, p, def.DefinitionJson);
                if (cmd != null) pendingCommandsToPublish.Add(cmd);
            }

            await _uow.SaveChangesAsync();

            // 7. BÂY GIỜ MỚI KÍCH HOẠT MASSTRANSIT PUBLISH
            var routingKey = $"{MessagingConstants.PatternPlugin.TrimEnd('#')}execute";
            foreach (var cmd in pendingCommandsToPublish)
            {
                await _publishEndpoint.Publish(cmd, ctx => ctx.SetRoutingKey(routingKey));
            }

        }

        // ONE SAVE TO RULE THEM ALL
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
                await _uow.SaveChangesAsync();
                retryCommandToPublish = await _dispatcher.CreateDispatchCommand(instance, pointer, def.DefinitionJson);
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
            }

            // Tương lai: Cần thêm logic để track xem khi nào tất cả lệnh Compensate chạy xong thì mới đổi status thành Compensated.
            // Tạm thời ở mức MVP, ta fire-and-forget

            // ONE SAVE TO RULE THEM ALL
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
        if (pointer.Status != ExecutionPointerStatus.WaitingForEvent)
        {
            _logger.LogWarning("[IDEMPOTENCY] Pointer {Id} is in status {Status}. Resume rejected.", pointer.Id, pointer.Status);
            return Result.Success(); 
        }

        // Lấy Instance chuẩn xác qua Repository
        var instance = await _instanceRepo.GetInstanceByIdAsync(pointer.InstanceId);
        if (instance == null || instance.Status != WorkflowInstanceStatus.Running)
            return Result.Failure(Error.Unexpected("Instance.Invalid", "Workflow is not running."));

        // Đã xóa dòng fetch WorkflowDefinition thừa thãi và vi phạm Clean Architecture

        // =================================================================
        // FR-11: WAKE UP & INJECT DATA (Đánh thức và Bơm dữ liệu)
        // =================================================================
        // Cập nhật Output cho chính Node Wait/Delay này
        pointer.CompleteFromWait(resumeData);

        // Cập nhật state Context Data của toàn bộ Workflow bằng hàm xịn bạn vừa viết
        _contextManager.MergeStepOutput(instance, pointer.StepId, resumeData);

        // Đổi trạng thái sang Pending để đánh lừa luồng HandleStepCompletionAsync phía sau 
        // hiểu rằng Node này vừa được "Worker" chạy xong

        ///pointer.ResetToPending();


        // Hãy để hàm HandleStepCompletionAsync tính toán Node tiếp theo rồi Save 1 lần duy nhất
        // Như vậy hệ thống mới đảm bảo tính Atomic .
        // Tái sử dụng logic điều hướng chuẩn của Engine
        return await HandleStepCompletionAsync(instance.Id, pointer.Id, resumeData);
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
}
