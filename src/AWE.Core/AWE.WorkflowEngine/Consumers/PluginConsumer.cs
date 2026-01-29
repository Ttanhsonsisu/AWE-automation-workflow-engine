using System;
using System.Collections.Generic;
using System.Text;
using AWE.Contracts.Messages;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Consumers;

public class PluginConsumer : IConsumer<ExecutePluginCommand>
{
    private readonly ILogger<PluginConsumer> _logger;

    public PluginConsumer(ILogger<PluginConsumer> logger) => _logger = logger;

    public async Task Consume(ConsumeContext<ExecutePluginCommand> context)
    {
        // TODO: code login here
        var cmd = context.Message;

        // Log màu mè chút cho dễ nhìn Console
        Console.ForegroundColor = ConsoleColor.Cyan;
        _logger.LogInformation(
            "🛠️ [PLUGIN] Đang xử lý Step {StepId} | Type: {Type} | Data: {Payload}",
            cmd.StepId, cmd.StepType, cmd.Payload);
        Console.ResetColor();

        // Giả lập xử lý nặng
        await Task.Delay(2000);

        // Giả lập lỗi để test Retry (Uncomment để test)
        // if (new Random().Next(0, 10) > 8) throw new Exception("🔥 Lỗi giả lập plugin!");

        _logger.LogInformation("✅ [PLUGIN] Hoàn thành Step {StepId}", cmd.StepId);

        // TODO: push event StepCompletedEvent to Core
    }
}
