using System;

namespace AWE.Application.UseCases.Workflows.UpdateDefinition;

public class UpdateDefinitionResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
}
