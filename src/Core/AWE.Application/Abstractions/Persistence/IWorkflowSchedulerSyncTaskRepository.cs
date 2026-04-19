using AWE.Domain.Entities;
using AWE.Domain.Enums;

namespace AWE.Application.Abstractions.Persistence;

public interface IWorkflowSchedulerSyncTaskRepository
{
    Task EnqueueAsync(Guid definitionId, WorkflowSchedulerSyncOperation operation, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkflowSchedulerSyncTask>> GetDueTasksAsync(DateTime utcNow, int take, CancellationToken cancellationToken = default);

    Task<bool> HasPendingTaskAsync(Guid definitionId, WorkflowSchedulerSyncOperation operation, CancellationToken cancellationToken = default);
}
