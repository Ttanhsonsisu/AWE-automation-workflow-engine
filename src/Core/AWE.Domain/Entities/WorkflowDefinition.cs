using System.Text.Json;
using AWE.Domain.Common;

namespace AWE.Domain.Entities;

/// <summary>
/// Represents a workflow definition (DAG template)
/// Immutable after publication for version pinning
/// </summary>
public class WorkflowDefinition : AuditableEntity
{
    /// <summary>
    /// Human-readable name of the workflow
    /// </summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Semantic version number (1, 2, 3...)
    /// </summary>
    public int Version { get; private set; }

    /// <summary>
    /// Complete DAG structure stored as JSONB
    /// Contains: Nodes, Edges, Metadata
    /// </summary>
    public JsonDocument DefinitionJson { get; private set; } = null!;

    /// <summary>
    /// Whether this version is published and can be instantiated
    /// </summary>
    public bool IsPublished { get; private set; }

    /// Optional JSON for UI rendering (e.g., layout, node positions)
    public JsonDocument UiJson { get; set; } = null!;

    /// <summary>
    /// Navigation property to instances
    /// </summary>
    public virtual ICollection<WorkflowInstance> Instances { get; private set; } = new List<WorkflowInstance>();

    // Private constructor for EF Core
    private WorkflowDefinition() : base() { }

    public WorkflowDefinition(string name, int version, JsonDocument definitionJson, JsonDocument? uiJson = null)
        : base()
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Workflow name cannot be empty", nameof(name));

        if (version < 1)
            throw new ArgumentException("Version must be >= 1", nameof(version));

        Name = name;
        Version = version;
        DefinitionJson = definitionJson ?? throw new ArgumentNullException(nameof(definitionJson));
        UiJson = uiJson ?? JsonDocument.Parse("{}");
        IsPublished = false;
    }

    public void Publish()
    {
        if (IsPublished)
            throw new InvalidOperationException("Workflow is already published");

        IsPublished = true;
        MarkAsUpdated();
    }

    public void Unpublish()
    {
        IsPublished = false;
        MarkAsUpdated();
    }

    public void UpdateContent(JsonDocument definitionJson, JsonDocument? uiJson = null)
    {
        if (IsPublished)
            throw new InvalidOperationException("Cannot update a published workflow directly. Create a new version instead.");

        DefinitionJson = definitionJson ?? throw new ArgumentNullException(nameof(definitionJson));
        UiJson = uiJson ?? JsonDocument.Parse("{}");
        MarkAsUpdated();
    }
}
