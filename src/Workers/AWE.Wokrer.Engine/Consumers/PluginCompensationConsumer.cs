using AWE.Contracts.Messages;
using AWE.Sdk;
using AWE.WorkflowEngine.Interfaces;
using MassTransit;
using Microsoft.Win32;

namespace AWE.Wokrer.Engine.Consumers;

public class PluginCompensationConsumer : IConsumer<CompensatePluginCommand>
{
    private readonly ILogger<PluginCompensationConsumer> _logger;
    private readonly IPluginRegistry _registry;

    public PluginCompensationConsumer(ILogger<PluginCompensationConsumer> logger, IPluginRegistry pluginRegistry)
    {
        _logger = logger;
        _registry = pluginRegistry;
    }

    public async Task Consume(ConsumeContext<CompensatePluginCommand> context)
    {
        var msg = context.Message;
        _logger.LogWarning("⏪ [Worker] START COMPENSATING Step {NodeId} ({StepType})", msg.NodeId, msg.StepType);

        try
        {
            var plugin = _registry.GetPlugin(msg.StepType);
            var pluginContext = new PluginContext(msg.Payload, context.CancellationToken); // Payload lúc này là Output cũ

            var compResult = await plugin.CompensateAsync(pluginContext);

            if (!compResult.IsSuccess)
            {
                throw new Exception($"Compensation failed: {compResult.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [Worker] Failed to compensate Step {NodeId}", msg.NodeId);
            // Có thể bắn message ra Dead-Letter Queue để Admin vào xử lý tay
        }
    }
}
