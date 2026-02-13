using AWE.Contracts.Messages;
using AWE.Infrastructure.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[ApiController]
[Route("api/workflows")]
public class WorkflowController : ControllerBase
{
    private readonly IPublishEndpoint _publishEndpoint;

    public WorkflowController(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    [HttpPost]
    public async Task<IActionResult> SubmitWorkflow([FromBody] SubmitRequest request, [FromServices] ApplicationDbContext dbcontext)
    {
        // Tạo Command gửi xuống Engine
        var command = new SubmitWorkflowCommand(
            DefinitionId: request.DefinitionId,
            JobName: request.JobName ?? $"Job-{DateTime.UtcNow:HHmmss}",
            InputData: request.InputData?.ToString() ?? "{}", // Chuyển JSON Object thành String
            CorrelationId: Guid.NewGuid()
        );

        // Bắn vào RabbitMQ
        await _publishEndpoint.Publish(command);

        await dbcontext.SaveChangesAsync();

        return Accepted(new
        {
            Message = "Workflow request submitted",
            TrackingId = command.CorrelationId
        });
    }
}

// Model nhận dữ liệu từ Postman
public record SubmitRequest(
    Guid DefinitionId,
    string? JobName,
    object? InputData // Để object để Postman gửi JSON thoải mái
);
