namespace AWE.Application.Dtos.PluginDtos;

public record PluginPackageDto(
    Guid Id,
    string UniqueName,
    string DisplayName,
    string? Description);
