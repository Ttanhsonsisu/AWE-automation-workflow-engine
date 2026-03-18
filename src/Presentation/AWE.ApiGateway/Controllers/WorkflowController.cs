using AWE.Application.UseCases.Workflows.CloneDefinition;
using AWE.Application.UseCases.Workflows.CreateDefinition;
using AWE.Application.UseCases.Workflows.DeleteDefinition;
using AWE.Application.UseCases.Workflows.ExportDefinition;
using AWE.Application.UseCases.Workflows.ImportDefinition;
using AWE.Application.UseCases.Workflows.UpdateDefinition;
using AWE.Contracts.Messages;
using AWE.Infrastructure.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/workflows")]
public class WorkflowController : ApiController
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

    [HttpPost("definitions")]
    public async Task<IActionResult> CreateDefinition([FromBody] CreateDefinitionRequest request, [FromServices] ICreateDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("definitions/{id:guid}")]
    public async Task<IActionResult> UpdateDefinition(Guid id, [FromBody] UpdateDefinitionRequest request, [FromServices] IUpdateDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
        if (id != request.Id)
        {
            return BadRequest("Id in URL does not match Id in body.");
        }
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete("definitions/{id:guid}")]
    public async Task<IActionResult> DeleteDefinition(Guid id, [FromServices] IDeleteDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
        var request = new DeleteDefinitionRequest { Id = id };
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("definitions/{id:guid}/clone")]
    public async Task<IActionResult> CloneDefinition(Guid id, [FromBody] CloneDefinitionRequest request, [FromServices] ICloneDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
        if (id != request.SourceDefinitionId)
        {
            return BadRequest("Id in URL does not match SourceDefinitionId in body.");
        }
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("definitions/{id:guid}/export")]
    public async Task<IActionResult> ExportDefinition(Guid id, [FromServices] IExportDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
        var request = new ExportDefinitionRequest { Id = id };
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("definitions/import")]
    public async Task<IActionResult> ImportDefinition([FromBody] ImportDefinitionRequest request, [FromServices] IImportDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }
}

// Model nhận dữ liệu từ Postman
public record SubmitRequest(
    Guid DefinitionId,
    string? JobName,
    object? InputData // Để object để Postman gửi JSON thoải mái
);
