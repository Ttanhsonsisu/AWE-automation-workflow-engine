using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Application.Dtos.PluginDtos;
using AWE.Application.Services;
using AWE.Domain.Entities;
using AWE.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Services;

public class PluginService : IPluginService
{
    private readonly IPluginPackageRepository _packages;
    private readonly IPluginVersionRepository _versions;
    private readonly IUnitOfWork _uow;
    private readonly IStorageService _storage;

    public PluginService(
        IPluginPackageRepository packages,
        IPluginVersionRepository versions,
        IUnitOfWork uow,
        IStorageService storage)
    {
        _packages = packages;
        _versions = versions;
        _uow = uow;
        _storage = storage;
    }

    // -------- Package --------

    public async Task<PluginPackageDto> CreatePackageAsync(
        string uniqueName,
        string displayName,
        string? description,
        CancellationToken ct = default)
    {
        if (await _packages.ExistsByUniqueNameAsync(uniqueName, ct))
            throw new InvalidOperationException($"PluginPackage '{uniqueName}' already exists.");

        var pkg = new PluginPackage(uniqueName, displayName, description);

        await _packages.AddAsync(pkg, ct);
        await _uow.SaveChangesAsync(ct);

        return new PluginPackageDto(pkg.Id, pkg.UniqueName, pkg.DisplayName, pkg.Description);
    }

    public async Task<IReadOnlyList<PluginPackageDto>> ListPackagesAsync(CancellationToken ct = default)
    {
        var list = await _packages.ListAsync(ct);
        return list.Select(x => new PluginPackageDto(x.Id, x.UniqueName, x.DisplayName, x.Description)).ToList();
    }

    // -------- Version --------

    public async Task<PluginVersionDto> UploadVersionAsync(
        Guid packageId,
        string version,
        Stream dllStream,
        string fileName,
        string bucket,
        JsonDocument? configSchema = null,
        string? releaseNotes = null,
        CancellationToken ct = default)
    {
        var pkg = await _packages.GetByIdAsync(packageId, ct)
            ?? throw new KeyNotFoundException($"PluginPackage '{packageId}' not found.");

        if (await _versions.ExistsVersionAsync(packageId, version, ct))
            throw new InvalidOperationException($"Version '{version}' already exists for package '{pkg.UniqueName}'.");

        using var ms = new MemoryStream();
        await dllStream.CopyToAsync(ms, ct);
        ms.Position = 0;

        if (ms.Length <= 0) throw new InvalidOperationException("Empty DLL stream.");

        var sha256 = ComputeSha256Hex(ms);
        ms.Position = 0;

        var safeFileName = SanitizeFileName(fileName);
        var objectKey = $"plugins/{pkg.UniqueName}/{version}/{safeFileName}";

        await _storage.PutObjectAsync(bucket, objectKey, ms, "application/octet-stream", ct);

        var entity = new PluginVersion(
            packageId: packageId,
            version: version,
            bucket: bucket,
            objectKey: objectKey,
            sha256: sha256,
            size: ms.Length,
            configSchema: configSchema,
            releaseNotes: releaseNotes,
            storageProvider: "MinIO"
        );

        await _versions.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return Map(entity);
    }

    public async Task<Stream> DownloadVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var ver = await _versions.GetByIdAsync(versionId, ct)
            ?? throw new KeyNotFoundException($"PluginVersion '{versionId}' not found.");

        return await _storage.GetObjectAsync(ver.Bucket, ver.ObjectKey, ct);
    }

    public async Task<IReadOnlyList<PluginVersionDto>> ListVersionsAsync(Guid packageId, CancellationToken ct = default)
    {
        var list = await _versions.ListByPackageIdAsync(packageId, ct);
        return list.Select(Map).ToList();
    }

    public async Task ActivateVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var ver = await _versions.GetByIdAsync(versionId, ct)
            ?? throw new KeyNotFoundException($"PluginVersion '{versionId}' not found.");

        ver.Activate();
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeactivateVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var ver = await _versions.GetByIdAsync(versionId, ct)
            ?? throw new KeyNotFoundException($"PluginVersion '{versionId}' not found.");

        ver.Deactivate();
        await _uow.SaveChangesAsync(ct);
    }

    public async Task DeleteVersionAsync(Guid versionId, bool deleteObject = true, CancellationToken ct = default)
    {
        var ver = await _versions.GetByIdAsync(versionId, ct);
        if (ver is null) return;

        if (deleteObject)
            await _storage.DeleteObjectAsync(ver.Bucket, ver.ObjectKey, ct);

        _versions.Remove(ver);
        await _uow.SaveChangesAsync(ct);
    }

    // -------- helpers --------

    private static PluginVersionDto Map(PluginVersion x)
        => new(
            x.Id, x.PackageId, x.Version, x.Bucket, x.ObjectKey,
            x.Sha256, x.Size, x.StorageProvider, x.IsActive, x.ReleaseNotes);

    private static string ComputeSha256Hex(Stream stream)
    {
        stream.Position = 0;
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        stream.Position = 0;

        var sb = new StringBuilder(64);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(fileName.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "plugin.dll" : cleaned;
    }
}
