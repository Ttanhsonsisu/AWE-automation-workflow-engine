using System.Text.Json;
using Quartz;

namespace AWE.Application.UseCases.Workflows;

internal static class CronScheduleSyncHelper
{
    public const string WorkflowCronJobTypeName = "AWE.WorkflowEngine.Jobs.WorkflowCronJob, AWE.WorkflowEngine";
    private const string WorkflowCronGroup = "Workflows";

    public static async Task SyncAsync(
        IScheduler scheduler,
        Guid definitionId,
        JsonDocument definitionJson,
        CancellationToken cancellationToken)
    {
        var jobKey = BuildJobKey(definitionId);
        var triggerKey = BuildTriggerKey(definitionId);

        var cronConfig = ExtractCronConfig(definitionJson);
        if (cronConfig is null)
        {
            await scheduler.DeleteJob(jobKey, cancellationToken);
            return;
        }

        var jobType = ResolveWorkflowCronJobType();
        if (jobType is null)
        {
            throw new InvalidOperationException(
                $"Không tìm thấy Quartz job type '{WorkflowCronJobTypeName}'. Vui lòng đảm bảo assembly AWE.WorkflowEngine được load.");
        }

        var job = JobBuilder.Create(jobType)
            .WithIdentity(jobKey)
            .UsingJobData("DefinitionId", definitionId.ToString())
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .WithCronSchedule(cronConfig.CronExpression, x => x
                .InTimeZone(ResolveTimeZoneInfo(cronConfig.TimeZoneId))
                .WithMisfireHandlingInstructionDoNothing())
            .Build();

        await scheduler.DeleteJob(jobKey, cancellationToken);
        await scheduler.ScheduleJob(job, trigger, cancellationToken);
    }

    public static async Task DeleteAsync(IScheduler scheduler, Guid definitionId, CancellationToken cancellationToken)
    {
        await scheduler.DeleteJob(BuildJobKey(definitionId), cancellationToken);
    }

    private static JobKey BuildJobKey(Guid definitionId)
        => new($"CronJob-{definitionId}", WorkflowCronGroup);

    private static TriggerKey BuildTriggerKey(Guid definitionId)
        => new($"Trigger-{definitionId}", WorkflowCronGroup);

    private static Type? ResolveWorkflowCronJobType()
    {
        var resolved = Type.GetType(WorkflowCronJobTypeName, throwOnError: false);
        if (resolved is not null)
        {
            return resolved;
        }

        return AppDomain.CurrentDomain
            .GetAssemblies()
            .Select(x => x.GetType("AWE.WorkflowEngine.Jobs.WorkflowCronJob", throwOnError: false, ignoreCase: false))
            .FirstOrDefault(x => x is not null);
    }

    private static CronConfig? ExtractCronConfig(JsonDocument definitionJson)
    {
        if (!definitionJson.RootElement.TryGetProperty("Steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var step in steps.EnumerateArray())
        {
            if (!TryGetStringProperty(step, "Type", out var type)
                || (!string.Equals(type, "CronTrigger", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "CronTriggerPlugin", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!TryGetPropertyIgnoreCase(step, "Inputs", out var inputs)
                || inputs.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryGetStringProperty(inputs, "CronExpression", out var cronExpression)
                || string.IsNullOrWhiteSpace(cronExpression))
            {
                continue;
            }

            TryGetStringProperty(inputs, "TimeZone", out var timeZone);
            if (string.IsNullOrWhiteSpace(timeZone))
            {
                TryGetStringProperty(inputs, "TimeZoneId", out timeZone);
            }

            return new CronConfig(cronExpression.Trim(), string.IsNullOrWhiteSpace(timeZone) ? null : timeZone.Trim());
        }

        return null;
    }

    private static TimeZoneInfo ResolveTimeZoneInfo(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        value = null;

        if (!TryGetPropertyIgnoreCase(element, propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return true;
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement propertyValue)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    propertyValue = property.Value;
                    return true;
                }
            }
        }

        propertyValue = default;
        return false;
    }

    private sealed record CronConfig(string CronExpression, string? TimeZoneId);
}
