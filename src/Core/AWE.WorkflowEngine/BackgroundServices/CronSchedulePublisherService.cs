using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Infrastructure.Persistence;
using Cronos;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.BackgroundServices;

public class CronSchedulePublisherService(IServiceProvider serviceProvider, ILogger<CronSchedulePublisherService> logger) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    private readonly ILogger<CronSchedulePublisherService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🚀 Cron Schedule Publisher started.");

        // Quét mỗi 30 giây (Chuẩn cron thường có độ phân giải theo phút, 30s là an toàn)
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

        // 1. Chỉ query những dòng ĐÃ ĐẾN GIỜ (Nhanh như chớp vì có Index)
        var dueSchedules = await dbContext.Set<WorkflowSchedule>()
            .Where(x => x.IsActive && x.NextRunAt != null && x.NextRunAt <= now)
            .ToListAsync(stoppingToken);

        if (!dueSchedules.Any()) return;

        foreach (var schedule in dueSchedules)
        {
            try
            {
                // 2. Cập nhật thời gian cho lần chạy tiếp theo
                var expression = CronExpression.Parse(schedule.CronExpression, CronFormat.Standard);
                var nextRun = expression.GetNextOccurrence(now);

                schedule.LastRunAt = now;
                schedule.NextRunAt = nextRun;
                schedule.Version = Guid.NewGuid(); // Thay đổi version để check concurrency

                // 3. Tạo Command gửi cho Engine chạy Workflow
                var command = new SubmitWorkflowCommand(
                    DefinitionId: schedule.DefinitionId,
                    JobName: $"CronTrigger-{schedule.Id}-{now:yyyyMMddHHmm}",
                    InputData: "{}", // Chạy định kỳ thường không có payload đầu vào, hoặc set mặc định
                    CorrelationId: Guid.NewGuid()
                );

                // 4. Publish Command (Thực chất là lưu vào bảng Outbox của EF)
                await publishEndpoint.Publish(command, stoppingToken);

                // LƯU Ý: Ở đây ta save từng cái một. 
                // Nếu có lỗi do nhiều server cùng tranh nhau 1 schedule, 
                // thì lệnh SaveChanges này sẽ ném DbUpdateConcurrencyException.
                await dbContext.SaveChangesAsync(stoppingToken);

                _logger.LogInformation("Triggered Workflow {DefId} via Cron. Next run: {Next}", schedule.DefinitionId, nextRun);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Worker ở Server khác đã xử lý cái này rồi, bỏ qua không sao cả!
                _logger.LogDebug("Concurrency lock hit for schedule {ScheduleId}. Another worker processed it.", schedule.Id);

                // Trả Entity State về bình thường để chạy các schedule tiếp theo trong vòng lặp
                dbContext.Entry(schedule).State = EntityState.Detached;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process schedule {ScheduleId}", schedule.Id);
            }
        }
    }
}
