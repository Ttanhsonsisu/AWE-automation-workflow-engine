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

    /// <summary>
    /// Physical path to the plugin assembly DLL
    /// Used by AssemblyLoadContext to load the plugin at runtime
    /// </summary>
    public string ObjectKey { get; private set; } = string.Empty;

    public string Bucket { get; private set; } = string.Empty;
    public string Sha256 { get; private set; } = string.Empty;
    public long Size { get; private set;  }
    public string StorageProvider { get; private set; } = string.Empty;


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
        string bucket,
        string objectKey,
        string sha256,
        long size,
        JsonDocument? configSchema = null,
        string? releaseNotes = null,
        string? storageProvider = "MinIO")
        : base()
    {
        if (packageId == Guid.Empty)
            throw new ArgumentException("PackageId cannot be empty", nameof(packageId));

        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version cannot be empty", nameof(version));

        if (string.IsNullOrWhiteSpace(bucket))
            throw new ArgumentException("Bucket cannot be empty", nameof(bucket));

        if (string.IsNullOrWhiteSpace(objectKey))
            throw new ArgumentException("ObjectKey cannot be empty", nameof(objectKey));

        if (string.IsNullOrWhiteSpace(sha256) || sha256.Length != 64)
            throw new ArgumentException("Sha256 must be a 64-character hex string", nameof(sha256));

        if (size <= 0)
            throw new ArgumentException("Size must be greater than 0", nameof(size));

        PackageId = packageId;
        Version = version;
        Bucket = bucket;
        ObjectKey = objectKey;
        Sha256 = sha256;
        Size = size;

        StorageProvider = storageProvider;
        ConfigSchema = configSchema;
        ReleaseNotes = releaseNotes;

        IsActive = true;
    }

    public void Activate() { IsActive = true; MarkAsUpdated(); } 
    public void Deactivate() { IsActive = false; MarkAsUpdated(); }
    public void UpdateConfigSchema(JsonDocument configSchema) { ConfigSchema = configSchema; MarkAsUpdated(); }
}
