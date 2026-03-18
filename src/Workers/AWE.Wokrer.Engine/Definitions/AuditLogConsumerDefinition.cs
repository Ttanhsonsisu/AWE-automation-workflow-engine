using AWE.Infrastructure.Extensions;
using AWE.Wokrer.Engine.Consumers;
using MassTransit;

namespace AWE.Wokrer.Engine.Definitions;

/// <summary>
/// Consumer definition for audit log processing.
/// </summary>
/// <remarks>
/// - Optimized for high-throughput event consumption
/// - Uses RabbitMQ lazy queue to reduce memory pressure
/// - Subscribes to all audit-related events
/// </remarks>
public class AuditLogConsumerDefinition : ConsumerDefinition<AuditLogConsumer>
{
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<AuditLogConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Increase prefetch to improve throughput for write-only audit logs.
        // Safe because audit events are independent and idempotent.
        endpointConfigurator.PrefetchCount = 100;

        // RabbitMQ-specific topology configuration
        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbit)
        {
            // Configure lazy queue to minimize memory usage.
            // Bind to all audit-related routing keys (audit.#).
            rabbit.ConfigureLazyQueue("awe.events", "audit.#");
        }
    }
}
