using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Enums;
using AWE.WorkflowEngine.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.BackgroundServices;

public class DelayWakeUpBackgroundService(IServiceProvider serviceProvider, ILogger<DelayWakeUpBackgroundService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<DelayWakeUpBackgroundService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Delay Wake-up Service started.");

        // Cứ 10 giây quét Database 1 lần
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var executeRepo = scope.ServiceProvider.GetRequiredService<IExecutionPointerRepository>();
                var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkflowOrchestrator>();

                // Tìm tất cả các Node đang ngủ và ĐÃ ĐẾN GIỜ THỨC DẬY
                var now = DateTime.UtcNow;

                // Lưu ý: Bạn cần thêm hàm này vào IExecutionPointerRepository
                // Query: Status == WaitingForEvent && ResumeAt != null && ResumeAt <= now
                var expiredPointers = await executeRepo.GetExpiredWaitingForEventAsync(now, stoppingToken);

                foreach (var pointer in expiredPointers)
                {
                    _logger.LogInformation("Time's up! Waking up Pointer {PointerId}", pointer.Id);

                    // Xóa báo thức để tránh đánh thức lại lần sau
                    //pointer.ResumeAt = null;

                    // Giả lập payload trả về cho Node "Delay" (Bạn có thể tùy chỉnh theo nhu cầu, hoặc thậm chí để trống nếu không cần)
                    var wakeupPayload = JsonDocument.Parse($"{{\"Message\": \"Woke up successfully at {now:O}\"}}");

                    await orchestrator.ResumeStepAsync(pointer.Id, wakeupPayload);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔥 Error in Delay Wake-up Service.");
            }
        }
    }
}
