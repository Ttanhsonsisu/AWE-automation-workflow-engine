using AWE.Sdk;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class IfPlugin : IWorkflowPlugin
{
    public string Name => "If";
    public string DisplayName => "Điều kiện (If/Else)";
    public string Description => "Kiểm tra điều kiện để rẽ nhánh luồng thực thi.";
    public string Category => "Logic";
    public string Icon => "lucide-git-branch";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "value1": { "type": "string", "title": "Giá trị A" },
        "operator": { 
            "type": "string", 
            "title": "Toán tử", 
            "enum": ["==", "!=", ">", "<", "contains"],
            "default": "=="
        },
        "value2": { "type": "string", "title": "Giá trị B" }
      },
      "required": ["value1", "operator", "value2"]
    }
    """;

    public string OutputSchema => """
    {
      "type": "object",
      "properties": {
        "IsMatch": { "type": "boolean", "title": "Kết quả khớp" }
      }
    }
    """;

    public Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        var val1 = context.Get<string>("value1") ?? "";
        var op = context.Get<string>("operator") ?? "==";
        var val2 = context.Get<string>("value2") ?? "";

        bool isMatch = op switch
        {
            "==" => val1.Equals(val2, StringComparison.OrdinalIgnoreCase),
            "!=" => !val1.Equals(val2, StringComparison.OrdinalIgnoreCase),
            "contains" => val1.Contains(val2, StringComparison.OrdinalIgnoreCase),
            _ => false
        };

        return Task.FromResult(PluginResult.Success(new Dictionary<string, object> { { "IsMatch", isMatch } }));
    }

    public Task<PluginResult> CompensateAsync(PluginContext context) => Task.FromResult(PluginResult.Success());
}
