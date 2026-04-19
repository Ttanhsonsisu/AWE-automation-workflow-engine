using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Application.Dtos.WorkflowDto;
using AWE.Application.Services;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using AWE.Shared.Primitives;

namespace AWE.Infrastructure.Services;

public class WorkflowService(IWorkflowDefinitionRepository definitionRepository) : IWorkflowService
{
    private readonly IWorkflowDefinitionRepository _definitionRepository = definitionRepository;
    public async Task<Result<WorkflowDetailDto>> GetWorkflowDetailsAsync(Guid id, CancellationToken ct = default)
    {
        var workflow = await _definitionRepository.GetDefinitionByIdAsync(id, ct);

        if (workflow == null)
        {
            return Result.Failure<WorkflowDetailDto>(Error.NotFound("Workflow.NotFound", $"Không tìm thấy workflow có ID {id}"));
        }

        JsonElement definitionElement;
        JsonElement uiJson = workflow.UiJson != null ?
            workflow.UiJson.RootElement.Clone() :
            JsonDocument.Parse("{}").RootElement.Clone();

        if (workflow.DefinitionJson != null)
        {
            definitionElement = workflow.DefinitionJson.RootElement.Clone();
        }
        else
        {
            definitionElement = JsonDocument.Parse("{\"nodes\":[], \"edges\":[]}").RootElement.Clone();
        }

        var dto = new WorkflowDetailDto(
            Id: workflow.Id,
            Name: workflow.Name,
            Description: workflow.Description,
            IsPublished: workflow.IsPublished,
            Definition: definitionElement,
            UiJson: uiJson
        );

        return Result.Success(dto);
    }

    public async Task<Result<PagedResult<WorkflowDto>>> GetPagingWorkflowAsync(
        int pageSize = Consts.PAGE_SIZE_DEFAULT,
        int pageNo = 1,
        bool? isPublished = null,
        string? name = null,
        CancellationToken ct = default)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        if (pageSize <= 0)
        {
            return Result.Failure<PagedResult<WorkflowDto>>(
                Error.Validation("Workflow.Paging.InvalidPageSize", "pageSize phải lớn hơn 0"));
        }

        if (pageNo < 1)
        {
            return Result.Failure<PagedResult<WorkflowDto>>(
                Error.Validation("Workflow.Paging.InvalidPageNo", "pageNo phải lớn hơn hoặc bằng 1"));
        }

        var totalCountLong = await _definitionRepository.CountAsync(
            x => (!isPublished.HasValue || x.IsPublished == isPublished.Value)
                 && (normalizedName == null || x.Name.Contains(normalizedName)),
            ct);
        if (totalCountLong > int.MaxValue)
        {
            return Result.Failure<PagedResult<WorkflowDto>>(
                Error.Failure("Workflow.Paging.TotalCountOverflow", "Total workflow count is too large."));
        }

        var totalCount = (int)totalCountLong;
        var pageIndex = pageNo - 1;
        if (pageIndex > int.MaxValue / pageSize)
        {
            return Result.Failure<PagedResult<WorkflowDto>>(
                Error.Validation("Workflow.Paging.InvalidRange", "Giá trị pageNo và pageSize quá lớn."));
        }

        var skip = pageIndex * pageSize;

        var workflows = await _definitionRepository.GetDefinitionsAsync(skip, pageSize, isPublished, normalizedName, ct);
        var metrics = await BuildRunMetricsAsync(workflows.Select(x => x.Id).ToArray(), ct);

        var items = workflows
            .Select(x => MapWorkflowDto(x, metrics))
            .ToArray();

        var pagedResult = PagedResult<WorkflowDto>.Create(
            items: items,
            totalCount: totalCount,
            pageNumber: pageNo,
            pageSize: pageSize);

