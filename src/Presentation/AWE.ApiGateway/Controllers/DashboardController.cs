using AWE.Application.UseCases.Monitor.Daskboard;
using AWE.Domain.Enums;
using AWE.Infrastructure.Persistence;
using AWE.Shared.Consts;
using AWE.Shared.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AWE.ApiGateway.Controllers;

[Route("api/dashboard")]
[Authorize(Policy = AppPolicies.RequireOperator)]
[AllowAnonymous]
public class DashboardController : ApiController
{
    /// <summary>
    /// Lấy toàn bộ số liệu thống kê cho màn hình Dashboard
    /// </summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics([FromServices] IGetDashboardMetricsQueryHandler handler , CancellationToken cancellationToken)
    {
        Result<DashboardMetricsResponse> result = await handler.HandleAsync(cancellationToken);

        return HandleResult(result);
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? timezone,
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var (fromUtc, toUtc, tz) = ResolveTimeRange(from, to, timezone, TimeSpan.FromDays(30));

        var scopedInstances = dbContext.WorkflowInstances
            .AsNoTracking()
            .Where(x => x.CreatedAt >= fromUtc && x.CreatedAt <= toUtc);

        var totalExecutions = await scopedInstances.CountAsync(cancellationToken);
        var completedExecutions = await scopedInstances.CountAsync(x => x.Status == WorkflowInstanceStatus.Completed, cancellationToken);
        var failedExecutions = await scopedInstances.CountAsync(x => x.Status == WorkflowInstanceStatus.Failed || x.Status == WorkflowInstanceStatus.Compensating, cancellationToken);
        var runningNow = await dbContext.WorkflowInstances.AsNoTracking().CountAsync(x => x.Status == WorkflowInstanceStatus.Running, cancellationToken);

        var activeWorkflows = await dbContext.WorkflowDefinitions.AsNoTracking().CountAsync(x => x.IsPublished, cancellationToken);
        var totalWorkflows = await dbContext.WorkflowDefinitions.AsNoTracking().CountAsync(cancellationToken);

        var durationRows = await scopedInstances
            .Where(x => x.EndTime != null)
            .Select(x => new { x.StartTime, EndTime = x.EndTime!.Value })
            .ToListAsync(cancellationToken);

        var durationMinutes = durationRows
            .Select(x => (x.EndTime - x.StartTime).TotalMinutes)
            .Where(x => x >= 0)
            .OrderBy(x => x)
            .ToList();

        var avgDuration = durationMinutes.Count == 0 ? 0 : durationMinutes.Average();
        var p95Duration = Percentile(durationMinutes, 0.95);
        var successRate = totalExecutions == 0 ? 0 : Math.Round(completedExecutions * 100d / totalExecutions, 2);
        var failureRate = totalExecutions == 0 ? 0 : Math.Round(failedExecutions * 100d / totalExecutions, 2);

        var response = new DashboardOverviewResponse(
            fromUtc,
            toUtc,
            tz.Id,
            totalWorkflows,
            activeWorkflows,
            totalExecutions,
            runningNow,
            completedExecutions,
            failedExecutions,
            successRate,
            failureRate,
            Math.Round(avgDuration, 2),
            Math.Round(p95Duration, 2));

        return HandleResult(Result.Success(response));
    }

