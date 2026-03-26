using System.Collections.Concurrent;
using System.Text.Json;
using AWE.Application.Services;
using AWE.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AWE.Infrastructure.Plugins;

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
        var metaDoc = JsonDocument.Parse(metadataJson).RootElement;
        string bucket = metaDoc.GetProperty("Bucket").GetString()!;
        string objectKey = metaDoc.GetProperty("ObjectKey").GetString()!;
        string sha256 = metaDoc.GetProperty("Sha256").GetString()!;

        // 2. Kéo Plugin Type từ Cache (Hoặc nạp mới nếu chưa có)
        var cachedContext = await GetOrLoadPluginTypeAsync(bucket, objectKey, sha256, ct);

        // Cập nhật thời gian truy cập để tránh bị dọn rác
        cachedContext.LastAccessedAt = DateTime.UtcNow;

        // 3. THỰC THI (Tạo Instance mới cho MỖI request để Thread-Safe và dùng đúng Scoped DI)
        using var scope = _serviceProvider.CreateScope();

        if (ActivatorUtilities.CreateInstance(scope.ServiceProvider, cachedContext.PluginType) is not IWorkflowPlugin pluginInstance)
        {
            return PluginResult.Failure("Lỗi khởi tạo Plugin. Kiểm tra lại Constructor.");
        }

        var pluginContext = new PluginContext(payload, ct);

        try
        {
            return isCompensation
                ? await pluginInstance.CompensateAsync(pluginContext)
                : await pluginInstance.ExecuteAsync(pluginContext);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 Lỗi Runtime khi chạy Plugin (SHA256: {Sha256})", sha256);
            throw; // Quăng lỗi để Consumer bắt và bắn Event
        }
    }

    private async Task<CachedPluginContext> GetOrLoadPluginTypeAsync(string bucket, string objectKey, string sha256, CancellationToken ct)
    {
        // CACHE HIT (RAM): Trả về ngay lập tức (~0ms)
        if (_hotPlugins.TryGetValue(sha256, out var hotContext)) return hotContext;

        var asyncLock = _downloadLocks.GetOrAdd(sha256, _ => new SemaphoreSlim(1, 1));
        await asyncLock.WaitAsync(ct);

        try
        {
            // Double-check lock
            if (_hotPlugins.TryGetValue(sha256, out hotContext)) return hotContext;

            // Đường dẫn Cache trên ổ cứng: /app/PluginCache/{sha256}/
            string cacheDir = Path.Combine(AppContext.BaseDirectory, "PluginCache", sha256);
            if (!Directory.Exists(cacheDir)) Directory.CreateDirectory(cacheDir);

            string localDllPath = Path.Combine(cacheDir, Path.GetFileName(objectKey));

            // LỚP CACHE 1 (DISK): Tải từ MinIO nếu ổ cứng chưa có
            if (!File.Exists(localDllPath))
            {
                _logger.LogInformation("⬇️ Downloading Plugin {Sha256} từ Storage...", sha256);
                using var minioStream = await _storageService.GetObjectAsync(bucket, objectKey, ct);
                using var fileStream = new FileStream(localDllPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await minioStream.CopyToAsync(fileStream, ct);
            }

            // NẠP VÀO RAM bằng PluginLoadContext của bạn
            _logger.LogInformation("🔥 Nạp Plugin {Sha256} vào RAM...", sha256);
            var alc = new PluginLoadContext(localDllPath);
            var assembly = alc.LoadFromAssemblyPath(localDllPath);

            var pluginType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IWorkflowPlugin).IsAssignableFrom(t) && !t.IsAbstract && t.IsClass);

            if (pluginType == null)
            {
                alc.Unload();
                throw new Exception("Không tìm thấy class implement IWorkflowPlugin trong DLL.");
            }

            var newContext = new CachedPluginContext
            {
                Sha256 = sha256,
                Alc = alc,
                PluginType = pluginType,
                CacheDirectory = cacheDir,
                LastAccessedAt = DateTime.UtcNow
            };

            _hotPlugins.TryAdd(sha256, newContext);
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
                _logger.LogInformation("🧹 Dọn dẹp RAM và Xóa thư mục Cache của Plugin {Sha256}", plugin.Sha256);

                // 1. Nhả ALC
                plugin.Alc.Unload();

                // 2. Xóa vật lý trên ổ đĩa
                if (Directory.Exists(plugin.CacheDirectory))
                {
                    try { Directory.Delete(plugin.CacheDirectory, true); }
                    catch { /* Bỏ qua lỗi khóa file nếu có */ }
                }
            }
        }
    }
}
