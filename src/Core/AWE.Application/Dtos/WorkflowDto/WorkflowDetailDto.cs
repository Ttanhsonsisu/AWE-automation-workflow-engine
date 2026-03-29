using System.Text.Json;

namespace AWE.Application.Dtos.WorkflowDto;

public record WorkflowDetailDto
(
    Guid Id,
    string Name,
    bool? IsPublished,
    JsonElement Definition, // Bọc toàn bộ Node, Edge, Canvas viewport vào đây
    JsonElement? UiJson
);
