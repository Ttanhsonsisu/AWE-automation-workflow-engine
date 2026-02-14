using AWE.Contracts.Messages;
using AWE.WorkflowEngine.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.Worker.WorkflowEvent;

/// <summary>
/// Consumer này nằm ở Engine.
/// Nhiệm vụ: Lắng nghe báo cáo (Completed/Failed) từ Worker để kích hoạt bước tiếp theo.
/// </summary>
public class WorkflowEventConsumer :
    IConsumer<StepCompletedEvent>,
    IConsumer<StepFailedEvent>
{
    private readonly IWorkflowOrchestrator _orchestrator;
    private readonly ILogger<WorkflowEventConsumer> _logger;

    public WorkflowEventConsumer(
        IWorkflowOrchestrator orchestrator,
        ILogger<WorkflowEventConsumer> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
    }

    /// <summary>
    /// Xử lý khi Worker báo cáo thành công
    /// </summary>
    public async Task Consume(ConsumeContext<StepCompletedEvent> context)
    {
        var msg = context.Message;

        // Log Routing Key để debug xem message có đi đúng đường không
        var routingKey = context.RoutingKey();
        _logger.LogInformation("📩 [ENGINE] Received Step Completed: {StepId} (Key: {RoutingKey})", msg.StepId, routingKey);

        try
        {
            // Gọi Orchestrator để tính toán bước tiếp theo (Next Node)
            await _orchestrator.HandleStepCompletionAsync(
                msg.WorkflowInstanceId,
                msg.ExecutionPointerId,
                msg.Output
            );

            _logger.LogInformation("✅ [ENGINE] Step {StepId} handled successfully. Workflow moved forward.", msg.StepId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error handling step completion for {StepId}", msg.StepId);
            // Throw để MassTransit retry (quan trọng nếu DB bị lock hoặc lỗi mạng)
            throw;
        }
    }

    /// <summary>
    /// Xử lý khi Worker báo cáo thất bại (Crash hoặc Exception)
    /// </summary>
    public async Task Consume(ConsumeContext<StepFailedEvent> context)
    {
        var msg = context.Message;
        _logger.LogWarning("⚠️ [ENGINE] Received Step Failed: {StepId}. Reason: {Error}", msg.StepId, msg.ErrorMessage);

        try
        {
            // Gọi Orchestrator để xử lý lỗi (Retry, Compensation, hoặc đánh dấu Workflow Failed)
            // Lưu ý: Hiện tại contract StepFailedEvent của bạn chưa có ExecutionPointerId,
            // nên tạm thời truyền Guid.Empty hoặc cần update contract sau này.
            await _orchestrator.HandleStepFailureAsync(
                msg.InstanceId,
                Guid.Empty,
                msg.ErrorMessage
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error handling step failure for {StepId}", msg.StepId);
            // Với lỗi Failed, tuỳ chiến lược mà có throw hay không. 
            // Thường thì đã fail rồi thì không retry xử lý fail nữa, trừ khi lỗi DB.
        }
    }
}
