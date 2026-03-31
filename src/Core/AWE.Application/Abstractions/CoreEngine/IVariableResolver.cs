using System.Text.Json;

namespace AWE.Application.Abstractions.CoreEngine;

public record ResolveResult(bool IsSuccess, string ResolvedPayload, IReadOnlyList<string> MissingVariables)
{
    public string ErrorMessage => IsSuccess ? string.Empty : $"Missing variables in Context: {string.Join(", ", MissingVariables)}";
}

public interface IVariableResolver
{
    //public string Resolve(string jsonTemplate, JsonDocument globalContext);

    ResolveResult Resolve(string rawJsonPayload, JsonDocument contextData);

}
