using AWE.Domain.Common;
using AWE.Domain.Enums;
using System.Text.Json;

namespace AWE.Domain.Entities;

/// <summary>
/// Represents a token moving through the workflow DAG
/// This is the GROUND TRUTH for runtime state
/// HOT TABLE - heavily updated
/// </summary>
public class ExecutionPointer : Entity
{
    /// <summary>
    /// Reference to parent workflow instance
    /// </summary>
    public Guid InstanceId { get; private set; }

    /// <summary>
    /// ID of the current node/step in the DAG
    /// </summary>
    public string StepId { get; private set; } = string.Empty;

    /// <summary>
    /// Current execution status
    /// </summary>
    public ExecutionPointerStatus Status { get; private set; }

    /// <summary>
    /// Whether this pointer is still active (not archived)
    /// </summary>
    public bool Active { get; private set; }

    /// <summary>
    /// LEASING MECHANISM: Timestamp when the lease expires
    /// Used for zombie detection: if NOW > LeasedUntil and Status=Running -> Zombie
    /// </summary>
    public DateTime? LeasedUntil { get; private set; }

    /// <summary>
    /// LEASING MECHANISM: Worker ID that currently holds the lease
    /// Format: WorkerId-ProcessId or IP:Port
    /// </summary>
    public string? LeasedBy { get; private set; }

    /// <summary>
    /// Number of retry attempts made
    /// </summary>
    public int RetryCount { get; private set; }

    /// <summary>
    /// Reference to the parent pointer (for backtracking)
    /// </summary>
    public Guid? PredecessorId { get; private set; }

    /// <summary>
    /// Scope information for parallel branches
    /// Stored as JSON array of BranchIds: ["branch1", "branch2"]
    /// </summary>
    public JsonDocument Scope { get; private set; }

    /// <summary>
    /// When this pointer was created
    /// Used for partitioning and ordering
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Step-level context data (inputs/outputs)
    /// </summary>
    public JsonDocument? StepContext { get; private set; }

    /// <summary>
    /// Navigation properties
    /// </summary>
    public virtual WorkflowInstance Instance { get; private set; } = null!;

    public virtual ICollection<ExecutionLog> ExecutionLogs { get; private set; } = new List<ExecutionLog>();

    // Private constructor for EF Core
    private ExecutionPointer() { }

    public ExecutionPointer(
        Guid instanceId,
        string stepId,
        Guid? predecessorId = null,
        JsonDocument? scope = null)
    {
        if (string.IsNullOrWhiteSpace(stepId))
            throw new ArgumentException("Step ID cannot be empty", nameof(stepId));

        InstanceId = instanceId;
        StepId = stepId;
        Status = ExecutionPointerStatus.Pending;
        Active = true;
        RetryCount = 0;
        PredecessorId = predecessorId;
        Scope = scope ?? JsonDocument.Parse("[]");
        CreatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Acquire a lease for this pointer
    /// Returns true if lease acquired successfully
    /// </summary>
    public bool TryAcquireLease(string workerId, TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker ID cannot be empty", nameof(workerId));

        // Can only acquire lease if:
        // 1. Status is Pending, OR
        // 2. Status is Running but lease has expired (zombie recovery)
        var now = DateTime.UtcNow;

        if (Status == ExecutionPointerStatus.Pending)
        {
            Status = ExecutionPointerStatus.Running;
            LeasedUntil = now.Add(leaseDuration);
            LeasedBy = workerId;
            return true;
        }

        if (Status == ExecutionPointerStatus.Running && LeasedUntil.HasValue && LeasedUntil.Value < now)
        {
            // Zombie recovery - lease expired, can be re-acquired
            LeasedUntil = now.Add(leaseDuration);
            LeasedBy = workerId;
            RetryCount++;
            return true;
        }

        return false; // Already leased or in terminal state
    }

    /// <summary>
    /// Renew the current lease (heartbeat mechanism)
    /// </summary>
    public void RenewLease(string workerId, TimeSpan leaseDuration)
    {
        if (LeasedBy != workerId)
            throw new InvalidOperationException($"Lease is held by {LeasedBy}, not {workerId}");

        if (Status != ExecutionPointerStatus.Running)
            throw new InvalidOperationException($"Cannot renew lease in status: {Status}");

        LeasedUntil = DateTime.UtcNow.Add(leaseDuration);
    }

    /// <summary>
    /// Release the lease and mark as completed
    /// </summary>
    public void Complete(string workerId, JsonDocument? outputContext = null)
    {
        ValidateLeaseOwnership(workerId);

        Status = ExecutionPointerStatus.Completed;
        Active = false;
        LeasedUntil = null;
        LeasedBy = null;
        StepContext = outputContext;
    }

    /// <summary>
    /// Mark pointer as failed
    /// </summary>
    public void MarkAsFailed(string workerId, JsonDocument? errorContext = null)
    {
        ValidateLeaseOwnership(workerId);

        Status = ExecutionPointerStatus.Failed;
        Active = false;
        LeasedUntil = null;
        LeasedBy = null;
        StepContext = errorContext;
    }

    /// <summary>
    /// Skip this pointer (e.g., conditional branch not taken)
    /// </summary>
    public void Skip()
    {
        Status = ExecutionPointerStatus.Skipped;
        Active = false;
        LeasedUntil = null;
        LeasedBy = null;
    }

    /// <summary>
    /// Reset pointer to Pending state (for retry or zombie recovery)
    /// </summary>
    public void ResetToPending()
    {
        if (Status == ExecutionPointerStatus.Completed || Status == ExecutionPointerStatus.Skipped)
            throw new InvalidOperationException($"Cannot reset pointer in terminal state: {Status}");

        Status = ExecutionPointerStatus.Pending;
        LeasedUntil = null;
        LeasedBy = null;
        RetryCount++;
    }

    /// <summary>
    /// Check if this pointer is a zombie (lease expired while running)
    /// </summary>
    public bool IsZombie()
    {
        return Status == ExecutionPointerStatus.Running
            && LeasedUntil.HasValue
            && LeasedUntil.Value < DateTime.UtcNow;
    }

    private void ValidateLeaseOwnership(string workerId)
    {
        if (LeasedBy != workerId)
            throw new InvalidOperationException($"Lease is held by {LeasedBy}, not {workerId}");

        if (Status != ExecutionPointerStatus.Running)
            throw new InvalidOperationException($"Pointer is not in Running state: {Status}");
    }
}
