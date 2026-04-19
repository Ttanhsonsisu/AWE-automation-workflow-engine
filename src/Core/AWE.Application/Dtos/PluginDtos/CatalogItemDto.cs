using System.Text.Json;

namespace AWE.Application.Dtos.PluginDtos;

public record CatalogItemDto(
    Guid? PackageId,
    string? ActiveVersion,
    string Name,
    string DisplayName,
    string? Description,
    string Category,
    string Icon,
    string ExecutionMode,
    JsonElement InputSchema,
    JsonElement OutputSchema,
    string? TriggerSource,
    bool IsSingleton
);

public record CatalogGroupDto(
    string Category,
    IReadOnlyList<CatalogItemDto> Plugins
);
