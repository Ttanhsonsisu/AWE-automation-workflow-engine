using System.Text.Json;
using AWE.Application.UseCases.Workflows;
using AWE.Contracts.Messages;
using AWE.Domain.Enums;
using MassTransit;
using Microsoft.Extensions.Logging;
using Quartz;
using TimeZoneConverter;

namespace AWE.WorkflowEngine.Jobs;

[DisallowConcurrentExecution]
public class WorkflowCronJob(
    IBus bus,
    ILogger<WorkflowCronJob> logger) : IJob
{
    public const string DefinitionIdJobDataKey = "DefinitionId";
    public const string StepIdJobDataKey = "StepId";
    public const string CronExpressionJobDataKey = "CronExpression";
    public const string TimeZoneIdJobDataKey = "TimeZoneId";
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
        var triggerStepId = context.MergedJobDataMap.GetString(StepIdJobDataKey);
        var inputPayload = JsonSerializer.Serialize(new
        {
            Trigger = new
            {
                Source = WorkflowTriggerSource.Cron.ToString(),
                FiredAtUtc = firedAtUtc,
                StepId = triggerStepId,
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
            TriggerSource: WorkflowTriggerSource.Cron,
            TriggerRoutePath: string.IsNullOrWhiteSpace(triggerStepId) ? null : triggerStepId);

        await _publishEndpoint.Publish(command, publishContext =>
        {
            publishContext.SetRoutingKey(SubmitWorkflowRoutingKey);
        }, context.CancellationToken);

        _logger.LogInformation(
            "Quartz WorkflowCronJob published SubmitWorkflowCommand for DefinitionId={DefinitionId}, JobKey={JobKey}",
            definitionId,
            context.JobDetail.Key);

        await RescheduleNextOccurrenceAsync(context, definitionId, triggerStepId);
    }

    private async Task RescheduleNextOccurrenceAsync(
        IJobExecutionContext context,
        Guid definitionId,
        string? triggerStepId)
    {
        var cronExpression = context.MergedJobDataMap.GetString(CronExpressionJobDataKey);
        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            _logger.LogWarning(
                "Quartz WorkflowCronJob cannot reschedule because CronExpression is missing. DefinitionId={DefinitionId}, JobKey={JobKey}",
                definitionId,
                context.JobDetail.Key);
            return;
        }

        var timeZone = ResolveTimeZoneInfo(context.MergedJobDataMap.GetString(TimeZoneIdJobDataKey));
        if (!CronScheduleSyncHelper.TryGetNextRunAtUtc(
                cronExpression,
                timeZone,
                DateTime.UtcNow,
                out var nextRunAtUtc,
                out _,
                out var errorMessage))
        {
            _logger.LogError(
                "Quartz WorkflowCronJob cannot parse CronExpression '{CronExpression}'. DefinitionId={DefinitionId}, StepId={StepId}. Error={ErrorMessage}",
                cronExpression,
                definitionId,
                triggerStepId ?? "(legacy)",
                errorMessage);
            return;
        }

        if (nextRunAtUtc is null)
        {
            _logger.LogWarning(
                "Quartz WorkflowCronJob has no next occurrence. DefinitionId={DefinitionId}, StepId={StepId}",
                definitionId,
                triggerStepId ?? "(legacy)");
            return;
        }

        var replacementTrigger = TriggerBuilder.Create()
            .WithIdentity(context.Trigger.Key)
            .ForJob(context.JobDetail.Key)
            .StartAt(new DateTimeOffset(DateTime.SpecifyKind(nextRunAtUtc.Value, DateTimeKind.Utc)))
            .Build();

        await context.Scheduler.RescheduleJob(context.Trigger.Key, replacementTrigger, context.CancellationToken);
    }

    private static TimeZoneInfo ResolveTimeZoneInfo(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TZConvert.GetTimeZoneInfo(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }
}
