using System.Security.Cryptography;
using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Application.Abstractions.Validation;
using AWE.Application.Dtos.PluginDtos;
using AWE.Application.Dtos.WorkflowDto;
using AWE.Application.Extensions;
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
    IPluginValidator validator,
    AWE.Application.Abstractions.CoreEngine.IPluginRegistry pluginRegistry) : IPluginService
{
    private readonly IPluginPackageRepository _packages = packages;
    private readonly IPluginVersionRepository _versions = versions;
    private readonly IUnitOfWork _uow = uow;
    private readonly IStorageService _storage = storage;
    private readonly IPluginValidator _validator = validator;
    private readonly AWE.Application.Abstractions.CoreEngine.IPluginRegistry _pluginRegistry = pluginRegistry;

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

    public async Task<Result<PluginDetailDto>> GetPluginDetailsAsync(
        PluginExecutionMode mode, string? name, Guid? packageId, string? version, CancellationToken ct = default)
    {
        if (mode == PluginExecutionMode.BuiltIn)
        {
            if (string.IsNullOrWhiteSpace(name))
                return Result.Failure<PluginDetailDto>(Error.Validation("MissingName", "Built-in plugin requires 'name'."));

            var plugin = _pluginRegistry.GetPlugin(name);
            return new PluginDetailDto(
                Name: plugin.Name, DisplayName: plugin.DisplayName, ExecutionMode: mode.ToString(), Version: null, ExecutionMetadata: null,
                InputSchema: ParseSchema(PluginSchemaGenerator.GenerateSchema(plugin.InputType)),
                OutputSchema: ParseSchema(PluginSchemaGenerator.GenerateSchema(plugin.OutputType))
            );
        }

        if (mode == PluginExecutionMode.DynamicDll)
        {
            if (packageId == null)
                return Result.Failure<PluginDetailDto>(Error.Validation("MissingPackageId", "Custom plugin requires 'packageId'."));

            var pkg = await _packages.GetByIdAsync(packageId.Value, ct);
            if (pkg == null) return PluginErrors.Package.NotFound(packageId.Value);

            var targetVersion = string.IsNullOrWhiteSpace(version)
                ? pkg.Versions.FirstOrDefault(v => v.IsActive)
                : pkg.Versions.FirstOrDefault(v => v.Version == version);

            if (targetVersion == null)
                return Result.Failure<PluginDetailDto>(Error.NotFound("VersionNotFound", "No active version found."));

            var outSchema = targetVersion.ExecutionMetadata.RootElement.TryGetProperty("OutputSchema", out var schemaProp)
            ? schemaProp.Clone()
            : ParseSchema("{}");

            return new PluginDetailDto(
                Name: pkg.UniqueName, DisplayName: pkg.DisplayName, ExecutionMode: mode.ToString(), Version: targetVersion.Version,
                ExecutionMetadata: targetVersion.ExecutionMetadata.RootElement,
                InputSchema: targetVersion.ConfigSchema?.RootElement ?? ParseSchema("{}"),
                OutputSchema: outSchema
            );
        }

        return Result.Failure<PluginDetailDto>(Error.Validation("InvalidMode", "Unsupported Execution Mode."));
    }

    public async Task<Result<IReadOnlyList<CatalogGroupDto>>> GetCatalogAsync(CancellationToken ct = default)
    {
        var catalog = new List<CatalogItemDto>();

        var builtInPlugins = _pluginRegistry.GetAllPlugins();
        var builtInItems = builtInPlugins.Select(p => new CatalogItemDto(
            PackageId: null,
            ActiveVersion: "Built-in",
            Name: p.Name,
            DisplayName: p.DisplayName,
            Description: p.Description,
            Category: p.Category,
            Icon: p.Icon,
            ExecutionMode: PluginExecutionMode.BuiltIn.ToString(),
            InputSchema: ParseSchema(PluginSchemaGenerator.GenerateSchema(p.InputType)),
        OutputSchema: ParseSchema(PluginSchemaGenerator.GenerateSchema(p.OutputType))
        ));

        catalog.AddRange(builtInItems);

        var customPackages = await _packages.ListAsync(ct);

        foreach (var pkg in customPackages)
        {
            var latestActiveVersion = pkg.Versions
                .Where(v => v.IsActive)
                .MaxBy(v => v.CreatedAt);

            if (latestActiveVersion == null) continue;

            catalog.Add(new CatalogItemDto(
                PackageId: pkg.Id,
                ActiveVersion: latestActiveVersion.Version,
                Name: pkg.UniqueName,
                DisplayName: pkg.DisplayName,
                Description: pkg.Description,
                Category: pkg.Category ?? "Custom",
                Icon: pkg.Icon ?? "lucide-box",
                ExecutionMode: pkg.ExecutionMode.ToString(),
                InputSchema: latestActiveVersion.ConfigSchema?.RootElement ?? ParseSchema("{}"),
                OutputSchema: ParseSchema("{}")
            ));
        }

        var groupedCatalog = catalog
            .OrderBy(p => p.Category)
            .ThenBy(p => p.DisplayName)
            .GroupBy(p => p.Category)
            .Select(g => new CatalogGroupDto(g.Key, g.ToList()))
            .ToList();

        return Result.Success<IReadOnlyList<CatalogGroupDto>>(groupedCatalog);
    }

    private JsonElement ParseSchema(string schemaString)
    {
        if (string.IsNullOrWhiteSpace(schemaString))
            schemaString = "{}";

        try
        {
            using var doc = JsonDocument.Parse(schemaString);
            return doc.RootElement.Clone();
        }
        catch
        {
            using var doc = JsonDocument.Parse("{}");
            return doc.RootElement.Clone();
        }
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

        var extracted = validationResult.Value;

        // Cập nhật Metadata cho Package từ thông tin trích xuất được 
        pkg.UpdateMetadata(
            displayName: extracted.DisplayName,
            description: extracted.Description,
            category: extracted.Category,
            icon: extracted.Icon
        );

        // 1. Tính SHA256
        ms.Position = 0;
        var sha256 = ComputeSha256Hex(ms);

        // 3. Upload lên Storage
        ms.Position = 0;
        var safeFileName = SanitizeFileName(fileName);
        var objectKey = $"plugins/{pkg.UniqueName}/{sha256}.dll";

        try
        {
            await _storage.PutObjectAsync(bucket, objectKey, ms, "application/octet-stream", ct);
        }
        catch (Exception ex)
        {
            return PluginErrors.Version.UploadFailed(ex.Message);
        }

        // 4. Đóng gói ExecutionMetadata
        var metadata = new {
            PluginType = extracted.Name,
            Bucket = bucket,
            ObjectKey = objectKey,
            Sha256 = sha256,
            Size = ms.Length,
            OutputSchema = extracted.OutputSchema
        };

        var metadataJson = JsonSerializer.SerializeToDocument(metadata);

        var entity = new PluginVersion(
            packageId: packageId,
            version: version,
            executionMetadata: metadataJson,
            configSchema: extracted.InputSchema,
            releaseNotes: releaseNotes
        );

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
        {
            return PluginErrors.Package.NotFound(packageId);
        }
        var list = await _versions.ListByPackageIdAsync(packageId, ct);
        var dtos = list.Select(Map).ToList();
        return Result.Success<IReadOnlyList<PluginVersionDto>>(dtos);
    }

    public async Task<Result<IReadOnlyList<string>>> ListVersionPackageDropDownAsyn(Guid packageId, CancellationToken ct = default)
    {
        if (!await _packages.ExistsAsync(packageId, ct))
        {
            return PluginErrors.Package.NotFound(packageId);
        }

        var list = await _versions.ListByPackageIdAsync(packageId, ct);
        var dtos = list.Select(x => x.Version).ToList();

        return Result.Success<IReadOnlyList<string>>(dtos);
    }

    public async Task<Result> DeleteVersionAsync(Guid versionId, bool deleteObject = true, CancellationToken ct = default)
    {
        var ver = await _versions.GetByIdAsync(versionId, ct);
        if (ver is null)
        {
            return PluginErrors.Version.NotFound(versionId);
        }

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
