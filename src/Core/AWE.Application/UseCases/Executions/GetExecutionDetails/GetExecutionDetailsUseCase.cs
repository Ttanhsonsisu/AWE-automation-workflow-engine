using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Executions.GetExecutionDetails;

public class GetExecutionDetailsRequest
{
    public Guid Id { get; set; }
}

public class ExecutionDetailsDto
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public int DefinitionVersion { get; set; }
    public WorkflowInstanceStatus Status { get; set; }
    public JsonDocument ContextData { get; set; } = null!;
    public DateTime StartTime { get; set; }
    public DateTime CreatedAt { get; set; }

    public List<ExecutionPointerDto> Pointers { get; set; } = new();
}

public class ExecutionPointerDto
{
    public Guid Id { get; set; }
    public string StepId { get; set; } = string.Empty;
    public ExecutionPointerStatus Status { get; set; }
    public bool Active { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EndTime { get; set; }
}

public interface IGetExecutionDetailsUseCase
{
    Task<Result<ExecutionDetailsDto>> ExecuteAsync(GetExecutionDetailsRequest request, CancellationToken cancellationToken = default);
}

public class GetExecutionDetailsUseCase : IGetExecutionDetailsUseCase
{
    private readonly IWorkflowInstanceRepository _repository;

    public GetExecutionDetailsUseCase(IWorkflowInstanceRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<ExecutionDetailsDto>> ExecuteAsync(GetExecutionDetailsRequest request, CancellationToken cancellationToken = default)
    {
        var instance = await _repository.GetInstanceWithPointersAsync(request.Id, cancellationToken);

        if (instance == null)
            return Result.Failure<ExecutionDetailsDto>(Error.NotFound("Execution.NotFound", "Workflow execution not found"));

        var dto = new ExecutionDetailsDto
        {
            Id = instance.Id,
            DefinitionId = instance.DefinitionId,
            DefinitionVersion = instance.DefinitionVersion,
            Status = instance.Status,
            ContextData = instance.ContextData,
            StartTime = instance.StartTime,
            CreatedAt = instance.CreatedAt,
            Pointers = instance.ExecutionPointers.Select(p => new ExecutionPointerDto
            {
                Id = p.Id,
                StepId = p.StepId,
                Status = p.Status,
                Active = p.Active,
                CreatedAt = p.CreatedAt,
                EndTime = p.EndTime
            }).ToList()
        };

        return Result.Success(dto);
    }
}
