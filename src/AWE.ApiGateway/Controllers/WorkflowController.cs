using AWE.Contracts.Messages;
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
    public async Task<IActionResult> Submit([FromBody] SubmitRequest request)
    {
        var correlationId = Guid.NewGuid();

        // Gửi lệnh vào Queue: q.workflow.core (Quorum)
        await _publishEndpoint.Publish(new SubmitWorkflowCommand(
            DefinitionId: request.DefinitionId,
            JobName: request.JobName,
            CorrelationId: correlationId,
            InputData: request.InputData
        ));

        return Accepted(new { CorrelationId = correlationId, Status = "Queued" });
    }
}

public record SubmitRequest(Guid DefinitionId, string JobName, string InputData);
