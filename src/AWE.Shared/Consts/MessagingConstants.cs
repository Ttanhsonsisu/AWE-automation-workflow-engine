using System;
using System.Collections.Generic;
using System.Text;

namespace AWE.Shared.Consts;
/// <summary>
/// Defines messaging-related constants used across the AWE platform.
/// </summary>
/// <remarks>
/// This includes RabbitMQ configuration sections, exchanges, queues,
/// and routing key patterns that form the messaging contract between
/// core services, plugins, and infrastructure components.
/// </remarks>
public class MessagingConstants
{
    // Section trong appsettings.json
    public const string SectionName = "RabbitMq";

    // Exchange (Duy nhất cho workflow)
    public const string ExchangeWorkflow = "ex.workflow";

    // Queues
    public const string QueueCore = "q.workflow.core";      // Engine
    public const string QueuePlugin = "q.workflow.plugin";  // Worker
    public const string QueueAudit = "q.workflow.audit";    // Logger

    // Routing Keys
    public const string PatternCmd = "workflow.cmd.#";
    public const string PatternPlugin = "workflow.plugin.#";
    public const string PatternAudit = "workflow.#";
}
