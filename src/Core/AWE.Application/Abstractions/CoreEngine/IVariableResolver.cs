using System.Text.Json;

namespace AWE.Application.Abstractions.CoreEngine;

public interface IVariableResolver
{
    public string Resolve(string jsonTemplate, JsonDocument globalContext);

}
