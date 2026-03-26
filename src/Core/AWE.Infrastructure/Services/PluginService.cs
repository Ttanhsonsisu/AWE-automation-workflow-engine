//using System.Security.Cryptography;
//using System.Text.Json;
//using AWE.Application.Abstractions.Persistence;
//using AWE.Application.Abstractions.Validation; 
//using AWE.Application.Dtos.PluginDtos;
//using AWE.Application.Services;
//using AWE.Domain.Entities;
//using AWE.Domain.Errors;
//using AWE.Shared.Primitives;

//namespace AWE.Infrastructure.Services;

//public class PluginService(
//    IPluginPackageRepository packages,
//    IPluginVersionRepository versions,
//    IUnitOfWork uow,
//    IStorageService storage,
//    IPluginValidator validator) : IPluginService
//{
//    private readonly IPluginPackageRepository _packages = packages;
//    private readonly IPluginVersionRepository _versions = versions;
//    private readonly IUnitOfWork _uow = uow;
//    private readonly IStorageService _storage = storage;
//    private readonly IPluginValidator _validator = validator;

//    // -------- Package --------

//    public async Task<Result<PluginPackageDto>> CreatePackageAsync(
//        string uniqueName,
//        string displayName,
//        string? description,
//        CancellationToken ct = default)
//    {
//        // Kiểm tra logic nghiệp vụ -> Trả về Error
//        if (await _packages.ExistsByUniqueNameAsync(uniqueName, ct))
//        {
//            return PluginErrors.Package.AlreadyExists(uniqueName);
//        }

//        var pkg = new PluginPackage(uniqueName, displayName, description);

//        await _packages.AddAsync(pkg, ct);
//        await _uow.SaveChangesAsync(ct);

//        return new PluginPackageDto(pkg.Id, pkg.UniqueName, pkg.DisplayName, pkg.Description);
//    }

//    public async Task<Result<IReadOnlyList<PluginPackageDto>>> ListPackagesAsync(CancellationToken ct = default)
//    {
//        var list = await _packages.ListAsync(ct);
//        var dtos = list.Select(x => new PluginPackageDto(x.Id, x.UniqueName, x.DisplayName, x.Description)).ToList();

//        return Result.Success<IReadOnlyList<PluginPackageDto>>(dtos);
//    }

//    // -------- Version --------

//    public async Task<Result<PluginVersionDto>> UploadVersionAsync(
//        Guid packageId,
//        string version,
//        Stream dllStream,
//        string fileName,
//        string bucket,
//        JsonDocument? configSchema = null,
//        string? releaseNotes = null,
//        CancellationToken ct = default)
//    {
//        var pkg = await _packages.GetByIdAsync(packageId, ct);
//        if (pkg is null)
//        {
//            return PluginErrors.Package.NotFound(packageId);
//        }

//        if (await _versions.ExistsVersionAsync(packageId, version, ct))
//        {
//            return PluginErrors.Version.AlreadyExists(version);
//        }

//        // Copy stream để xử lý
//        using var ms = new MemoryStream();
//        await dllStream.CopyToAsync(ms, ct);

//        if (ms.Length == 0) return PluginErrors.Version.EmptyDll;

//        // VALIDATE DLL
//        var validationResult = _validator.ValidateAssembly(ms);
//        if (validationResult.IsFailure)
//        {
//            return Result.Failure<PluginVersionDto>(validationResult.Error!);
//        }

//        ms.Position = 0;
//        var sha256 = ComputeSha256Hex(ms);

//        ms.Position = 0;
//        var safeFileName = SanitizeFileName(fileName);
//        var objectKey = $"plugins/{pkg.UniqueName}/{version}/{safeFileName}";

//        try
//        {
//            await _storage.PutObjectAsync(bucket, objectKey, ms, "application/octet-stream", ct);
//        }
//        catch (Exception ex)
//        {
//            return PluginErrors.Version.UploadFailed(ex.Message);
//        }

