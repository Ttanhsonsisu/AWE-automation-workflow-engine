namespace AWE.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of a workflow instance
/// </summary>
public enum WorkflowInstanceStatus
{
    /// <summary>
    /// Workflow is currently executing
    /// </summary>
    Running,

    /// <summary>
    /// Workflow is paused and waiting for external trigger
    /// </summary>
    Suspended,

    /// <summary>
    /// Workflow completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Workflow failed and cannot continue
    /// </summary>
    Failed,

    /// <summary>
    /// Workflow is performing compensation (rollback)
    /// </summary>
    Compensating,

    /// <summary>
    /// Workflow compensation completed
    /// </summary>
    Compensated,

    /// <summary>
    /// Workflow was canceled before completion 
    /// </summary>
    Cancelled
}
