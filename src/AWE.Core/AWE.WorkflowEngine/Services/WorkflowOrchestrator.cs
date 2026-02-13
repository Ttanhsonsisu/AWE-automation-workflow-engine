using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
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

    public async Task StartWorkflowAsync(Guid definitionId, string jobName, string inputData, Guid? correlationId)
    {
        var def = await _defRepo.GetDefinitionByIdAsync(definitionId);
        if (def == null) throw new Exception($"Definition {definitionId} not found");

        // 1. Init Instance Context
        var rawInput = string.IsNullOrWhiteSpace(inputData) ? "{}" : inputData;
        var initialContext = new JsonObject
        {
            ["Inputs"] = System.Text.Json.Nodes.JsonNode.Parse(rawInput),
            ["Steps"] = new System.Text.Json.Nodes.JsonObject(),
            ["System"] = new System.Text.Json.Nodes.JsonObject
            {
                ["CorrelationId"] = correlationId,
                ["JobName"] = jobName, // [UPDATE] Lưu JobName vào Context hệ thống
                ["StartedAt"] = DateTime.UtcNow
            }
        };

        // 2. Tạo Instance
        // (Nếu Entity WorkflowInstance có cột Description/Name thì gán vào đó luôn)
        var instance = new WorkflowInstance(def.Id, def.Version, JsonDocument.Parse(initialContext.ToJsonString()));
        // instance.Description = jobName; // Nếu có cột này

        await _instanceRepo.AddInstanceAsync(instance);

        // 3. Tìm Start Node & Tạo Pointer
        var startNodeId = FindStartNodeId(def.DefinitionJson); // Logic tìm node đầu (đã làm ở bài trước)
        var pointer = new ExecutionPointer(instance.Id, startNodeId);
        await _pointerRepo.AddPointerAsync(pointer);

        await _uow.SaveChangesAsync(); // Commit Transaction khởi tạo

        _logger.LogInformation("🚀 [ENGINE] Started Job '{Name}' (ID: {Id})", jobName, instance.Id);

        // 4. Dispatch
        await DispatchPointerAsync(instance, pointer, def);
    }

    public async Task HandleStepCompletionAsync(Guid instanceId, Guid executionPointerId, JsonDocument? eventOutput)
    {
        // 1. Load State
        var instance = await _instanceRepo.GetInstanceByIdAsync(instanceId);
        var pointer = await _pointerRepo.GetPointerByIdAsync(executionPointerId);

        if (pointer == null) return; // Should not happen

        // 2. Merge Context (Quan trọng: Đọc Output từ Pointer trong DB thay vì Event để chắc ăn)
        if (pointer.Output != null)
        {
            MergeStepOutputToContext(instance, pointer.StepId, pointer.Output);
        }

        // 3. Navigation (Graph Traversal)
        var def = await _defRepo.GetDefinitionByIdAsync(instance.DefinitionId);
        var nextNodeIds = FindNextNodes(def.DefinitionJson, pointer.StepId);

        if (nextNodeIds.Count == 0)
        {
            instance.Complete(); // End Workflow
            _logger.LogInformation("🏁 Workflow {Id} Completed.", instanceId);
        }
        else
        {
            foreach (var nextNodeId in nextNodeIds)
            {
                // Sequence Flow: ParentTokenId giữ nguyên
                var newPointer = new ExecutionPointer(instance.Id, nextNodeId,
                    parentTokenId: pointer.ParentTokenId,
                    branchId: pointer.BranchId);

                await _pointerRepo.AddPointerAsync(newPointer);
                await _uow.SaveChangesAsync();

                await DispatchPointerAsync(instance, newPointer, def);
            }
        }
        await _uow.SaveChangesAsync();
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

        // 3. Send Command
        await _publishEndpoint.Publish(new ExecutePluginCommand(
            InstanceId: instance.Id,
            ExecutionPointerId: pointer.Id,
            NodeId: pointer.StepId,
            StepType: stepType,
            Payload: resolvedPayload
        ));
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

    public Task HandleStepFailureAsync(Guid instanceId, Guid pointerId, string error) => Task.CompletedTask;
}
