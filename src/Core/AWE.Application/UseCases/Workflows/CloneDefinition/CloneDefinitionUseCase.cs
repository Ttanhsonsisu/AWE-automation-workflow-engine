using System;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.CloneDefinition;

public class CloneDefinitionUseCase : ICloneDefinitionUseCase
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public CloneDefinitionUseCase(
        IWorkflowDefinitionRepository definitionRepository,
        IUnitOfWork unitOfWork)
    {
        _definitionRepository = definitionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<CloneDefinitionResponse>> ExecuteAsync(CloneDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var sourceDef = await _definitionRepository.GetDefinitionByIdAsync(request.SourceDefinitionId, cancellationToken);
        if (sourceDef == null)
        {
            return Result.Failure<CloneDefinitionResponse>(Error.NotFound("CloneDefinition.SourceNotFound", "Source Definition not found"));
        }

        var existingWithTargetName = await _definitionRepository.GetLatestVersionByNameAsync(request.NewName, cancellationToken);
        int nextVersion = existingWithTargetName != null ? existingWithTargetName.Version + 1 : 1;

        var clonedDef = new WorkflowDefinition(
            request.NewName,
            nextVersion,
            System.Text.Json.JsonDocument.Parse(sourceDef.DefinitionJson.RootElement.GetRawText()),
            System.Text.Json.JsonDocument.Parse(sourceDef.UiJson.RootElement.GetRawText())
        );

        await _definitionRepository.AddDefinitionAsync(clonedDef, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CloneDefinitionResponse
        {
            Id = clonedDef.Id,
            Name = clonedDef.Name,
            Version = clonedDef.Version
        });
    }
}
