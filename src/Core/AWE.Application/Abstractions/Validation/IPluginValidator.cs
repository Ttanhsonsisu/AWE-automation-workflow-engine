using AWE.Application.Dtos.PluginDtos;
using AWE.Shared.Primitives;

namespace AWE.Application.Abstractions.Validation;

public interface IPluginValidator
{
    Result<PluginMetadataDto> ValidateAssembly(Stream dllStream);
}
