using AWE.Sdk;

namespace AWE.WorkflowEngine.Interfaces;

public interface IPluginRegistry
{
    IWorkflowPlugin GetPlugin(string name);
}
