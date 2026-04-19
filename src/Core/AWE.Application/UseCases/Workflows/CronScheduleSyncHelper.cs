using System.Text.Json;
using AWE.Domain.Entities;
using Cronos;
using Quartz;
using Quartz.Impl.Matchers;
using TimeZoneConverter;

namespace AWE.Application.UseCases.Workflows;

public static class CronScheduleSyncHelper
{
    public const string WorkflowCronJobTypeName = "AWE.WorkflowEngine.Jobs.WorkflowCronJob, AWE.WorkflowEngine";
    private const string WorkflowCronGroupPrefix = "workflow:";
    private const string DefinitionIdJobDataKey = "DefinitionId";
    private const string StepIdJobDataKey = "StepId";
    private const string CronExpressionJobDataKey = "CronExpression";
    private const string TimeZoneIdJobDataKey = "TimeZoneId";

    public static async Task SyncAsync(
        IScheduler scheduler,
        Guid definitionId,
        JsonDocument definitionJson,
        CancellationToken cancellationToken)
    {
        var cronTriggers = ExtractCronTriggers(definitionJson).ToList();
        var group = BuildDefinitionGroup(definitionId);
        var existingJobs = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group), cancellationToken);
        foreach (var existing in existingJobs)
        {
            await scheduler.DeleteJob(existing, cancellationToken);
        }

        if (cronTriggers.Count == 0)
        {
            return;
        }

        var jobType = ResolveWorkflowCronJobType();
        if (jobType is null)
        {
            throw new InvalidOperationException(
                $"Không tìm thấy Quartz job type '{WorkflowCronJobTypeName}'. Vui lòng đảm bảo assembly AWE.WorkflowEngine được load.");
        }

        foreach (var cronTrigger in cronTriggers)
        {
            var jobKey = BuildStepJobKey(definitionId, cronTrigger.StepId);
            var triggerKey = BuildStepTriggerKey(definitionId, cronTrigger.StepId);

            await ScheduleOneShotAsync(
                scheduler,
                jobType,
                jobKey,
                triggerKey,
                definitionId,
                cronTrigger.StepId,
                cronTrigger.CronExpression,
                cronTrigger.TimeZoneId,
                cancellationToken);
        }
    }

    public static async Task ProjectDefinitionToQuartzAsync(
        IScheduler scheduler,
        Guid definitionId,
        WorkflowDefinition? definition,
        IReadOnlyList<WorkflowSchedule>? legacySchedules,
        CancellationToken cancellationToken)
    {
        if (definition is null || !definition.IsPublished)
        {
            await DeleteAsync(scheduler, definitionId, cancellationToken);
            return;
        }

        if (HasEmbeddedCronTrigger(definition.DefinitionJson))
        {
            await SyncAsync(scheduler, definitionId, definition.DefinitionJson, cancellationToken);
            return;
        }

        var schedules = (legacySchedules ?? [])
            .Where(x => x.IsActive && x.DefinitionId == definitionId)
            .ToList();

        if (schedules.Count == 0)
        {
            await DeleteAsync(scheduler, definitionId, cancellationToken);
            return;
        }

        await DeleteAsync(scheduler, definitionId, cancellationToken);

        foreach (var schedule in schedules)
        {
            await SyncLegacyScheduleAsync(scheduler, schedule, cancellationToken);
        }
    }

    public static async Task SyncLegacyScheduleAsync(
        IScheduler scheduler,
        WorkflowSchedule schedule,
        CancellationToken cancellationToken)
    {
        if (!schedule.IsActive)
        {
            return;
        }

        var jobType = ResolveWorkflowCronJobType();
        if (jobType is null)
        {
            throw new InvalidOperationException(
                $"Không tìm thấy Quartz job type '{WorkflowCronJobTypeName}'. Vui lòng đảm bảo assembly AWE.WorkflowEngine được load.");
        }

        var jobKey = BuildLegacyJobKey(schedule.DefinitionId, schedule.Id);
        var triggerKey = BuildLegacyTriggerKey(schedule.DefinitionId, schedule.Id);

        await scheduler.DeleteJob(jobKey, cancellationToken);
        await ScheduleOneShotAsync(
            scheduler,
            jobType,
            jobKey,
            triggerKey,
            schedule.DefinitionId,
            triggerStepId: null,
            schedule.CronExpression,
            schedule.TimeZoneId,
            cancellationToken);
    }

    public static async Task DeleteAsync(IScheduler scheduler, Guid definitionId, CancellationToken cancellationToken)
    {
        var group = BuildDefinitionGroup(definitionId);
        var jobKeys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(group), cancellationToken);
        foreach (var jobKey in jobKeys)
        {
            await scheduler.DeleteJob(jobKey, cancellationToken);
        }
    }

    public static bool HasEmbeddedCronTrigger(JsonDocument definitionJson)
    {
        return ExtractCronTriggers(definitionJson).Any();
    }

    private static async Task ScheduleOneShotAsync(
        IScheduler scheduler,
        Type jobType,
        JobKey jobKey,
        TriggerKey triggerKey,
        Guid definitionId,
        string? triggerStepId,
        string cronExpression,
        string? timeZoneId,
        CancellationToken cancellationToken)
    {
        var timeZone = ResolveTimeZoneInfo(timeZoneId);
        var nextRunAtUtc = GetNextRunAtUtc(cronExpression, timeZone, triggerStepId, out var normalizedCronExpression);
        if (nextRunAtUtc is null)
        {
            return;
        }

        var job = JobBuilder.Create(jobType)
            .WithIdentity(jobKey)
            .UsingJobData(DefinitionIdJobDataKey, definitionId.ToString())
            .UsingJobData(StepIdJobDataKey, triggerStepId)
            .UsingJobData(CronExpressionJobDataKey, normalizedCronExpression)
            .UsingJobData(TimeZoneIdJobDataKey, timeZoneId)
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartAt(new DateTimeOffset(DateTime.SpecifyKind(nextRunAtUtc.Value, DateTimeKind.Utc)))
            .Build();

        await scheduler.ScheduleJob(job, trigger, cancellationToken);
    }

    public static string? ValidateCronExpressions(JsonDocument definitionJson)
    {
        foreach (var cronTrigger in ExtractCronTriggers(definitionJson))
        {
            var timeZone = ResolveTimeZoneInfo(cronTrigger.TimeZoneId);
            if (!TryGetNextRunAtUtc(
                    cronTrigger.CronExpression,
                    timeZone,
                    DateTime.UtcNow,
                    out _,
                    out _,
                    out var errorMessage))
            {
                return $"CronExpression không hợp lệ tại step '{cronTrigger.StepId}': {errorMessage}";
            }
        }

        return null;
    }

    public static bool TryGetNextRunAtUtc(
        string cronExpression,
        TimeZoneInfo timeZone,
        DateTime fromUtc,
        out DateTime? nextRunAtUtc,
        out string normalizedCronExpression,
        out string? errorMessage)
    {
        nextRunAtUtc = null;
        normalizedCronExpression = string.Empty;

        if (!TryParseCronExpression(cronExpression, out var parsedExpression, out normalizedCronExpression, out errorMessage))
        {
            return false;
        }

        nextRunAtUtc = parsedExpression.GetNextOccurrence(fromUtc, timeZone);
        return true;
    }

    private static DateTime? GetNextRunAtUtc(
        string cronExpression,
        TimeZoneInfo timeZone,
        string? stepId,
        out string normalizedCronExpression)
    {
        if (!TryGetNextRunAtUtc(
                cronExpression,
                timeZone,
                DateTime.UtcNow,
                out var nextRunAtUtc,
                out normalizedCronExpression,
                out var errorMessage))
        {
            throw new InvalidOperationException(
                $"CronExpression không hợp lệ tại step '{stepId ?? "(legacy)"}': {errorMessage}");
        }

        return nextRunAtUtc;
    }

    private static bool TryParseCronExpression(
        string cronExpression,
        out Cronos.CronExpression parsedExpression,
        out string normalizedCronExpression,
        out string? errorMessage)
    {
        parsedExpression = null!;
        normalizedCronExpression = string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            errorMessage = "giá trị rỗng.";
            return false;
        }

        var segments = cronExpression.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        CronFormat format;

        switch (segments.Length)
        {
            case 5:
                normalizedCronExpression = string.Join(' ', segments);
                format = CronFormat.Standard;
                break;
            case 6:
                normalizedCronExpression = string.Join(' ', segments.Select(NormalizeQuartzSegment));
                format = CronFormat.IncludeSeconds;
                break;
            default:
                errorMessage =
                    $"'{cronExpression}' không đúng định dạng. Hệ thống chỉ hỗ trợ cron 5-field hoặc 6-field có giây; chưa hỗ trợ year field.";
                return false;
        }

        try
        {
            parsedExpression = Cronos.CronExpression.Parse(normalizedCronExpression, format);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage =
                $"'{cronExpression}' không hợp lệ ({ex.Message}). Dùng 5-field kiểu Unix, hoặc 6-field có giây. Với biểu thức Quartz, thay '?' bằng '*'.";
            return false;
        }
    }

    private static string BuildDefinitionGroup(Guid definitionId)
        => $"{WorkflowCronGroupPrefix}{definitionId:D}";

    private static JobKey BuildStepJobKey(Guid definitionId, string stepId)
        => new($"CronJob-{SanitizeKeySegment(stepId)}", BuildDefinitionGroup(definitionId));

    private static TriggerKey BuildStepTriggerKey(Guid definitionId, string stepId)
        => new($"Trigger-{SanitizeKeySegment(stepId)}", BuildDefinitionGroup(definitionId));

    private static JobKey BuildLegacyJobKey(Guid definitionId, Guid scheduleId)
        => new($"CronJob-Legacy-{scheduleId:D}", BuildDefinitionGroup(definitionId));

    private static TriggerKey BuildLegacyTriggerKey(Guid definitionId, Guid scheduleId)
        => new($"Trigger-Legacy-{scheduleId:D}", BuildDefinitionGroup(definitionId));

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

    private static IEnumerable<CronTriggerConfig> ExtractCronTriggers(JsonDocument definitionJson)
    {
        if (!definitionJson.RootElement.TryGetProperty("Steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var step in steps.EnumerateArray())
        {
            if (!TryGetStringProperty(step, "Type", out var type)
                || (!string.Equals(type, "CronTrigger", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(type, "CronTriggerPlugin", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (!TryGetStringProperty(step, "Id", out var stepId)
                || string.IsNullOrWhiteSpace(stepId))
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

            if (!TryGetStringProperty(inputs, "TimeZoneId", out var timeZoneId))
            {
                timeZoneId = null;
            }

            yield return new CronTriggerConfig(
                StepId: stepId.Trim(),
                CronExpression: cronExpression.Trim(),
                TimeZoneId: string.IsNullOrWhiteSpace(timeZoneId) ? null : timeZoneId.Trim());
        }
    }

    private static string SanitizeKeySegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return string.Concat(value.Trim().Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));
    }

    private static string NormalizeQuartzSegment(string segment)
        => string.Equals(segment, "?", StringComparison.Ordinal) ? "*" : segment;

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

    private sealed record CronTriggerConfig(string StepId, string CronExpression, string? TimeZoneId);
}
