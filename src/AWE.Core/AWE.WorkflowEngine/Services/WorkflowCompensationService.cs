using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Shared.Consts;
using AWE.WorkflowEngine.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class WorkflowCompensationService : IWorkflowCompensationService
{
    private readonly IExecutionPointerRepository _pointerRepo;
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ILogger<WorkflowCompensationService> _logger;

    public WorkflowCompensationService(
        IExecutionPointerRepository pointerRepo,
        IPublishEndpoint publishEndpoint,
        ILogger<WorkflowCompensationService> logger)
    {
        _pointerRepo = pointerRepo;
        _publishEndpoint = publishEndpoint;
        _logger = logger;
    }

    public async Task TriggerCompensationAsync(WorkflowInstance instance, JsonDocument defJson)
    {
        _logger.LogWarning("🚨 Initiating SAGA COMPENSATION for Instance {InstanceId}", instance.Id);

        // 1. Lấy danh sách các node đã Completed (Đã được sort LIFO từ DB)
        var completedPointers = await _pointerRepo.GetCompletedPointersByInstanceIdAsync(instance.Id);

        if (!completedPointers.Any())
        {
            _logger.LogInformation("No completed steps found to compensate for Instance {InstanceId}.", instance.Id);
            return;
        }

        // 2. Lặp qua từng Node và bắn lệnh Rollback
        var routingKey = $"{MessagingConstants.PatternPlugin.TrimEnd('#')}compensate";

        foreach (var pointer in completedPointers)
        {
            var stepDef = GetStepDefinition(defJson, pointer.StepId);
            string stepType = stepDef.GetProperty("Type").GetString()!;

            // LƯU Ý: Gửi OUTPUT cũ cho Plugin để nó biết đường Rollback
            // Nếu step không có output, gửi chuỗi "{}"
            string rollbackPayload = pointer.Output?.RootElement.GetRawText() ?? "{}";

            await _publishEndpoint.Publish(new CompensatePluginCommand(
                InstanceId: instance.Id,
                ExecutionPointerId: pointer.Id,
                NodeId: pointer.StepId,
                StepType: stepType,
                Payload: rollbackPayload
            ), ctx => ctx.SetRoutingKey(routingKey));

            _logger.LogInformation("Dispatched Rollback Command for Step {StepId} ({StepType})", pointer.StepId, stepType);
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
