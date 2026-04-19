using System;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Application.UseCases.Workflows;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.DeleteDefinition;

public class DeleteDefinitionUseCase : IDeleteDefinitionUseCase
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWorkflowSchedulerSyncTaskRepository _schedulerSyncTaskRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDefinitionUseCase(
        IWorkflowDefinitionRepository definitionRepository,
        IWorkflowSchedulerSyncTaskRepository schedulerSyncTaskRepository,
        IUnitOfWork unitOfWork)
    {
        _definitionRepository = definitionRepository;
        _schedulerSyncTaskRepository = schedulerSyncTaskRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> ExecuteAsync(DeleteDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _definitionRepository.GetDefinitionByIdAsync(request.Id, cancellationToken);
        if (existing == null)
            return Result.Failure(Error.NotFound("DeleteDefinition.NotFound", "Workflow definition not found."));

        await _definitionRepository.DeleteDefinitionAsync(request.Id, cancellationToken);
        await _schedulerSyncTaskRepository.EnqueueAsync(request.Id, WorkflowSchedulerSyncOperation.Delete, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
