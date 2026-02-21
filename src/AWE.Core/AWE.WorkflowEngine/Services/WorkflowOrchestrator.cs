using System.Text.Json;
using System.Text.Json.Nodes;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using AWE.Shared.Primitives;
using AWE.WorkflowEngine.Interfaces;
using MassTransit;
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
    private readonly ILogger<WorkflowOrchestrator> _logger;

    public WorkflowOrchestrator(
        IUnitOfWork uow,
        IWorkflowDefinitionRepository defRepo,
        IWorkflowInstanceRepository instanceRepo,
        IExecutionPointerRepository pointerRepo,
        IPublishEndpoint publishEndpoint,
        IVariableResolver resolver,
        ILogger<WorkflowOrchestrator> logger)
    {
        _uow = uow;
        _defRepo = defRepo;
        _instanceRepo = instanceRepo;
        _pointerRepo = pointerRepo;
        _publishEndpoint = publishEndpoint;
        _resolver = resolver;
        _logger = logger;
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
        // 1. Load State
        try
        {
            // 1. Load Data
            var instance = await _instanceRepo.GetInstanceByIdAsync(instanceId);
            var pointer = await _pointerRepo.GetPointerByIdAsync(executionPointerId);

            if (instance == null)
                return Result.Failure(Error.NotFound("Instance.NotFound", $"Instance {instanceId} not found"));

            if (pointer == null || pointer.Status == ExecutionPointerStatus.Completed)
            {
                _logger.LogWarning("Pointer {PointerId} not found, possibly already processed.", executionPointerId);
                return Result.Success(); // Coi như thành công để khỏi retry
            }

            pointer.Status = ExecutionPointerStatus.Completed;

            // 2. Update Context (Merge Output của Step vào Workflow Data)
            // Ưu tiên dùng output lưu trong DB (nếu Worker đã update pointer) hoặc từ Event
            var outputToMerge = pointer.Output ?? eventOutput;
            if (outputToMerge != null)
            {
                MergeStepOutputToContext(instance, pointer.StepId, outputToMerge);
                // Mark pointer as completed in DB logic (nếu Repository chưa làm)
                // pointer.Status = PointerStatus.Completed; 
                // pointer.EndTime = DateTime.UtcNow;
            }

            // 3. Navigation (Tìm bước tiếp theo)
            var def = await _defRepo.GetDefinitionByIdAsync(instance.DefinitionId);
            var nextNodeIds = FindNextNodes(def!.DefinitionJson, pointer.StepId);

            if (nextNodeIds.Count == 0)
            {
                // End Workflow
                // instance.Status = WorkflowStatus.Completed;
                // instance.EndTime = DateTime.UtcNow;
                _logger.LogInformation("🏁 Workflow {Id} Completed successfully.", instanceId);
            }
            else
            {
                // Spawn next pointers
                foreach (var nextNodeId in nextNodeIds)
                {
                    var newPointer = new ExecutionPointer(instance.Id, nextNodeId,
                        parentTokenId: pointer.ParentTokenId,
                        branchId: pointer.BranchId);

                    await _pointerRepo.AddPointerAsync(newPointer);

                    // Dispatch lệnh thực thi cho pointer mới
                    await DispatchPointerAsync(instance, newPointer, def);
                }
            }

            // 4. Save Changes
            await _uow.SaveChangesAsync();

            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "System error in HandleStepCompletionAsync");
            return Result.Failure(Error.Unexpected("System.StepCompletionError", ex.Message));
        }
    }

    // --- Private Helpers ---

    private async Task DispatchPointerAsync(WorkflowInstance instance, ExecutionPointer pointer, WorkflowDefinition def)
    {
        var stepDef = GetStepDefinition(def.DefinitionJson, pointer.StepId);
        string stepType = stepDef.GetProperty("Type").GetString()!;

        // 1. Get Inputs Template
        string rawInputs = "{}";
        if (stepDef.TryGetProperty("Inputs", out var inputsElem))
        {
            rawInputs = inputsElem.GetRawText();
        }

        // 2. Resolve Variables
        string resolvedPayload = _resolver.Resolve(rawInputs, instance.ContextData);

        //var routingKey = "workflow.plugin.execute";
        // change const:
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
