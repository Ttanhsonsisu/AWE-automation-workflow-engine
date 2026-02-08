using System.Text.Json;
using AWE.ApiGateway.Dtos.Requests;
using AWE.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[ApiController]
[Route("api/plugins")]
public class PluginsController : ControllerBase
{
    private readonly IPluginService _pluginService;

    public PluginsController(IPluginService pluginService)
    {
        _pluginService = pluginService;
    }

    // ---------------- Packages ----------------

    [HttpPost("packages")]
    public async Task<IActionResult> CreatePackage(
        [FromBody] CreatePluginPackageRequest req,
        CancellationToken ct)
    {
        var result = await _pluginService.CreatePackageAsync(
            req.UniqueName,
            req.DisplayName,
            req.Description,
            ct);

        return Ok(result);
    }

    [HttpGet("packages")]
    public async Task<IActionResult> ListPackages(CancellationToken ct)
    {
        var result = await _pluginService.ListPackagesAsync(ct);
        return Ok(result);
    }

    // ---------------- Versions ----------------

    [HttpPost("packages/{packageId:guid}/versions")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadVersion(
        [FromRoute] Guid packageId,
        [FromForm] UploadPluginVersionRequest req,
        CancellationToken ct)
    {
        if (req.File is null || req.File.Length == 0)
            return BadRequest("File is required.");

        JsonDocument? schema = null;
        if (!string.IsNullOrWhiteSpace(req.ConfigSchemaJson))
        {
            try { schema = JsonDocument.Parse(req.ConfigSchemaJson); }
            catch { return BadRequest("ConfigSchemaJson is not valid JSON."); }
        }

        await using var stream = req.File.OpenReadStream();

        var result = await _pluginService.UploadVersionAsync(
            packageId: packageId,
            version: req.Version,
            dllStream: stream,
            fileName: req.File.FileName,
            bucket: req.Bucket,
            configSchema: schema,
            releaseNotes: req.ReleaseNotes,
            ct: ct);

        return Ok(result);
    }

    [HttpGet("packages/{packageId:guid}/versions")]
    public async Task<IActionResult> ListVersions(
        [FromRoute] Guid packageId,
        CancellationToken ct)
    {
        var result = await _pluginService.ListVersionsAsync(packageId, ct);
        return Ok(result);
    }

    [HttpGet("versions/{versionId:guid}/download")]
    public async Task<IActionResult> DownloadVersion(
        [FromRoute] Guid versionId,
        CancellationToken ct)
    {
        var stream = await _pluginService.DownloadVersionAsync(versionId, ct);

        // Trả file binary (dll)
        return File(stream, "application/octet-stream", fileDownloadName: $"plugin-{versionId}.dll");
    }

    [HttpPost("versions/{versionId:guid}/activate")]
    public async Task<IActionResult> ActivateVersion(
        [FromRoute] Guid versionId,
        CancellationToken ct)
    {
        await _pluginService.ActivateVersionAsync(versionId, ct);
        return Ok();
    }

    [HttpPost("versions/{versionId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateVersion(
        [FromRoute] Guid versionId,
        CancellationToken ct)
    {
        await _pluginService.DeactivateVersionAsync(versionId, ct);
        return Ok();
    }

    [HttpDelete("versions/{versionId:guid}")]
    public async Task<IActionResult> DeleteVersion(
        [FromRoute] Guid versionId,
        [FromQuery] bool deleteObject = true,
        CancellationToken ct = default)
    {
        await _pluginService.DeleteVersionAsync(versionId, deleteObject, ct);
        return Ok();
    }
}
