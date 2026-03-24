
using AWE.Application.Abstractions.CoreEngine;
using AWE.Contracts.Messages;
using MassTransit;

namespace AWE.Wokrer.Engine.Consumers;

public class SuspendStepConsumer(IWorkflowOrchestrator orchestrator) : IConsumer<SuspendStepCommand>
{
    public async Task Consume(ConsumeContext<SuspendStepCommand> context)
    {
        await orchestrator.HandleStepSuspendedAsync(
            context.Message.InstanceId,
            context.Message.PointerId,
            context.Message.Reason
        );
    }
}
