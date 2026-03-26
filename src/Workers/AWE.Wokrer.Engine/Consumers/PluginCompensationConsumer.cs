using AWE.Application.Abstractions.CoreEngine;
using AWE.Contracts.Messages;
using AWE.Domain.Enums;
using AWE.Infrastructure.Plugins;
using AWE.Sdk;
using MassTransit;

namespace AWE.Wokrer.Engine.Consumers;

public class PluginCompensationConsumer(ILogger<PluginCompensationConsumer> logger, IPluginRegistry pluginRegistry, PluginLoader pluginLoader, PluginCacheManager pluginCacheManager) : IConsumer<CompensatePluginCommand>
{
    private readonly ILogger<PluginCompensationConsumer> _logger = logger;
    private readonly IPluginRegistry _registry = pluginRegistry;
    private readonly PluginLoader _pluginLoader = pluginLoader;
    private readonly PluginCacheManager _pluginCacheManager = pluginCacheManager;

    public async Task Consume(ConsumeContext<CompensatePluginCommand> context)
    {
        var msg = context.Message;
        _logger.LogWarning("[Worker] START COMPENSATING Step {NodeId} ({StepType})", msg.NodeId, msg.StepType);

        // Write Audit Log
        await context.Publish(new WriteAuditLogCommand(
            InstanceId: msg.InstanceId,
            Event: "CompensationStarted",
            Message: $"Bắt đầu Rollback Node: {msg.NodeId}",
            Level: Domain.Enums.LogLevel.Warning,
            ExecutionPointerId: msg.ExecutionPointerId,
            NodeId: msg.NodeId,
            WorkerId: Environment.MachineName
        ));

        try
        {
            PluginResult compResult;
            var pluginContext = new PluginContext(msg.Payload, context.CancellationToken);

            // Thêm logic điều hướng Dynamic DLL y hệt lúc Execute
            switch (msg.ExecutionMode)
            {
                case PluginExecutionMode.DynamicDll:
                    compResult = await _pluginCacheManager.CompensatePluginAsync(msg.DllPath!, msg.Payload, context.CancellationToken);
                    break;
                case PluginExecutionMode.RemoteGrpc:
                    throw new NotImplementedException("gRPC Remote Runner is not supported yet.");
                case PluginExecutionMode.BuiltIn:
                default:
                    var plugin = _registry.GetPlugin(msg.StepType);
                    compResult = await plugin.CompensateAsync(pluginContext);
                    break;
            }

            if (!compResult.IsSuccess)
            {
                throw new Exception($"Compensation failed: {compResult.ErrorMessage}");
            }

            _logger.LogInformation("[Worker] Compensate Step {NodeId} Completed.", msg.NodeId);

            await context.Publish(new WriteAuditLogCommand(
                InstanceId: msg.InstanceId,
                Event: "CompensationCompleted",
                Message: $"Rollback Node {msg.NodeId} thành công.",
                Level: Domain.Enums.LogLevel.Information,
                ExecutionPointerId: msg.ExecutionPointerId,
                NodeId: msg.NodeId,
                WorkerId: Environment.MachineName
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Worker] Failed to compensate Step {NodeId}", msg.NodeId);

            await context.Publish(new WriteAuditLogCommand(
                InstanceId: msg.InstanceId,
                Event: "CompensationError",
                Message: $"Lỗi khi Rollback Node: {ex.Message}",
                Level: Domain.Enums.LogLevel.Error,
                ExecutionPointerId: msg.ExecutionPointerId,
                NodeId: msg.NodeId,
                WorkerId: Environment.MachineName
            ));

            // Có thể bắn message ra Dead-Letter Queue để Admin vào xử lý tay
            throw; // Rethrow để MassTransit có thể xử lý retry hoặc dead-letter tùy cấu hình
        }
    }
}
