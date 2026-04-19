using System.Text.Json;
using AWE.Sdk.v2;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class WebhookTriggerInput
{
    public string RoutePath { get; set; } = string.Empty;
    public string? SecretToken { get; set; }
    public string? IdempotencyKeyPath { get; set; }
}

public class WebhookTriggerPlugin : ITriggerPlugin
{
    public string Name => "WebhookTrigger";
    public string TriggerSource => "Webhook";
    public bool IsSingleton => false;
    public string DisplayName => "Webhook Trigger";
    public string Description => "Điểm khởi đầu cho luồng webhook. Nhận payload từ API Gateway và chuyển tiếp cho các node tiếp theo.";
    public string Category => "Trigger";
    public string Icon => "lucide-webhook";

    public Type? InputType => typeof(WebhookTriggerInput);
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
