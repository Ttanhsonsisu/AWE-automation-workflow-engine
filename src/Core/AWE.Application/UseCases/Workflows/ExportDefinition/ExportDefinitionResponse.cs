namespace AWE.Application.UseCases.Workflows.ExportDefinition;

public class ExportDefinitionResponse
{
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public object? ExportedJson { get; set; } = default!;
}
