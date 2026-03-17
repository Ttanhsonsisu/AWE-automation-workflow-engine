using AWE.Sdk;
using AWE.WorkflowEngine.Interfaces;

namespace AWE.WorkflowEngine.Services;

public class PluginRegistry(IEnumerable<IWorkflowPlugin> plugins) : IPluginRegistry
{
    private readonly IEnumerable<IWorkflowPlugin> _plugins = plugins;

    public IWorkflowPlugin GetPlugin(string name)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (plugin == null)
        {
            throw new NotSupportedException($"Plugin '{name}' is not registered in the system.");
        }
        return plugin;
    }

    public IEnumerable<IWorkflowPlugin> GetAllPlugins() => _plugins;
}