        return Result.Success(pagedResult);
    }

    public async Task<Result<PagedResult<WorkflowPagingDto>>> GetPagingGroupVersionWorkflowAsync(
        int pageSize = Consts.PAGE_SIZE_DEFAULT,
        int pageNo = 1,
        bool? isPublished = null,
        string? name = null,
        CancellationToken ct = default)
    {
        var normalizedName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        if (pageSize <= 0)
        {
            return Result.Failure<PagedResult<WorkflowPagingDto>>(
                Error.Validation("Workflow.Paging.InvalidPageSize", "pageSize phải lớn hơn 0"));
        }

        if (pageNo < 1)
        {
            return Result.Failure<PagedResult<WorkflowPagingDto>>(
                Error.Validation("Workflow.Paging.InvalidPageNo", "pageNo phải lớn hơn hoặc bằng 1"));
        }

        var totalCountLong = await _definitionRepository.CountDistinctNamesAsync(isPublished, normalizedName, ct);
        if (totalCountLong > int.MaxValue)
        {
            return Result.Failure<PagedResult<WorkflowPagingDto>>(
                Error.Failure("Workflow.Paging.TotalCountOverflow", "Total workflow count is too large."));
        }

        var totalCount = (int)totalCountLong;
        var pageIndex = pageNo - 1;

        if (pageIndex > int.MaxValue / pageSize)
        {
            return Result.Failure<PagedResult<WorkflowPagingDto>>(
                Error.Validation("Workflow.Paging.InvalidRange", "Giá trị pageNo và pageSize quá lớn."));
        }

        var skip = pageIndex * pageSize;

        var names = await _definitionRepository.GetDefinitionNamesAsync(skip, pageSize, isPublished, normalizedName, ct);
        if (names.Count == 0)
        {
            var emptyPaged = PagedResult<WorkflowPagingDto>.Create([], totalCount, pageNo, pageSize);
            return Result.Success(emptyPaged);
        }

        var workflows = await _definitionRepository.GetDefinitionsByNamesAsync(names, isPublished, normalizedName, ct);
        var metrics = await BuildRunMetricsAsync(workflows.Select(x => x.Id).ToArray(), ct);

        var groupedWorkflows = workflows
            .GroupBy(x => x.Name)
            .OrderBy(x => x.Key)
            .ToList();

        var items = groupedWorkflows
            .Select(group => new WorkflowPagingDto(
                Name: group.Key,
                Versions: group
                    .OrderByDescending(x => x.Version)
                    .Select(x => MapWorkflowDto(x, metrics))
                    .ToList()))
            .ToArray();

        var pagedResult = PagedResult<WorkflowPagingDto>.Create(
            items: items,
            totalCount: totalCount,
            pageNumber: pageNo,
            pageSize: pageSize);

        return Result.Success(pagedResult);
    }

    private async Task<Dictionary<Guid, (int TotalRunCount, Dictionary<string, int> StatusCounts)>> BuildRunMetricsAsync(
        IReadOnlyCollection<Guid> definitionIds,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, (int TotalRunCount, Dictionary<string, int> StatusCounts)>();
        if (definitionIds.Count == 0)
        {
            return result;
        }

        var aggregates = await _definitionRepository.GetExecutionStatusAggregatesAsync(definitionIds, ct);
        var grouped = aggregates
            .GroupBy(x => x.DefinitionId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(x => x.Status, x => x.Count));

        var statuses = Enum.GetValues<WorkflowInstanceStatus>();
        foreach (var definitionId in definitionIds.Distinct())
        {
            grouped.TryGetValue(definitionId, out var countsByStatus);

            var statusCounts = statuses
                .ToDictionary(
                    status => status.ToString(),
                    status => countsByStatus is not null && countsByStatus.TryGetValue(status, out var count) ? count : 0);

            result[definitionId] = (statusCounts.Values.Sum(), statusCounts);
        }

        return result;
    }

    private static WorkflowDto MapWorkflowDto(
        WorkflowDefinition x,
        IReadOnlyDictionary<Guid, (int TotalRunCount, Dictionary<string, int> StatusCounts)> metrics)
    {
        var metric = metrics.TryGetValue(x.Id, out var value)
            ? value
            : (TotalRunCount: 0, StatusCounts: Enum.GetValues<WorkflowInstanceStatus>()
                .ToDictionary(status => status.ToString(), _ => 0));

        return new(
            Id: x.Id,
            Name: x.Name,
            Description: x.Description,
            Version: x.Version,
            IsPublished: x.IsPublished,
            CreatedAt: x.CreatedAt,
            LastUpdated: x.LastUpdated,
            TotalRunCount: metric.TotalRunCount,
            StatusCounts: metric.StatusCounts);
    }
}
