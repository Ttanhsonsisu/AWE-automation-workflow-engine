using System;
using System.Collections.Generic;
using System.Text;
using AWE.Contracts.Messages;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Consumers;

public class AuditLogConsumer : IConsumer<WriteAuditLogCommand>
{
    private readonly ILogger<AuditLogConsumer> _logger;
    public AuditLogConsumer(ILogger<AuditLogConsumer> logger) => _logger = logger;

    public Task Consume(ConsumeContext<WriteAuditLogCommand> context)
    {
        // TODO: code logic here
        _logger.LogInformation("[AUDIT] [{Source}] {Action} @ {Time}",
            context.Message.Source, context.Message.Action, context.Message.Timestamp);

        return Task.CompletedTask;
    }
}
