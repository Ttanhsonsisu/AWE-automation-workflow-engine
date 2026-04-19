using System.Collections.Concurrent;
using System.Text.Json;
using AWE.Application.Services;
using AWE.Sdk.v2;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AWE.Infrastructure.Plugins;

// DTO Định kiểu rõ ràng cho Metadata để tránh lỗi Magic String
public record PluginExecutionMetadata(
    string Bucket,
    string ObjectKey,
    string Sha256,
    string PluginType // Đã được thêm vào ở bước Auto-Discovery trước đó
);

public class CachedPluginContext
{
    public required string Sha256 { get; set; }
    public required PluginLoadContext Alc { get; set; }
    public required Type PluginType { get; set; }
    public required string CacheDirectory { get; set; }
    public DateTime LastAccessedAt { get; set; }
}

public class PluginCacheManager
{
    private readonly ILogger<PluginCacheManager> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IStorageService _storageService;

    // LỚP CACHE 2 (RAM): Chứa ALC và Type. Key là mã SHA256
    private readonly ConcurrentDictionary<string, CachedPluginContext> _hotPlugins = new();

    // Lock để tránh tình trạng 10 request đến cùng lúc bắt Worker tải file MinIO 10 lần
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadLocks = new();

    public PluginCacheManager(ILogger<PluginCacheManager> logger, IServiceProvider serviceProvider, IStorageService storageService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _storageService = storageService;
    }

    public async Task<PluginResult> ExecutePluginAsync(string metadataJson, string payload, CancellationToken ct)
    {
        return await RunPluginAsync(metadataJson, payload, ct, isCompensation: false);
    }

    public async Task<PluginResult> CompensatePluginAsync(string metadataJson, string payload, CancellationToken ct)
    {
        return await RunPluginAsync(metadataJson, payload, ct, isCompensation: true);
    }

    private async Task<PluginResult> RunPluginAsync(string metadataJson, string payload, CancellationToken ct, bool isCompensation)
    {
        // 1. Đọc Metadata
        var meta = JsonSerializer.Deserialize<PluginExecutionMetadata>(metadataJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (meta == null || string.IsNullOrEmpty(meta.Sha256))
        {
            return PluginResult.Failure("ExecutionMetadata không hợp lệ hoặc thiếu Sha256.");
        }

        // 2. Lấy từ RAM hoặc Tải về
        var cachedContext = await GetOrLoadPluginTypeAsync(meta, ct);
        cachedContext.LastAccessedAt = DateTime.UtcNow;

        // 3. THỰC THI (Luôn dùng Scope mới để DI an toàn)
        using var scope = _serviceProvider.CreateScope();

        if (ActivatorUtilities.CreateInstance(scope.ServiceProvider, cachedContext.PluginType) is not IWorkflowPlugin pluginInstance)
        {
            return PluginResult.Failure($"Không thể khởi tạo Plugin type: {cachedContext.PluginType.Name}");
        }

        // Truyền payload đã được phân giải (resolved) vào Context
        var pluginContext = new PluginContext(payload, ct);

        try
        {
            return isCompensation
                ? await pluginInstance.CompensateAsync(pluginContext)
                : await pluginInstance.ExecuteAsync(pluginContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 Lỗi Runtime khi chạy Plugin (SHA256: {Sha256}, Type: {Type})", meta.Sha256, meta.PluginType);
            throw;
        }
    }

    private async Task<CachedPluginContext> GetOrLoadPluginTypeAsync(PluginExecutionMetadata meta, CancellationToken ct)
    {
        if (_hotPlugins.TryGetValue(meta.Sha256, out var hotContext)) return hotContext;

        var asyncLock = _downloadLocks.GetOrAdd(meta.Sha256, _ => new SemaphoreSlim(1, 1));
        await asyncLock.WaitAsync(ct);

        try
        {
            // Double-check
            if (_hotPlugins.TryGetValue(meta.Sha256, out hotContext)) return hotContext;

            string cacheDir = Path.Combine(AppContext.BaseDirectory, "PluginCache", meta.Sha256);
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            // Bỏ version ra khỏi đường dẫn vật lý theo chuẩn Deduplication chúng ta đã chốt
            string localDllPath = Path.Combine(cacheDir, $"{meta.Sha256}.dll");

            // CACHE LAYER 1 (DISK)
            if (!File.Exists(localDllPath))
            {
                _logger.LogInformation("⬇️ Downloading Plugin {Sha256} từ Storage...", meta.Sha256);
                using var minioStream = await _storageService.GetObjectAsync(meta.Bucket, meta.ObjectKey, ct);
                using var fileStream = new FileStream(localDllPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await minioStream.CopyToAsync(fileStream, ct);
            }

            // NẠP VÀO RAM
            _logger.LogInformation("🔥 Nạp Plugin {Sha256} vào RAM...", meta.Sha256);
            var alc = new PluginLoadContext(localDllPath);
            var assembly = alc.LoadFromAssemblyPath(localDllPath);

            // TỐI ƯU: Lấy thẳng Type từ Name lưu trong DB, thay vì quét toàn bộ DLL
            Type? pluginType = null;
            if (!string.IsNullOrEmpty(meta.PluginType))
            {
                pluginType = assembly.GetType(meta.PluginType);
            }

            // Fallback: Lỡ DB không có PluginType thì mới quét (Dành cho các version cũ)
            pluginType ??= assembly.GetTypes()
                .FirstOrDefault(t => typeof(IWorkflowPlugin).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

            if (pluginType == null)
            {
                alc.Unload();
                throw new Exception($"Không tìm thấy Plugin Type '{meta.PluginType}' trong DLL.");
            }

            var newContext = new CachedPluginContext
            {
                Sha256 = meta.Sha256,
                Alc = alc,
                PluginType = pluginType,
                CacheDirectory = cacheDir,
                LastAccessedAt = DateTime.UtcNow
            };

            _hotPlugins.TryAdd(meta.Sha256, newContext);
            return newContext;
        }
        finally
        {
            asyncLock.Release();
        }
    }

    /// <summary>
    /// Hàm dành cho Background Service gọi để dọn dẹp các Plugin lâu không dùng
    /// </summary>
    public void EvictIdlePlugins(TimeSpan maxIdleTime)
    {
        var cutoffTime = DateTime.UtcNow.Subtract(maxIdleTime);
        var pluginsToEvict = _hotPlugins.Values.Where(p => p.LastAccessedAt < cutoffTime).ToList();

        foreach (var plugin in pluginsToEvict)
        {
            if (_hotPlugins.TryRemove(plugin.Sha256, out _))
            {
                _logger.LogInformation("Dọn dẹp RAM và Xóa thư mục Cache của Plugin {Sha256}", plugin.Sha256);

                // 1. Nhả ALC
                plugin.Alc.Unload();

                // 2. Xóa vật lý trên ổ đĩa
                if (Directory.Exists(plugin.CacheDirectory))
                {
                    try { Directory.Delete(plugin.CacheDirectory, true); }
                    catch { /* Bỏ qua lỗi khóa file nếu có */ }
                }
            }

            foreach (var key in _downloadLocks.Keys)
            {
                if (!_hotPlugins.ContainsKey(key) && _downloadLocks.TryRemove(key, out var sem))
                {
                    sem.Dispose();
                }
            }
        }
    }
}
