using System.Security.Cryptography;
using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Application.Abstractions.Validation;
using AWE.Application.Dtos.PluginDtos;
using AWE.Application.Services;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Domain.Errors;
using AWE.Shared.Primitives;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;

namespace AWE.Infrastructure.Services;

public class PluginService(
    IPluginPackageRepository packages,
    IPluginVersionRepository versions,
    IUnitOfWork uow,
    IStorageService storage,
    IPluginValidator validator) : IPluginService
{
    private readonly IPluginPackageRepository _packages = packages;
    private readonly IPluginVersionRepository _versions = versions;
    private readonly IUnitOfWork _uow = uow;
    private readonly IStorageService _storage = storage;
    private readonly IPluginValidator _validator = validator;

    // -------- Package --------

    public async Task<Result<PluginPackageDto>> CreatePackageAsync(
        string uniqueName,
        string displayName,
        PluginExecutionMode executionMode, 
        string category,                 
        string icon,                      
        string? description,
        CancellationToken ct = default)
    {
        if (await _packages.ExistsByUniqueNameAsync(uniqueName, ct))
        {
            return PluginErrors.Package.AlreadyExists(uniqueName);
        }

        var pkg = new PluginPackage(uniqueName, displayName, executionMode, category, icon, description);

        await _packages.AddAsync(pkg, ct);
        await _uow.SaveChangesAsync(ct);

        return new PluginPackageDto(pkg.Id, pkg.UniqueName, pkg.DisplayName,pkg.ExecutionMode,pkg.Category, pkg.Icon, pkg.Description);
    }

    public async Task<Result<IReadOnlyList<PluginPackageDto>>> ListPackagesAsync(CancellationToken ct = default)
    {
        var list = await _packages.ListAsync(ct);
        var dtos = list.Select(x => new PluginPackageDto(x.Id, x.UniqueName, x.DisplayName, x.ExecutionMode, x.Category, x.Icon, x.Description)).ToList();

        return Result.Success<IReadOnlyList<PluginPackageDto>>(dtos);
    }

    // -------- Version --------

    public async Task<Result<PluginVersionDto>> UploadVersionAsync(
        Guid packageId,
        string version,
        Stream dllStream,
        string fileName,
        string bucket, 
        string? releaseNotes = null,
        CancellationToken ct = default)
    {
        var pkg = await _packages.GetByIdAsync(packageId, ct);
        if (pkg is null) return PluginErrors.Package.NotFound(packageId);

        if (pkg.ExecutionMode != PluginExecutionMode.DynamicDll)
            return Result.Failure<PluginVersionDto>(Error.Validation("InvalidMode", "Package này không hỗ trợ upload DLL."));

        if (await _versions.ExistsVersionAsync(packageId, version, ct))
            return PluginErrors.Version.AlreadyExists(version);

        using var ms = new MemoryStream();
        await dllStream.CopyToAsync(ms, ct);
        if (ms.Length == 0) return PluginErrors.Version.EmptyDll;

        ms.Position = 0;
        var validationResult = _validator.ValidateAndExtractSchema(ms);
        if (validationResult.IsFailure)
        {
            return Result.Failure<PluginVersionDto>(validationResult.Error!);
        }

        JsonDocument configSchema = validationResult.Value;

        // 1. Tính SHA256
        ms.Position = 0;
        var sha256 = ComputeSha256Hex(ms);

        // 3. Upload lên Storage
        ms.Position = 0;
        var safeFileName = SanitizeFileName(fileName);
        var objectKey = $"plugins/{pkg.UniqueName}/{version}/{sha256}.dll";

        try
        {
            await _storage.PutObjectAsync(bucket, objectKey, ms, "application/octet-stream", ct);
        }
        catch (Exception ex)
        {
            return PluginErrors.Version.UploadFailed(ex.Message);
        }

        // 4. Đóng gói ExecutionMetadata
        var metadata = new { Bucket = bucket, ObjectKey = objectKey, Sha256 = sha256, Size = ms.Length };
        var metadataJson = JsonSerializer.SerializeToDocument(metadata);

        var entity = new PluginVersion(
            packageId: packageId,
            version: version,
            executionMetadata: metadataJson,
            configSchema: configSchema,
            releaseNotes: releaseNotes
        );

        // Đảm bảo chỉ có 1 bản Active
        foreach (var v in pkg.Versions)
        {
            v.Deactivate();
        }

        await _versions.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);

        return Map(entity);
    }

    public async Task<Result<Stream>> DownloadVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var ver = await _versions.GetByIdAsync(versionId, ct);
        if (ver is null) return PluginErrors.Version.NotFound(versionId);

        // Đọc Metadata JSON thay vì cột vật lý
        var meta = ver.ExecutionMetadata.RootElement;
        string bucket = meta.GetProperty("Bucket").GetString()!;
        string objectKey = meta.GetProperty("ObjectKey").GetString()!;

        try
        {
            var stream = await _storage.GetObjectAsync(bucket, objectKey, ct);
            return Result.Success(stream);
        }
        catch
        {
            return PluginErrors.Version.UploadFailed("File not found in storage.");
        }
    }

    public async Task<Result<IReadOnlyList<PluginVersionDto>>> ListVersionsAsync(Guid packageId, CancellationToken ct = default)
    {
        // Kiểm tra package tồn tại trước
        if (!await _packages.ExistsAsync(packageId, ct))
            return PluginErrors.Package.NotFound(packageId);
        var list = await _versions.ListByPackageIdAsync(packageId, ct);
        var dtos = list.Select(Map).ToList();
        return Result.Success<IReadOnlyList<PluginVersionDto>>(dtos);
    }

    public async Task<Result> DeleteVersionAsync(Guid versionId, bool deleteObject = true, CancellationToken ct = default)
    {
        var ver = await _versions.GetByIdAsync(versionId, ct);
        if (ver is null) return PluginErrors.Version.NotFound(versionId);

        if (deleteObject)
        {
            try
            {
                var meta = ver.ExecutionMetadata.RootElement;
                string bucket = meta.GetProperty("Bucket").GetString()!;
                string objectKey = meta.GetProperty("ObjectKey").GetString()!;
                await _storage.DeleteObjectAsync(bucket, objectKey, ct);
            }
            catch { /* Log warning */ }
        }

        _versions.Remove(ver);
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> ActivateVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var ver = await _versions.GetByIdAsync(versionId, ct);
        if (ver is null) return PluginErrors.Version.NotFound(versionId);

        ver.Activate();
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result> DeactivateVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var ver = await _versions.GetByIdAsync(versionId, ct);
        if (ver is null) return PluginErrors.Version.NotFound(versionId);

        ver.Deactivate();
        await _uow.SaveChangesAsync(ct);
        return Result.Success();
    }

    // Helpers
    // DTO của bạn cần update lại các tham số cho khớp nhé! Tạm thời mình map cơ bản
    private static PluginVersionDto Map(PluginVersion x) =>
        new(
            Id: x.Id,
            PackageId: x.PackageId,
            Version: x.Version,
            IsActive: x.IsActive,
            ReleaseNotes: x.ReleaseNotes,
            ExecutionMetadata: x.ExecutionMetadata.RootElement,
            ConfigSchema: x.ConfigSchema?.RootElement
        );

    private static string ComputeSha256Hex(Stream stream)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "plugin.dll" : cleaned;
    }

}
