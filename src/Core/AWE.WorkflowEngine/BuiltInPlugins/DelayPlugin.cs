using AWE.Sdk;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class DelayPlugin : IWorkflowPlugin
{
    public string Name => "Delay";
    public string DisplayName => "Chờ Đợi (Delay)";
    public string Description => "Tạm dừng luồng trong một khoảng thời gian nhất định (Hibernate).";
    public string Category => "Core";
    public string Icon => "lucide-timer";

    public string InputSchema => """
    {
      "type": "object",
      "properties": {
        "seconds": { 
          "type": "integer", 
          "title": "Thời gian chờ (Giây)",
          "default": 60,
          "minimum": 1
        }
      },
      "required": ["seconds"]
    }
    """;

    public string OutputSchema => "{}";

    public Task<PluginResult> ExecuteAsync(PluginContext context)
        => Task.FromResult(PluginResult.Success());

    public Task<PluginResult> CompensateAsync(PluginContext context)
        => Task.FromResult(PluginResult.Success());
}
