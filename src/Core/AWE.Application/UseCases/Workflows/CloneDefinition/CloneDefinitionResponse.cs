using System;

namespace AWE.Application.UseCases.Workflows.CloneDefinition;

public class CloneDefinitionResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
}
