namespace AWE.Contracts.Messages;

/// <summary>
///  Represents a command to resume a workflow at a specific pointer with the provided resume data.
/// </summary>
/// <param name="PointerId">The unique identifier of the workflow pointer to resume.</param>
/// <param name="ResumeDataJson">A JSON-formatted string containing the data required to resume the workflow.</param>
public record ResumeWorkflowCommand(
    Guid PointerId,
    string ResumeDataJson 
);
