using AWE.Contracts.Messages;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Consumers;

public class AuditLogConsumer(ILogger<AuditLogConsumer> logger) : IConsumer<WriteAuditLogCommand>
{
    private readonly ILogger<AuditLogConsumer> _logger = logger;

    public Task Consume(ConsumeContext<WriteAuditLogCommand> context)
    {
        // TODO: code logic here
        _logger.LogInformation("[AUDIT] [{Source}] {Action} @ {Time}",
            context.Message.Source, context.Message.Action, context.Message.Timestamp);

        return Task.CompletedTask;
    }
}
