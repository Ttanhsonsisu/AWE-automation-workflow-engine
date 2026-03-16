using System.Text.Json;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using AWE.WorkflowEngine.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class PointerDispatcher : IPointerDispatcher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IVariableResolver _resolver;
    private readonly ILogger<PointerDispatcher> _logger;

    public PointerDispatcher(IPublishEndpoint publishEndpoint, IVariableResolver resolver, ILogger<PointerDispatcher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _resolver = resolver;
        _logger = logger;
    }

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

        // Logic Resolve Variable và Publish giữ nguyên
        string rawInputs = stepDef.TryGetProperty("Inputs", out var inputsElem) ? inputsElem.GetRawText() : "{}";
        string resolvedPayload = _resolver.Resolve(rawInputs, instance.ContextData);
        var routingKey = $"{MessagingConstants.PatternPlugin.TrimEnd('#')}execute";

        await _publishEndpoint.Publish(new ExecutePluginCommand(
            InstanceId: instance.Id,
            ExecutionPointerId: pointer.Id,
            NodeId: pointer.StepId,
            StepType: stepType,
            Payload: resolvedPayload
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
