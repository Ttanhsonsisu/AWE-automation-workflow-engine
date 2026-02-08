using System;
using System.Collections.Generic;
using System.Text;
using AWE.Infrastructure.Extensions;
using MassTransit;

namespace AWE.WorkflowEngine.Consumers.Definitions;

/// <summary>
/// Consumer definition for job execution commands.
/// </summary>
/// <remarks>
/// - Controls concurrency to protect workflow execution resources
/// - Uses retry + in-memory outbox for reliable message processing
/// - Backed by RabbitMQ quorum queue for durability
/// </remarks>
public class JobExecutionConsumerDefinition : ConsumerDefinition<JobExecutionConsumer>
{
    protected override void ConfigureConsumer(IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<JobExecutionConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // =====================================================
        // PERFORMANCE – Controlled parallel message processing
        // =====================================================

        // Maximum number of messages prefetched from RabbitMQ
        // Improves throughput while avoiding excessive memory usage
        endpointConfigurator.PrefetchCount = 20;

        // Maximum number of messages processed concurrently
        // Should not exceed PrefetchCount to prevent message hoarding
        consumerConfigurator.UseConcurrencyLimit(20);

        // =====================================================
        // RESILIENCE – Transient fault handling
        // =====================================================

        // Retry immediately for short-lived failures
        // (e.g., database locks, brief network interruptions)
        endpointConfigurator.UseMessageRetry(r => r.Immediate(5));

        // In-memory outbox to ensure exactly-once publish semantics
        // Prevents duplicate outgoing messages during in-place retries
        endpointConfigurator.UseInMemoryOutbox(context);

        // =====================================================
        // TOPOLOGY – RabbitMQ queue and binding configuration
        // =====================================================
        if (endpointConfigurator is IRabbitMqReceiveEndpointConfigurator rabbit)
        {
            // Configure a Quorum Queue for high availability and data safety
            // Bind to the "awe.events" exchange and consume only submit commands
            // routed with the key "workflow.job.submit"
            rabbit.ConfigureQuorumQueue("awe.events", "workflow.job.submit");
        }
    }
}
