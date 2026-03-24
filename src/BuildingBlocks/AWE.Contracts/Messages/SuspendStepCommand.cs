namespace AWE.Contracts.Messages;

/// <summary>
/// Lệnh do Worker bắn ngược về Core Engine để yêu cầu Pause một Node
/// </summary>
public record SuspendStepCommand(
    Guid InstanceId,
    Guid PointerId,
    string? Reason = null
);
