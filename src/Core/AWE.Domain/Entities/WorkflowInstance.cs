using System.Text.Json;
using AWE.Domain.Common;
using AWE.Domain.Enums;

namespace AWE.Domain.Entities;

/// <summary>
/// Represents a runtime instance of a workflow
/// Ground truth for workflow execution state
/// </summary>
public class WorkflowInstance : AuditableEntity
{
    /// <summary>
    /// Reference to the workflow definition
    /// </summary>
    public Guid DefinitionId { get; private set; }

    /// <summary>
    /// Version pinning - locks the definition version at instantiation time
    /// Prevents runtime changes when definition is updated
    /// </summary>
    public int DefinitionVersion { get; private set; }

    /// <summary>
    /// Current lifecycle status
    /// </summary>
    public WorkflowInstanceStatus Status { get; set; }

    /// <summary>
    /// Global runtime context (variables)
    /// Stored as namespace: Steps[NodeId] -> { key: value }
    /// </summary>
    public JsonDocument ContextData { get; private set; }

    /// <summary>
    /// When the workflow started execution
    /// </summary>
    public DateTime StartTime { get; private set; }
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// ID của luồng Cha (nếu đây là luồng Con)
    /// </summary>
    public Guid? ParentInstanceId { get; private set; }

    /// <summary>
    /// ID của Node gọi luồng Con này (để đánh thức Cha khi xong việc)
    /// </summary>
    public Guid? ParentPointerId { get; private set; }

    /// <summary>
    /// Navigation properties
    /// </summary>
    public virtual WorkflowDefinition Definition { get; private set; } = null!;
    public virtual ICollection<ExecutionPointer> ExecutionPointers { get; private set; } = new List<ExecutionPointer>();
    public virtual ICollection<JoinBarrier> JoinBarriers { get; private set; } = new List<JoinBarrier>();
    public virtual ICollection<ExecutionLog> ExecutionLogs { get; private set; } = new List<ExecutionLog>();

    // Private constructor for EF Core
    private WorkflowInstance() : base() { }

    public WorkflowInstance(
        Guid definitionId,
        int definitionVersion,
        JsonDocument? initialContext = null,
        Guid? parentInstanceId = null,
        Guid? parentPointerId = null)
        : base()
    {
        DefinitionId = definitionId;
        DefinitionVersion = definitionVersion;
        Status = WorkflowInstanceStatus.Running;
        ContextData = initialContext ?? JsonDocument.Parse("{}");
        StartTime = DateTime.UtcNow;
        ParentInstanceId = parentInstanceId;
        ParentPointerId = parentPointerId;
    }

    /// <summary>
    /// Update the global context with new data
    /// </summary>
    public void UpdateContext(JsonDocument newContext)
    {
        ContextData = newContext ?? throw new ArgumentNullException(nameof(newContext));
        MarkAsUpdated();
    }

    public void Complete()
    {
        if (Status == WorkflowInstanceStatus.Completed)
            return;

        Status = WorkflowInstanceStatus.Completed;
        MarkAsUpdated();
    }

    public void Fail()
    {
        Status = WorkflowInstanceStatus.Failed;
        MarkAsUpdated();
    }

    public void Suspend()
    {
        if (Status != WorkflowInstanceStatus.Running)
            throw new InvalidOperationException($"Cannot suspend workflow in status: {Status}");

        Status = WorkflowInstanceStatus.Suspended;
        MarkAsUpdated();
    }

    public void Resume()
    {
        if (Status != WorkflowInstanceStatus.Suspended)
            throw new InvalidOperationException($"Cannot resume workflow in status: {Status}");

        Status = WorkflowInstanceStatus.Running;
        MarkAsUpdated();
    }

    public void StartCompensation()
    {
        Status = WorkflowInstanceStatus.Compensating;
        MarkAsUpdated();
    }

    public void CompleteCompensation()
    {
        if (Status != WorkflowInstanceStatus.Compensating)
            throw new InvalidOperationException("Workflow is not in compensating state");

        Status = WorkflowInstanceStatus.Compensated;
        MarkAsUpdated();
    }
}