//        var entity = new PluginVersion(
//            packageId: packageId,
//            version: version,
//            bucket: bucket,
//            objectKey: objectKey,
//            sha256: sha256,
//            size: ms.Length,
//            configSchema: configSchema,
//            releaseNotes: releaseNotes
//        );

//        await _versions.AddAsync(entity, ct);
//        await _uow.SaveChangesAsync(ct);

//        return Map(entity);
//    }

//    public async Task<Result<Stream>> DownloadVersionAsync(Guid versionId, CancellationToken ct = default)
//    {
//        var ver = await _versions.GetByIdAsync(versionId, ct);
//        if (ver is null)
//        {
//            return PluginErrors.Version.NotFound(versionId);
//        }

//        try
//        {
//            var stream = await _storage.GetObjectAsync(ver.Bucket, ver.ObjectKey, ct);
//            return Result.Success(stream);
//        }
//        catch
//        {
//            return PluginErrors.Version.UploadFailed("File not found in storage.");
//        }
//    }

//    public async Task<Result<IReadOnlyList<PluginVersionDto>>> ListVersionsAsync(Guid packageId, CancellationToken ct = default)
//    {
//        // Kiểm tra package tồn tại trước
//        if (!await _packages.ExistsAsync(packageId, ct))
//            return PluginErrors.Package.NotFound(packageId);

//        var list = await _versions.ListByPackageIdAsync(packageId, ct);
//        var dtos = list.Select(Map).ToList();
//        return Result.Success<IReadOnlyList<PluginVersionDto>>(dtos);
//    }

//    public async Task<Result> ActivateVersionAsync(Guid versionId, CancellationToken ct = default)
//    {
//        var ver = await _versions.GetByIdAsync(versionId, ct);
//        if (ver is null) return PluginErrors.Version.NotFound(versionId);

//        ver.Activate();
//        await _uow.SaveChangesAsync(ct);
//        return Result.Success();
//    }

//    public async Task<Result> DeactivateVersionAsync(Guid versionId, CancellationToken ct = default)
//    {
//        var ver = await _versions.GetByIdAsync(versionId, ct);
//        if (ver is null) return PluginErrors.Version.NotFound(versionId);

//        ver.Deactivate();
//        await _uow.SaveChangesAsync(ct);
//        return Result.Success();
//    }

//    public async Task<Result> DeleteVersionAsync(Guid versionId, bool deleteObject = true, CancellationToken ct = default)
//    {
//        var ver = await _versions.GetByIdAsync(versionId, ct);
//        if (ver is null) return PluginErrors.Version.NotFound(versionId);

//        if (deleteObject)
//        {
//            try { await _storage.DeleteObjectAsync(ver.Bucket, ver.ObjectKey, ct); }
//            catch { /* Log warning nhưng vẫn xoá DB */ }
//        }

//        _versions.Remove(ver);
//        await _uow.SaveChangesAsync(ct);
//        return Result.Success();
//    }

//    // Helpers
//    private static PluginVersionDto Map(PluginVersion x) =>
//        new(x.Id, x.PackageId, x.Version, x.Bucket, x.ObjectKey, x.Sha256, x.Size, x.StorageProvider, x.IsActive, x.ReleaseNotes);

//    private static string ComputeSha256Hex(Stream stream)
//    {
//        using var sha = SHA256.Create();
//        var hash = sha.ComputeHash(stream);
//        return Convert.ToHexString(hash).ToLowerInvariant();
//    }

//    private static string SanitizeFileName(string fileName)
//    {
//        var invalid = Path.GetInvalidFileNameChars();
//        var cleaned = string.Join("_", fileName.Split(invalid, StringSplitOptions.RemoveEmptyEntries)).Trim();
//        return string.IsNullOrWhiteSpace(cleaned) ? "plugin.dll" : cleaned;
//    }
//}
