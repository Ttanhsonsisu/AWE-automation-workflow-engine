using AWE.Sdk.v2;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class LogInput 
{
    public string? Msg { get; set; }
}

public class LogOutput 
{
    public string? LogStatus { get; set; }
}

public class LogPlugin(ILogger<LogPlugin> logger) : IWorkflowPlugin
{
    private readonly ILogger<LogPlugin> _logger = logger;

    public string Name => "Log";
    public string DisplayName => "Ghi Log Hệ Thống";
    public string Description => "In một thông báo ra màn hình Console của Worker.";
    public string Category => "Core";
    public string Icon => "lucide-terminal";

    public Type? InputType => typeof(LogInput);
    public Type? OutputType => typeof(LogOutput);

    public Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        // Sử dụng Helper Get<T> của bạn rất tiện lợi
        var msg = context.Get<string>("Msg") ?? "No message";
        _logger.LogInformation("[LogPlugin] EXECUTE: {Msg}", msg);

        return Task.FromResult(PluginResult.Success(new Dictionary<string, object>
        {
            { "LogStatus", "Written to Console" }
        }));
    }

    public Task<PluginResult> CompensateAsync(PluginContext context)
    {
        _logger.LogWarning("[LogPlugin] COMPENSATE: Hủy bỏ log (Thực tế là không làm gì cả).");
        return Task.FromResult(PluginResult.Success());
    }
}
