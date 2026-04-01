using AWE.Application.Services;
using AWE.Contracts.Messages;
using AWE.WorkflowEngine.Services;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace AWE.ApiGateway.Consumers;

public class UiNotificationConsumer : IConsumer<UiNodeStatusChangedEvent>
{
    private readonly IHubContext<WorkflowHub, IWorkflowHubClient> _hubContext;

    public UiNotificationConsumer(IHubContext<WorkflowHub, IWorkflowHubClient> hubContext)
    {
        _hubContext = hubContext;
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
}
