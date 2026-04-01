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
