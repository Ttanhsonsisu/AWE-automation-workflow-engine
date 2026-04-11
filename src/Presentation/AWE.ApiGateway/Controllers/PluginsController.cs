using System.Text.Json;
using AWE.ApiGateway.Dtos.Requests;
using AWE.Application.Services;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/plugins")]
public class PluginsController(IPluginService pluginService) : ApiController
{
    private readonly IPluginService _pluginService = pluginService;

    [HttpPost("packages")]
    public async Task<IActionResult> CreatePackage(
        [FromBody] CreatePluginPackageRequest req,
        CancellationToken ct)
    {
        var result = await _pluginService.CreatePackageAsync(
            req.UniqueName,
            req.DisplayName,
            req.ExecutionMode, 
            req.Category ?? "Custom",
            req.Icon ?? "lucide-box",
            req.Description,
            ct);

        return HandleResult(result);
    }

    [HttpGet("packages")]
    public async Task<IActionResult> ListPackages(
        [FromQuery] int page = 1,
        [FromQuery] int size = 10,
        [FromQuery] string? search = null,
        [FromQuery] PluginExecutionMode? executionMode = null,
        [FromQuery] string? category = null,
        CancellationToken ct = default)
    {
        var result = await _pluginService.ListPackagesAsync(page, size, search, executionMode, category, ct);
        return HandleResult(result);
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetPluginCatalog(CancellationToken ct)
    {
        var result = await _pluginService.GetCatalogAsync(ct);
        return HandleResult(result);
    }

    // Versions
    [HttpPost("packages/{packageId:guid}/versions")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadVersion(
        [FromRoute] Guid packageId,
        [FromForm] UploadPluginVersionRequest req, 
        CancellationToken ct)
    {
        if (req.File is null || req.File.Length == 0)
        {
            return HandleFailure(Error.Validation("Request.FileRequired", "File is required."));
        }

        using var stream = req.File.OpenReadStream();

        var result = await _pluginService.UploadVersionAsync(
            packageId: packageId,
            version: req.Version,
            dllStream: stream,
            fileName: req.File.FileName,
            bucket: req.Bucket ?? "awe-plugins",
            releaseNotes: req.ReleaseNotes,
            ct: ct);

        return HandleResult(result);
    }

    [HttpGet("packages/{packageId:guid}/versions")]
    public async Task<IActionResult> ListVersions(
        [FromRoute] Guid packageId,
        CancellationToken ct)
    {
        var result = await _pluginService.ListVersionsAsync(packageId, ct);
        return HandleResult(result);
    }

    [HttpGet("versions/{versionId:guid}/download")]
    public async Task<IActionResult> DownloadVersion(
        [FromRoute] Guid versionId,
        CancellationToken ct)
    {
        var result = await _pluginService.DownloadVersionAsync(versionId, ct);

        if (result.IsFailure)
        {
            return HandleFailure(result.Error!);
        }

        return File(result.Value, "application/octet-stream", fileDownloadName: $"plugin-{versionId}.dll");
    }

    [HttpPost("versions/{versionId:guid}/activate")]
    public async Task<IActionResult> ActivateVersion(
        [FromRoute] Guid versionId,
        CancellationToken ct)
    {
        var result = await _pluginService.ActivateVersionAsync(versionId, ct);
        return HandleResult(result);
    }

    [HttpPost("versions/{versionId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateVersion(
        [FromRoute] Guid versionId,
        CancellationToken ct)
    {
        var result = await _pluginService.DeactivateVersionAsync(versionId, ct);
        return HandleResult(result);
    }

    [HttpDelete("versions/{versionId:guid}")]
    public async Task<IActionResult> DeleteVersion(
        [FromRoute] Guid versionId,
        [FromQuery] bool deleteObject = true,
        CancellationToken ct = default)
    {
        var result = await _pluginService.DeleteVersionAsync(versionId, deleteObject, ct);
        return HandleResult(result);
    }

    [HttpGet("details")]
    public async Task<IActionResult> GetPluginDetails(
        [FromQuery] PluginExecutionMode mode,
        [FromQuery] string? name,
        [FromQuery] Guid? packageId,
        [FromQuery] string? version,
        CancellationToken ct)
    {
        var result = await _pluginService.GetPluginDetailsAsync(mode, name, packageId, version, ct);
        return HandleResult(result);
    }

    [HttpGet("details/by-sha256/{sha256}")]
    public async Task<IActionResult> GetPluginDetailsByHash(
       [FromRoute] string sha256,
       CancellationToken ct)
    {
        var result = await _pluginService.GetDetailsBySha256Async(sha256, ct);
        return HandleResult(result);
    }
}
