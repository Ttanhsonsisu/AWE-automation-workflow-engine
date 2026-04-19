using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.Persistence;

public interface IJoinBarrierRepository
{
    Task<JoinBarrier?> GetBarrierByInstanceAndStepAsync(Guid instanceId, string stepId, CancellationToken cancellationToken = default);
    Task AddBarrierAsync(JoinBarrier barrier, CancellationToken cancellationToken = default);
    Task UpdateBarrierAsync(JoinBarrier barrier, CancellationToken cancellationToken = default);
}
