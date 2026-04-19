using AWE.Application.UseCases.Monitor.Daskboard;
using AWE.Shared.Consts;
using AWE.Shared.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/dashboard")]
[Authorize(Policy = AppPolicies.RequireOperator)]
public class DashboardController : ApiController
{
    /// <summary>
    /// Lấy toàn bộ số liệu thống kê cho màn hình Dashboard
    /// </summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics([FromServices] IGetDashboardMetricsQueryHandler handler , CancellationToken cancellationToken)
    {
        Result<DashboardMetricsResponse> result = await handler.HandleAsync(cancellationToken);

        return HandleResult(result);
    }
}
