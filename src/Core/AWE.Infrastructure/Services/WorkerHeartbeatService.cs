using AWE.Domain.Entities;
using AWE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AWE.Infrastructure.Services;

public sealed class WorkerHeartbeatService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WorkerHeartbeatOptions _options;
    private readonly ILogger<WorkerHeartbeatService> _logger;
    private readonly string _workerId;
    private readonly string _machineName;
    private readonly int _processId;

    public WorkerHeartbeatService(
        IServiceProvider serviceProvider,
        WorkerHeartbeatOptions options,
        ILogger<WorkerHeartbeatService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
        _machineName = Environment.MachineName;
        _processId = Environment.ProcessId;
        _workerId = BuildWorkerId(options.WorkerType, _machineName, _processId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WriteHeartbeatAsync(stoppingToken);

        using var timer = new PeriodicTimer(_options.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await WriteHeartbeatAsync(stoppingToken);
        }
    }

    private async Task WriteHeartbeatAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var heartbeat = await dbContext.WorkerHeartbeats
                .FirstOrDefaultAsync(x => x.WorkerId == _workerId, cancellationToken);

            if (heartbeat is null)
            {
                heartbeat = new WorkerHeartbeat(
                    _workerId,
                    _options.WorkerType,
                    _machineName,
                    _processId,
                    now);

                await dbContext.WorkerHeartbeats.AddAsync(heartbeat, cancellationToken);
            }
            else
            {
                heartbeat.MarkSeen(now);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to write {WorkerType} heartbeat.", _options.WorkerType);
        }
    }

    private static string NormalizeWorkerType(string workerType)
    {
        return string.IsNullOrWhiteSpace(workerType)
            ? "worker"
            : workerType.Trim().ToLowerInvariant();
    }

    private static string BuildWorkerId(string workerType, string machineName, int processId)
    {
        var workerId = $"{NormalizeWorkerType(workerType)}-{machineName}-{processId}-{Guid.NewGuid():N}";
        return workerId.Length <= 160 ? workerId : workerId[..160];
    }
}
