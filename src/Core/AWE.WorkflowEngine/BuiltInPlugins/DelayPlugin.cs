using AWE.Sdk;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class DelayPlugin : IWorkflowPlugin
{
    public string Name => "Delay";

    public async Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        var delayTime = context.Get<int?>("time") ?? 500;
        await Task.Delay(delayTime, context.CancellationToken);

        return PluginResult.Success(new Dictionary<string, object>
        {
            { "Waited", $"{delayTime}ms" }
        });
    }

    public Task<PluginResult> CompensateAsync(PluginContext context)
    {
        return Task.FromResult(PluginResult.Success());
    }
}