    [HttpGet("trends")]
    public async Task<IActionResult> GetTrends(
        [FromQuery] string? metric,
        [FromQuery] string? granularity,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? timezone,
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var normalizedMetric = string.IsNullOrWhiteSpace(metric) ? "throughput" : metric.Trim().ToLowerInvariant();
        var normalizedGranularity = string.IsNullOrWhiteSpace(granularity) ? "day" : granularity.Trim().ToLowerInvariant();
        if (normalizedMetric is not ("throughput" or "successrate" or "duration"))
        {
            return HandleFailure(Error.Validation("Dashboard.Trends.Metric.Invalid", "metric chỉ hỗ trợ: throughput | successRate | duration"));
        }

        if (normalizedGranularity is not ("hour" or "day"))
        {
            return HandleFailure(Error.Validation("Dashboard.Trends.Granularity.Invalid", "granularity chỉ hỗ trợ: hour | day"));
        }

        var (fromUtc, toUtc, tz) = ResolveTimeRange(from, to, timezone, TimeSpan.FromDays(30));
        var rawRows = await dbContext.WorkflowInstances
            .AsNoTracking()
            .Where(x => x.CreatedAt >= fromUtc && x.CreatedAt <= toUtc)
            .Select(x => new TrendRawRow(x.CreatedAt, x.StartTime, x.EndTime, x.Status))
            .ToListAsync(cancellationToken);

        var points = rawRows
            .GroupBy(x => FloorByGranularity(TimeZoneInfo.ConvertTimeFromUtc(x.CreatedAt, tz), normalizedGranularity))
            .OrderBy(x => x.Key)
            .Select(g =>
            {
                var throughput = g.Count();
                var completed = g.Count(x => x.Status == WorkflowInstanceStatus.Completed);
                var durations = g.Where(x => x.EndTime.HasValue)
                    .Select(x => (x.EndTime!.Value - x.StartTime).TotalMinutes)
                    .Where(x => x >= 0)
                    .ToList();

                var value = normalizedMetric switch
                {
                    "throughput" => throughput,
                    "successrate" => throughput == 0 ? 0 : Math.Round(completed * 100d / throughput, 2),
                    "duration" => durations.Count == 0 ? 0 : Math.Round(durations.Average(), 2),
                    _ => 0
                };

                return new DashboardTrendPoint(g.Key, value);
            })
            .ToList();

        var response = new DashboardTrendsResponse(fromUtc, toUtc, tz.Id, normalizedMetric, normalizedGranularity, points);
        return HandleResult(Result.Success(response));
    }

    [HttpGet("failures/top")]
    public async Task<IActionResult> GetTopFailures(
        [FromQuery] int? limit,
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var cappedLimit = Math.Clamp(limit ?? 10, 1, 50);
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        var topFailureRows = await dbContext.WorkflowInstances
            .AsNoTracking()
            .Where(x => x.CreatedAt >= thirtyDaysAgo
                     && (x.Status == WorkflowInstanceStatus.Failed || x.Status == WorkflowInstanceStatus.Compensating))
            .Join(
                dbContext.WorkflowDefinitions.AsNoTracking(),
                instance => instance.DefinitionId,
                definition => definition.Id,
                (instance, definition) => new { instance, definition.Name })
            .GroupBy(x => new { x.instance.DefinitionId, x.Name })
            .Select(g => new
            {
                g.Key.DefinitionId,
                DefinitionName = g.Key.Name,
                FailureCount = g.Count(),
                LastFailureAt = g.Max(x => x.instance.CreatedAt)
            })
            .OrderByDescending(x => x.FailureCount)
            .ThenByDescending(x => x.LastFailureAt)
            .Take(cappedLimit)
            .ToListAsync(cancellationToken);

        var topFailures = topFailureRows
            .Select(x => new DashboardTopFailureItem(
                x.DefinitionId,
                x.DefinitionName,
                x.FailureCount,
                x.LastFailureAt))
            .ToList();

        return HandleResult(Result.Success(topFailures));
    }

