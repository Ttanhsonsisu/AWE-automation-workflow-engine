using AWE.Sdk.v2;

namespace AWE.Plugins.Experiments;

public class SleepInput
{
    public int Seconds { get; set; } = 180;
    public string? Message { get; set; }
}

public class SleepOutput
{
    public int Seconds { get; set; }
    public string? Message { get; set; }
    public string Status { get; set; } = "Completed";
}

public class SleepPlugin : IWorkflowPlugin
{
    public string Name => "AWE.Experiments.Sleep";
    public string DisplayName => "Sleep / Crash Test";
    public string Description => "Long-running plugin used to verify worker lease recovery after a node is stopped.";
    public string Category => "Testing";
    public string Icon => "lucide-hourglass";

    public Type? InputType => typeof(SleepInput);
    public Type? OutputType => typeof(SleepOutput);

    public async Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        var seconds = Math.Clamp(context.Get<int?>("Seconds") ?? 180, 1, 3600);
        var message = context.Get<string>("Message");

        await Task.Delay(TimeSpan.FromSeconds(seconds), context.CancellationToken);

        return PluginResult.Success(new SleepOutput
        {
            Seconds = seconds,
            Message = message,
            Status = "Completed"
        });
    }

    public Task<PluginResult> CompensateAsync(PluginContext context) =>
        Task.FromResult(PluginResult.Success());
}
