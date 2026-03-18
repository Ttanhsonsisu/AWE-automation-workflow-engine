using System;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Executions.ResumeExecution;

public class ResumeExecutionRequest
{
    public Guid InstanceId { get; set; }
}

public interface IResumeExecutionUseCase
{
    Task<Result> ExecuteAsync(ResumeExecutionRequest request, CancellationToken cancellationToken = default);
}

public class ResumeExecutionUseCase : IResumeExecutionUseCase
{
    private readonly IWorkflowInstanceRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public ResumeExecutionUseCase(
        IWorkflowInstanceRepository repository,
        IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> ExecuteAsync(ResumeExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var instance = await _repository.GetInstanceByIdAsync(request.InstanceId, cancellationToken);

        if (instance == null)
            return Result.Failure(Error.NotFound("Execution.NotFound", "Workflow execution not found"));

        try
        {
            // Update status back to running
            instance.Resume();
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
