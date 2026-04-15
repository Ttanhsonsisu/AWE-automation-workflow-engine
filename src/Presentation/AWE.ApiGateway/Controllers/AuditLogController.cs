using System.Reflection.Metadata;
using AWE.Application.UseCases.Audit;
using AWE.Shared.Consts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/audit-logs")]
[Authorize(Policy = AppPolicies.RequireAdmin)]
public class AuditLogController : ApiController
{

    [HttpGet("history/{recordId}")]
    public async Task<IActionResult> GetHistory(Guid recordId, [FromServices] IGetAuditHistoryQueryHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(recordId, cancellationToken);
        return HandleResult(result);
    }
}
