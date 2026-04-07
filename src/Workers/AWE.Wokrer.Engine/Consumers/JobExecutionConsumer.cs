using AWE.Application.Abstractions.CoreEngine;
using AWE.Contracts.Messages;
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
            cmd.DefinitionId,
            cmd.JobName,
            cmd.InputData,
            cmd.CorrelationId,
            isTest: cmd.IsTest,
            stopAtStepId: cmd.StopAtStepId
        );

        if (result.IsSuccess)
        {
            _logger.LogInformation("✅ [SUCCESS] StartJob completed. InstanceId: {InstanceId}", result.Value);

            await context.RespondAsync(new SubmitWorkflowResponse(
                IsSuccess: true,
                InstanceId: result.Value,
                CorrelationId: cmd.CorrelationId));

            return;
        }

        _logger.LogWarning("❌ [FAILED] StartJob failed. Code: {Code}, Message: {Message}", result.Error?.Code, result.Error?.Message);

        await context.RespondAsync(new SubmitWorkflowResponse(
            IsSuccess: false,
            InstanceId: null,
            CorrelationId: cmd.CorrelationId,
            ErrorCode: result.Error?.Code,
            ErrorMessage: result.Error?.Message));
    }
}
