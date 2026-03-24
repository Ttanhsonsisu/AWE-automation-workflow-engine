using System.Text.Json;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Contracts.Messages;
using MassTransit;

namespace AWE.Wokrer.Engine.Consumers;

public class ResumeWorkflowConsumer(IWorkflowOrchestrator orchestrator, ILogger<ResumeWorkflowConsumer> logger)
    : IConsumer<ResumeWorkflowCommand>
{
    public async Task Consume(ConsumeContext<ResumeWorkflowCommand> context)
    {
        var msg = context.Message;
        logger.LogInformation("Nhận lệnh đánh thức Pointer {PointerId} từ Queue...", msg.PointerId);

        // Chuyển lại chuỗi JSON thành JsonDocument
        var resumeDataDoc = JsonDocument.Parse(msg.ResumeDataJson);

        var result = await orchestrator.ResumeStepAsync(msg.PointerId, resumeDataDoc);

        if (result.IsFailure)
        {
            logger.LogError("Lỗi khi đánh thức Workflow: {Error}", result.Error);
            // Có thể throw exception để MassTransit tự động Retry
            throw new Exception(result?.Error?.Message);
        }
    }
}
