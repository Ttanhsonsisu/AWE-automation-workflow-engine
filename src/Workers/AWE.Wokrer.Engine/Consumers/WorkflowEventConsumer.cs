using AWE.Application.Abstractions.CoreEngine;
using AWE.Contracts.Messages;
using AWE.Infrastructure.Extensions;
using MassTransit;

namespace AWE.Wokrer.Engine.Consumers;

/// <summary>
/// Consumer này nằm ở Engine.
/// Nhiệm vụ: Lắng nghe báo cáo (Completed/Failed) từ Worker để kích hoạt bước tiếp theo.
/// </summary>
public class WorkflowEventConsumer :
    IConsumer<StepCompletedEvent>,
    IConsumer<StepFailedEvent>
{
    private readonly ILogger<WorkflowEventConsumer> _logger;
    private readonly IWorkflowOrchestrator _orchestrator;

    public WorkflowEventConsumer(ILogger<WorkflowEventConsumer> logger, IWorkflowOrchestrator orchestrator)
    {
        _logger = logger;
        _orchestrator = orchestrator;
    }

    public async Task Consume(ConsumeContext<StepCompletedEvent> context)
    {
        var msg = context.Message;

        var result = await _orchestrator.HandleStepCompletionAsync(
            msg.WorkflowInstanceId, msg.ExecutionPointerId, msg.Output
        );

        await context.ProcessResultAsync(result, _logger, $"StepSuccess:{msg.StepId}");
    }

    public async Task Consume(ConsumeContext<StepFailedEvent> context)
    {
        var msg = context.Message;

        var result = await _orchestrator.HandleStepFailureAsync(
            msg.InstanceId, msg.ExecutionPointerId, msg.ErrorMessage
        );

        await context.ProcessResultAsync(result, _logger, $"StepFail:{msg.StepId}");
    }
}
