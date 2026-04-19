using System;

namespace AWE.Application.UseCases.Workflows.CreateDefinition;

public class CreateDefinitionResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; } = string.Empty;
    public int Version { get; set; }
}
