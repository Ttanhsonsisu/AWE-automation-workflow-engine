using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Domain.Enums;

namespace AWE.WorkflowEngine.Tests.Fakes;

internal sealed class InMemoryExecutionPointerRepository : IExecutionPointerRepository
{
    private readonly List<ExecutionPointer> _pointers = [];

    public InMemoryExecutionPointerRepository(params ExecutionPointer[] pointers)
    {
        _pointers.AddRange(pointers);
    }

    public Task<ExecutionPointer?> GetPointerByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_pointers.FirstOrDefault(x => x.Id == id));

    public Task<IReadOnlyList<ExecutionPointer>> GetPendingPointersAsync(int limit, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var result = _pointers
            .Where(x => x.Active
                        && (x.Status == ExecutionPointerStatus.Pending
                            || (x.Status == ExecutionPointerStatus.Running
                                && x.LeasedUntil.HasValue
                                && x.LeasedUntil.Value < now)))
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<ExecutionPointer>>(result);
    }

    public Task<IReadOnlyList<ExecutionPointer>> GetZombiePointersAsync(CancellationToken cancellationToken = default)
    {
        var result = _pointers.Where(x => x.IsZombie()).ToList();
        return Task.FromResult<IReadOnlyList<ExecutionPointer>>(result);
    }

    public Task<IReadOnlyList<ExecutionPointer>> GetActivePointersByInstanceAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        var result = _pointers.Where(x => x.InstanceId == instanceId && x.Active).ToList();
        return Task.FromResult<IReadOnlyList<ExecutionPointer>>(result);
    }

    public Task AddPointerAsync(ExecutionPointer pointer, CancellationToken cancellationToken = default)
    {
        _pointers.Add(pointer);
        return Task.CompletedTask;
    }

    public Task UpdatePointerAsync(ExecutionPointer pointer, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public async Task<bool> TryAcquireLeaseAsync(
        Guid pointerId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        var pointer = await GetPointerByIdAsync(pointerId, ct);
        return pointer?.TryAcquireLease(workerId, leaseDuration) == true;
    }

    public async Task<bool> RenewLeaseAsync(Guid pointerId, string workerId, TimeSpan extension, CancellationToken ct = default)
    {
        var pointer = await GetPointerByIdAsync(pointerId, ct);
        if (pointer is null)
        {
            return false;
        }

        try
        {
            pointer.RenewLease(workerId, extension);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public Task<List<ExecutionPointer>> GetExpiredPointersAsync(DateTime utcNow, int count, CancellationToken ct = default)
    {
        var result = _pointers
            .Where(x => x.Status == ExecutionPointerStatus.Running
                        && x.LeasedUntil.HasValue
                        && x.LeasedUntil.Value < utcNow)
            .Take(count)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<int> ResetRawPointersAsync(List<Guid> pointerIds, CancellationToken ct = default)
    {
        var affected = 0;
        foreach (var pointer in _pointers.Where(x => pointerIds.Contains(x.Id)))
        {
            pointer.ResetToPending();
            affected++;
        }

        return Task.FromResult(affected);
    }

    public Task<int> CountArrivedPointersByStepIdAsync(Guid instanceId, string stepId)
    {
        var count = _pointers.Count(p => p.InstanceId == instanceId && p.StepId == stepId);
        return Task.FromResult(count);
    }

    public Task<List<ExecutionPointer>> GetPointersByStepIdAsync(Guid instanceId, string stepId)
    {
        var result = _pointers.Where(p => p.InstanceId == instanceId && p.StepId == stepId).ToList();
        return Task.FromResult(result);
    }

    public Task<List<ExecutionPointer>> GetCompletedPointersByInstanceIdAsync(Guid instanceId)
    {
        var result = _pointers
            .Where(p => p.InstanceId == instanceId && p.Status == ExecutionPointerStatus.Completed)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<List<ExecutionPointer>> GetExpiredSuspendedPointersAsync(
        DateTime now,
        CancellationToken cancellationToken = default)
    {
        var result = _pointers
            .Where(p => p.Status == ExecutionPointerStatus.Suspended
                        && p.ResumeAt.HasValue
                        && p.ResumeAt.Value <= now)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<ExecutionPointer>> GetPointersByInstanceIdAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        var result = _pointers.Where(p => p.InstanceId == instanceId).ToList();
        return Task.FromResult<IReadOnlyList<ExecutionPointer>>(result);
    }
}
