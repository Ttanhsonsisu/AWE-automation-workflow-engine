using AWE.Application.Abstractions.Persistence;
using AWE.Application.UseCases.Workflows;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.PublishDefinition;

public class PublishDefinitionUseCase : IPublishDefinitionUseCase
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IWebhookRouteRepository _webhookRouteRepository;
    private readonly IUnitOfWork _unitOfWork;

    public PublishDefinitionUseCase(
        IWorkflowDefinitionRepository definitionRepository,
        IWebhookRouteRepository webhookRouteRepository,
        IUnitOfWork unitOfWork)
    {
        _definitionRepository = definitionRepository;
        _webhookRouteRepository = webhookRouteRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<PublishDefinitionResponse>> ExecuteAsync(PublishDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var definition = await _definitionRepository.GetDefinitionByIdAsync(request.Id, cancellationToken);
        if (definition is null)
        {
            return Result.Failure<PublishDefinitionResponse>(
                Error.NotFound("PublishDefinition.NotFound", "Workflow definition not found"));
        }

        if (!definition.IsPublished)
        {
            definition.Publish();
            await _definitionRepository.UpdateDefinitionAsync(definition, cancellationToken);
        }

        await WebhookRouteSyncHelper.SyncAsync(_webhookRouteRepository, definition.Id, definition.DefinitionJson, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new PublishDefinitionResponse
        {
            Id = definition.Id,
            Name = definition.Name,
            Version = definition.Version,
            IsPublished = definition.IsPublished
        });
    }
}
