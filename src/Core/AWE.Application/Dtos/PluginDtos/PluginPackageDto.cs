using AWE.Domain.Enums;

namespace AWE.Application.Dtos.PluginDtos;

public record PluginPackageDto(
    Guid Id,
    string UniqueName,
    string DisplayName,
    PluginExecutionMode ExecutionMode, 
    string Category,                 
    string Icon,                    
    string? Description
);
