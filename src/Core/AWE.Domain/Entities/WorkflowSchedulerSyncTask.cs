using AWE.Domain.Common;
using AWE.Domain.Enums;

namespace AWE.Domain.Entities;

public class WorkflowSchedulerSyncTask : AuditableEntity
{
    public Guid DefinitionId { get; private set; }
    public WorkflowSchedulerSyncOperation Operation { get; private set; }
    public int RetryCount { get; private set; }
    public DateTime NextAttemptAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public bool IsCompleted { get; private set; }

    private WorkflowSchedulerSyncTask() : base() { }

    public WorkflowSchedulerSyncTask(Guid definitionId, WorkflowSchedulerSyncOperation operation)
    {
        DefinitionId = definitionId;
        Operation = operation;
        RetryCount = 0;
        NextAttemptAtUtc = DateTime.UtcNow;
        IsCompleted = false;
    }

    public void MarkSucceeded()
    {
        IsCompleted = true;
        ProcessedAtUtc = DateTime.UtcNow;
        LastError = null;
        MarkAsUpdated();
    }

    public void MarkFailed(string? errorMessage)
    {
        RetryCount++;
        LastError = string.IsNullOrWhiteSpace(errorMessage)
            ? "Scheduler sync failed."
            : errorMessage;

        var delaySeconds = Math.Min(300, (int)Math.Pow(2, Math.Min(RetryCount, 8)));
        NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(delaySeconds);
        MarkAsUpdated();
    }
}
