using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Executions.CancelExecution;

public interface ICancelExecutionUseCase
{
    Task<Result> ExecuteAsync(CancelExecutionRequest request, CancellationToken cancellationToken = default);
}
