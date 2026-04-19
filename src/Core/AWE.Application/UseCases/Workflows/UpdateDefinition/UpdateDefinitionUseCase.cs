using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.UpdateDefinition;

public class UpdateDefinitionUseCase : IUpdateDefinitionUseCase
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateDefinitionUseCase(
        IWorkflowDefinitionRepository definitionRepository,
        IUnitOfWork unitOfWork)
    {
        _definitionRepository = definitionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<UpdateDefinitionResponse>> ExecuteAsync(UpdateDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var existingDef = await _definitionRepository.GetDefinitionByIdAsync(request.Id, cancellationToken);
        if (existingDef == null)
            return Result.Failure<UpdateDefinitionResponse>(Error.NotFound("UpdateDefinition.NotFound", "Workflow definition not found"));

        if (existingDef.IsPublished)
        {
            var latestVer = await _definitionRepository.GetLatestVersionByNameAsync(existingDef.Name, cancellationToken);
            int nextVersion = (latestVer != null) ? latestVer.Version + 1 : existingDef.Version + 1;

            var newVersionDef = new WorkflowDefinition(
                existingDef.Name,
                nextVersion,
                request.DefinitionJson,
                request.UiJson
            );

            await _definitionRepository.AddDefinitionAsync(newVersionDef, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(new UpdateDefinitionResponse
            {
                Id = newVersionDef.Id,
                Name = newVersionDef.Name,
                Version = newVersionDef.Version
            });
        }
        else
        {
            existingDef.UpdateContent(request.DefinitionJson, request.UiJson);
            await _definitionRepository.UpdateDefinitionAsync(existingDef, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(new UpdateDefinitionResponse
            {
                Id = existingDef.Id,
                Name = existingDef.Name,
                Version = existingDef.Version
            });
        }
    }
}
