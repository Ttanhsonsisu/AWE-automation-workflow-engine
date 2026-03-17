using AWE.Domain.Common;

namespace AWE.Domain.Entities;

/// <summary>
/// Represents a plugin package (top-level container)
/// </summary>
public class PluginPackage : AuditableEntity
{
    /// <summary>
    /// Unique name for the plugin (e.g., "AWE.HttpPlugin")
    /// Used for loading and referencing
    /// </summary>
    public string UniqueName { get; private set; } = string.Empty;

    /// <summary>
    /// Display name shown in UI Designer
    /// </summary>
    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>
    /// Brief description of what this plugin does
    /// </summary>
    public string? Description { get; private set; }

    /// <summary>
    /// Navigation property to versions
    /// </summary>
    public virtual ICollection<PluginVersion> Versions { get; private set; } = new List<PluginVersion>();

    // Private constructor for EF Core
    private PluginPackage() : base() { }

    public PluginPackage(string uniqueName, string displayName, string? description = null)
        : base()
    {
        if (string.IsNullOrWhiteSpace(uniqueName))
            throw new ArgumentException("Unique name cannot be empty", nameof(uniqueName));

        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name cannot be empty", nameof(displayName));

        UniqueName = uniqueName;
        DisplayName = displayName;
        Description = description;
    }

    public void UpdateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("Display name cannot be empty", nameof(displayName));

        DisplayName = displayName;
        MarkAsUpdated();
    }

    public void UpdateDescription(string? description)
    {
        Description = description;
        MarkAsUpdated();
    }
}
