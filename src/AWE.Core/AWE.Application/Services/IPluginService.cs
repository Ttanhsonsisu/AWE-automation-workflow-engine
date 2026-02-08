using System.Text.Json;
using AWE.Application.Dtos.PluginDtos;

namespace AWE.Application.Services;

public interface IPluginService
{
    // Package
    Task<PluginPackageDto> CreatePackageAsync(
        string uniqueName,
        string displayName,
        string? description,
        CancellationToken ct = default);

    Task<IReadOnlyList<PluginPackageDto>> ListPackagesAsync(CancellationToken ct = default);

    // Version
    Task<PluginVersionDto> UploadVersionAsync(
        Guid packageId,
        string version,
        Stream dllStream,
        string fileName, // để build objectKey đẹp + giữ extension
        string bucket,
        JsonDocument? configSchema = null,
        string? releaseNotes = null,
        CancellationToken ct = default);

    Task<Stream> DownloadVersionAsync(Guid versionId, CancellationToken ct = default);

    Task<IReadOnlyList<PluginVersionDto>> ListVersionsAsync(Guid packageId, CancellationToken ct = default);

    Task ActivateVersionAsync(Guid versionId, CancellationToken ct = default);
    Task DeactivateVersionAsync(Guid versionId, CancellationToken ct = default);

    Task DeleteVersionAsync(Guid versionId, bool deleteObject = true, CancellationToken ct = default);
}
