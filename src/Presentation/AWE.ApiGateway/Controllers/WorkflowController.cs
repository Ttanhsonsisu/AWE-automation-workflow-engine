using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Application.Services;
using AWE.Application.UseCases.Workflows.CloneDefinition;
using AWE.Application.UseCases.Workflows.CreateDefinition;
using AWE.Application.UseCases.Workflows.DeleteDefinition;
using AWE.Application.UseCases.Workflows.ExportDefinition;
using AWE.Application.UseCases.Workflows.ImportDefinition;
using AWE.Application.UseCases.Workflows.ScheduleDefinition;
using AWE.Application.UseCases.Workflows.UpdateDefinition;
using AWE.Contracts.Messages;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using AWE.Shared.Primitives;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/workflows")]
[Authorize]
public class WorkflowController : ApiController
{
    private readonly IRequestClient<SubmitWorkflowCommand> _submitWorkflowClient;
    private readonly IWorkflowService _workflowService;

    public WorkflowController(IRequestClient<SubmitWorkflowCommand> submitWorkflowClient, IWorkflowService workflowService)
    {
        _submitWorkflowClient = submitWorkflowClient;
        _workflowService = workflowService;
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = AppPolicies.RequireOperator)]
    public async Task<IActionResult> GetDefinitions([FromRoute] Guid id, CancellationToken ct)
    {
        var result = await _workflowService.GetWorkflowDetailsAsync(id, ct);
        return HandleResult(result);
    }

    [HttpGet("{instanceId}/context")]
    [Authorize(Policy = AppPolicies.RequireOperator)]
    public async Task<IActionResult> GetInstanceDataContext(Guid instanceId,
        [FromServices] IWorkflowInstanceRepository instanceRepo,
        CancellationToken ct)
    {
        var instance = await instanceRepo.GetInstanceByIdAsync(instanceId, ct);
        if (instance == null)
        {
            return HandleResult(Result.Failure<JsonElement>(
                Error.NotFound("Workflow.Instance.NotFound", $"Không tìm thấy instance '{instanceId}'.")));
        }

        return HandleResult(Result.Success(instance.ContextData?.RootElement.Clone()));
    }
    [HttpGet("{instanceId:guid}/steps/{stepId}")]
    [Authorize(Policy = AppPolicies.RequireOperator)]
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
            Status: pointer.Status,
            ErrorMessage: errorMessage,
            StartTime: pointer.StartTime ?? startedLog?.CreatedAt ?? pointer.CreatedAt,
            EndTime: pointer.EndTime);

        return HandleResult(Result.Success(response));
    }

    [HttpGet("definitions")]
    [Authorize(Policy = AppPolicies.RequireOperator)]
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
    [Authorize(Policy = AppPolicies.RequireOperator)]
    public async Task<IActionResult> SubmitWorkflow([FromBody] SubmitRequest request)
    {
        var correlationId = Guid.NewGuid();
        var inputData = request.InputData switch
        {
            null => "{}",
            JsonElement element => element.GetRawText(),
            JsonDocument document => document.RootElement.GetRawText(),
            _ => JsonSerializer.Serialize(request.InputData)
        };

        var command = new SubmitWorkflowCommand(
            DefinitionId: request.DefinitionId,
            JobName: request.JobName ?? $"Job-{DateTime.UtcNow:HHmmss}",
            InputData: inputData,
            CorrelationId: correlationId,
            IsTest: request.IsTest,
            StopAtStepId: request.StopAtStepId);

        var response = await _submitWorkflowClient.GetResponse<SubmitWorkflowResponse>(command);

        if (!response.Message.IsSuccess || response.Message.InstanceId is null)
        {
            return HandleResult(Result.Failure<SubmitWorkflowApiResponse>(Error.Failure(
                response.Message.ErrorCode ?? "Workflow.Submit.Failed",
                response.Message.ErrorMessage ?? "Workflow submission failed.")));
        }

        return HandleResult(Result.Success(new SubmitWorkflowApiResponse(
            Message: "Workflow request submitted",
            TrackingId: correlationId,
            InstanceId: response.Message.InstanceId.Value)));
    }

    [HttpPost("definitions")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
    public async Task<IActionResult> CreateDefinition([FromBody] CreateDefinitionRequest request, [FromServices] ICreateDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPut("definitions")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
    public async Task<IActionResult> UpdateDefinition([FromBody] UpdateDefinitionRequest request, [FromServices] IUpdateDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpDelete("definitions/{id:guid}")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
    public async Task<IActionResult> DeleteDefinition(Guid id, [FromServices] IDeleteDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
        var request = new DeleteDefinitionRequest { Id = id };
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("definitions/{id:guid}/clone")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
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
    [Authorize(Policy = AppPolicies.RequireEditor)]
    public async Task<IActionResult> ExportDefinition(Guid id, [FromServices] IExportDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
        var request = new ExportDefinitionRequest { Id = id };
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpPost("definitions/import")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
    public async Task<IActionResult> ImportDefinition([FromBody] ImportDefinitionRequest request, [FromServices] IImportDefinitionUseCase useCase, CancellationToken cancellationToken)
    {
        var result = await useCase.ExecuteAsync(request, cancellationToken);
        return HandleResult(result);
    }

    [HttpGet("definitions/{id:guid}/input-data")]
    [Authorize(Policy = AppPolicies.RequireOperator)]
    public async Task<IActionResult> GetDefinitionInputData(
        Guid id,
        [FromServices] IWorkflowDefinitionRepository definitionRepository,
        CancellationToken ct)
    {
        var definition = await definitionRepository.GetDefinitionByIdAsync(id, ct);
        if (definition is null)
        {
            return HandleResult(Result.Failure<JsonElement?>(
                Error.NotFound("Workflow.Definition.NotFound", $"Không tìm thấy workflow definition '{id}'.")));
        }

        return HandleResult(Result.Success(definition.InputData?.RootElement.Clone()));
    }

    [HttpPost("definitions/{id:guid}/input-data")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
    public async Task<IActionResult> CreateDefinitionInputData(
        Guid id,
        [FromBody] WorkflowDefinitionInputDataRequest request,
        [FromServices] IWorkflowDefinitionRepository definitionRepository,
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        if (request.InputData.ValueKind == JsonValueKind.Undefined)
        {
            return HandleResult(Result.Failure(
                Error.Validation("Workflow.Definition.InputData.Invalid", "InputData không hợp lệ.")));
        }

        var definition = await definitionRepository.GetDefinitionByIdAsync(id, ct);
        if (definition is null)
        {
            return HandleResult(Result.Failure(
                Error.NotFound("Workflow.Definition.NotFound", $"Không tìm thấy workflow definition '{id}'.")));
        }

        if (definition.InputData is not null)
        {
            return HandleResult(Result.Failure(
                Error.Conflict("Workflow.Definition.InputData.AlreadyExists", "InputData đã tồn tại, hãy dùng PUT để cập nhật.")));
        }

        definition.InputData = JsonDocument.Parse(request.InputData.GetRawText());
        await definitionRepository.UpdateDefinitionAsync(definition, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return HandleResult(Result.Success(definition.InputData.RootElement.Clone()));
    }

    [HttpPut("definitions/{id:guid}/input-data")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
    public async Task<IActionResult> UpdateDefinitionInputData(
        Guid id,
        [FromBody] WorkflowDefinitionInputDataRequest request,
        [FromServices] IWorkflowDefinitionRepository definitionRepository,
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        if (request.InputData.ValueKind == JsonValueKind.Undefined)
        {
            return HandleResult(Result.Failure(
                Error.Validation("Workflow.Definition.InputData.Invalid", "InputData không hợp lệ.")));
        }

        var definition = await definitionRepository.GetDefinitionByIdAsync(id, ct);
        if (definition is null)
        {
            return HandleResult(Result.Failure(
                Error.NotFound("Workflow.Definition.NotFound", $"Không tìm thấy workflow definition '{id}'.")));
        }

        definition.InputData = JsonDocument.Parse(request.InputData.GetRawText());
        await definitionRepository.UpdateDefinitionAsync(definition, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return HandleResult(Result.Success(definition.InputData.RootElement.Clone()));
    }

    [HttpDelete("definitions/{id:guid}/input-data")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
    public async Task<IActionResult> DeleteDefinitionInputData(
        Guid id,
        [FromServices] IWorkflowDefinitionRepository definitionRepository,
        [FromServices] IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var definition = await definitionRepository.GetDefinitionByIdAsync(id, ct);
        if (definition is null)
        {
            return HandleResult(Result.Failure(
                Error.NotFound("Workflow.Definition.NotFound", $"Không tìm thấy workflow definition '{id}'.")));
        }

        definition.InputData = null;
        await definitionRepository.UpdateDefinitionAsync(definition, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return HandleResult(Result.Success());
    }

    /// <summary>
    /// Thêm lịch chạy (Cron) cho Workflow
    /// </summary>
    [HttpPost("{definitionId}/schedules")]
    [Authorize(Policy = AppPolicies.RequireEditor)]
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
    object? InputData,
    bool IsTest = false,
    string? StopAtStepId = null
);

public record SubmitWorkflowApiResponse(
    string Message,
    Guid TrackingId,
    Guid InstanceId
);

public record WorkflowStepDetailResponse(
    Guid InstanceId,
    string StepId,
    JsonElement? Input,
    JsonElement? Output,
    ExecutionPointerStatus Status,
    string? ErrorMessage,
    DateTime? StartTime,
    DateTime? EndTime);

public record WorkflowDefinitionInputDataRequest(JsonElement InputData);
