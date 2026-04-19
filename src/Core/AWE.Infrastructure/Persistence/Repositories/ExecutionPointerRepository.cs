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
        // Rely on EF Core change tracking instead of calling Update(), which forces updating all columns.
        if (_context.Entry(pointer).State == EntityState.Detached)
        {
            _context.ExecutionPointers.Update(pointer);
        }
        return Task.CompletedTask;
    }

    public async Task<bool> TryAcquireLeaseAsync(Guid pointerId, string workerId, TimeSpan leaseDuration, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var leasedUntil = now.Add(leaseDuration);

        var affected = await _context.Set<ExecutionPointer>()
            .Where(x => x.Id == pointerId
                        && x.Active
                        && (x.Status == ExecutionPointerStatus.Pending
                            || (x.Status == ExecutionPointerStatus.Running
                                && x.LeasedUntil.HasValue
                                && x.LeasedUntil.Value < now)))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, ExecutionPointerStatus.Running)
                .SetProperty(p => p.StartTime, now)
                .SetProperty(p => p.LeasedBy, workerId)
                .SetProperty(p => p.LeasedUntil, leasedUntil)
                .SetProperty(p => p.RetryCount,
                    p => p.Status == ExecutionPointerStatus.Running
                        ? p.RetryCount + 1
                        : p.RetryCount),
                ct);

        return affected > 0;
    }

    public async Task<bool> RenewLeaseAsync(Guid pointerId, string workerId, TimeSpan extension, CancellationToken ct = default)
    {
        var affected = await _context.Set<ExecutionPointer>()
            .Where(x => x.Id == pointerId && x.LeasedBy == workerId && x.Status == ExecutionPointerStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.LeasedUntil, DateTime.UtcNow.Add(extension)),
                ct);

        return affected > 0;
    }

    public async Task<List<ExecutionPointer>> GetExpiredPointersAsync(DateTime utcNow, int count, CancellationToken ct = default)
    {
        return await _context.Set<ExecutionPointer>()
            .Where(x => x.Status == ExecutionPointerStatus.Running
                        && x.LeasedUntil < utcNow)
            .OrderBy(x => x.LeasedUntil) // Ưu tiên xử lý cái chết lâu nhất
            .Take(count)
            .ToListAsync(ct);
    }

    public async Task<int> ResetRawPointersAsync(List<Guid> pointerIds, CancellationToken ct = default)
    {
        if (pointerIds.Count == 0) return 0;

        // Reset về Pending, xóa LeasedBy, tăng RetryCount
        return await _context.Set<ExecutionPointer>()
            .Where(x => pointerIds.Contains(x.Id))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, ExecutionPointerStatus.Pending)
                .SetProperty(p => p.LeasedBy, (string?)null)
                .SetProperty(p => p.LeasedUntil, (DateTime?)null)
                .SetProperty(p => p.RetryCount, p => p.RetryCount + 1),
                ct);
    }

    public async Task<int> CountArrivedPointersByStepIdAsync(Guid instanceId, string stepId)
    {
        return await _context.ExecutionPointers
            .CountAsync(p => p.InstanceId == instanceId && p.StepId == stepId);
    }

    public async Task<List<ExecutionPointer>> GetPointersByStepIdAsync(Guid instanceId, string stepId)
    {
        return await _context.ExecutionPointers
            .Where(p => p.InstanceId == instanceId && p.StepId == stepId)
            .ToListAsync();
    }

    public async Task<List<ExecutionPointer>> GetCompletedPointersByInstanceIdAsync(Guid instanceId)
    {
        return await _context.ExecutionPointers
            .Where(p => p.InstanceId == instanceId && p.Status == ExecutionPointerStatus.Completed)
            .OrderByDescending(p => p.EndTime) // Sắp xếp LIFO: Chạy sau cùng thì Rollback đầu tiên
            .ToListAsync();
    }

    public Task<List<ExecutionPointer>> GetExpiredSuspendedPointersAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var runningInstanceIds = _context.WorkflowInstances
            .Where(x => x.Status == WorkflowInstanceStatus.Running)
            .Select(x => x.Id);

        // 2. Lọc các Pointer đến giờ thức dậy VÀ phải thuộc về các luồng Running ở trên
        return _context.ExecutionPointers
            .Where(p => p.Status == ExecutionPointerStatus.Suspended
                     && p.ResumeAt.HasValue
                     && p.ResumeAt.Value <= now
                     && runningInstanceIds.Contains(p.InstanceId)) // EF Core sẽ dịch câu này thành INNER JOIN hoặc IN (...)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ExecutionPointer>> GetPointersByInstanceIdAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        return await _context.ExecutionPointers
            .Where(p => p.InstanceId == instanceId)
            .ToListAsync(cancellationToken);
    }
}
