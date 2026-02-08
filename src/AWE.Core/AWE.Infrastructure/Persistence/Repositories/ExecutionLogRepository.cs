using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class ExecutionLogRepository(ApplicationDbContext _context) : IExecutionLogRepository
{
    public async Task<IReadOnlyList<ExecutionLog>> GetLogsByInstanceAsync(
        Guid instanceId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ExecutionLogs
            .AsNoTracking()
            .Where(x => x.InstanceId == instanceId)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddLogAsync(
        ExecutionLog log,
        CancellationToken cancellationToken = default)
    {
        await _context.ExecutionLogs.AddAsync(log, cancellationToken);
    }
}
