using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.CreateDefinition;

public class CreateDefinitionUseCase : ICreateDefinitionUseCase
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CreateDefinitionUseCase(
        IWorkflowDefinitionRepository definitionRepository,
        IUnitOfWork unitOfWork)
    {
        _definitionRepository = definitionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CreateDefinitionResponse>> ExecuteAsync(CreateDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Validate request
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Result.Failure<CreateDefinitionResponse>(Error.Validation("CreateDefinition.NameRequired", "Workflow name cannot be empty"));
        }

        // 2. Determine initial version
        // Find existing to know what next version should be if it's considered an initial creation with existing name
        var existing = await _definitionRepository.GetLatestVersionByNameAsync(request.Name, cancellationToken);
        int nextVersion = existing != null ? existing.Version + 1 : 1;

        // 3. Create domain entity
        var newDefinition = new WorkflowDefinition(
            request.Name,
            nextVersion,
            request.DefinitionJson,
            request.UiJson);

        if (request.Description != null)
        {
            newDefinition.Description = request.Description;
        }

        await _definitionRepository.AddDefinitionAsync(newDefinition, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateDefinitionResponse
        {
            Id = newDefinition.Id,
            Name = newDefinition.Name,
            Description = newDefinition.Description,
            Version = newDefinition.Version
        });
    }
}
