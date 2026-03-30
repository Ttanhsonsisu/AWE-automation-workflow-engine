using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class WorkflowInstanceRepository(ApplicationDbContext _context) : IWorkflowInstanceRepository
{
    public async Task<WorkflowInstance?> GetInstanceByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<WorkflowInstance?> GetInstanceWithPointersAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowInstances
            .Include(x => x.ExecutionPointers.Where(p => p.Active))
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowInstance>> GetInstancesByStatusAsync(
        WorkflowInstanceStatus status,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowInstances
            .AsNoTracking()
            .Where(x => x.Status == status)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowInstance>> GetInstancesAsync(
        int page = 1,
        int size = 10,
        IReadOnlyCollection<Guid>? definitionIds = null,
        WorkflowInstanceStatus? status = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplyFilters(_context.WorkflowInstances.AsNoTracking(), definitionIds, status, createdFrom, createdTo)
             .Include(x => x.Definition)
             .OrderByDescending(x => x.CreatedAt)
             .Skip((page - 1) * size)
             .Take(size)
             .ToListAsync(cancellationToken);
    }

    public async Task<long> CountInstancesAsync(
        IReadOnlyCollection<Guid>? definitionIds = null,
        WorkflowInstanceStatus? status = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        CancellationToken cancellationToken = default)
    {
        return await ApplyFilters(_context.WorkflowInstances.AsNoTracking(), definitionIds, status, createdFrom, createdTo)
            .LongCountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowInstance>> GetChildInstancesAsync(
        IReadOnlyCollection<Guid> parentInstanceIds,
        CancellationToken cancellationToken = default)
    {
        if (parentInstanceIds.Count == 0)
        {
            return [];
        }

        return await _context.WorkflowInstances
            .AsNoTracking()
            .Include(x => x.Definition)
            .Where(x => x.ParentInstanceId.HasValue && parentInstanceIds.Contains(x.ParentInstanceId.Value))
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddInstanceAsync(
        WorkflowInstance instance,
        CancellationToken cancellationToken = default)
    {
        await _context.WorkflowInstances.AddAsync(instance, cancellationToken);
    }

    public Task UpdateInstanceAsync(
        WorkflowInstance instance,
        CancellationToken cancellationToken = default)
    {
        _context.WorkflowInstances.Update(instance);
        return Task.CompletedTask;
    }

    private static IQueryable<WorkflowInstance> ApplyFilters(
        IQueryable<WorkflowInstance> query,
        IReadOnlyCollection<Guid>? definitionIds,
        WorkflowInstanceStatus? status,
        DateTime? createdFrom,
        DateTime? createdTo)
    {
        if (definitionIds is { Count: > 0 })
        {
            query = query.Where(x => definitionIds.Contains(x.DefinitionId));
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (createdFrom.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= createdFrom.Value);
        }

        if (createdTo.HasValue)
        {
            query = query.Where(x => x.CreatedAt <= createdTo.Value);
        }

        return query;
    }
}
