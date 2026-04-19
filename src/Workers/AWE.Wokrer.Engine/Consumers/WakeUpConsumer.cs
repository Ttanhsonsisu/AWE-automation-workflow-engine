using System.Text.Json;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Contracts.Messages;
using MassTransit;

namespace AWE.Wokrer.Engine.Consumers;

public class WakeUpConsumer : IConsumer<ResumeStepCommand>
{
    private readonly IWorkflowOrchestrator _orchestrator;
    private readonly ILogger<WakeUpConsumer> _logger;

    public WakeUpConsumer(IWorkflowOrchestrator orchestrator, ILogger<WakeUpConsumer> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ResumeStepCommand> context)
    {
        var cmd = context.Message;
        _logger.LogInformation("⏰ [WAKE-UP] Đã đến giờ đánh thức Node {NodeId} của Instance {InstanceId}", cmd.StepId, cmd.InstanceId);

        // Tạo một Payload trả về cho Node Delay
        // Payload này sẽ được lưu vào ContextData để các Node sau có thể lấy dùng nếu cần
        var wakeupPayload = JsonDocument.Parse($@"{{ 
            ""Message"": ""Woke up successfully"", 
            ""WakeUpTime"": ""{DateTime.UtcNow:O}"" 
        }}");

        // Đẩy thẳng vào Orchestrator để nó tiếp tục đánh giá và chạy Node tiếp theo
        var result = await _orchestrator.ResumeStepAsync(cmd.PointerId, wakeupPayload);

        if (result.IsFailure)
        {
            _logger.LogError("❌ [WAKE-UP] Thất bại khi đánh thức Pointer {PointerId}: {Error}", cmd.PointerId, result.Error.Message);
            // Tùy chọn: Bạn có thể throw exception ở đây để MassTransit retry nếu lỗi này là tạm thời
        }
    }
}
