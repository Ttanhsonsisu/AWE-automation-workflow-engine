using System.Runtime.CompilerServices;
using AWE.Application.Services;
using AWE.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AWE.Infrastructure.Plugins;

public class PluginLoader(ILogger<PluginLoader> logger, IServiceProvider serviceProvider)
{
    private readonly ILogger<PluginLoader> _logger = logger;
    private readonly IServiceProvider _serviceProvider = serviceProvider;
    //private readonly IStorageService _storageService = storageService;


    public async Task<PluginResult> ExecutePluginAsync(string dllPath, string payload, CancellationToken ct)
    {
        return await RunWithCleanupAsync(dllPath, payload, ct, isCompensation: false);
    }

    public async Task<PluginResult> CompensatePluginAsync(string dllPath, string payload, CancellationToken ct)
    {
        return await RunWithCleanupAsync(dllPath, payload, ct, isCompensation: true);
    }

    private async Task<PluginResult> RunWithCleanupAsync(string fileKey, string payload, CancellationToken ct, bool isCompensation)
    {
        // 1. Kéo DLL từ MinIO về ổ cứng local (Thư mục Temp)
        string localDllPath = await DownloadDllToTempAsync(fileKey, ct);
        if (localDllPath == null) return PluginResult.Failure($"Không thể tải Plugin DLL từ Storage: {fileKey}");

        WeakReference alcWeakRef;
        PluginResult result;

        try
        {
            var loadContext = new PluginLoadContext(localDllPath);
            alcWeakRef = new WeakReference(loadContext);

            // 2. GỌI HÀM BỊ CÔ LẬP (Tránh Memory Leak của async state machine)
            result = await ExecuteIsolatedAsync(loadContext, localDllPath, payload, isCompensation, ct);

            // 3. UNLOAD NGAY LẬP TỨC
            loadContext.Unload();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 Host Error executing plugin at {Path}", localDllPath);
            return PluginResult.Failure($"Host Error: {ex.Message}");
        }
        finally
        {
            // Xóa file temp cho sạch ổ cứng Worker
            if (File.Exists(localDllPath)) File.Delete(localDllPath);
        }

        // 4. Ép rác (GC) dọn dẹp ALC
        for (int i = 0; i < 10 && alcWeakRef.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (alcWeakRef.IsAlive) _logger.LogWarning("⚠️ ALC Memory Leak detected for: {Path}", localDllPath);

        return result;
    }

    // [QUAN TRỌNG]: Tách hàm và cấm Inlining để C# không giam reference của PluginInstance
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<PluginResult> ExecuteIsolatedAsync(
        PluginLoadContext loadContext, string dllPath, string payload, bool isCompensation, CancellationToken ct)
    {
        var assembly = loadContext.LoadFromAssemblyPath(dllPath);
        var pluginType = assembly.GetTypes()
            .FirstOrDefault(t => typeof(IWorkflowPlugin).IsAssignableFrom(t) && !t.IsAbstract);

        if (pluginType == null) return PluginResult.Failure("Missing IWorkflowPlugin implementation in DLL.");

        // DÙNG ACTIVATOR UTILITIES ĐỂ BƠM DEPENDENCY (DI) VÀO PLUGIN ĐỘNG
        using var scope = _serviceProvider.CreateScope();
        if (ActivatorUtilities.CreateInstance(scope.ServiceProvider, pluginType) is not IWorkflowPlugin pluginInstance)
        {
            return PluginResult.Failure("Failed to instantiate plugin. Check Constructor parameters.");
        }

        var pluginContext = new PluginContext(payload, ct);

        // Chạy Tiến hoặc Lùi
        return isCompensation
            ? await pluginInstance.CompensateAsync(pluginContext)
            : await pluginInstance.ExecuteAsync(pluginContext);
    }

    private async Task<string?> DownloadDllToTempAsync(string fileKey, CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var storageService = scope.ServiceProvider.GetRequiredService<IStorageService>();
        // Ví dụ logic: Kéo từ MinIO -> Lưu vào Path.GetTempFileName() + ".dll"
        // await _storageService.DownloadFileAsync("bucket-name", fileKey, localPath, ct);
        // Tạm mock trả về đường dẫn hiện tại nếu bạn chưa implement StorageService
        return File.Exists(fileKey) ? fileKey : null;
    }
}
