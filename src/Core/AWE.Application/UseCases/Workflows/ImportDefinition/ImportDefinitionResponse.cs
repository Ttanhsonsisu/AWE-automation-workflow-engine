using System;

namespace AWE.Application.UseCases.Workflows.ImportDefinition;

public class ImportDefinitionResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
}
