using System;
using System.Collections.Generic;
using System.Text;
using AWE.Application.Abstractions.Persistence;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Audit;

public interface IGetAuditHistoryQueryHandler
{
    Task<Result<List<AuditLogResponse>>> HandleAsync(Guid request, CancellationToken cancellationToken);
}

public class GetAuditHistoryQueryHandler(ISystemAuditLogRepository auditLogRepository) : IGetAuditHistoryQueryHandler
{
    private readonly ISystemAuditLogRepository _auditLogRepository = auditLogRepository;
    public async Task<Result<List<AuditLogResponse>>> HandleAsync(Guid request, CancellationToken cancellationToken)
    {
        var logs = await _auditLogRepository.GetAuditLogsAsync(request, cancellationToken);

        return Result.Success(logs);
    }
}
