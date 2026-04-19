namespace AWE.Domain.Enums;

/// <summary>
/// Represents the execution status of a workflow step (token)
/// </summary>
public enum ExecutionPointerStatus
{
    /// <summary>
    /// Token is waiting to be picked up by a worker
    /// </summary>
    Pending,

    /// <summary>
    /// Token is currently being processed by a worker
    /// </summary>
    Running,

    /// <summary>
    /// Token execution completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Token execution failed
    /// </summary>
    Failed,

    /// <summary>
    /// Token was skipped (e.g., conditional branch not taken)
    /// </summary>
    Skipped,

    /// <summary>
    /// Token is suspended and waiting for resume signal (timer/webhook/manual)
    /// </summary>
    Suspended
}
