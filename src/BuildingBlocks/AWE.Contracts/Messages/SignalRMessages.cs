namespace AWE.Contracts.Messages;

public record NodeStatusUpdateMessage(
    string StepId,
    string Status,
    DateTime Timestamp
);

public record StepStartedEvent(
    Guid InstanceId,
    Guid ExecutionPointerId,
    string StepId,
    DateTime StartedAt
);

// Event này chuyên dùng để báo cho UI biết trạng thái thay đổi
public record UiNodeStatusChangedEvent(
    Guid InstanceId,
    string StepId,
    string Status,
    DateTime Timestamp
);

// Event chuyên dùng để báo cho UI trạng thái tổng thể của Workflow
public record UiWorkflowStatusChangedEvent(
    Guid InstanceId,
    string Status,
    DateTime Timestamp
);

/// <summary>
/// DTO chuyên dụng cho SignalR để bắn log thẳng lên màn hình UI (Terminal)
/// </summary>
public record LogUpdateMessage(
    string StepId,
    string Level, // Info, Warning, Error
    string Message,
    DateTime Timestamp
);
