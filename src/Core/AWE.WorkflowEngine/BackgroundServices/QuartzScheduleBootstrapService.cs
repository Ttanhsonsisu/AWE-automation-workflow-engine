using AWE.Application.Abstractions.Persistence;
using AWE.Application.UseCases.Workflows;
using AWE.Domain.Enums;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace AWE.WorkflowEngine.BackgroundServices;

public class QuartzScheduleBootstrapService(
    IServiceProvider serviceProvider,
    ISchedulerFactory schedulerFactory,
    IDistributedLockProvider lockProvider,
    ILogger<QuartzScheduleBootstrapService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
    private readonly IDistributedLockProvider _lockProvider = lockProvider;
    private readonly ILogger<QuartzScheduleBootstrapService> _logger = logger;
    private const string BootstrapLockName = "workflow-scheduler-bootstrap";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            await using var lockHandle = await _lockProvider.TryAcquireLockAsync(
                BootstrapLockName,
                TimeSpan.FromSeconds(45),
                cancellationToken: stoppingToken);

            if (lockHandle is null)
            {
                _logger.LogInformation(
                    "Skip Quartz bootstrap because lock '{LockName}' is currently held by another host.",
                    BootstrapLockName);
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var definitionRepository = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();
            var scheduleRepository = scope.ServiceProvider.GetRequiredService<IWorkflowScheduleRepository>();
            var schedulerSyncTaskRepository = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerSyncTaskRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var scheduler = await _schedulerFactory.GetScheduler(stoppingToken);

            var allDefinitions = await definitionRepository.GetAllDefinitionsAsync(stoppingToken);
            var queuedRetryCount = 0;

            foreach (var definition in allDefinitions)
            {
                try
                {
                    var legacySchedules = await scheduleRepository.GetActiveSchedulesByDefinitionIdAsync(definition.Id, stoppingToken);

                    await CronScheduleSyncHelper.ProjectDefinitionToQuartzAsync(
                        scheduler,
                        definition.Id,
                        definition,
                        legacySchedules,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Quartz bootstrap failed for definition. DefinitionId={DefinitionId}",
                        definition.Id);

                    try
                    {
                        await schedulerSyncTaskRepository.EnqueueAsync(
                            definition.Id,
                            WorkflowSchedulerSyncOperation.Publish,
                            stoppingToken);
                        queuedRetryCount++;
                    }
                    catch (Exception enqueueEx) when (enqueueEx is not OperationCanceledException)
                    {
                        _logger.LogError(
                            enqueueEx,
                            "Failed to enqueue scheduler sync retry task during bootstrap. DefinitionId={DefinitionId}",
                            definition.Id);
                    }
                }
            }

            if (queuedRetryCount > 0)
            {
                await unitOfWork.SaveChangesAsync(stoppingToken);
            }

            _logger.LogInformation(
                "Quartz cron bootstrap completed. Reconciled {DefinitionCount} definitions, queued {RetryCount} retries.",
                allDefinitions.Count,
                queuedRetryCount);
        }
        catch (OperationCanceledException)
        {
            // ignore shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Quartz cron bootstrap failed.");
        }
    }

}
