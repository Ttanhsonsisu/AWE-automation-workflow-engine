using AWE.Application.Abstractions.Persistence;
using AWE.Application.UseCases.Workflows;
using AWE.Domain.Enums;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace AWE.WorkflowEngine.BackgroundServices;

public class WorkflowSchedulerSyncReconcilerService(
    IServiceProvider serviceProvider,
    ISchedulerFactory schedulerFactory,
    IDistributedLockProvider lockProvider,
    ILogger<WorkflowSchedulerSyncReconcilerService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ISchedulerFactory _schedulerFactory = schedulerFactory;
    private readonly IDistributedLockProvider _lockProvider = lockProvider;
    private readonly ILogger<WorkflowSchedulerSyncReconcilerService> _logger = logger;
    private const string ReconcilerLockName = "workflow-scheduler-sync-reconciler";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (!stoppingToken.IsCancellationRequested
               && await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ReconcileOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Workflow scheduler sync reconciler failed.");
            }
        }
    }

    private async Task ReconcileOnceAsync(CancellationToken cancellationToken)
    {
        await using var lockHandle = await _lockProvider.TryAcquireLockAsync(
            ReconcilerLockName,
            TimeSpan.FromSeconds(8),
            cancellationToken: cancellationToken);

        if (lockHandle is null)
        {
            _logger.LogDebug("Skip scheduler reconcile tick because lock '{LockName}' is currently held by another host.", ReconcilerLockName);
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var taskRepository = scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerSyncTaskRepository>();
        var definitionRepository = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();
        var scheduleRepository = scope.ServiceProvider.GetRequiredService<IWorkflowScheduleRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);

        var tasks = await taskRepository.GetDueTasksAsync(DateTime.UtcNow, take: 50, cancellationToken);
        if (tasks.Count == 0)
        {
            return;
        }

        foreach (var task in tasks)
        {
            try
            {
                var definition = await definitionRepository.GetDefinitionByIdAsync(task.DefinitionId, cancellationToken);
                var legacySchedules = await scheduleRepository
                    .GetActiveSchedulesByDefinitionIdAsync(task.DefinitionId, cancellationToken);

                await CronScheduleSyncHelper.ProjectDefinitionToQuartzAsync(
                    scheduler,
                    task.DefinitionId,
                    definition,
                    legacySchedules,
                    cancellationToken);

                task.MarkSucceeded();
            }
            catch (Exception ex)
            {
                task.MarkFailed(ex.Message);
                _logger.LogWarning(
                    ex,
                    "Scheduler sync task failed. TaskId={TaskId}, DefinitionId={DefinitionId}, Operation={Operation}, RetryCount={RetryCount}",
                    task.Id,
                    task.DefinitionId,
                    task.Operation,
                    task.RetryCount);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
