using AWE.Domain.Enums;

namespace AWE.Application.Dtos.PluginDtos;

public record PluginPackageListItemDto(
    Guid? Id,
    string UniqueName,
    string DisplayName,
    PluginExecutionMode ExecutionMode,
    string Category,
    string Icon,
    string? Description,
    string? LatestVersion,
    bool IsBuiltIn
);
