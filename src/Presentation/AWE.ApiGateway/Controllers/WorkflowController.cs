using AWE.Application.Services;
using AWE.Application.Abstractions.Persistence;
using AWE.Application.UseCases.Workflows.CloneDefinition;
using AWE.Application.UseCases.Workflows.CreateDefinition;
using AWE.Application.UseCases.Workflows.DeleteDefinition;
using AWE.Application.UseCases.Workflows.ExportDefinition;
using AWE.Application.UseCases.Workflows.ImportDefinition;
using AWE.Application.UseCases.Workflows.ScheduleDefinition;
using AWE.Application.UseCases.Workflows.UpdateDefinition;
using AWE.Contracts.Messages;
using AWE.Infrastructure.Persistence;
using AWE.Shared.Primitives;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AWE.ApiGateway.Controllers;

[Route("api/workflows")]
public class WorkflowController : ApiController
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly IWorkflowService _workflowService;

    public WorkflowController(IPublishEndpoint publishEndpoint, IWorkflowService workflowService)
    {
        _publishEndpoint = publishEndpoint;
        _workflowService = workflowService;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetDefinitions([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _workflowService.GetWorkflowDetailsAsync(id, ct);
        return HandleResult(result);
    }

    [HttpGet("{instanceId:guid}/steps/{stepId}")]
    public async Task<IActionResult> GetStepDetails(
        Guid instanceId,
        string stepId,
        [FromServices] IExecutionPointerRepository pointerRepository,
        [FromServices] IExecutionLogRepository executionLogRepository,
        CancellationToken ct)
    {
        var pointers = await pointerRepository.GetPointersByStepIdAsync(instanceId, stepId);
        var pointer = pointers
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefault();

        if (pointer == null)
        {
            return HandleResult(Result.Failure<WorkflowStepDetailResponse>(
                Error.NotFound("Workflow.Step.NotFound", $"Không tìm thấy step '{stepId}' của instance '{instanceId}'.")));
        }

        var logs = await executionLogRepository.GetLogsByInstanceAsync(instanceId, ct);
        var pointerLogs = logs
            .Where(x => x.ExecutionPointerId == pointer.Id)
            .OrderBy(x => x.CreatedAt)
            .ToList();

        var startedLog = pointerLogs.FirstOrDefault(x => x.Event == "StepStarted");
        var errorLog = pointerLogs.LastOrDefault(x => x.Event == "StepError" || x.Event == "WorkflowFailed");

        var input = pointer.InputData?.RootElement.Clone()
            ?? (startedLog?.Metadata is null
                ? null
                : TryGetMetadataValue(startedLog.Metadata, "Input", "input", "Payload", "payload"));

        var output = pointer.Output?.RootElement.Clone();

        var errorMessage = errorLog?.Message;
        if (errorLog?.Metadata is not null)
        {
            var metadataErrorMessage = TryGetMetadataString(errorLog.Metadata, "ErrorMessage", "Error", "Message", "message");
            if (!string.IsNullOrWhiteSpace(metadataErrorMessage))
            {
                errorMessage = metadataErrorMessage;
            }
        }

        var response = new WorkflowStepDetailResponse(
            InstanceId: instanceId,
            StepId: stepId,
            Input: input,
            Output: output,
            ErrorMessage: errorMessage,
            StartTime: pointer.StartTime ?? startedLog?.CreatedAt ?? pointer.CreatedAt,
            EndTime: pointer.EndTime);

        return HandleResult(Result.Success(response));
    }

    [HttpGet("definitions")]
    public async Task<IActionResult> GetDefinitions(
        [FromQuery] int pageSize = 30,
        [FromQuery] int pageNo = 1,
        [FromQuery] bool? isPublished = null,
        [FromQuery] string? name = null,
        [FromQuery] bool groupVersion = false,
        CancellationToken ct = default)
    {
        if (groupVersion)
        {
            return HandleResult(await _workflowService.GetPagingGroupVersionWorkflowAsync(pageSize, pageNo, isPublished, name, ct));
        }

        return HandleResult(await _workflowService.GetPagingWorkflowAsync(pageSize, pageNo, isPublished, name, ct));
    }

    [HttpPost]
    public async Task<IActionResult> SubmitWorkflow([FromBody] SubmitRequest request, [FromServices] ApplicationDbContext dbcontext)
    {
        // Tạo Command gửi xuống Engine
        var command = new SubmitWorkflowCommand(
            DefinitionId: request.DefinitionId,
            JobName: request.JobName ?? $"Job-{DateTime.UtcNow:HHmmss}",
            InputData: request.InputData?.ToString() ?? "{}", // Chuyển JSON Object thành String
            CorrelationId: Guid.NewGuid(),
            IsTest: request.IsTest
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

    [HttpPut("definitions")]
    public async Task<IActionResult> UpdateDefinition([FromBody] UpdateDefinitionRequest request, [FromServices] IUpdateDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// Thêm lịch chạy (Cron) cho Workflow
    /// </summary>
    [HttpPost("{definitionId}/schedules")]
    public async Task<IActionResult> CreateSchedule(Guid definitionId, [FromBody] CreateScheduleRequest request, [FromServices] ICreateScheduleUseCase useCase , CancellationToken cancellationToken)
    {
        var command = new CreateScheduleCommand(definitionId, request.CronExpression);

        Result<ScheduleResponse> result = await useCase.ExecuteAsync(command, cancellationToken);

        // Uỷ quyền cho BaseController xử lý việc map ra HTTP 200, 400, hay 404
        return HandleResult(result);
    }

    private static JsonElement? TryGetMetadataValue(JsonDocument metadata, params string[] candidateKeys)
    {
        if (metadata.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var key in candidateKeys)
        {
            if (metadata.RootElement.TryGetProperty(key, out var value))
            {
                return value.Clone();
            }
        }

        return null;
    }

    private static string? TryGetMetadataString(JsonDocument metadata, params string[] candidateKeys)
    {
        var value = TryGetMetadataValue(metadata, candidateKeys);
        if (value is null)
        {
            return null;
        }

        return value.Value.ValueKind == JsonValueKind.String
            ? value.Value.GetString()
            : value.Value.GetRawText();
    }
}

public record SubmitRequest(
    Guid DefinitionId,
    string? JobName,
    object? InputData ,
    bool IsTest = false
);

public record WorkflowStepDetailResponse(
    Guid InstanceId,
    string StepId,
    JsonElement? Input,
    JsonElement? Output,
    string? ErrorMessage,
    DateTime? StartTime,
    DateTime? EndTime);
