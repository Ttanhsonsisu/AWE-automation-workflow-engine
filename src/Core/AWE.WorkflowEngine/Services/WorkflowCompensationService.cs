using System.Text.Json;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class WorkflowCompensationService(
    IExecutionPointerRepository pointerRepo,
    ILogger<WorkflowCompensationService> logger) : IWorkflowCompensationService
{
    private readonly IExecutionPointerRepository _pointerRepo = pointerRepo;
    private readonly ILogger<WorkflowCompensationService> _logger = logger;

    public async Task<List<CompensatePluginCommand>> TriggerCompensationAsync(WorkflowInstance instance, JsonDocument defJson)
    {
        _logger.LogWarning("Initiating SAGA COMPENSATION for Instance {InstanceId}", instance.Id);

        var commands = new List<CompensatePluginCommand>();

        // 1. Lấy danh sách các node đã Completed (Đã được sort LIFO từ DB)
        var completedPointers = await _pointerRepo.GetCompletedPointersByInstanceIdAsync(instance.Id);

        if (!completedPointers.Any())
        {
            _logger.LogInformation("No completed steps found to compensate for Instance {InstanceId}.", instance.Id);
            return commands;
        }

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

            string? executionMetadata = stepDef.TryGetProperty("ExecutionMetadata", out var metaElem)
                ? metaElem.GetRawText()
                : null;

            // update: Đối với Rollback, chúng ta sẽ luôn gửi lệnh CompensatePluginCommand,
            // và bên Plugin sẽ tự quyết định có thực hiện rollback logic hay không dựa trên payload & execution mode
            commands.Add(new CompensatePluginCommand(
                InstanceId: instance.Id,
                ExecutionPointerId: pointer.Id,
                NodeId: pointer.StepId,
                StepType: stepType,
                Payload: rollbackPayload,
                ExecutionMode: executionMode,
                ExecutionMetadata: executionMetadata
            ));

            _logger.LogInformation("Dispatched Rollback Command for Step {StepId} ({StepType}) via Mode {Mode}", pointer.StepId, stepType, executionMode);
        }

        return commands;
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
