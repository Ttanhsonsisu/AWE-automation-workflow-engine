using AWE.Sdk;
using AWE.WorkflowEngine.Interfaces;

namespace AWE.WorkflowEngine.Services;

public class PluginRegistry : IPluginRegistry
{
    private readonly IEnumerable<IWorkflowPlugin> _plugins;

    public PluginRegistry(IEnumerable<IWorkflowPlugin> plugins)
    {
        _plugins = plugins;
    }

    public IWorkflowPlugin GetPlugin(string name)
    {
        var plugin = _plugins.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (plugin == null)
        {
            throw new NotSupportedException($"Plugin '{name}' is not registered in the system.");
        }
        return plugin;
    }
}
