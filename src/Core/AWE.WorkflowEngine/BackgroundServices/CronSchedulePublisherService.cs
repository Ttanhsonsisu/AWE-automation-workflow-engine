using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Infrastructure.Persistence;
using Cronos;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace AWE.WorkflowEngine.BackgroundServices;

public class CronSchedulePublisherService(IServiceProvider serviceProvider, ILogger<CronSchedulePublisherService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<CronSchedulePublisherService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Cron Schedule Publisher started.");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessSchedulesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while processing cron schedules.");
            }
        }
    }

    private async Task ProcessSchedulesAsync(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var publishEndpoint = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();

        var now = DateTime.UtcNow;

        var dueSchedules = await dbContext.Set<WorkflowSchedule>()
            .Where(x => x.IsActive && x.NextRunAt != null && x.NextRunAt <= now)
            .ToListAsync(stoppingToken);

        if (!dueSchedules.Any())
        {
            return;
        }

        foreach (var schedule in dueSchedules)
        {
            try
            {
                var expression = CronExpression.Parse(schedule.CronExpression, CronFormat.Standard);
                var nextRun = expression.GetNextOccurrence(now);

                schedule.LastRunAt = now;
                schedule.NextRunAt = nextRun;
                schedule.Version = Guid.NewGuid();

                var inputPayload = JsonSerializer.Serialize(new
                {
                    Trigger = new
                    {
                        Source = WorkflowTriggerSource.Cron.ToString(),
                        ScheduleId = schedule.Id,
                        CronExpression = schedule.CronExpression,
                        FiredAtUtc = now,
                        NextRunAtUtc = nextRun
                    },
                    Workflow = new
                    {
                        DefinitionId = schedule.DefinitionId
                    }
                });

                var command = new SubmitWorkflowCommand(
                    DefinitionId: schedule.DefinitionId,
                    JobName: $"CronTrigger-{schedule.Id}-{now:yyyyMMddHHmm}",
                    InputData: inputPayload,
                    CorrelationId: Guid.NewGuid(),
                    TriggerSource: WorkflowTriggerSource.Cron
                );

                await publishEndpoint.Publish(command, stoppingToken);
                await dbContext.SaveChangesAsync(stoppingToken);

                _logger.LogInformation(
                    "Triggered Workflow {DefinitionId} via Cron schedule {ScheduleId}. Next run: {NextRun}",
                    schedule.DefinitionId,
                    schedule.Id,
                    nextRun);
            }
            catch (DbUpdateConcurrencyException)
            {
                _logger.LogDebug("Concurrency lock hit for schedule {ScheduleId}. Another worker processed it.", schedule.Id);
                dbContext.Entry(schedule).State = EntityState.Detached;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process schedule {ScheduleId}", schedule.Id);
            }
        }
    }
}
