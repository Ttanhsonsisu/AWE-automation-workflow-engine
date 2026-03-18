using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.ExportDefinition;

public class ExportDefinitionUseCase : IExportDefinitionUseCase
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;

    public ExportDefinitionUseCase(IWorkflowDefinitionRepository definitionRepository)
    {
        _definitionRepository = definitionRepository;
    }

    public async Task<Result<ExportDefinitionResponse>> ExecuteAsync(ExportDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        var existingDef = await _definitionRepository.GetDefinitionByIdAsync(request.Id, cancellationToken);
        if (existingDef == null)
            return Result.Failure<ExportDefinitionResponse>(Error.NotFound("ExportDefinition.NotFound", "Workflow definition not found"));

        var payload = new
        {
            Name = existingDef.Name,
            DefinitionJson = existingDef.DefinitionJson.RootElement.Clone(),
            UiJson = existingDef.UiJson.RootElement.Clone()
        };

       
        return Result.Success(new ExportDefinitionResponse
        {
            Name = existingDef.Name,
            Version = existingDef.Version,
            ExportedJson = payload
        });
    }
}
