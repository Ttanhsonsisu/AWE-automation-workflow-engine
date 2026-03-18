using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Workflows.ImportDefinition;

public class ImportDefinitionUseCase : IImportDefinitionUseCase
{
    private readonly IWorkflowDefinitionRepository _definitionRepository;
    private readonly IUnitOfWork _unitOfWork;

    public ImportDefinitionUseCase(
        IWorkflowDefinitionRepository definitionRepository,
        IUnitOfWork unitOfWork)
    {
        _definitionRepository = definitionRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<ImportDefinitionResponse>> ExecuteAsync(ImportDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ImportedJson))
            return Result.Failure<ImportDefinitionResponse>(Error.Validation("ImportDefinition.EmptyJson", "Imported JSON cannot be empty"));

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(request.ImportedJson);
        }
        catch (JsonException ex)
        {
            return Result.Failure<ImportDefinitionResponse>(Error.Validation("ImportDefinition.InvalidJson", $"Invalid JSON format: {ex.Message}"));
        }

        using (document)
        {
            var root = document.RootElement;

            bool nameExists = root.TryGetProperty("Name", out var nameElement);
            var name = nameExists ? nameElement.GetString() ?? "ImportedWorkflow" : "ImportedWorkflow";

            if (!root.TryGetProperty("DefinitionJson", out var defJsonProp))
            {
                return Result.Failure<ImportDefinitionResponse>(Error.Validation("ImportDefinition.MissingDefinition", "Missing DefinitionJson"));
            }

            var defJsonStr = defJsonProp.GetRawText();
            var defJson = JsonDocument.Parse(defJsonStr);

            JsonDocument uiJson;
            if (root.TryGetProperty("UiJson", out var uiElement))
                uiJson = JsonDocument.Parse(uiElement.GetRawText());
            else
                uiJson = JsonDocument.Parse("{}");

            var existing = await _definitionRepository.GetLatestVersionByNameAsync(name, cancellationToken);
            int nextVersion = existing != null ? existing.Version + 1 : 1;

            var newDefinition = new WorkflowDefinition(name, nextVersion, defJson, uiJson);

            await _definitionRepository.AddDefinitionAsync(newDefinition, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(new ImportDefinitionResponse
            {
                Id = newDefinition.Id,
                Name = newDefinition.Name,
                Version = newDefinition.Version
            });
        }
    }
}

