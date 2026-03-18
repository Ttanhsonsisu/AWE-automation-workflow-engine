using AWE.Sdk;

namespace AWE.Application.Abstractions.CoreEngine;

public interface IPluginRegistry
{
    IWorkflowPlugin GetPlugin(string name);
    IEnumerable<IWorkflowPlugin> GetAllPlugins();
}
