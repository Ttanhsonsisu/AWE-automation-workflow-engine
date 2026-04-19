using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class WorkflowSchedulerSyncTaskRepository(ApplicationDbContext context) : IWorkflowSchedulerSyncTaskRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task EnqueueAsync(Guid definitionId, WorkflowSchedulerSyncOperation operation, CancellationToken cancellationToken = default)
    {
        if (await HasPendingTaskAsync(definitionId, operation, cancellationToken))
        {
            return;
        }

        await _context.Set<WorkflowSchedulerSyncTask>()
            .AddAsync(new WorkflowSchedulerSyncTask(definitionId, operation), cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowSchedulerSyncTask>> GetDueTasksAsync(DateTime utcNow, int take, CancellationToken cancellationToken = default)
    {
        return await _context.Set<WorkflowSchedulerSyncTask>()
            .Where(x => !x.IsCompleted && x.NextAttemptAtUtc <= utcNow)
            .OrderBy(x => x.NextAttemptAtUtc)
            .ThenBy(x => x.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasPendingTaskAsync(Guid definitionId, WorkflowSchedulerSyncOperation operation, CancellationToken cancellationToken = default)
    {
        return await _context.Set<WorkflowSchedulerSyncTask>()
            .AnyAsync(x => x.DefinitionId == definitionId
                           && x.Operation == operation
                           && !x.IsCompleted,
                cancellationToken);
    }
}
