using AWE.Application.Abstractions.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.BackgroundServices;

public class RecoveryBackgroundService(IServiceProvider serviceProvider, ILogger<RecoveryBackgroundService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<RecoveryBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🧟 Recovery Service Started. Hunting for zombies...");

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30)); // Quét mỗi 30s

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IExecutionPointerRepository>();

                // 1. Tìm Zombie (Running mà quá hạn)
                var zombies = await repo.GetExpiredPointersAsync(DateTime.UtcNow, 50, stoppingToken);

                if (zombies.Count > 0)
                {
                    _logger.LogWarning("🧟 Found {Count} zombie pointers. Resetting...", zombies.Count);

                    var ids = zombies.Select(x => x.Id).ToList();

                    // 2. Reset về Pending
                    await repo.ResetRawPointersAsync(ids, stoppingToken);

                    // 3. (Tuần 6) Có thể Publish Event "ZombieReset" để cảnh báo
                }
           }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in Recovery Service");
            }
        }
    }
}
