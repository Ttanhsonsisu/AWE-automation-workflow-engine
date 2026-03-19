using AWE.Application.Services;
using AWE.Contracts.Messages;
using MassTransit;

namespace AWE.Wokrer.Engine.Consumers;

public class TelegramAlertConsumer : IConsumer<WriteAuditLogCommand>
{
    private readonly ITelegramNotificationService _telegramService;
    private readonly ILogger<TelegramAlertConsumer> _logger;

    public TelegramAlertConsumer(ITelegramNotificationService telegramService, ILogger<TelegramAlertConsumer> logger)
    {
        _telegramService = telegramService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WriteAuditLogCommand> context)
    {
        var msg = context.Message;

        // Chỉ quan tâm đến các sự kiện lỗi nghiêm trọng (Workflow bị chết)
        //if (msg.Level == Domain.Enums.LogLevel.Error && msg.Event == "WorkflowFailed")
        //{
            _logger.LogInformation("🚨 Bắt được sự kiện lỗi! Đang gửi cảnh báo Telegram cho Instance {Id}", msg.InstanceId);

            // Trang trí tin nhắn Telegram cho ngầu
            var alertMessage = $@"
                🚨 <b>AWE SYSTEM ALERT</b> 🚨
                <b>Tình trạng:</b> WORKFLOW THẤT BẠI VĨNH VIỄN ❌

                🆔 <b>Instance ID:</b> <code>{msg.InstanceId}</code>
                📍 <b>Node Lỗi:</b> {msg.NodeId}
                ⏰ <b>Thời gian:</b> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC

                📝 <b>Chi tiết:</b> 
                {msg.Message}

                <i>Vui lòng truy cập hệ thống để kiểm tra và xử lý Saga (Rollback)!</i>
                ";
            await _telegramService.SendAlertAsync(alertMessage);
        //}
    }
}
