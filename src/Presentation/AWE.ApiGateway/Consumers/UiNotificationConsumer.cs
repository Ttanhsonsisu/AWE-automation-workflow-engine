using AWE.Application.Services;
using AWE.Contracts.Messages;
using AWE.WorkflowEngine.Services;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Quartz.Logging;

namespace AWE.ApiGateway.Consumers;

public class UiNotificationConsumer : 
    IConsumer<UiNodeStatusChangedEvent>, 
    IConsumer<UiWorkflowStatusChangedEvent>, 
    IConsumer<StepStartedEvent>, 
    IConsumer<WriteAuditLogCommand>
{
    private readonly IHubContext<WorkflowHub, IWorkflowHubClient> _hubContext;
    private readonly ILogger<UiNotificationConsumer> _logger;

    public UiNotificationConsumer(IHubContext<WorkflowHub, IWorkflowHubClient> hubContext, ILogger<UiNotificationConsumer> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<UiWorkflowStatusChangedEvent> context)
    {
        var msg = context.Message;
        await _hubContext.Clients.Group(msg.InstanceId.ToString()).WorkflowStatusChanged(msg.Status, msg.Timestamp);
        Console.WriteLine($"[Gateway] Đã push SignalR cho Workflow {msg.InstanceId} - Trạng thái: {msg.Status}");
    }

    public async Task Consume(ConsumeContext<UiNodeStatusChangedEvent> context)
    {
        var msg = context.Message;

        // Đóng gói data siêu nhẹ
        var updateMsg = new NodeStatusUpdateMessage(msg.StepId, msg.Status, msg.Timestamp);

        // Push qua SignalR cho các Client đang kết nối vào Gateway
        await _hubContext.Clients.Group(msg.InstanceId.ToString()).NodeStatusChanged(updateMsg);

        Console.WriteLine($"[Gateway] Đã push SignalR cho Step {msg.StepId} - Trạng thái: {msg.Status}");
    }

    // =========================================================
    // 2. XỬ LÝ SỰ KIỆN WORKER BẮT ĐẦU CHẠY (MÀU VÀNG)
    // =========================================================
    public async Task Consume(ConsumeContext<StepStartedEvent> context)
    {
        var msg = context.Message;
        var updateMsg = new NodeStatusUpdateMessage(msg.StepId, "Running", msg.StartedAt);

        await _hubContext.Clients.Group(msg.InstanceId.ToString()).NodeStatusChanged(updateMsg);
        _logger.LogInformation("[SignalR] Đã Push UI Trạng thái RUNNING cho Step {StepId}", msg.StepId);
    }

    // =========================================================
    // 3. XỬ LÝ SỰ KIỆN LOG (STREAMING TERMINAL)
    // =========================================================
    public async Task Consume(ConsumeContext<WriteAuditLogCommand> context)
    {
        var cmd = context.Message;

        // Bỏ qua các log hệ thống chung chung (không thuộc về Node nào) để đỡ rác UI
        if (string.IsNullOrEmpty(cmd.NodeId) || cmd.NodeId == "System")
            return;

        var logMsg = new LogUpdateMessage(
            StepId: cmd.NodeId,
            Level: cmd.Level.ToString(),
            Message: cmd.Message,
            Timestamp: DateTime.UtcNow
        );

        await _hubContext.Clients.Group(cmd.InstanceId.ToString()).WorkflowLogReceived(logMsg);
    }
}
