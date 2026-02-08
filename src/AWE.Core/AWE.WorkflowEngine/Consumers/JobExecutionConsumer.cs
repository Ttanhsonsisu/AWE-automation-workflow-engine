using AWE.Shared.Consts;
using MassTransit;
using Microsoft.Extensions.Logging;
using AWE.Contracts.Messages;

namespace AWE.WorkflowEngine.Consumers;

public class JobExecutionConsumer : IConsumer<SubmitWorkflowCommand>
{
    private readonly ILogger<JobExecutionConsumer> _logger;

    public JobExecutionConsumer(ILogger<JobExecutionConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<SubmitWorkflowCommand> context)
    {

        // TODO: code login here

        var cmd = context.Message;

        // === CREATE INSTANCE (MOCK DATA) ===
        var instanceId = Guid.NewGuid();
        var stepId = Guid.NewGuid();

        _logger.LogInformation(
            "🚀 [CORE] Init Workflow {InstanceId} từ Def {DefId} | Job: {JobName} | CorrelationId: {CorrelationId}",
            instanceId, cmd.DefinitionId, cmd.JobName, cmd.CorrelationId
        );

        // === 2. ROUTING To PLUGIN ===
        // test
        const string stepType = "video";

        _logger.LogInformation("👉 [ROUTING] Điều hướng Step {StepId} tới phân hệ: {StepType}", stepId, stepType);

        // Gửi lệnh kèm theo Routing Key động
        await context.Publish(new ExecutePluginCommand(
            InstanceId: instanceId,
            StepId: stepId,
            StepType: stepType,
            Payload: "{ \"fileUrl\": \"video.mp4\" }"
        ), publishCtx =>
        {
            // Dynamic Routing Key: "workflow.step.video"
            publishCtx.SetRoutingKey($"workflow.step.{stepType}");

            // CorrelationId để trace log toàn hệ thống
            if (cmd.CorrelationId.HasValue)
                publishCtx.CorrelationId = cmd.CorrelationId;
        });
    }
}
