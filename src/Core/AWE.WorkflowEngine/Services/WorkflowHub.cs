using AWE.Application.Services;
using Microsoft.AspNetCore.SignalR;

namespace AWE.WorkflowEngine.Services;

// TODO: [Security Phase] - Bật [Authorize] khi tích hợp Keycloak
// TODO: Kiểm tra TenantId/OwnerId của Context.User so với DB trước khi AddToGroup
public class WorkflowHub : Hub<IWorkflowHubClient>
{
    public async Task JoinWorkflowGroup(string instanceId)
    {
        // Add User hiện tại vào một "Phòng (Group)" mang tên InstanceId
        await Groups.AddToGroupAsync(Context.ConnectionId, instanceId);

        // Log để debug xem Client có connect thành công không
        Console.WriteLine($"[SignalR] Client {Context.ConnectionId} joined Group {instanceId}");
    }

    public async Task LeaveWorkflowGroup(string instanceId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, instanceId);
    }

}
