using AWE.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/dropdown")]
public class DropdownController(IPluginService pluginService) : ApiController
{
    private readonly IPluginService _pluginService = pluginService;

    [HttpGet("version/package")]

    public async Task<IActionResult> ListVersionPackageDropDownAsyn([FromQuery] Guid packageId, CancellationToken ct)
    {
        var result = await _pluginService.ListVersionPackageDropDownAsyn(packageId, ct);
        return HandleResult(result);
    }
}
