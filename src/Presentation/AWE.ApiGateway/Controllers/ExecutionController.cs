using AWE.Application.UseCases.Executions.CancelExecution;
using AWE.Application.UseCases.Executions.GetExecutionDetails;
using AWE.Application.UseCases.Executions.GetExecutionLogs;
using AWE.Application.UseCases.Executions.GetExecutions;
using AWE.Application.UseCases.Executions.ResumeExecution;
using AWE.Application.UseCases.Executions.RetryExecution;
using AWE.Application.UseCases.Executions.SuspendExecution;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/executions")]
public class ExecutionController : ApiController
{
    [HttpGet]
    public async Task<IActionResult> GetExecutions(
        [FromQuery] int page,
        [FromQuery] int size,
        [FromServices] IGetExecutionsUseCase useCase,
        CancellationToken cancellationToken)
    {
        var request = new GetExecutionsRequest { Page = page, Size = size };
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetExecutionDetails(
        Guid id,
        [FromServices] IGetExecutionDetailsUseCase useCase,
        CancellationToken cancellationToken)
    {
        var request = new GetExecutionDetailsRequest { Id = id };
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("{id:guid}/logs")]
    public async Task<IActionResult> GetExecutionLogs(
        Guid id,
        [FromServices] IGetExecutionLogsUseCase useCase,
        CancellationToken cancellationToken)
    {
        var request = new GetExecutionLogsRequest { InstanceId = id };
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/suspend")]
    public async Task<IActionResult> SuspendExecution(
        Guid id,
        [FromServices] ISuspendExecutionUseCase useCase,
        CancellationToken cancellationToken)
    {
        var request = new SuspendExecutionRequest { InstanceId = id };
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/resume")]
    public async Task<IActionResult> ResumeExecution(
        Guid id,
        [FromServices] IResumeExecutionUseCase useCase,
        CancellationToken cancellationToken)
    {
        var request = new ResumeExecutionRequest { InstanceId = id };
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/retry")]
    public async Task<IActionResult> RetryExecution(
        Guid id,
        [FromServices] IRetryExecutionUseCase useCase,
        CancellationToken cancellationToken)
    {
        var request = new RetryExecutionRequest { InstanceId = id };
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> CancelExecution(
        Guid id,
        [FromServices] ICancelExecutionUseCase useCase,
        CancellationToken cancellationToken)
    {
        var request = new CancelExecutionRequest { InstanceId = id };
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }
}
