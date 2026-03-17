using System.Text.Json;

namespace AWE.WorkflowEngine.Interfaces;

public interface IVariableResolver
{
    public string Resolve(string jsonTemplate, JsonDocument globalContext);

}
