using AWE.Domain.Common;
using System.Text.Json;

namespace AWE.Domain.Entities;

/// <summary>
/// Represents a specific version of a plugin package
/// Multiple versions can coexist for backward compatibility
/// </summary>
public class PluginVersion : AuditableEntity
{
    /// <summary>
    /// Reference to parent package
    /// </summary>
    public Guid PackageId { get; private set; }

    /// <summary>
    /// Semantic version (e.g., "1.0.0", "2.1.3")
    /// </summary>
    public string Version { get; private set; } = string.Empty;

    // Storage information for the plugin assembly DLL (thay cho Bucket, Key, Sha256...)
    public JsonDocument ExecutionMetadata { get; private set; } = null!;


    /// <summary>
    /// JSON Schema for configuration validation
    /// Used by frontend to render configuration forms
    /// </summary>
    public JsonDocument? ConfigSchema { get; private set; }

    /// <summary>
    /// Whether this version is active and can be used in new workflows
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    /// Release notes for this version
    /// </summary>
    public string? ReleaseNotes { get; private set; }

    /// <summary>
    /// Navigation property
    /// </summary>
    public virtual PluginPackage Package { get; private set; } = null!;

    // Private constructor for EF Core
    private PluginVersion() : base() { }

    public PluginVersion(
        Guid packageId,
        string version,
        JsonDocument executionMetadata,
        JsonDocument? configSchema = null,
        string? releaseNotes = null)
        : base()
    {
        if (packageId == Guid.Empty)
        {
            throw new ArgumentException("PackageId cannot be empty", nameof(packageId));
        }
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Version cannot be empty", nameof(version));
        } 

        PackageId = packageId;
        Version = version;
        ExecutionMetadata = executionMetadata; // Dữ liệu kỹ thuật
        ConfigSchema = configSchema; // Dữ liệu giao diện
        ReleaseNotes = releaseNotes;
        IsActive = true;
    }

    public void Activate() { IsActive = true; MarkAsUpdated(); } 
    public void Deactivate() { IsActive = false; MarkAsUpdated(); }
    public void UpdateConfigSchema(JsonDocument configSchema) { ConfigSchema = configSchema; MarkAsUpdated(); }
}
