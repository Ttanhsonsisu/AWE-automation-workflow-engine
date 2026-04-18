using System;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Application.UseCases.Workflows;
using AWE.Shared.Primitives;
using Quartz;

namespace AWE.Application.UseCases.Workflows.DeleteDefinition;

public class DeleteDefinitionUseCase : IDeleteDefinitionUseCase
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly ISchedulerFactory _schedulerFactory;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDefinitionUseCase(
        IWorkflowDefinitionRepository definitionRepository,
        ISchedulerFactory schedulerFactory,
        IUnitOfWork unitOfWork)
    {
        _definitionRepository = definitionRepository;
        _schedulerFactory = schedulerFactory;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> ExecuteAsync(DeleteDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _definitionRepository.GetDefinitionByIdAsync(request.Id, cancellationToken);
        if (existing == null)
            return Result.Failure(Error.NotFound("DeleteDefinition.NotFound", "Workflow definition not found."));

        var scheduler = await _schedulerFactory.GetScheduler(cancellationToken);
        await CronScheduleSyncHelper.DeleteAsync(scheduler, request.Id, cancellationToken);

        await _definitionRepository.DeleteDefinitionAsync(request.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
