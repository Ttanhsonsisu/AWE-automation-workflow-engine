using System;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Executions.SuspendExecution;

public class SuspendExecutionRequest
{
    public Guid InstanceId { get; set; }
}

public interface ISuspendExecutionUseCase
{
    Task<Result> ExecuteAsync(SuspendExecutionRequest request, CancellationToken cancellationToken = default);
}

public class SuspendExecutionUseCase : ISuspendExecutionUseCase
{
    private readonly IWorkflowInstanceRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public SuspendExecutionUseCase(
        IWorkflowInstanceRepository repository,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> ExecuteAsync(SuspendExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var instance = await _repository.GetInstanceByIdAsync(request.InstanceId, cancellationToken);
        
        if (instance == null)
            return Result.Failure(Error.NotFound("Execution.NotFound", "Workflow execution not found"));

        try
        {
            instance.Suspend();
            await _repository.UpdateInstanceAsync(instance, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.BusinessRule("Execution.InvalidState", ex.Message));
        }
    }
}
