using AWE.Domain.Common;
using AWE.Domain.Enums;

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

    // metadata for categorization and UI
    public string Category { get; private set; }
    public string Icon { get; private set; } = "lucide-box";

    /// Determines how the plugin is executed at runtime
    public PluginExecutionMode ExecutionMode { get; private set; }

    /// <summary>
    /// Navigation property to versions
    /// </summary>
    public virtual ICollection<PluginVersion> Versions { get; private set; } = new List<PluginVersion>();

    // Private constructor for EF Core
    private PluginPackage() : base() { }

    public PluginPackage(string uniqueName,
        string displayName,
        PluginExecutionMode executionMode,
        string category = "Custom",
        string icon = "lucide-box",
        string? description = null)
            : base()
    {
        if (string.IsNullOrWhiteSpace(uniqueName))
        {
            throw new ArgumentException("Unique name cannot be empty", nameof(uniqueName));
        }
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name cannot be empty", nameof(displayName));
        }

        UniqueName = uniqueName;
        DisplayName = displayName;
        ExecutionMode = executionMode;
        Category = category;
        Icon = icon;
        Description = description;
    }

    public void UpdateDetails(string displayName, string category, string icon, string? description)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name cannot be empty", nameof(displayName));
        }

        DisplayName = displayName;
        Category = category;
        Icon = icon;
        Description = description;
        MarkAsUpdated();
    }

    public void UpdateMetadata(string displayName, string? description, string category, string icon)
    {
        DisplayName = displayName;
        Description = description;
        Category = string.IsNullOrWhiteSpace(category) ? "Custom" : category;
        Icon = string.IsNullOrWhiteSpace(icon) ? "lucide-box" : icon;
    }

    public void UpdateDescription(string? description)
    {
        Description = description;
        MarkAsUpdated();
    }
}
