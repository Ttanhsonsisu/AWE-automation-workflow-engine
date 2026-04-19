using System.Text.Json;
using AWE.Application.Dtos.PluginDtos;
using AWE.Shared.Primitives;

namespace AWE.Application.Abstractions.Validation;

public record PluginExtractionResult(
    string Name, 
    string DisplayName,
    string Description,
    string Category,
    string Icon,
    JsonDocument InputSchema,
    JsonDocument OutputSchema
);

public interface IPluginValidator
{
    Result<PluginMetadataDto> ValidateAssembly(Stream dllStream);

    Result<PluginExtractionResult> ValidateAndExtractSchema(Stream dllStream);

}
