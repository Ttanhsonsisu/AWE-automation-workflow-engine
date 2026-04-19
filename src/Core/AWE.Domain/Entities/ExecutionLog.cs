using System.Text.Json;
using AWE.Domain.Enums;

namespace AWE.Domain.Entities;

/// <summary>
/// Audit log for workflow execution events
/// Immutable append-only table for debugging and compliance
/// </summary>
public class ExecutionLog
{
    /// <summary>
    /// Auto-incrementing primary key
    /// </summary>
    public long Id { get; private set; }

    /// <summary>
    /// Reference to workflow instance
    /// </summary>
    public Guid InstanceId { get; private set; }

    /// <summary>
    /// Reference to execution pointer
    /// </summary>
    public Guid? ExecutionPointerId { get; private set;  }

    /// <summary>
    /// Node/Step ID where the event occurred
    /// </summary>
    public string? NodeId { get; private set; } = string.Empty;

    public LogLevel Level { get; private set; }

    /// <summary>
    /// Event type (e.g., "StepStarted", "StepCompleted", "StepFailed", "VariableSet")
    /// </summary>
    public string Event { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;

    /// <summary>
    /// Additional event metadata (errors, input/output values, etc.)
    /// </summary>
    public JsonDocument? Metadata { get; private set; }

    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>
    /// Identifier of the worker/machine that generated this log
    /// </summary>
    public string? WorkerId { get; private set; }


    /// <summary>
    /// Navigation property
    /// </summary>
    public virtual WorkflowInstance Instance { get; private set; } = null!;

    public virtual ExecutionPointer? ExecutionPointer { get; private set; }

    // Private constructor for EF Core
    private ExecutionLog() { }

    public ExecutionLog(
        Guid instanceId,
        string eventType,
        string message,
        LogLevel level = LogLevel.Information,
        Guid? executionPointerId = null,
        string? nodeId = null,
        string? workerId = null,
        JsonDocument? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type cannot be empty", nameof(eventType));

        InstanceId = instanceId;
        Message = message;
        ExecutionPointerId = executionPointerId;
        NodeId = nodeId;
        Level = level;
        Event = eventType;
        Metadata = metadata;
        WorkerId = workerId;
        CreatedAt = DateTime.UtcNow;
    }
}
