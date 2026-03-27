using AWE.Sdk;
using AWE.Sdk.v2;

namespace AWE.Application.Abstractions.CoreEngine;

public interface IPluginRegistry
{
    Sdk.v2.IWorkflowPlugin GetPlugin(string name);
    IEnumerable<Sdk.v2.IWorkflowPlugin> GetAllPlugins();
}
