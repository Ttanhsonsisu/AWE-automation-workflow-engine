using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class ExecutionPointerRepository(ApplicationDbContext _context) : IExecutionPointerRepository
{
    public async Task<ExecutionPointer?> GetPointerByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.ExecutionPointers
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionPointer>> GetPendingPointersAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // CRITICAL QUERY: Worker polling
        // Finds pointers that are either:
        // 1. Status = Pending (never started), OR
        // 2. Status = Running but lease expired (zombie recovery)
        return await _context.ExecutionPointers
            .Where(x => x.Active &&
                       (x.Status == ExecutionPointerStatus.Pending ||
                        (x.Status == ExecutionPointerStatus.Running &&
                         x.LeasedUntil.HasValue &&
                         x.LeasedUntil.Value < now)))
            .OrderBy(x => x.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionPointer>> GetZombiePointersAsync(
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        return await _context.ExecutionPointers
            .Where(x => x.Status == ExecutionPointerStatus.Running &&
                       x.LeasedUntil.HasValue &&
                       x.LeasedUntil.Value < now)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionPointer>> GetActivePointersByInstanceAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ExecutionPointers
            .AsNoTracking()
            .Where(x => x.InstanceId == instanceId && x.Active)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddPointerAsync(
        ExecutionPointer pointer,
        CancellationToken cancellationToken = default)
    {
        await _context.ExecutionPointers.AddAsync(pointer, cancellationToken);
    }

    public Task UpdatePointerAsync(
        ExecutionPointer pointer,
        CancellationToken cancellationToken = default)
    {
        _context.ExecutionPointers.Update(pointer);
        return Task.CompletedTask;
    }
}
