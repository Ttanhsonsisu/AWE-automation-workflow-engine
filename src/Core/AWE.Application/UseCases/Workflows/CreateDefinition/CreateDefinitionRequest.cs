using System.Text.Json;

namespace AWE.Application.UseCases.Workflows.CreateDefinition;

public class CreateDefinitionRequest
{
    public string Name { get; set; } = string.Empty;
    public JsonDocument DefinitionJson { get; set; } = null!;
    public JsonDocument? UiJson { get; set; }
}
