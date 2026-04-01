using AWE.Contracts.Messages;

namespace AWE.Application.Services;

public interface IWorkflowHubClient
{
    // Báo cho UI biết trạng thái một Node vừa thay đổi
    Task NodeStatusChanged(NodeStatusUpdateMessage message);

    // Báo cho UI biết trạng thái tổng thể của Workflow
    Task WorkflowStatusChanged(string status, DateTime timestamp);

    Task WorkflowLogReceived(LogUpdateMessage log);
}
