using System.Text.Json;
using System.Text.RegularExpressions;
using AWE.WorkflowEngine.Interfaces;

namespace AWE.WorkflowEngine.Services;

public class VariableResolver : IVariableResolver
{
    private static readonly Regex _regex = new Regex(@"\{\{(.*?)\}\}", RegexOptions.Compiled);

    public string Resolve(string jsonTemplate, JsonDocument globalContext)
    {
        if (string.IsNullOrWhiteSpace(jsonTemplate)) return "{}";

        return _regex.Replace(jsonTemplate, match =>
        {
            var rawPath = match.Groups[1].Value.Trim();
            // Mapping cú pháp Docs -> Cấu trúc DB
            // Docs: workflow.input.Id  -> DB: Inputs.Id
            // Docs: steps.NodeA.output -> DB: Steps.NodeA.Output
            string jsonPath = MapPathToEntity(rawPath);

            var value = ExtractValue(globalContext.RootElement, jsonPath);
            return value?.ToString() ?? "null";
        });
    }

    private string MapPathToEntity(string path)
    {
        if (path.StartsWith("workflow.input.", StringComparison.OrdinalIgnoreCase))
            return "Inputs." + path.Substring(15);

        if (path.StartsWith("steps.", StringComparison.OrdinalIgnoreCase))
        {
            // steps.A.output.x -> Steps.A.Output.x
            var parts = path.Split('.');
            if (parts.Length >= 3 && parts[2].Equals("output", StringComparison.OrdinalIgnoreCase))
            {
                parts[0] = "Steps";
                parts[2] = "Output";
                return string.Join(".", parts);
            }
        }
        return path;
    }

    private object? ExtractValue(JsonElement root, string path)
    {
        var current = root;
        var segments = path.Split('.');

        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object) return null;

            // Case-insensitive property search
            JsonProperty prop = default;
            bool found = false;
            foreach (var p in current.EnumerateObject())
            {
                if (p.Name.Equals(segment, StringComparison.OrdinalIgnoreCase))
                {
                    prop = p;
                    found = true;
                    break;
                }
            }

            if (found) current = prop.Value;
            else return null;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => current.GetRawText() // Object/Array trả về string JSON
        };
    }
}

