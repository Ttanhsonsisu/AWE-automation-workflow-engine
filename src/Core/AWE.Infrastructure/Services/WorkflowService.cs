using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Application.Dtos.WorkflowDto;
using AWE.Application.Services;
using AWE.Shared.Primitives;

namespace AWE.Infrastructure.Services;

public class WorkflowService(IWorkflowDefinitionRepository definitionRepository) : IWorkflowService
{
    private readonly IWorkflowDefinitionRepository _definitionRepository = definitionRepository;
    public async Task<Result<WorkflowDetailDto>> GetWorkflowDetailsAsync(Guid id, CancellationToken ct = default)
    {
        // 1. Lấy workflow từ Database
        var workflow = await _definitionRepository.GetDefinitionByIdAsync(id, ct);

        if (workflow == null)
        {
            return Result.Failure<WorkflowDetailDto>(Error.NotFound("Workflow.NotFound", $"Không tìm thấy workflow có ID {id}"));
        }

        // 2. Lấy thẳng RootElement từ JsonDocument của Entity
        JsonElement definitionElement;
        JsonElement uiJson = workflow.UiJson != null ?
            workflow.UiJson.RootElement.Clone() :
            JsonDocument.Parse("{}").RootElement.Clone();

        if (workflow.DefinitionJson != null)
        {
            definitionElement = workflow.DefinitionJson.RootElement.Clone();
        }
        else
        {
            // Trả về Canvas trống nếu chưa có data
            definitionElement = JsonDocument.Parse("{\"nodes\":[], \"edges\":[]}").RootElement.Clone();
        }

        // 3. Đóng gói trả về cho Frontend
        var dto = new WorkflowDetailDto(
            Id: workflow.Id,
            Name: workflow.Name,
            IsPublished: workflow.IsPublished,
            Definition: definitionElement,
            UiJson: uiJson
        );

        return Result.Success(dto);
    }
}
