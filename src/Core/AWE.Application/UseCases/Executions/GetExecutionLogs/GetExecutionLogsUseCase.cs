using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Executions.GetExecutionLogs;

public class GetExecutionLogsRequest
{
    public Guid InstanceId { get; set; }
}

public class ExecutionLogDto
{
    public long Id { get; set; }
    public Guid InstanceId { get; set; }
    public Guid? ExecutionPointerId { get; set; }
    public string? NodeId { get; set; }
    public LogLevel Level { get; set; }
    public string Event { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public JsonDocument? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
}

public interface IGetExecutionLogsUseCase
{
    Task<Result<IReadOnlyList<ExecutionLogDto>>> ExecuteAsync(GetExecutionLogsRequest request, CancellationToken cancellationToken = default);
}

public class GetExecutionLogsUseCase : IGetExecutionLogsUseCase
{
    private readonly IExecutionLogRepository _repository;

    public GetExecutionLogsUseCase(IExecutionLogRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<ExecutionLogDto>>> ExecuteAsync(GetExecutionLogsRequest request, CancellationToken cancellationToken = default)
    {
        var logs = await _repository.GetLogsByInstanceAsync(request.InstanceId, cancellationToken);

        var dtos = logs.Select(x => new ExecutionLogDto
        {
            Id = x.Id,
            InstanceId = x.InstanceId,
            ExecutionPointerId = x.ExecutionPointerId,
            NodeId = x.NodeId,
            Level = x.Level,
            Event = x.Event,
            Message = x.Message,
            Metadata = x.Metadata,
            CreatedAt = x.CreatedAt
        }).ToList();

        return Result.Success<IReadOnlyList<ExecutionLogDto>>(dtos);
    }
}
