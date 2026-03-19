using System;
using System.Collections.Generic;
using System.Text;
using AWE.Application.UseCases.Audit;

namespace AWE.Application.Abstractions.Persistence;

public interface ISystemAuditLogRepository
{
    public Task<List<AuditLogResponse>> GetAuditLogsAsync(Guid workflowInstanceId, CancellationToken cancellationToken = default);
}
