using AWE.Application.Abstractions.Persistence;
using AWE.Application.UseCases.Audit;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class SystemAuditLogRepository(ApplicationDbContext context) : ISystemAuditLogRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<List<AuditLogResponse>> GetAuditLogsAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default)
    {
        var logs = await _context.Set<SystemAuditLog>()
            .Where(x => x.RecordId == workflowInstanceId.ToString())
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AuditLogResponse(
                x.Id,
                x.Action,
                x.UserName ?? "Unknown",
                x.OldValues ?? "{}",
                x.NewValues ?? "{}",
                x.CreatedAt
            ))
            .ToListAsync(cancellationToken);

        return logs;

    }
}

