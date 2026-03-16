using AWE.Contracts.Messages;
using MassTransit;

namespace AWE.Wokrer.Engine.Consumers;

public class PluginCompensationConsumer : IConsumer<CompensatePluginCommand>
{
    private readonly ILogger<PluginCompensationConsumer> _logger;

    public PluginCompensationConsumer(ILogger<PluginCompensationConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CompensatePluginCommand> context)
    {
        var msg = context.Message;
        _logger.LogWarning("⏪ [Worker] START COMPENSATING Step {NodeId} ({StepType})", msg.NodeId, msg.StepType);

        try
        {
            // TODO: Gọi hàm ExecuteCompensationAsync của Plugin tương ứng
            // Ví dụ:
            // if (msg.StepType == "CreateUser") -> Gọi API Http Delete User(msg.Payload.UserId)
            // if (msg.StepType == "ChargeCreditCard") -> Gọi Stripe API Refund(msg.Payload.TransactionId)

            await Task.Delay(500); // Giả lập chạy Rollback

            _logger.LogInformation("✅ [Worker] Step {NodeId} Compensated Successfully.", msg.NodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [Worker] Failed to compensate Step {NodeId}", msg.NodeId);
            // Có thể bắn message ra Dead-Letter Queue để Admin vào xử lý tay
        }
    }
}
