using AWE.Sdk;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class JoinPlugin : IWorkflowPlugin
{
    public string Name => "Join";
    public string DisplayName => "Gộp Luồng (Join)";
    public string Description => "Điểm hội tụ chờ các nhánh song song chạy xong.";
    public string Category => "Logic";
    public string Icon => "lucide-git-merge"; 

    // Join không cần input gì từ user cả
    public string InputSchema => "{}";

    public string OutputSchema => """
    {
      "type": "object",
      "properties": {
        "Message": { "type": "string" }
      }
    }
    """;

    public Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        // Passthrough: Không làm gì cả, chỉ nhả ra data để luồng đi tiếp
        return Task.FromResult(PluginResult.Success(new Dictionary<string, object>
        {
            { "Message", "Barrier broken. All branches joined successfully!" }
        }));
    }

    public Task<PluginResult> CompensateAsync(PluginContext context)
    {
        // Khi lùi xe (Rollback), Join cũng không cần làm gì
        return Task.FromResult(PluginResult.Success());
    }
}
