namespace AWE.Contracts.Messages;

public record ResumeStepCommand(
    Guid InstanceId,
    Guid PointerId,
    string StepId
);
