using System.Text.Json;
using AWE.Application.Dtos.PluginDtos;
using AWE.Application.Dtos.WorkflowDto;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;

namespace AWE.Application.Services;

public interface IPluginService
{
    // Package
    Task<Result<PluginPackageDto>> CreatePackageAsync(
        string uniqueName,
        string displayName,
        PluginExecutionMode executionMode,
        string category,
        string icon,
        string? description,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<PluginPackageDto>>> ListPackagesAsync(CancellationToken ct = default);

    Task<Result<IReadOnlyList<CatalogGroupDto>>> GetCatalogAsync(CancellationToken ct = default);

    // Version
    Task<Result<PluginVersionDto>> UploadVersionAsync(
        Guid packageId,
        string version,
        Stream dllStream,
        string fileName,
        string bucket,
        string? releaseNotes = null,
        CancellationToken ct = default);

    Task<Result<Stream>> DownloadVersionAsync(Guid versionId, CancellationToken ct = default);

    Task<Result<PluginDetailDto>> GetPluginDetailsAsync(
        PluginExecutionMode mode,
        string? name,
        Guid? packageId,
        string? version,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<PluginVersionDto>>> ListVersionsAsync(Guid packageId, CancellationToken ct = default);

    Task<Result<IReadOnlyList<string>>> ListVersionPackageDropDownAsyn(Guid packageId, CancellationToken ct = default);
    Task<Result> ActivateVersionAsync(Guid versionId, CancellationToken ct = default);
    Task<Result> DeactivateVersionAsync(Guid versionId, CancellationToken ct = default);
    Task<Result> DeleteVersionAsync(Guid versionId, bool deleteObject = true, CancellationToken ct = default);
}
