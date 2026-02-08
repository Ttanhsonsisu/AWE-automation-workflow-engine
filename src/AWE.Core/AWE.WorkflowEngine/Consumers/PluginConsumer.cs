using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using AWE.Contracts.Messages;
using AWE.Domain.Plugins;
using AWE.Infrastructure.Plugins;
using AWE.Shared.Extensions;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Consumers;

public class PluginConsumer : IConsumer<ExecutePluginCommand>
{
    private readonly ILogger<PluginConsumer> _logger;
    private readonly PluginLoader _pluginLoader;

    // Giả sử plugin được lưu trong folder này (Trong thực tế sẽ config trong appsettings)
    //private const string PLUGIN_DIR = "/app/plugins";
    private readonly string _pluginDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
    public PluginConsumer(ILogger<PluginConsumer> logger, PluginLoader pluginLoader)
    {
        _logger = logger;
        _pluginLoader = pluginLoader;
    }
    public async Task Consume(ConsumeContext<ExecutePluginCommand> context)
    {
        var cmd = context.Message;
        _logger.LogInformation("🚀 [WORKER] Processing Step {StepId} | Plugin: {Type}", cmd.StepId, cmd.StepType);

        try
        {
            // Logic tìm file DLL
            string dllName = $"AWE.Plugins.{cmd.StepType}.dll";
            string dllPath = Path.Combine(_pluginDir, dllName);

            // Debug log để bạn biết nó đang tìm file ở đâu
            _logger.LogInformation("🔍 Looking for DLL at: {Path}", dllPath);

            // SỬA 3: Kiểm tra file tồn tại trước khi gọi Loader
            if (!File.Exists(dllPath))
            {
                throw new FileNotFoundException($"Cannot find plugin DLL at {dllPath}. Please build the plugin project and copy DLL here.");
            }

            // Load Plugin
            var plugin = _pluginLoader.LoadPlugin(cmd.StepType, dllPath);

            // SỬA 4: Guard clause (đề phòng LoadPlugin trả về null)
            if (plugin == null)
            {
                throw new InvalidOperationException($"PluginLoader returned null for {cmd.StepType}");
            }

            // Deserialize Input
            var inputs = JsonExtensions.ToJsonDocument(cmd.Payload);

            // Tạo Context (Dòng này khả năng cao là dòng 37 cũ của bạn)
            var pluginCtx = new AWE.Domain.Plugins.PluginContext(
                cmd.InstanceId,
                cmd.StepId,
                inputs,
                context.CancellationToken
            );

            // Execute
            var result = await plugin.ExecuteAsync(pluginCtx);

            if (result.IsSuccess)
            {
                _logger.LogInformation("✅ [WORKER] Step Success!");
                // Publish event success...
            }
            else
            {
                _logger.LogError("❌ [WORKER] Step Failed: {Msg}", result.ErrorMessage);
                throw new Exception(result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 Error executing plugin");
            throw; // Ném lỗi để MassTransit Retry
        }
    }

    //public async Task Consume(ConsumeContext<ExecutePluginCommand> context)
    //{
    //    // TODO: code login here
    //    var cmd = context.Message;

    //    // Log màu mè chút cho dễ nhìn Console
    //    Console.ForegroundColor = ConsoleColor.Cyan;
    //    _logger.LogInformation(
    //        "🛠️ [PLUGIN] Đang xử lý Step {StepId} | Type: {Type} | Data: {Payload}",
    //        cmd.StepId, cmd.StepType, cmd.Payload);
    //    Console.ResetColor();

    //    // Giả lập xử lý nặng
    //    await Task.Delay(2000);

    //    // Giả lập lỗi để test Retry (Uncomment để test)
    //    // if (new Random().Next(0, 10) > 8) throw new Exception("🔥 Lỗi giả lập plugin!");

    //    _logger.LogInformation("✅ [PLUGIN] Hoàn thành Step {StepId}", cmd.StepId);

    //    // TODO: push event StepCompletedEvent to Core
    //}
}
