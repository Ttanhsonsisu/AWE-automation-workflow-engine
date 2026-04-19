using System;

namespace AWE.Application.UseCases.Workflows.CloneDefinition;

public class CloneDefinitionRequest
{
    public Guid SourceDefinitionId { get; set; }
    public string NewName { get; set; } = string.Empty;
}
