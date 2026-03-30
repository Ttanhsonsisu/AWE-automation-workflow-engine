using AWE.Infrastructure.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.BackgroundServices;

public class PluginGarbageCollectorService : BackgroundService
{
    private readonly PluginCacheManager _cacheManager;
    private readonly ILogger<PluginGarbageCollectorService> _logger;

    public PluginGarbageCollectorService(PluginCacheManager cacheManager, ILogger<PluginGarbageCollectorService> logger)
    {
        _cacheManager = cacheManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🧹 Trình dọn rác Plugin (Garbage Collector) đã khởi động.");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Ngủ 1 tiếng rồi dậy dọn dẹp một lần
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);

            try
            {
                // Dọn các Plugin không có ai xài trong vòng 24 giờ qua
                _cacheManager.EvictIdlePlugins(maxIdleTime: TimeSpan.FromHours(24));

                // Ép rác toàn hệ thống (tùy chọn)
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Lỗi khi chạy Garbage Collector: {Message}", ex.Message);
            }
        }
    }
}
