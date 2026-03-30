using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Executions.GetExecutions;

public class GetExecutionsRequest
{
    public int Page { get; set; } = 1;
    public int Size { get; set; } = 10;
    public Guid[]? DefinitionIds { get; set; }
    public WorkflowInstanceStatus? Status { get; set; }
    public DateTime? CreatedFrom { get; set; }
    public DateTime? CreatedTo { get; set; }
}

public class ExecutionItemDto
{
    public Guid Id { get; set; }
    public Guid DefinitionId { get; set; }
    public string DefinitionName { get; set; } = string.Empty;
    public int DefinitionVersion { get; set; }
    public WorkflowInstanceStatus Status { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public double DurationSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? ParentInstanceId { get; set; }
    public List<ExecutionItemDto> ChildInstances { get; set; } = new();
}

public interface IGetExecutionsUseCase
{
    Task<Result<PagedResult<ExecutionItemDto>>> ExecuteAsync(GetExecutionsRequest request, CancellationToken cancellationToken = default);
}

public class GetExecutionsUseCase : IGetExecutionsUseCase
{
    private readonly IWorkflowInstanceRepository _repository;

    public GetExecutionsUseCase(IWorkflowInstanceRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<PagedResult<ExecutionItemDto>>> ExecuteAsync(GetExecutionsRequest request, CancellationToken cancellationToken = default)
    {
        var page = request.Page > 0 ? request.Page : 1;
        var size = request.Size > 0 ? request.Size : 10;
        var definitionIds = request.DefinitionIds?.Distinct().ToArray();

        if (request.CreatedFrom.HasValue && request.CreatedTo.HasValue && request.CreatedFrom > request.CreatedTo)
        {
            return Result.Failure<PagedResult<ExecutionItemDto>>(
                Error.Validation("Execution.Paging.InvalidDateRange", "createdFrom phải nhỏ hơn hoặc bằng createdTo"));
        }

        var totalCountLong = await _repository.CountInstancesAsync(definitionIds, request.Status, request.CreatedFrom, request.CreatedTo, cancellationToken);
        if (totalCountLong > int.MaxValue)
        {
            return Result.Failure<PagedResult<ExecutionItemDto>>(
                Error.Failure("Execution.Paging.TotalCountOverflow", "Total execution count is too large."));
        }

        var instances = await _repository.GetInstancesAsync(page, size, definitionIds, request.Status, request.CreatedFrom, request.CreatedTo, cancellationToken);
        var descendants = await LoadDescendantsAsync(instances.Select(x => x.Id).ToArray(), cancellationToken);

        var allInstances = instances
            .Concat(descendants)
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .ToList();

        var dtoById = allInstances.ToDictionary(x => x.Id, MapExecutionItem);

        foreach (var instance in allInstances.Where(x => x.ParentInstanceId.HasValue))
        {
            if (!dtoById.TryGetValue(instance.Id, out var childDto))
            {
                continue;
            }

            var parentId = instance.ParentInstanceId!.Value;
            if (dtoById.TryGetValue(parentId, out var parentDto))
            {
                parentDto.ChildInstances.Add(childDto);
            }
        }

        SortChildrenRecursively(dtoById.Values);

        var dtos = instances
            .Where(x => dtoById.ContainsKey(x.Id))
            .Select(x => dtoById[x.Id])
            .ToList();

        var pagedResult = PagedResult<ExecutionItemDto>.Create(dtos, (int)totalCountLong, page, size);
        return Result.Success(pagedResult);
    }

    private async Task<List<WorkflowInstance>> LoadDescendantsAsync(
        IReadOnlyCollection<Guid> rootInstanceIds,
        CancellationToken cancellationToken)
    {
        var result = new List<WorkflowInstance>();
        var frontier = rootInstanceIds.Distinct().ToArray();
        var visited = new HashSet<Guid>(frontier);

        while (frontier.Length > 0)
        {
            var children = await _repository.GetChildInstancesAsync(frontier, cancellationToken);
            var nextLevel = children
                .Where(x => visited.Add(x.Id))
                .ToList();

            if (nextLevel.Count == 0)
            {
                break;
            }

            result.AddRange(nextLevel);
            frontier = nextLevel.Select(x => x.Id).ToArray();
        }

        return result;
    }

    private static void SortChildrenRecursively(IEnumerable<ExecutionItemDto> items)
    {
        foreach (var item in items)
        {
            item.ChildInstances = item.ChildInstances
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            if (item.ChildInstances.Count > 0)
            {
                SortChildrenRecursively(item.ChildInstances);
            }
        }
    }

    private static ExecutionItemDto MapExecutionItem(WorkflowInstance x)
        => new()
        {
            Id = x.Id,
            DefinitionId = x.DefinitionId,
            DefinitionName = x.Definition?.Name ?? string.Empty,
            DefinitionVersion = x.DefinitionVersion,
            Status = x.Status,
            StartTime = x.StartTime,
            EndTime = x.EndTime,
            DurationSeconds = GetDurationSeconds(x.StartTime, x.EndTime),
            CreatedAt = x.CreatedAt,
            ParentInstanceId = x.ParentInstanceId,
            ChildInstances = []
        };

    private static double GetDurationSeconds(DateTime startTime, DateTime? endTime)
    {
        var end = endTime ?? DateTime.UtcNow;
        var duration = end - startTime;
        return duration.TotalSeconds < 0 ? 0 : duration.TotalSeconds;
    }
}
