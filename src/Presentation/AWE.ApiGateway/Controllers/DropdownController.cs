using AWE.Application.Abstractions.Persistence;
using AWE.Application.Services;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using AWE.Shared.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/dropdown")]
[Authorize]
public class DropdownController(IPluginService pluginService, IWorkflowDefinitionRepository definitionRepository) : ApiController
{
    private readonly IPluginService _pluginService = pluginService;
    private readonly IWorkflowDefinitionRepository _definitionRepository = definitionRepository;

    [HttpGet("workflow-definition")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
    public async Task<IActionResult> ListWorkflowDefinitionDropdownAsync(CancellationToken ct)
    {
        var definitions = await _definitionRepository.GetAllDefinitionsAsync(ct);

        var result = definitions.Select(x => new WorkflowDefinitionDropdownItemDto(
            Id: x.Id,
            Name: x.Name,
            Version: x.Version,
            Description: x.Description));

        return HandleResult(Result.Success(result));
    }

    [HttpGet("timezones")]
    [AllowAnonymous]
    public IActionResult GetTimeZones()
    {
        // Sử dụng chuẩn IANA Timezone (nếu bạn có cài thư viện TimeZoneConverter)
        // hoặc dùng System TimeZones của .NET
        var timezones = TimeZoneInfo.GetSystemTimeZones()
            .Select(tz => new
            {
                Value = tz.Id,
                Label = tz.DisplayName
            })
            .ToList();

        return Ok(timezones);
    }


    [HttpGet("version/package")]
    [Authorize(Policy = AppPolicies.RequireEditor)]

    public async Task<IActionResult> ListVersionPackageDropDownAsyn([FromQuery] Guid packageId, CancellationToken ct)
    {
        var result = await _pluginService.ListVersionPackageDropDownAsyn(packageId, ct);
        return HandleResult(result);
    }

    [HttpGet("workflow-instance-status")]
    [Authorize(Policy = AppPolicies.RequireOperator)]
    public IActionResult GetWorkflowInstanceStatuses()
    {
        var statuses = Enum.GetValues<WorkflowInstanceStatus>()
            .Select(x => new EnumDropdownItemDto(
                Value: x.ToString(),
                Label: x.ToString()))
            .ToList();

        return HandleResult(Result.Success(statuses));
    }

    [HttpGet("plugin-execution-mode")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
    public IActionResult GetPluginExecutionModes()
    {
        var modes = Enum.GetValues<PluginExecutionMode>()
            .Select(x => new EnumDropdownItemDto(
                Value: x.ToString(),
                Label: x.ToString()))
            .ToList();

        return HandleResult(Result.Success(modes));
    }

    [HttpGet("plugin-category")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
    public async Task<IActionResult> GetPluginCategories(CancellationToken ct)
    {
        var result = await _pluginService.ListPluginCategoriesAsync(ct);
        if (result.IsFailure)
        {
            return HandleFailure(result.Error!);
        }

        var categories = result.Value
            .Select(x => new EnumDropdownItemDto(Value: x, Label: x))
            .ToList();

        return HandleResult(Result.Success(categories));
    }
}

public record EnumDropdownItemDto(string Value, string Label);
public record WorkflowDefinitionDropdownItemDto(Guid Id, string Name, int Version, string? Description);
