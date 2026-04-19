using AWE.Application.Abstractions.Persistence;
using AWE.Application.UseCases.Workflows;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.UnpublishDefinition;

public class UnpublishDefinitionUseCase : IUnpublishDefinitionUseCase
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWebhookRouteRepository _webhookRouteRepository;
    private readonly IWorkflowSchedulerSyncTaskRepository _schedulerSyncTaskRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UnpublishDefinitionUseCase(
        IWorkflowDefinitionRepository definitionRepository,
        IWebhookRouteRepository webhookRouteRepository,
        IWorkflowSchedulerSyncTaskRepository schedulerSyncTaskRepository,
        IUnitOfWork unitOfWork)
    {
        _definitionRepository = definitionRepository;
        _webhookRouteRepository = webhookRouteRepository;
        _schedulerSyncTaskRepository = schedulerSyncTaskRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<UnpublishDefinitionResponse>> ExecuteAsync(UnpublishDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var definition = await _definitionRepository.GetDefinitionByIdAsync(request.Id, cancellationToken);
        if (definition is null)
        {
            return Result.Failure<UnpublishDefinitionResponse>(
                Error.NotFound("UnpublishDefinition.NotFound", "Workflow definition not found"));
        }

        if (definition.IsPublished)
        {
            definition.Unpublish();
            await _definitionRepository.UpdateDefinitionAsync(definition, cancellationToken);
        }

        var routes = await _webhookRouteRepository.GetByWorkflowDefinitionIdAsync(definition.Id, cancellationToken);
        foreach (var route in routes.Where(x => x.IsActive))
        {
            route.Deactivate();
            await _webhookRouteRepository.UpdateAsync(route, cancellationToken);
        }

        await _schedulerSyncTaskRepository.EnqueueAsync(definition.Id, WorkflowSchedulerSyncOperation.Unpublish, cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new UnpublishDefinitionResponse
        {
            Id = definition.Id,
            Name = definition.Name,
            Version = definition.Version,
            IsPublished = definition.IsPublished
        });
    }
}
