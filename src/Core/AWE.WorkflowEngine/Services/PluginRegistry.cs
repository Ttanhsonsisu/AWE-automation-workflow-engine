using AWE.Application.Abstractions.CoreEngine;
using AWE.Sdk.v2;

namespace AWE.WorkflowEngine.Services;

public class PluginRegistry : IPluginRegistry
{
    private readonly IReadOnlyDictionary<string, IWorkflowPlugin> _plugins;

    public PluginRegistry(IEnumerable<IWorkflowPlugin> plugins)
    {
        // Chuyển IEnumerable thành Dictionary ngay khi khởi tạo (Lúc App vừa chạy lên).
        // StringComparer.OrdinalIgnoreCase giúp so sánh tên không phân biệt hoa/thường cực nhanh.
        _plugins = plugins.ToDictionary(
            p => p.Name,
            p => p,
            StringComparer.OrdinalIgnoreCase
        );
    }

    public IWorkflowPlugin GetPlugin(string name)
    {
        if (_plugins.TryGetValue(name, out var plugin))
        {
            return plugin;
        }

        throw new NotSupportedException($"Built-in Plugin '{name}' chưa được đăng ký trong hệ thống.");
    }

    public IEnumerable<IWorkflowPlugin> GetAllPlugins() => _plugins.Values;

}
