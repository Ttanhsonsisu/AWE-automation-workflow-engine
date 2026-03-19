using System.Reflection.Metadata;
using AWE.Application.UseCases.Audit;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/audit-logs")]
public class AuditLogController : ApiController
{

    [HttpGet("history/{recordId}")]
    public async Task<IActionResult> GetHistory(Guid recordId, [FromServices] IGetAuditHistoryQueryHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(recordId, cancellationToken);
        return HandleResult(result);
    }
}
