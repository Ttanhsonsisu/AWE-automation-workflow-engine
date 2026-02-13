using AWE.Sdk;

namespace AWE.Plugins.BasicMath;

public class BasicMathPlugin : IWorkflowPlugin
{
    public string Name => Name;

    public Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        try
        {
            // 1. Đọc Input (Sử dụng Helper Get<T> của SDK)
            // JSON Input mong đợi: { "a": 10, "b": 5, "operation": "add" }
            var a = context.Get<double>("a");
            var b = context.Get<double>("b");
            var op = context.Get<string>("operation")?.ToLower() ?? "add";

            double result = 0;

            // 2. Xử lý Logic
            switch (op)
            {
                case "add": result = a + b; break;
                case "sub": result = a - b; break;
                case "mul": result = a * b; break;
                case "div":
                    if (b == 0) return Task.FromResult(PluginResult.Failure("Division by zero"));
                    result = a / b;
                    break;
                default:
                    return Task.FromResult(PluginResult.Failure($"Unknown operation: {op}"));
            }

            // 3. Trả về kết quả
            var outputs = new Dictionary<string, object>
            {
                ["result"] = result,
                ["message"] = $"Phép tính {op} thành công",
                ["calculatedAt"] = DateTime.UtcNow
            };

            return Task.FromResult(PluginResult.Success(outputs));
        }
        catch (Exception ex)
        {
            return Task.FromResult(PluginResult.Failure($"Plugin Crash: {ex.Message}"));
        }
    }
}
