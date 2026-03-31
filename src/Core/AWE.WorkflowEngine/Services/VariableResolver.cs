using System.Text.Json;
using System.Text.RegularExpressions;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Shared.Extensions;

namespace AWE.WorkflowEngine.Services;

public class VariableResolver : IVariableResolver
{
    // Regex bắt chuỗi {{...}}
    private static readonly Regex _regex = new Regex(@"(""?)(\{\{([^{}]+)\}\})\1", RegexOptions.Compiled);

    public ResolveResult Resolve(string rawJsonPayload, JsonDocument contextData)
    {
        if (string.IsNullOrWhiteSpace(rawJsonPayload))
            return new ResolveResult(true, "{}", Array.Empty<string>());

        var missingVariables = new List<string>();

        var resolvedJson = _regex.Replace(rawJsonPayload, match =>
        {
            var hasQuotes = match.Groups[1].Value == "\"";
            var rawPath = match.Groups[3].Value.Trim();

            var jsonPath = MapPathToEntity(rawPath);

            var element = contextData.RootElement.GetElement(jsonPath);

            if (element == null)
            {
                missingVariables.Add(rawPath);
                return match.Value;
            }

            // SMART UNQUOTE
            return FormatValue(element!.Value, hasQuotes);
        });

        if (missingVariables.Any())
            return new ResolveResult(false, rawJsonPayload, missingVariables);

        return new ResolveResult(true, resolvedJson, Array.Empty<string>());
    }

    private string FormatValue(JsonElement element, bool hasQuotes)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                string strVal = element.GetString() ?? string.Empty;
                return hasQuotes ? JsonSerializer.Serialize(strVal) : strVal;
            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Object:
            case JsonValueKind.Array:
                return element.GetRawText(); 
            case JsonValueKind.Null:
            default:
                return "null";
        }
    }

    private string MapPathToEntity(string path)
    {
        if (path.StartsWith("workflow.input.", StringComparison.OrdinalIgnoreCase)) return "Inputs." + path.Substring(15);
        if (path.StartsWith("steps.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = path.Split('.');
            if (parts.Length >= 3 && parts[2].Equals("output", StringComparison.OrdinalIgnoreCase))
            {
                parts[0] = "Steps"; parts[2] = "Output"; return string.Join(".", parts);
            }
        }

        if (path.StartsWith("workflow.system.", StringComparison.OrdinalIgnoreCase))
            return "Meta." + path.Substring(16);

        return path;
    }
}

