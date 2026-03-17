using System;
using System.Collections.Generic;
using System.Text;
using AWE.Sdk;

namespace BuiltInPlugins;

public class IWorkflowPlugin
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
