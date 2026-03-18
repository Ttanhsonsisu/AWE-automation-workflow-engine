using System;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.DeleteDefinition;

public class DeleteDefinitionUseCase : IDeleteDefinitionUseCase
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteDefinitionUseCase(
        IWorkflowDefinitionRepository definitionRepository,
        IUnitOfWork unitOfWork)
    {
        _definitionRepository = definitionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> ExecuteAsync(DeleteDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _definitionRepository.GetDefinitionByIdAsync(request.Id, cancellationToken);
        if (existing == null)
            return Result.Failure(Error.NotFound("DeleteDefinition.NotFound", "Workflow definition not found."));

        await _definitionRepository.DeleteDefinitionAsync(request.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
