using System.Text.Json;

namespace AWE.Application.Dtos.PluginDtos;

public record PluginVersionDto(
    Guid Id,
    Guid PackageId,
    string Version,
    bool IsActive,
    string? ReleaseNotes,
    // Trả về thẳng cục Magic Box cho Frontend nếu nó cần hiển thị thông số MinIO
    JsonElement ExecutionMetadata,
    JsonElement? ConfigSchema);
