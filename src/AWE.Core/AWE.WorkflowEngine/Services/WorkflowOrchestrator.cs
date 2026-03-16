using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;
using AWE.WorkflowEngine.Interfaces;
using Jint;
using MassTransit;
using Medallion.Threading;
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

        foreach (var startNodeId in startNodeIds)
        {
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

            // Dispatch vào Outbox RAM
            await _dispatcher.DispatchAsync(instance, pointer, def.DefinitionJson);
        }

        // 4. Lưu DB (Atomic) chốt hạ tất cả Pointers và Messages cùng lúc
        await _uow.SaveChangesAsync();

        _logger.LogInformation("🚀 Started Job '{Name}' (ID: {Id}) with {Count} Start Nodes.", jobName, instance.Id, startNodeIds.Count);
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
            _logger.LogInformation("ℹ️ Pointer {Id} already routed. Ignoring duplicate event.", pointer.Id);
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
            _logger.LogInformation("🏁 Workflow {Id} Completed successfully.", instanceId);
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

            // 5. Giải quyết các tụ điểm Join
            foreach (var joinNodeId in joinNodesToCheck)
            {
                int edgesCount = _evaluator.GetIncomingEdgesCount(def.DefinitionJson, joinNodeId);
                var joinResult = await _joinService.EvaluateBarrierAsync(instance, joinNodeId, edgesCount);

                if (joinResult.IsBarrierBroken && joinResult.PointerToDispatch != null)
                {
                    await _dispatcher.DispatchAsync(instance, joinResult.PointerToDispatch, def.DefinitionJson);
                }
            }

            // 6. Gửi lệnh các đường thẳng/rẽ nhánh bình thường
            foreach (var p in pointersToDispatch)
            {
                await _dispatcher.DispatchAsync(instance, p, def.DefinitionJson);
            }

            // 👉 [GIẢI QUYẾT DỨT ĐIỂM] LẦN SAVE THỨ 2: CHỐT OUTBOX VÀ ĐẨY EVENT
            // Lệnh DispatchAsync ở vòng lặp trên chưa gửi RabbitMQ ngay, nó chỉ đưa vào Outbox EF Core (Tracking).
            // Lần Save này sẽ chốt Transaction Outbox và thực sự kích hoạt MassTransit gửi message đi.
            // (Lần save này rất nhẹ vì Business data đã được save ở Lần 1 rồi).
            await _uow.SaveChangesAsync();
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
                _logger.LogWarning("⚠️ Received failure for Instance {Id} but PointerId is empty.", instanceId);
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

            if (pointer.RetryCount < maxRetries)
            {
                _logger.LogInformation("🔄 Retrying Step {StepId} (Attempt {Current} of {Max})", pointer.StepId, pointer.RetryCount + 1, maxRetries);

                // Reset trạng thái Pointer về Pending và tăng biến đếm Retry (Hàm của Entity)
                pointer.ResetToPending();

                // Lưu DB trước để chốt số lần Retry, sau đó ném lại cho Worker chạy
                await _uow.SaveChangesAsync();
                await _dispatcher.DispatchAsync(instance, pointer, def.DefinitionJson);
            }
            else
            {
                // 3. THẤT BẠI HOÀN TOÀN (Vượt quá số lần Retry hoặc không cho phép Retry)
                _logger.LogError("🔥 Step {StepId} failed permanently after {Retries} retries. Error: {Error}", pointer.StepId, pointer.RetryCount, error);

                // Đánh dấu Pointer Failed (Gọi hàm chuẩn của Entity)
                pointer.MarkAsFailed("Engine", errorDoc);

                // Đánh dấu cả Workflow Instance Failed (Dừng toàn bộ quá trình)
                instance.Status = WorkflowInstanceStatus.Failed;
                // instance.CompletedAt = DateTime.UtcNow; // (Tuỳ thuộc vào cách bạn khai báo trong Entity)

                // Lưu lịch sử lỗi vào Context tổng để dễ debug
                _contextManager.MergeStepOutput(instance, pointer.StepId, errorDoc);
            }

            // ONE SAVE TO RULE THEM ALL
            await _uow.SaveChangesAsync();
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System error in HandleStepFailureAsync");
            // Nếu bản thân Engine bị lỗi DB khi ghi log, ta báo Failure để hệ thống gọi lại sau
            return Result.Failure(Error.Unexpected("System.StepFailureError", ex.Message));
        }
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
