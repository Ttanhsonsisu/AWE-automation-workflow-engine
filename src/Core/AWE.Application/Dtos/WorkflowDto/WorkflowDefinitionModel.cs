using System.Text.Json;
using System.Text.Json.Serialization;
using AWE.Domain.Enums;
using AWE.Shared.Extensions;

namespace AWE.Application.Dtos.WorkflowDto;

public class WorkflowDefinitionModel
{
    public List<WorkflowStepModel> Steps { get; set; } = new();
    public List<WorkflowTransitionModel> Transitions { get; set; } = new();

    // Hàm tiện ích để Parse chuẩn từ chuỗi JSON bất kỳ của FE
    public static WorkflowDefinitionModel Parse(string jsonStr)
    {
        if (string.IsNullOrWhiteSpace(jsonStr)) return new WorkflowDefinitionModel();

        return JsonSerializer.Deserialize<WorkflowDefinitionModel>(jsonStr, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true, // Cứu tinh số 1
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        }) ?? new WorkflowDefinitionModel();
    }
}

public class WorkflowStepModel
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public PluginExecutionMode ExecutionMode { get; set; }

    public JsonElement Inputs { get; set; }
    public JsonElement? ExecutionMetadata { get; set; }

    /// <summary>
    /// 🔥 HÀM THÔNG DỊCH QUAN TRỌNG NHẤT:
    /// Trả về Tên Plugin thực sự cần chạy, bất chấp FE gửi "Type" là gì.
    /// </summary>
    public string GetActualPluginType()
    {
        // Nếu là Dynamic DLL, tên Plugin thật nằm giấu trong Metadata
        if (ExecutionMode == PluginExecutionMode.DynamicDll && ExecutionMetadata.HasValue)
        {
            if (ExecutionMetadata.Value.TryGetPropertyCaseInsensitive("PluginType", out var pt))
            {
                return pt.GetString() ?? Type;
            }
        }

        // Nếu là Built-in, tên Plugin chính là cái cột Type ở ngoài cùng
        return Type;
    }
}

public class WorkflowTransitionModel
{
    public string Id { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}
