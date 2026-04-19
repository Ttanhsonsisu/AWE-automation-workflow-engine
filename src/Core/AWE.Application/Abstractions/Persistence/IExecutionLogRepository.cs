using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.Persistence;

public interface IExecutionLogRepository
{
    Task<IReadOnlyList<ExecutionLog>> GetLogsByInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default);
    Task AddLogAsync(ExecutionLog log, CancellationToken cancellationToken = default);
}