    [HttpGet("live")]
    [Produces("text/event-stream")]
    public async Task GetLive(
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";
        Response.ContentType = "text/event-stream";

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var now = DateTime.UtcNow;
                var payload = new DashboardLiveSnapshot(
                    now,
                    await dbContext.WorkflowInstances.AsNoTracking().CountAsync(x => x.Status == WorkflowInstanceStatus.Running, cancellationToken),
                    await dbContext.ExecutionPointers.AsNoTracking().CountAsync(x => x.Status == ExecutionPointerStatus.Pending, cancellationToken),
                    await dbContext.WorkflowInstances.AsNoTracking().CountAsync(x => x.CreatedAt >= now.AddHours(-1) && x.Status == WorkflowInstanceStatus.Failed, cancellationToken));

                await Response.WriteAsync($"event: dashboard\n", cancellationToken);
                await Response.WriteAsync($"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);

                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    [HttpGet("workers/health")]
    public async Task<IActionResult> GetWorkersHealth(
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var rows = await dbContext.ExecutionLogs
            .AsNoTracking()
            .Where(x => !string.IsNullOrWhiteSpace(x.WorkerId) && x.CreatedAt >= now.AddDays(-1))
            .GroupBy(x => x.WorkerId!)
            .Select(g => new
            {
                WorkerId = g.Key,
                LastSeenAt = g.Max(x => x.CreatedAt),
                ErrorCountLast15m = g.Count(x => x.Level == AWE.Domain.Enums.LogLevel.Error && x.CreatedAt >= now.AddMinutes(-15))
            })
            .OrderByDescending(x => x.LastSeenAt)
            .ToListAsync(cancellationToken);

        var response = rows.Select(x => new WorkerHealthItem(
            x.WorkerId,
            x.LastSeenAt,
            x.LastSeenAt >= now.AddMinutes(-2) ? "Healthy" : "Stale",
            x.ErrorCountLast15m)).ToList();

        return HandleResult(Result.Success(response));
    }

    [HttpGet("queues/health")]
    public async Task<IActionResult> GetQueuesHealth(
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var pendingPointers = await dbContext.ExecutionPointers.AsNoTracking().CountAsync(x => x.Status == ExecutionPointerStatus.Pending, cancellationToken);
        var runningPointers = await dbContext.ExecutionPointers.AsNoTracking().CountAsync(x => x.Status == ExecutionPointerStatus.Running, cancellationToken);
        var suspendedPointers = await dbContext.ExecutionPointers.AsNoTracking().CountAsync(x => x.Status == ExecutionPointerStatus.Suspended, cancellationToken);
        var outboxBacklog = await dbContext.OutboxMessages.AsNoTracking().CountAsync(cancellationToken);

        var response = new QueueHealthResponse(pendingPointers, runningPointers, suspendedPointers, outboxBacklog);
        return HandleResult(Result.Success(response));
    }

    [HttpGet("scheduler/health")]
    public async Task<IActionResult> GetSchedulerHealth(
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var activeSchedules = await dbContext.WorkflowSchedules.AsNoTracking().CountAsync(x => x.IsActive, cancellationToken);
        var pendingSyncTasks = await dbContext.WorkflowSchedulerSyncTasks.AsNoTracking().CountAsync(x => !x.IsCompleted, cancellationToken);
        var failedSyncTasks = await dbContext.WorkflowSchedulerSyncTasks.AsNoTracking().CountAsync(x => !x.IsCompleted && x.LastError != null, cancellationToken);
        var overdueSyncTasks = await dbContext.WorkflowSchedulerSyncTasks.AsNoTracking().CountAsync(x => !x.IsCompleted && x.NextAttemptAtUtc < now.AddMinutes(-1), cancellationToken);

        var response = new SchedulerHealthResponse(activeSchedules, pendingSyncTasks, failedSyncTasks, overdueSyncTasks);
        return HandleResult(Result.Success(response));
    }

    [HttpGet("webhooks/health")]
    public async Task<IActionResult> GetWebhooksHealth(
        [FromServices] ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var last24h = now.AddHours(-24);

        var activeRoutes = await dbContext.WebhookRoutes.AsNoTracking().CountAsync(x => x.IsActive, cancellationToken);
        var idempotencyEnabledRoutes = await dbContext.WebhookRoutes.AsNoTracking()
            .CountAsync(x => x.IsActive && x.IdempotencyKeyPath != null && x.IdempotencyKeyPath != string.Empty, cancellationToken);

        var triggeredExecutions24h = await dbContext.WorkflowInstances.AsNoTracking()
            .CountAsync(x => x.CreatedAt >= last24h && x.IdempotencyKey != null, cancellationToken);

        var response = new WebhookHealthResponse(activeRoutes, idempotencyEnabledRoutes, triggeredExecutions24h);
        return HandleResult(Result.Success(response));
    }

    //[HttpGet("anomalies")]
    //public async Task<IActionResult> GetAnomalies(
    //    [FromServices] ApplicationDbContext dbContext,
    //    CancellationToken cancellationToken)
    //{
    //    var today = DateTime.UtcNow.Date;
    //    var fromUtc = today.AddDays(-14);

    //    var rows = await dbContext.WorkflowInstances
    //        .AsNoTracking()
    //        .Where(x => x.CreatedAt >= fromUtc)
    //        .GroupBy(x => x.CreatedAt.Date)
    //        .Select(g => new
    //        {
    //            Date = g.Key,
    //            Throughput = g.Count(),
    //            Failures = g.Count(x => x.Status == WorkflowInstanceStatus.Failed || x.Status == WorkflowInstanceStatus.Compensating)
    //        })
    //        .OrderBy(x => x.Date)
    //        .ToListAsync(cancellationToken);

    //    if (rows.Count == 0)
    //    {
    //        return HandleResult(Result.Success(new List<DashboardAnomalyItem>()));
    //    }

    //    var avg = rows.Average(x => (double)x.Throughput);
    //    var variance = rows.Average(x => Math.Pow(x.Throughput - avg, 2));
    //    var stdDev = Math.Sqrt(variance);
    //    var threshold = avg + (2 * stdDev);

    //    var anomalies = rows
    //        .Where(x => x.Throughput > threshold || (x.Throughput > 0 && x.Failures * 100d / x.Throughput >= 35))
    //        .Select(x => new DashboardAnomalyItem(
    //            x.Date,
    //            x.Throughput,
    //            x.Failures,
    //            x.Throughput > threshold ? "ThroughputSpike" : "FailureRateSpike"))
    //        .ToList();

    //    return HandleResult(Result.Success(anomalies));
    //}

    //[HttpGet("capacity-forecast")]
    //public async Task<IActionResult> GetCapacityForecast(
    //    [FromQuery] int? days,
    //    [FromServices] ApplicationDbContext dbContext,
    //    CancellationToken cancellationToken)
    //{
    //    var forecastDays = Math.Clamp(days ?? 7, 1, 30);
    //    var fromUtc = DateTime.UtcNow.Date.AddDays(-30);

    //    var history = await dbContext.WorkflowInstances
    //        .AsNoTracking()
    //        .Where(x => x.CreatedAt >= fromUtc)
    //        .GroupBy(x => x.CreatedAt.Date)
    //        .AsEnumerable()
    //        .Select(g => new ForecastHistoryRow(g.Key, g.Count()))
    //        .OrderBy(x => x.Date)
    //        .ToListAsync();

    //    var response = BuildForecast(history, forecastDays);
    //    return HandleResult(Result.Success(response));
    //}

    private static (DateTime fromUtc, DateTime toUtc, TimeZoneInfo timezone) ResolveTimeRange(
        DateTime? from,
        DateTime? to,
        string? timezone,
        TimeSpan defaultWindow)
    {
        var toUtc = to?.ToUniversalTime() ?? DateTime.UtcNow;
        var fromUtc = from?.ToUniversalTime() ?? toUtc.Subtract(defaultWindow);

        if (fromUtc > toUtc)
        {
            (fromUtc, toUtc) = (toUtc, fromUtc);
        }

        TimeZoneInfo tz;
        try
        {
            tz = string.IsNullOrWhiteSpace(timezone)
                ? TimeZoneInfo.Utc
                : TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch
        {
            tz = TimeZoneInfo.Utc;
        }

        return (fromUtc, toUtc, tz);
    }

    private static DateTime FloorByGranularity(DateTime value, string granularity)
    {
        return granularity == "hour"
            ? new DateTime(value.Year, value.Month, value.Day, value.Hour, 0, 0, DateTimeKind.Unspecified)
            : value.Date;
    }

    private static double Percentile(IReadOnlyList<double> sortedData, double percentile)
    {
        if (sortedData.Count == 0)
        {
            return 0;
        }

        var position = (sortedData.Count - 1) * percentile;
        var left = (int)Math.Floor(position);
        var right = (int)Math.Ceiling(position);

        if (left == right)
        {
            return sortedData[left];
        }

        var fraction = position - left;
        return sortedData[left] + (sortedData[right] - sortedData[left]) * fraction;
    }

    private static CapacityForecastResponse BuildForecast(
        IReadOnlyList<ForecastHistoryRow> history,
        int forecastDays)
    {
        if (history.Count == 0)
        {
            return new CapacityForecastResponse(Array.Empty<CapacityForecastPoint>(), Array.Empty<CapacityForecastPoint>());
        }

        var historyPoints = history
            .Select(x => new CapacityForecastPoint(x.Date, x.Count))
            .ToList();

        var n = historyPoints.Count;
        var xAvg = (n - 1) / 2d;
        var yAvg = historyPoints.Average(x => x.EstimatedExecutions);

        double numerator = 0;
        double denominator = 0;

        for (var i = 0; i < n; i++)
        {
            var dx = i - xAvg;
            numerator += dx * (historyPoints[i].EstimatedExecutions - yAvg);
            denominator += dx * dx;
        }

        var slope = denominator == 0 ? 0 : numerator / denominator;
        var intercept = yAvg - slope * xAvg;
        var startDate = historyPoints[^1].Date;

        var forecast = Enumerable.Range(1, forecastDays)
            .Select(i =>
            {
                var index = n - 1 + i;
                var estimate = Math.Max(0, intercept + slope * index);
                return new CapacityForecastPoint(startDate.AddDays(i), Math.Round(estimate, 2));
            })
            .ToList();

        return new CapacityForecastResponse(historyPoints, forecast);
    }
}

public record DashboardOverviewResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    string Timezone,
    int TotalWorkflows,
    int ActiveWorkflows,
    int TotalExecutions,
    int RunningNow,
    int CompletedExecutions,
    int FailedExecutions,
    double SuccessRate,
    double FailureRate,
    double AverageDurationMinutes,
    double P95DurationMinutes);

public record DashboardTrendPoint(DateTime Bucket, double Value);

public record DashboardTrendsResponse(
    DateTime FromUtc,
    DateTime ToUtc,
    string Timezone,
    string Metric,
    string Granularity,
    IReadOnlyList<DashboardTrendPoint> Points);

public record DashboardTopFailureItem(Guid DefinitionId, string DefinitionName, int FailureCount, DateTime LastFailureAt);

public record DashboardLiveSnapshot(DateTime Timestamp, int RunningInstances, int PendingPointers, int FailedLastHour);

public record WorkerHealthItem(string WorkerId, DateTime LastSeenAt, string Status, int ErrorCountLast15m);

public record QueueHealthResponse(int PendingPointers, int RunningPointers, int SuspendedPointers, int OutboxBacklog);

public record SchedulerHealthResponse(int ActiveSchedules, int PendingSyncTasks, int FailedSyncTasks, int OverdueSyncTasks);

public record WebhookHealthResponse(int ActiveRoutes, int IdempotencyEnabledRoutes, int TriggeredExecutionsLast24h);

public record DashboardAnomalyItem(DateTime Date, int Throughput, int Failures, string Type);

public record CapacityForecastPoint(DateTime Date, double EstimatedExecutions);

public record CapacityForecastResponse(IReadOnlyList<CapacityForecastPoint> History, IReadOnlyList<CapacityForecastPoint> Forecast);

internal record TrendRawRow(DateTime CreatedAt, DateTime StartTime, DateTime? EndTime, WorkflowInstanceStatus Status);
internal record ForecastHistoryRow(DateTime Date, int Count);
