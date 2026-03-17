using AWE.Sdk;
using Microsoft.Extensions.Logging;

namespace BuiltInPlugins;

public class LogPlugin : IWorkflowPlugin
{
    private readonly ILogger<LogPlugin> _logger;
    public LogPlugin(ILogger<LogPlugin> logger) => _logger = logger;

    public string Name => "Log";

    public Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        // Sử dụng Helper Get<T> của bạn rất tiện lợi
        var msg = context.Get<string>("msg") ?? "No message";
        _logger.LogInformation("📝 [LogPlugin] EXECUTE: {Msg}", msg);

        return Task.FromResult(PluginResult.Success(new Dictionary<string, object>
        {
            { "LogStatus", "Written to Console" }
        }));
    }

    public Task<PluginResult> CompensateAsync(PluginContext context)
    {
        _logger.LogWarning("⏪ [LogPlugin] COMPENSATE: Hủy bỏ log (Thực tế là không làm gì cả).");
        return Task.FromResult(PluginResult.Success());
    }
}
