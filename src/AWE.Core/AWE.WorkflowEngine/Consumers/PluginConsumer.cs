using System.Text.Json;
using AWE.Contracts.Messages;
using AWE.Infrastructure.Plugins;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Consumers;

public class PluginConsumer : IConsumer<ExecutePluginCommand>
{
    private readonly ILogger<PluginConsumer> _logger;
    private readonly PluginLoader _pluginLoader;
    private readonly string _basePluginDir;

    public PluginConsumer(ILogger<PluginConsumer> logger, PluginLoader pluginLoader)
    {
        _logger = logger;
        _pluginLoader = pluginLoader;

        // Setup thư mục plugins cục bộ
        _basePluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
        if (!Directory.Exists(_basePluginDir)) Directory.CreateDirectory(_basePluginDir);
    }

    public async Task Consume(ConsumeContext<ExecutePluginCommand> context)
    {
        var cmd = context.Message;
        _logger.LogInformation("🛠️ [PLUGIN-WORKER] Start Step {StepId} | Type: {Type}", cmd.StepId, cmd.StepType);

        try
        {
            // 1. Chuẩn bị đường dẫn DLL
            // TODO: Tích hợp StorageService để download file về nếu chưa có
            // Ví dụ: string dllPath = await _pluginRepository.GetPathAsync(cmd.StepType);
            string dllName = $"{char.ToUpper(cmd.StepType[0]) + cmd.StepType.Substring(1)}Plugin.dll";
            string dllPath = Path.Combine(_basePluginDir, dllName);

            // Mock tạo file nếu chưa có để test luồng
            // await EnsureMockFileAsync(dllPath); 

            // 2. Parse Input (JSON String -> Dictionary)
           
            // 3. GỌI PLUGIN LOADER (Chạy isolated)
            var result = await _pluginLoader.ExecutePluginAsync(dllPath, cmd.Payload, context.CancellationToken);

            // 4. Xử lý kết quả trả về
            if (result.IsSuccess)
            {
                // Chuyển đổi Dictionary Outputs -> JsonDocument cho Event
                var outputJson = JsonSerializer.SerializeToDocument(result.Outputs);

                // Publish Event thành công
                await context.Publish(new StepCompletedEvent(
                    WorkflowInstanceId: cmd.InstanceId,
                    ExecutionPointerId: cmd.StepId, // Lưu ý: Mapping StepId sang PointerId tùy logic Engine của bạn
                    StepId: cmd.StepId,
                    Output: outputJson,
                    CompletedAt: DateTime.UtcNow
                ));

                _logger.LogInformation("✅ [PLUGIN-DONE] Step {StepId} completed.", cmd.StepId);
            }
            else
            {
                // Publish Event thất bại
                await context.Publish(new StepFailedEvent(
                    InstanceId: cmd.InstanceId,
                    StepId: cmd.StepId,
                    ErrorMessage: result.ErrorMessage ?? "Unknown plugin error"
                ));

                _logger.LogError("❌ [PLUGIN-FAIL] Step {StepId} failed: {Err}", cmd.StepId, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // Lỗi hệ thống (System Crash, File Access, v.v.)
            _logger.LogError(ex, "🔥 [SYSTEM-FAIL] Critical error in PluginConsumer");

            await context.Publish(new StepFailedEvent(
                InstanceId: cmd.InstanceId,
                StepId: cmd.StepId,
                ErrorMessage: $"System Error: {ex.Message}"
            ));

            // Có thể throw lại để MassTransit Retry nếu lỗi là tạm thời (Transient)
            // throw; 
        }
    }
}
