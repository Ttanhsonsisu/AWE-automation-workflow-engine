using System;
using System.Text.Json;

namespace AWE.Application.UseCases.Workflows.UpdateDefinition;

public class UpdateDefinitionRequest
{
    public Guid Id { get; set; }
    public JsonDocument DefinitionJson { get; set; } = null!;
    public JsonDocument? UiJson { get; set; }
}
