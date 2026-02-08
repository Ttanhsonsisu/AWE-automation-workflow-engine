using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Text;
using AWE.Domain.Plugins;
using Microsoft.Extensions.Logging;

namespace AWE.Infrastructure.Plugins;

public class PluginLoader
{
    private readonly ILogger<PluginLoader> _logger;
    // Cache instance của plugin để không phải load lại liên tục (Optional)
    private static readonly Dictionary<string, IWorkflowPlugin> _pluginCache = new();

    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger;
    }

    public IWorkflowPlugin LoadPlugin(string pluginName, string dllPath)
    {
        // 1. Check Cache
        if (_pluginCache.TryGetValue(pluginName, out var cachedPlugin))
        {
            return cachedPlugin;
        }

        if (!File.Exists(dllPath))
        {
            throw new FileNotFoundException($"Không tìm thấy file plugin tại: {dllPath}");
        }

        try
        {
            _logger.LogInformation("🔌 Loading Plugin DLL: {Path}", dllPath);

            // 2. Load DLL vào Context riêng (để tránh xung đột version)
            var loadContext = new AssemblyLoadContext(pluginName, isCollectible: true);
            var assembly = loadContext.LoadFromAssemblyPath(dllPath);

            // 3. Scan tìm class implement IWorkflowPlugin
            var pluginType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IWorkflowPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            if (pluginType == null)
            {
                throw new InvalidOperationException($"DLL {dllPath} không chứa class nào implement IWorkflowPlugin.");
            }

            // 4. Create Instance
            var pluginInstance = (IWorkflowPlugin)Activator.CreateInstance(pluginType)!;

            // 5. Verify Name (Optional safety check)
            if (pluginInstance.Name != pluginName)
            {
                _logger.LogWarning("⚠️ Plugin Name không khớp! Yêu cầu: {Req}, Thực tế: {Act}", pluginName, pluginInstance.Name);
            }

            // 6. Cache lại
            _pluginCache[pluginName] = pluginInstance;

            return pluginInstance;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 Lỗi khi load plugin {Name}", pluginName);
            throw;
        }
    }
}
