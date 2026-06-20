using AWE.Sdk.v2;

using System.Globalization;
using System.Text.Json;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class IfConditionInput
{
    public string? Value1 { get; set; }
    public string? Operator { get; set; }
    public string? Value2 { get; set; }
}

public class IfConditionOutput
{
    public bool IsMatch { get; set; }
}

public class IfPlugin : IWorkflowPlugin
{
    public string Name => "If";
    public string DisplayName => "Điều kiện (If/Else)";
    public string Description => "Kiểm tra điều kiện để rẽ nhánh luồng thực thi.";
    public string Category => "Logic";
    public string Icon => "lucide-git-branch";

    public Type? InputType => typeof(IfConditionInput);
    public Type? OutputType => typeof(IfConditionOutput);

    public Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        var val1 = GetScalar(context.Root, "value1");
        var op = (GetScalar(context.Root, "operator") ?? "==").Trim().ToLowerInvariant();
        var val2 = GetScalar(context.Root, "value2");

        bool isMatch = op switch
        {
            "==" or "=" => EqualsValue(val1, val2),
            "!=" or "<>" => !EqualsValue(val1, val2),
            "contains" => (val1 ?? string.Empty).Contains(val2 ?? string.Empty, StringComparison.OrdinalIgnoreCase),
            ">" => CompareNumbers(val1, val2, (left, right) => left > right),
            ">=" => CompareNumbers(val1, val2, (left, right) => left >= right),
            "<" => CompareNumbers(val1, val2, (left, right) => left < right),
            "<=" => CompareNumbers(val1, val2, (left, right) => left <= right),
            _ => false
        };

        return Task.FromResult(PluginResult.Success(new Dictionary<string, object> { { "IsMatch", isMatch } }));
    }

    private static string? GetScalar(JsonElement root, string propertyName)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;

        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;

            return property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => property.Value.GetRawText()
            };
        }

        return null;
    }

    private static bool EqualsValue(string? left, string? right)
    {
        var normalizedLeft = left?.Trim() ?? string.Empty;
        var normalizedRight = right?.Trim() ?? string.Empty;

        if (decimal.TryParse(normalizedLeft, NumberStyles.Number, CultureInfo.InvariantCulture, out var leftNumber)
            && decimal.TryParse(normalizedRight, NumberStyles.Number, CultureInfo.InvariantCulture, out var rightNumber))
        {
            return leftNumber == rightNumber;
        }

        return string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CompareNumbers(string? left, string? right, Func<decimal, decimal, bool> compare)
    {
        return decimal.TryParse(left?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var leftNumber)
            && decimal.TryParse(right?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var rightNumber)
            && compare(leftNumber, rightNumber);
    }

    public Task<PluginResult> CompensateAsync(PluginContext context) => Task.FromResult(PluginResult.Success());
}
