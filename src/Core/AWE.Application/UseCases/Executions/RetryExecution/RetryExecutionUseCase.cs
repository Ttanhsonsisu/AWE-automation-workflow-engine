using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Executions.RetryExecution;

public class RetryExecutionRequest
{
    public Guid InstanceId { get; set; }
}

public interface IRetryExecutionUseCase
{
    Task<Result> ExecuteAsync(RetryExecutionRequest request, CancellationToken cancellationToken = default);
}

public class RetryExecutionUseCase : IRetryExecutionUseCase
{
    private readonly IWorkflowInstanceRepository _repository;

    public RetryExecutionUseCase(
        IWorkflowInstanceRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result> ExecuteAsync(RetryExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var instance = await _repository.GetInstanceByIdAsync(request.InstanceId, cancellationToken);

        if (instance == null)
            return Result.Failure(Error.NotFound("Execution.NotFound", "Workflow execution not found"));

        if (instance.Status != Domain.Enums.WorkflowInstanceStatus.Failed)
        {
            return Result.Failure(Error.BusinessRule("Execution.NotFailed", "Only failed workflows can be retried."));
        }

        // Generate a simple retry by submitting a new workflow execution with previous context
        var command = new SubmitWorkflowCommand(
            DefinitionId: instance.DefinitionId,
            JobName: $"Retry-{instance.Id}",
            InputData: instance.ContextData.RootElement.GetRawText(),
            CorrelationId: Guid.NewGuid()
        );


        return Result.Success();
    }
}
