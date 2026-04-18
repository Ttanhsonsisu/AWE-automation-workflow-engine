using System.Text.Json;
using AWE.Sdk.v2;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class CronTriggerInput
{
    public string CronExpression { get; set; } = "* * * * *";
    public string? TimeZone { get; set; }
}

public class CronTriggerPlugin : ITriggerPlugin
{
    public string Name => "CronTrigger";
    public string TriggerSource => "Cron";
    public bool IsSingleton => true;

    public string DisplayName => "Kích Hoạt Theo Lịch";
    public string Description => "Điểm khởi đầu cho các luồng chạy định kỳ theo biểu thức Cron.";
    public string Category => "Trigger";
    public string Icon => "lucide-calendar-clock";

    public Type? InputType => typeof(CronTriggerInput);
    public Type? OutputType => null;

    public Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        object? outputs = null;

        try
        {
            if (!string.IsNullOrWhiteSpace(context.Payload) && context.Payload != "{}")
            {
                outputs = JsonSerializer.Deserialize<Dictionary<string, object>>(context.Payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
        }
        catch
        {
            outputs = new { RawInput = context.Payload };
        }

        return Task.FromResult(PluginResult.Success(outputs));
    }

    public Task<PluginResult> CompensateAsync(PluginContext context)
        => Task.FromResult(PluginResult.Success());
}
