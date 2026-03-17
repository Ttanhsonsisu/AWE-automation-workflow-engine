namespace AWE.Application.Dtos.PluginDtos;

public record PluginVersionDto(
    Guid Id,
    Guid PackageId,
    string Version,
    string Bucket,
    string ObjectKey,
    string Sha256,
    long Size,
    string StorageProvider,
    bool IsActive,
    string? ReleaseNotes);
