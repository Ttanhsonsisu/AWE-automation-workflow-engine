using AWE.Shared.Consts;
using AWE.Wokrer.Engine.Consumers;
using AWE.Infrastructure.Extensions;
using MassTransit;

namespace AWE.Wokrer.Engine.Definitions;

public class WorkflowEventConsumerDefinition : ConsumerDefinition<WorkflowEventConsumer>
{
    public WorkflowEventConsumerDefinition()
    {
        // Đặt tên Queue cứng: "q.workflow.core"
        EndpointName = MessagingConstants.QueueCore;
    }

    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<WorkflowEventConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // 1. Performance (Xử lý song song cao)
        endpointConfigurator.PrefetchCount = 40;
        consumerConfigurator.UseConcurrencyLimit(20);

        // 2. Topology (Sử dụng Quorum Queue & Bind Pattern Event)
        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbit)
        {
            // Bind: ex.workflow -> q.workflow.core (với key: workflow.event.#)
            rabbit.ConfigureQuorumQueue(
                MessagingConstants.ExchangeWorkflow,
                MessagingConstants.PatternEvent
            );
        }
    }
}

