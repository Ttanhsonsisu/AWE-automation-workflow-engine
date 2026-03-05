using System.Text.Json;
using System.Text.Json.Nodes;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using AWE.Shared.Primitives;
using AWE.WorkflowEngine.Interfaces;
using Jint;
using MassTransit;
using Medallion.Threading;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class WorkflowOrchestrator : IWorkflowOrchestrator
{
    private readonly IUnitOfWork _uow;
    private readonly IWorkflowDefinitionRepository _defRepo;
    private readonly IWorkflowInstanceRepository _instanceRepo;
    private readonly IExecutionPointerRepository _pointerRepo;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IVariableResolver _resolver;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<WorkflowOrchestrator> _logger;

    public WorkflowOrchestrator(
        IUnitOfWork uow,
        IWorkflowDefinitionRepository defRepo,
        IWorkflowInstanceRepository instanceRepo,
        IExecutionPointerRepository pointerRepo,
        IPublishEndpoint publishEndpoint,
        IVariableResolver resolver,
        ILogger<WorkflowOrchestrator> logger,
        IDistributedLockProvider lockProvider)
    {
        _uow = uow;
        _defRepo = defRepo;
        _instanceRepo = instanceRepo;
        _pointerRepo = pointerRepo;
        _publishEndpoint = publishEndpoint;
        _resolver = resolver;
        _logger = logger;
        _lockProvider = lockProvider;
    }

    public async Task<Result<Guid>> StartWorkflowAsync(Guid definitionId, string jobName, string inputData, Guid? correlationId)
    {
        try
        {
            // 1. Validation Logic
            var def = await _defRepo.GetDefinitionByIdAsync(definitionId);
            if (def == null)
                return Result.Failure<Guid>(Error.NotFound("Definition.NotFound", $"Definition {definitionId} not found"));

            // 2. Parse Input an toàn
            JsonNode? inputsNode;
            try
            {
                var rawInput = string.IsNullOrWhiteSpace(inputData) ? "{}" : inputData;
                inputsNode = JsonNode.Parse(rawInput);
            }
            catch (JsonException)
            {
                return Result.Failure<Guid>(Error.Validation("Input.InvalidJson", "Input data is not valid JSON"));
            }

            // 3. Init Context
            var initialContext = new JsonObject
            {
                ["Inputs"] = inputsNode,
                ["Steps"] = new JsonObject(),
                ["System"] = new JsonObject
                {
                    ["CorrelationId"] = correlationId ?? Guid.NewGuid(),
                    ["JobName"] = jobName,
                    ["StartedAt"] = DateTime.UtcNow
                }
            };

            // 4. Create Instance
            var instance = new WorkflowInstance(def.Id, def.Version, JsonDocument.Parse(initialContext.ToJsonString()));
            // Nếu entity có cột Status, set nó là Running
            // instance.Status = WorkflowStatus.Running; 

            await _instanceRepo.AddInstanceAsync(instance);

            // 5. Find Start Node & Create Pointer
            var startNodeId = FindStartNodeId(def.DefinitionJson);
            if (string.IsNullOrEmpty(startNodeId))
                return Result.Failure<Guid>(Error.Validation("Definition.NoStartNode", "Cannot find start node"));

            var pointer = new ExecutionPointer(instance.Id, startNodeId);
            await _pointerRepo.AddPointerAsync(pointer);

            // 7. Commit Transaction (Lưu DB + Outbox Message cùng lúc)
            await _uow.SaveChangesAsync();

            // 6. Dispatch (Gửi lệnh thực thi bước đầu tiên)
            await DispatchPointerAsync(instance, pointer, def);

            _logger.LogInformation("🚀 [ENGINE] Started Job '{Name}' (ID: {Id})", jobName, instance.Id);

            return Result.Success(instance.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System error in StartWorkflowAsync");
            return Result.Failure<Guid>(Error.Unexpected("System.StartError", ex.Message));
        }
    }

    public async Task<Result> HandleStepCompletionAsync(Guid instanceId, Guid executionPointerId, JsonDocument? eventOutput)
    {
        try
        {
            var instance = await _instanceRepo.GetInstanceByIdAsync(instanceId);
            var pointer = await _pointerRepo.GetPointerByIdAsync(executionPointerId);

            if (instance == null) return Result.Failure(Error.NotFound("Instance.NotFound", ""));

            // Bỏ check pointer.Status == Completed ở đây để tránh dính bẫy Idempotency của chung Database
            if (pointer == null)
            {
                _logger.LogWarning("Pointer {PointerId} not found in DB.", executionPointerId);
                return Result.Success();
            }

            // 1. Merge Context
            var outputToMerge = pointer.Output ?? eventOutput;
            if (outputToMerge != null) MergeStepOutputToContext(instance, pointer.StepId, outputToMerge);

            // 2. Lấy Definition và đánh giá các nhánh tiếp theo (FORK & DEAD PATH)
            var def = await _defRepo.GetDefinitionByIdAsync(instance.DefinitionId);
            var nextTransitions = EvaluateTransitions(def!.DefinitionJson, pointer.StepId, instance.ContextData);

            if (nextTransitions.Count == 0)
            {
                _logger.LogInformation("🏁 Workflow {Id} Completed successfully.", instanceId);
            }
            else
            {
                var pointersToDispatch = new List<ExecutionPointer>();
                var joinNodesToCheck = new HashSet<string>();
                foreach (var transition in nextTransitions)
                {
                    // [FORK] - Cấp BranchId mới nếu rẽ thành nhiều nhánh

                    var newBranchId = nextTransitions.Count > 1 ? Guid.NewGuid().ToString() : pointer.BranchId;

                    var newPointer = new ExecutionPointer(instance.Id, transition.TargetNodeId,
                        parentTokenId: pointer.Id,
                        branchId: newBranchId);

                    if (!transition.IsConditionMet)
                    {
                        // [DEAD PATH] - Đánh dấu Skipped luôn
                        newPointer.Status = ExecutionPointerStatus.Skipped;
                        //newPointer. = DateTime.UtcNow;
                        await _pointerRepo.AddPointerAsync(newPointer);
                        joinNodesToCheck.Add(transition.TargetNodeId);

                        // Kích hoạt thử Join vì có thể đây là mảnh ghép cuối nó đang chờ
                        //await TryTriggerJoinBarrierAsync(instance, def, transition.TargetNodeId);
                    }
                    else
                    {
                        // Nhánh sống
                        await _pointerRepo.AddPointerAsync(newPointer);

                        var targetType = GetStepType(def.DefinitionJson, transition.TargetNodeId);
                        if (targetType == "Join")
                        {
                            joinNodesToCheck.Add(transition.TargetNodeId);
                            // [JOIN] - Chặn lại, đưa vào thuật toán Barrier
                            //await TryTriggerJoinBarrierAsync(instance, def, transition.TargetNodeId);
                        }
                        else
                        {
                            pointersToDispatch.Add(newPointer);
                        }
                    }
                }

                await _uow.SaveChangesAsync();

                foreach (var joinNodeId in joinNodesToCheck)
                {
                    await TryTriggerJoinBarrierAsync(instance, def, joinNodeId);
                }

                // Dispatch các node bình thường
                foreach (var p in pointersToDispatch)
                {
                    await DispatchPointerAsync(instance, p, def);
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System error in HandleStepCompletionAsync");
            return Result.Failure(Error.Unexpected("System.StepCompletionError", ex.Message));
        }
    }

    // =========================================================================
    // ADVANCED FLOW HELPERS (JOIN BARRIER & CONDITIONS)
    // =========================================================================

    private async Task TryTriggerJoinBarrierAsync(WorkflowInstance instance, WorkflowDefinition def, string joinNodeId)
    {
        // 1. Redis Lock - Chống Race Condition
        var lockKey = $"workflow:{instance.Id}:join:{joinNodeId}";
        await using var handle = await _lockProvider.TryAcquireLockAsync(lockKey, TimeSpan.FromSeconds(5));

        if (handle == null)
        {
            _logger.LogInformation("⏳ Join {JoinId} is locked by another thread. Yielding.", joinNodeId);
            return;
        }

        // 2. Tính số lượng mũi tên đầu vào
        int totalIncomingEdges = GetIncomingEdgesCount(def.DefinitionJson, joinNodeId);

        // 3. Đếm số Pointer đã tập kết (Pending + Completed + Skipped)
        int arrivedPointersCount = await _pointerRepo.CountArrivedPointersByStepIdAsync(instance.Id, joinNodeId);

        if (arrivedPointersCount >= totalIncomingEdges)
        {
            var joinPointers = await _pointerRepo.GetPointersByStepIdAsync(instance.Id, joinNodeId);

            // Đã chạy qua Barrier chưa? (Check xem có pointer nào completed chưa)
            if (joinPointers.Any(p => p.Status == ExecutionPointerStatus.Completed)) return;

            // Nếu tất cả các nhánh vào đều bị Skipped -> Skip luôn nút Join này
            if (joinPointers.All(p => p.Status == ExecutionPointerStatus.Skipped))
            {
                _logger.LogInformation("💀 All incoming paths to Join {JoinId} skipped.", joinNodeId);
                return;
            }

            // Chọn 1 Pointer Pending đại diện làm Token chạy
            var pointerToDispatch = joinPointers.FirstOrDefault(p => p.Status == ExecutionPointerStatus.Pending);

            // Đánh dấu các Pointer còn lại thành Completed để dọn rác
            foreach (var p in joinPointers.Where(x => x.Id != pointerToDispatch?.Id && x.Status == ExecutionPointerStatus.Pending))
            {
                p.Status = ExecutionPointerStatus.Completed;
            }

            if (pointerToDispatch != null)
            {
                _logger.LogInformation("🔗 Join Barrier broken for {JoinId}. Dispatching!", joinNodeId);
                await _uow.SaveChangesAsync();
                await DispatchPointerAsync(instance, pointerToDispatch, def);
            }
        }
    }

    private List<(string TargetNodeId, bool IsConditionMet)> EvaluateTransitions(JsonDocument defJson, string currentId, JsonDocument context)
    {
        var transitions = new List<(string TargetNodeId, bool IsConditionMet)>();
        if (defJson.RootElement.TryGetProperty("Transitions", out var transArray))
        {
            foreach (var t in transArray.EnumerateArray())
            {
                if (t.GetProperty("Source").GetString() == currentId)
                {
                    string target = t.GetProperty("Target").GetString()!;
                    bool conditionMet = true;

                    if (t.TryGetProperty("Condition", out var conditionElem))
                    {
                        string conditionStr = conditionElem.GetString() ?? "";
                        conditionMet = EvaluateCondition(conditionStr, context);
                    }

                    transitions.Add((target, conditionMet));
                }
            }
        }
        return transitions;
    }

    private bool EvaluateCondition(string conditionExpression, JsonDocument context)
    {
        // TODO: Logic phân tích biểu thức with jint
        if (string.IsNullOrWhiteSpace(conditionExpression))
        {
            return true;
        }

        try
        {
            string resolvedExpression = _resolver.Resolve(conditionExpression, context);

            // 2. Dùng Jint để thực thi chuỗi biểu thức như code Javascript
            var engine = new Jint.Engine();
            var result = engine.Evaluate(resolvedExpression);

            // 3. Ép kiểu kết quả về Boolean
            return result.AsBoolean();

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ Invalid condition expression: {Expression}", conditionExpression);
            return false;
        }
    }

    private int GetIncomingEdgesCount(JsonDocument defJson, string stepId)
    {
        int count = 0;
        if (defJson.RootElement.TryGetProperty("Transitions", out var transitions))
        {
            foreach (var t in transitions.EnumerateArray())
            {
                if (t.GetProperty("Target").GetString() == stepId) count++;
            }
        }
        return count;
    }

    // --- Private Helpers ---
    private string GetStepType(JsonDocument defJson, string stepId)
    {
        foreach (var step in defJson.RootElement.GetProperty("Steps").EnumerateArray())
        {
            if (step.GetProperty("Id").GetString() == stepId)
                return step.GetProperty("Type").GetString() ?? "";
        }
        return "";
    }

    private async Task DispatchPointerAsync(WorkflowInstance instance, ExecutionPointer pointer, WorkflowDefinition def)
    {
        var stepDef = GetStepDefinition(def.DefinitionJson, pointer.StepId);
        string stepType = stepDef.GetProperty("Type").GetString()!;

        // logic đóng bẳng for node wait
        if (stepType == "Wait")
        {
            pointer.Status = ExecutionPointerStatus.WaitingForEvent;

            // Vì DispatchPointerAsync thường được gọi sau SaveChanges, 
            // ta cần SaveChanges thêm 1 lần nữa để lưu trạng thái Waiting
            await _uow.SaveChangesAsync();

            _logger.LogInformation("Workflow {InstanceId} is PAUSED at Step {StepId}. Waiting for external trigger.", instance.Id, pointer.StepId);
            return; // DỪNG TẠI ĐÂY, KHÔNG PUBLISH QUA RABBITMQ
        }

        // 1. Get Inputs Template
        string rawInputs = "{}";
        if (stepDef.TryGetProperty("Inputs", out var inputsElem))
        {
            rawInputs = inputsElem.GetRawText();
        }

        // 2. Resolve Variables
        string resolvedPayload = _resolver.Resolve(rawInputs, instance.ContextData);

        //var routingKey = "workflow.plugin.execute";
        var routingKey = $"{MessagingConstants.PatternPlugin.TrimEnd('#')}execute";

        // 3. Send Command
        await _publishEndpoint.Publish(new ExecutePluginCommand(
            InstanceId: instance.Id,
            ExecutionPointerId: pointer.Id,
            NodeId: pointer.StepId,
            StepType: stepType,
            Payload: resolvedPayload
        ), context =>
        {
            context.SetRoutingKey(routingKey);
        });
    }

    private void MergeStepOutputToContext(WorkflowInstance instance, string nodeId, JsonDocument output)
    {
        var root = JsonNode.Parse(instance.ContextData.RootElement.GetRawText())!;
        if (root["Steps"] == null) root["Steps"] = new JsonObject();

        var stepData = new JsonObject();
        stepData["Output"] = JsonNode.Parse(output.RootElement.GetRawText());

        root["Steps"]![nodeId] = stepData;

        instance.UpdateContext(JsonDocument.Parse(root.ToJsonString()));
    }

    private string FindStartNodeId(JsonDocument defJson)
    {
        // Logic thực tế: Node nào không nằm trong danh sách "Target" của bất kỳ Edge nào
        var root = defJson.RootElement;
        var steps = root.GetProperty("Steps");

        // Cách đơn giản nhất cho MVP: Lấy node đầu tiên khai báo
        return steps[0].GetProperty("Id").GetString()!;
    }

    private List<string> FindNextNodes(JsonDocument defJson, string currentId)
    {
        var nextNodes = new List<string>();
        if (defJson.RootElement.TryGetProperty("Transitions", out var transitions))
        {
            foreach (var t in transitions.EnumerateArray())
            {
                if (t.GetProperty("Source").GetString() == currentId)
                {
                    nextNodes.Add(t.GetProperty("Target").GetString()!);
                }
            }
        }
        return nextNodes;
    }

    private JsonElement GetStepDefinition(JsonDocument defJson, string stepId)
    {
        foreach (var step in defJson.RootElement.GetProperty("Steps").EnumerateArray())
        {
            if (step.GetProperty("Id").GetString() == stepId) return step;
        }
        throw new Exception($"Step {stepId} not found in definition");
    }

    public async Task<Result> HandleStepFailureAsync(Guid instanceId, Guid pointerId, string error)
    {
        try
        {
            // Nếu không có pointerId (VD lỗi từ consumer không xác định), log và bỏ qua
            if (pointerId == Guid.Empty)
            {
                _logger.LogWarning("Received failure for Instance {Id} but PointerId is empty. Error: {Error}", instanceId, error);
                return Result.Success();
            }

            var pointer = await _pointerRepo.GetPointerByIdAsync(pointerId);
            if (pointer != null)
            {
                // Cập nhật trạng thái lỗi vào DB
                // pointer.Status = PointerStatus.Failed;
                // pointer.ErrorMessage = errorMsg;

                // TODO: Logic Retry workflow ở đây (nếu config cho phép retry)
                // if (CanRetry(pointer)) { ... } else { instance.Status = WorkflowStatus.Failed; }

                await _uow.SaveChangesAsync();
            }

            _logger.LogInformation("Record failure for Step {PointerId}: {Error}", pointerId, error);

            // Trả về Success để confirm là "Đã ghi nhận lỗi xong", không cần MassTransit retry nữa
            return Result.Success();
        }
        catch (Exception ex)
        {
            // Nếu lỗi DB khi ghi log lỗi -> Cần Retry
            return Result.Failure(Error.Unexpected("System.StepFailureError", ex.Message));
        }
    }
}
