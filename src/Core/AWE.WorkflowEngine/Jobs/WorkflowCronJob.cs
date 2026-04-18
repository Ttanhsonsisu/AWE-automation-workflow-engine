using System.Text.Json;
using AWE.Contracts.Messages;
using AWE.Domain.Enums;
using MassTransit;
using Microsoft.Extensions.Logging;
using Quartz;

namespace AWE.WorkflowEngine.Jobs;

[DisallowConcurrentExecution]
public class WorkflowCronJob(
    IBus bus,
    ILogger<WorkflowCronJob> logger) : IJob
{
    public const string DefinitionIdJobDataKey = "DefinitionId";
    private const string SubmitWorkflowRoutingKey = "workflow.job.submit";

    private readonly IBus _publishEndpoint = bus;
    private readonly ILogger<WorkflowCronJob> _logger = logger;

    public async Task Execute(IJobExecutionContext context)
    {
        var definitionIdRaw = context.MergedJobDataMap.GetString(DefinitionIdJobDataKey);
        if (!Guid.TryParse(definitionIdRaw, out var definitionId))
        {
            _logger.LogWarning(
                "Quartz WorkflowCronJob skipped because DefinitionId is missing or invalid. JobKey={JobKey}",
                context.JobDetail.Key);
            return;
        }

        var firedAtUtc = DateTime.UtcNow;
        var inputPayload = JsonSerializer.Serialize(new
        {
            Trigger = new
            {
                Source = WorkflowTriggerSource.Cron.ToString(),
                FiredAtUtc = firedAtUtc,
                Quartz = new
                {
                    context.FireInstanceId,
                    ScheduledFireTimeUtc = context.ScheduledFireTimeUtc?.UtcDateTime
                }
            },
            Workflow = new
            {
                DefinitionId = definitionId
            }
        });

        var command = new SubmitWorkflowCommand(
            DefinitionId: definitionId,
            JobName: $"CronTrigger-{definitionId}-{firedAtUtc:yyyyMMddHHmmss}",
            InputData: inputPayload,
            CorrelationId: Guid.NewGuid(),
            TriggerSource: WorkflowTriggerSource.Cron);

        await _publishEndpoint.Publish(command, publishContext =>
        {
            publishContext.SetRoutingKey(SubmitWorkflowRoutingKey);
        }, context.CancellationToken);

        _logger.LogInformation(
            "Quartz WorkflowCronJob published SubmitWorkflowCommand for DefinitionId={DefinitionId}, JobKey={JobKey}",
            definitionId,
            context.JobDetail.Key);
    }
}
