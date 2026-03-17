using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using AWE.WorkflowEngine.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class WorkflowCompensationService(
    IExecutionPointerRepository pointerRepo,
    IPublishEndpoint publishEndpoint,
    ILogger<WorkflowCompensationService> logger) : IWorkflowCompensationService
{
    private readonly IExecutionPointerRepository _pointerRepo = pointerRepo;
    private readonly IPublishEndpoint _publishEndpoint = publishEndpoint;
    private readonly ILogger<WorkflowCompensationService> _logger = logger;

    public async Task TriggerCompensationAsync(WorkflowInstance instance, JsonDocument defJson)
    {
        _logger.LogWarning("Initiating SAGA COMPENSATION for Instance {InstanceId}", instance.Id);

        // 1. Lấy danh sách các node đã Completed (Đã được sort LIFO từ DB)
        var completedPointers = await _pointerRepo.GetCompletedPointersByInstanceIdAsync(instance.Id);

        if (!completedPointers.Any())
        {
            _logger.LogInformation("No completed steps found to compensate for Instance {InstanceId}.", instance.Id);
            return;
        }

        var routingKey = $"{MessagingConstants.PatternPlugin.TrimEnd('#')}compensate";

        // 2. Lặp qua từng Node và bắn lệnh Rollback
        foreach (var pointer in completedPointers)
        {
            var stepDef = GetStepDefinition(defJson, pointer.StepId);
            string stepType = stepDef.GetProperty("Type").GetString()!;

            // LƯU Ý: Gửi OUTPUT cũ cho Plugin để nó biết đường Rollback
            string rollbackPayload = pointer.Output?.RootElement.GetRawText() ?? "{}";

            // =================================================================
            // ĐỌC CẤU HÌNH EXECUTION MODE & DLL PATH (Dành cho lúc Lùi xe)
            // =================================================================
            PluginExecutionMode executionMode = PluginExecutionMode.BuiltIn;

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

            // =================================================================
            // BẮN LỆNH COMPENSATE
            // =================================================================
            await _publishEndpoint.Publish(new CompensatePluginCommand(
                InstanceId: instance.Id,
                ExecutionPointerId: pointer.Id,
                NodeId: pointer.StepId,
                StepType: stepType,
                Payload: rollbackPayload,
                ExecutionMode: executionMode, 
                DllPath: dllPath              
            ), ctx => ctx.SetRoutingKey(routingKey));

            _logger.LogInformation("Dispatched Rollback Command for Step {StepId} ({StepType}) via Mode {Mode}", pointer.StepId, stepType, executionMode);
        }
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
