using AWE.Sdk; 
using Microsoft.Extensions.Logging;

namespace AWE.Infrastructure.Plugins;

public class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;

    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Hàm chính: Load DLL -> Execute -> Unload -> Return Result
    /// </summary>
    public async Task<PluginResult> ExecutePluginAsync(
        string dllPath,
        string jsonPayload,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(dllPath))
            return PluginResult.Failure($"Plugin DLL not found at: {dllPath}");

        WeakReference alcWeakRef;
        PluginResult result;

        try
        {
            // --- FIX: Sử dụng Tuple Deconstruction để lấy kết quả ---
            (result, alcWeakRef) = await ExecuteInContext(dllPath, jsonPayload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 Host Error executing plugin at {Path}", dllPath);
            return PluginResult.Failure($"Host Error: {ex.Message}");
        }

        // --- ALC CLEANUP LOGIC ---
        // Cố gắng Force GC để giải phóng AssemblyLoadContext
        for (int i = 0; i < 10 && alcWeakRef.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (alcWeakRef.IsAlive)
        {
            _logger.LogWarning("⚠️ Plugin ALC failed to unload via GC (Possible memory leak in Plugin code).");
        }
        else
        {
            _logger.LogDebug("✅ Plugin ALC unloaded successfully.");
        }

        return result;
    }

    // --- FIX: Đổi kiểu trả về thành Task<(Result, WeakReference)> ---
    private async Task<(PluginResult Result, WeakReference AlcWeakRef)> ExecuteInContext(
        string dllPath,
        string jsonPayload,
        CancellationToken ct)
    {
        // 1. Tạo Context
        var loadContext = new PluginLoadContext(dllPath);
        var alcWeakRef = new WeakReference(loadContext);

        try
        {
            // 2. Load Assembly
            var assembly = loadContext.LoadFromAssemblyPath(dllPath);

            // 3. Tìm Implementation của IWorkflowPlugin
            var pluginType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IWorkflowPlugin).IsAssignableFrom(t) && !t.IsAbstract);

            if (pluginType == null)
                return (PluginResult.Failure("DLL missing IWorkflowPlugin implementation."), alcWeakRef);

            if (Activator.CreateInstance(pluginType) is not IWorkflowPlugin pluginInstance)
                return (PluginResult.Failure("Failed to create plugin instance."), alcWeakRef);

            // 4. Prepare Context
            var pluginContext = new PluginContext(jsonPayload, ct);

            // 5. Execute
            _logger.LogInformation("🚀 executing: {Name}", pluginInstance.Name);
            var result = await pluginInstance.ExecuteAsync(pluginContext);

            // Trả về kết quả + tham chiếu yếu
            return (result, alcWeakRef);
        }
        finally
        {
            // 6. Đánh dấu Unload
            loadContext.Unload();
        }
    }
}
