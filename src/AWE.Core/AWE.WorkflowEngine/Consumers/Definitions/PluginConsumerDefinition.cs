using AWE.Infrastructure.Messaging;
using MassTransit;

namespace AWE.WorkflowEngine.Consumers.Definitions;

/// <summary>
/// Consumer definition for plugin execution.
/// </summary>
/// <remarks>
/// - Enforces strict serial execution to protect non-thread-safe plugins
/// - Uses exponential retry to tolerate unstable third-party integrations
/// - Optimized for low-latency classic RabbitMQ queues
/// </remarks>
public class PluginConsumerDefinition : ConsumerDefinition<PluginConsumer>
{
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PluginConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // =====================================================
        // PERFORMANCE – Strict serial processing
        // =====================================================

        // Prefetch only a single message to avoid parallel plugin execution.
        // This guarantees strict ordering and prevents resource contention.
        endpointConfigurator.PrefetchCount = 1;

        // Enforce one-at-a-time processing.
        // Required because plugins may be stateful or not thread-safe.
        consumerConfigurator.UseConcurrencyLimit(1);

        // =====================================================
        // RESILIENCE – Exponential retry for third-party plugins
        // =====================================================

        // Use exponential backoff to handle unstable or slow external plugins
        // without overwhelming downstream systems.
        endpointConfigurator.UseMessageRetry(r => r.Exponential(
            retryLimit: 10,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromMinutes(5),
            intervalDelta: TimeSpan.FromSeconds(2)
        ));

        // =====================================================
        // OUTBOX – Duplicate message prevention
        // =====================================================

        // In-memory outbox ensures outgoing messages are published
        // only once during consumer retries.
        endpointConfigurator.UseInMemoryOutbox(context);

        // =====================================================
        // TOPOLOGY – RabbitMQ classic queue binding
        // =====================================================
        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbit)
        {
            // Use classic queue for lower latency and simpler semantics.
            // This consumer handles all workflow step execution events
            // routed with the "workflow.step.#" wildcard.
            rabbit.ConfigureClassicQueue("awe.events", "workflow.step.#");
        }
    }
}
