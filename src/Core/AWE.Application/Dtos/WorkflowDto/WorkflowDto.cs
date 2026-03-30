using AWE.Domain.Enums;

namespace AWE.Application.Dtos.WorkflowDto;

public record WorkflowPagingDto(
    string Name,
    List<WorkflowDto> Versions);

public record WorkflowStatusCountDto(
    WorkflowInstanceStatus Status,
    int Count);

public record WorkflowExecutionStatusAggregate(
    Guid DefinitionId,
    WorkflowInstanceStatus Status,
    int Count);

public record WorkflowDto
(
    Guid Id,
    string Name,
    string? Description,
    int? Version,
    bool? IsPublished,
    DateTime CreatedAt,
    DateTime? LastUpdated,
    int TotalRunCount,
    Dictionary<string, int> StatusCounts
);
