using AWE.Contracts.Messages;
using AWE.WorkflowEngine.Interfaces;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.Worker.JobExecution.Consumer;

/// <summary>
/// Xử lý lệnh SubmitWorkflowCommand từ API/Scheduler.
/// Nhiệm vụ: Kích hoạt Orchestrator để sinh ra Instance mới.
/// </summary>
public class JobExecutionConsumer : IConsumer<SubmitWorkflowCommand>
{
    private readonly ILogger<JobExecutionConsumer> _logger;
    private readonly IWorkflowOrchestrator _orchestrator;

    public JobExecutionConsumer(
        ILogger<JobExecutionConsumer> logger,
        IWorkflowOrchestrator orchestrator)
    {
        _logger = logger;
        _orchestrator = orchestrator;
    }

    public async Task Consume(ConsumeContext<SubmitWorkflowCommand> context)
    {
        var cmd = context.Message;

        // Log kèm CorrelationId để dễ trace logs
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = cmd.CorrelationId ?? context.CorrelationId ?? Guid.NewGuid(),
            ["DefinitionId"] = cmd.DefinitionId
        });

        _logger.LogInformation("📥 [ENGINE] Received Request: {JobName}", cmd.JobName);

        try
        {
            // Gọi bộ não để bắt đầu quy trình
            await _orchestrator.StartWorkflowAsync(
                cmd.DefinitionId,
                cmd.JobName,
                cmd.InputData,
                cmd.CorrelationId ?? context.CorrelationId
            );

            _logger.LogInformation("✅ [ENGINE] Request Processed Successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [ENGINE] Failed to start job: {JobName}", cmd.JobName);
            // Throw để MassTransit retry (nếu lỗi DB tạm thời) hoặc đưa vào Error Queue
            throw;
        }
    }
}
