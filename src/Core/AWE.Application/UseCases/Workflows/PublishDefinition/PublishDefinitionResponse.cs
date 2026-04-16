namespace AWE.Application.UseCases.Workflows.PublishDefinition;

public class PublishDefinitionResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool IsPublished { get; set; }
}
