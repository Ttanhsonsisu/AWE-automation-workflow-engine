using System.Collections.Concurrent;
using AWE.Sdk.v2;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class RetryTestInput
{
    public int FailTimes { get; set; } = 1;
    public string ErrorType { get; set; } = "Timeout";
    public string? Message { get; set; }
}

public class RetryTestOutput
{
    public int Attempt { get; set; }
    public int FailTimes { get; set; }
    public string PointerId { get; set; } = string.Empty;
    public string Status { get; set; } = "Success";
}

public class RetryTestPlugin(ILogger<RetryTestPlugin> logger) : IWorkflowPlugin
{
    private static readonly ConcurrentDictionary<string, int> AttemptMap = new();
    private readonly ILogger<RetryTestPlugin> _logger = logger;

    public string Name => "RetryTest";
    public string DisplayName => "Retry Test Plugin";
    public string Description => "Built-in plugin dùng để giả lập lỗi retry và xác nhận luồng retry hoạt động.";
    public string Category => "Testing";
    public string Icon => "lucide-rotate-cw";

    public Type? InputType => typeof(RetryTestInput);
    public Type? OutputType => typeof(RetryTestOutput);

    public Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        var pointerId = context.Get<string>("PointerId") ?? "UNKNOWN";
        var failTimes = Math.Max(0, context.Get<int?>("FailTimes") ?? 1);
        var errorType = (context.Get<string>("ErrorType") ?? "Timeout").Trim();
        var message = context.Get<string>("Message") ?? "Simulated transient failure";

        var attempt = AttemptMap.AddOrUpdate(pointerId, 1, (_, oldValue) => oldValue + 1);

        _logger.LogInformation("[RetryTestPlugin] Pointer {PointerId} attempt {Attempt}/{FailTimes}", pointerId, attempt, failTimes);

        if (attempt <= failTimes)
        {
            throw errorType.Equals("Http", StringComparison.OrdinalIgnoreCase)
                ? new HttpRequestException($"[RetryTestPlugin] {message} - attempt {attempt}")
                : new TimeoutException($"[RetryTestPlugin] {message} - attempt {attempt}");
        }

        AttemptMap.TryRemove(pointerId, out _);

        return Task.FromResult(PluginResult.Success(new RetryTestOutput
        {
            Attempt = attempt,
            FailTimes = failTimes,
            PointerId = pointerId,
            Status = "Success"
        }));
    }

    public Task<PluginResult> CompensateAsync(PluginContext context)
    {
        var pointerId = context.Get<string>("PointerId") ?? "UNKNOWN";
        AttemptMap.TryRemove(pointerId, out _);
        return Task.FromResult(PluginResult.Success());
    }
}
