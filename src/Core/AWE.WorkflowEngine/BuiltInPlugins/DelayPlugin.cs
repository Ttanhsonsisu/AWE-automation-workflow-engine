using AWE.Sdk.v2;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class DelayInput
{
    public int Seconds { get; set; } = 60;
}

public class DelayPlugin : IWorkflowPlugin
{
    public string Name => "Delay";
    public string DisplayName => "Chờ Đợi (Delay)";
    public string Description => "Tạm dừng luồng trong một khoảng thời gian nhất định (Hibernate).";
    public string Category => "Core";
    public string Icon => "lucide-timer";

    public Type? InputType => typeof(DelayInput);
    public Type? OutputType => null;

    public Task<PluginResult> ExecuteAsync(PluginContext context)
        => Task.FromResult(PluginResult.Success());

    public Task<PluginResult> CompensateAsync(PluginContext context)
        => Task.FromResult(PluginResult.Success());
}
