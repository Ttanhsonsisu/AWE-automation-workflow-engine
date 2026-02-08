using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class JoinBarrierRepository(ApplicationDbContext _context) : IJoinBarrierRepository
{
    public async Task<JoinBarrier?> GetBarrierByInstanceAndStepAsync(
        Guid instanceId,
        string stepId,
        CancellationToken cancellationToken = default)
    {
        return await _context.JoinBarriers
            .FirstOrDefaultAsync(
                x => x.InstanceId == instanceId && x.StepId == stepId,
                cancellationToken);
    }

    public async Task AddBarrierAsync(
        JoinBarrier barrier,
        CancellationToken cancellationToken = default)
    {
        await _context.JoinBarriers.AddAsync(barrier, cancellationToken);
    }

    public Task UpdateBarrierAsync(
        JoinBarrier barrier,
        CancellationToken cancellationToken = default)
    {
        _context.JoinBarriers.Update(barrier);
        return Task.CompletedTask;
    }
}
