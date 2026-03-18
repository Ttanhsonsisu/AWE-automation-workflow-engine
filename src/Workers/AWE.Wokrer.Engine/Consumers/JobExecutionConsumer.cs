using AWE.Application.Abstractions.CoreEngine;
using AWE.Contracts.Messages;
using AWE.Infrastructure.Extensions;
using MassTransit;

namespace AWE.Wokrer.Engine.Consumers;

public class JobExecutionConsumer : IConsumer<SubmitWorkflowCommand>
{
    private readonly ILogger<JobExecutionConsumer> _logger;
    private readonly IWorkflowOrchestrator _orchestrator;

    public JobExecutionConsumer(ILogger<JobExecutionConsumer> logger, IWorkflowOrchestrator orchestrator)
    {
        _logger = logger;
        _orchestrator = orchestrator;
    }

    public async Task Consume(ConsumeContext<SubmitWorkflowCommand> context)
    {
        var cmd = context.Message;

        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["TraceId"] = cmd.CorrelationId ?? context.CorrelationId ?? Guid.NewGuid(),
            ["JobName"] = cmd.JobName
        });

        // Gọi Orchestrator Core
        var result = await _orchestrator.StartWorkflowAsync(
            cmd.DefinitionId, cmd.JobName, cmd.InputData, cmd.CorrelationId
        );

        // Extension xử lý Result chuẩn hóa
        await context.ProcessResultAsync(result, _logger, "StartJob");
    }
}
