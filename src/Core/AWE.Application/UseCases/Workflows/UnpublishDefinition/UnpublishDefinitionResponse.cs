namespace AWE.Application.UseCases.Workflows.UnpublishDefinition;

public class UnpublishDefinitionResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool IsPublished { get; set; }
}
