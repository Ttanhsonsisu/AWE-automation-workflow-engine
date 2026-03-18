using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Executions.GetExecutions;

public class GetExecutionsRequest
{
    public int Page { get; set; } = 1;
    public int Size { get; set; } = 10;
}

public class ExecutionItemDto
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public int DefinitionVersion { get; set; }
    public WorkflowInstanceStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime CreatedAt { get; set; }
}

public interface IGetExecutionsUseCase
{
    Task<Result<IReadOnlyList<ExecutionItemDto>>> ExecuteAsync(GetExecutionsRequest request, CancellationToken cancellationToken = default);
}

public class GetExecutionsUseCase : IGetExecutionsUseCase
{
    private readonly IWorkflowInstanceRepository _repository;

    public GetExecutionsUseCase(IWorkflowInstanceRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<ExecutionItemDto>>> ExecuteAsync(GetExecutionsRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Page > 0 ? request.Page : 1;
        var size = request.Size > 0 ? request.Size : 10;

        var instances = await _repository.GetInstancesAsync(page, size, cancellationToken);

        var dtos = instances.Select(x => new ExecutionItemDto
        {
            Id = x.Id,
            DefinitionId = x.DefinitionId,
            DefinitionVersion = x.DefinitionVersion,
            Status = x.Status,
            StartTime = x.StartTime,
            CreatedAt = x.CreatedAt
        }).ToList();

        return Result.Success<IReadOnlyList<ExecutionItemDto>>(dtos);
    }
}
