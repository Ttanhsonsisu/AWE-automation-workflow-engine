using AWE.Application.Services;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;
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

    [HttpGet("workflow-instance-status")]
    public IActionResult GetWorkflowInstanceStatuses()
    {
        var statuses = Enum.GetValues<WorkflowInstanceStatus>()
            .Select(x => new EnumDropdownItemDto(
                Value: x.ToString(),
                Label: x.ToString()))
            .ToList();

        return HandleResult(Result.Success(statuses));
    }
}

public record EnumDropdownItemDto(string Value, string Label);
