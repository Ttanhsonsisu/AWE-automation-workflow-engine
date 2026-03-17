using System.Text.Json;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using AWE.WorkflowEngine.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class PointerDispatcher(IPublishEndpoint publishEndpoint, IVariableResolver resolver, ILogger<PointerDispatcher> logger) : IPointerDispatcher
{
    private readonly IPublishEndpoint _publishEndpoint = publishEndpoint;
    private readonly IVariableResolver _resolver = resolver;
    private readonly ILogger<PointerDispatcher> _logger = logger;

    public async Task DispatchAsync(WorkflowInstance instance, ExecutionPointer pointer, JsonDocument defJson)
    {
        var stepDef = GetStepDefinition(defJson, pointer.StepId);
        string stepType = stepDef.GetProperty("Type").GetString()!;

        // Xử lý Node Wait
        if (stepType == "Wait")
        {
            pointer.Status = ExecutionPointerStatus.WaitingForEvent;
            _logger.LogInformation("⏸️ Workflow {InstanceId} PAUSED at Step {StepId}.", instance.Id, pointer.StepId);
            return;
        }

        // Logic Resolve Variable
        var rawInputs = stepDef.TryGetProperty("Inputs", out var inputsElem) ? inputsElem.GetRawText() : "{}";
        var resolvedPayload = _resolver.Resolve(rawInputs, instance.ContextData);

        // =================================================================
        // ĐỌC CẤU HÌNH EXECUTION MODE & DLL PATH
        // =================================================================
        PluginExecutionMode executionMode = PluginExecutionMode.BuiltIn; // Mặc định là chạy Built-in

        if (stepDef.TryGetProperty("ExecutionMode", out var modeElem))
        {
            if (modeElem.ValueKind == JsonValueKind.Number && modeElem.TryGetInt32(out int modeInt))
            {
                executionMode = (PluginExecutionMode)modeInt;
            }
            else if (modeElem.ValueKind == JsonValueKind.String && Enum.TryParse<PluginExecutionMode>(modeElem.GetString(), true, out var parsedMode))
            {
                executionMode = parsedMode;
            }
        }

        string? dllPath = stepDef.TryGetProperty("DllPath", out var dllElem) ? dllElem.GetString() : null;

        var routingKey = $"{MessagingConstants.PatternPlugin.TrimEnd('#')}execute";

        // =================================================================
        // GỬI LỆNH ĐẾN WORKER
        // =================================================================
        await _publishEndpoint.Publish(new ExecutePluginCommand(
            InstanceId: instance.Id,
            ExecutionPointerId: pointer.Id,
            NodeId: pointer.StepId,
            StepType: stepType,
            Payload: resolvedPayload,
            ExecutionMode: executionMode, 
            DllPath: dllPath             
        ), ctx => ctx.SetRoutingKey(routingKey));
    }

    private JsonElement GetStepDefinition(JsonDocument defJson, string stepId)
    {
        foreach (var step in defJson.RootElement.GetProperty("Steps").EnumerateArray())
        {
            if (step.GetProperty("Id").GetString() == stepId) return step;
        }
        throw new InvalidOperationException($"Step {stepId} not found");
    }
}
