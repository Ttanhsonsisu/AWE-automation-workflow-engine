using System.Text.Json;
using AWE.Sdk.v2;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class ManualTriggerPlugin : ITriggerPlugin
{
    public string Name => "ManualTrigger";
    public string TriggerSource => "Manual";
    public bool IsSingleton => false;

    public string DisplayName => "Kích Hoạt Bằng Tay";
    public string Description => "Điểm khởi đầu cho các luồng chạy thủ công. Nhận dữ liệu truyền vào lúc khởi tạo và chuyển nó cho các bước tiếp theo.";
    public string Category => "Trigger";
    public string Icon => "lucide-mouse-pointer-click";

    // Vì dữ liệu đầu vào và đầu ra là do người dùng tự quyết định lúc chạy (dynamic json),
    // nên ta không gán cố định cho một Type Class nào cả.
    public Type? InputType => null;
    public Type? OutputType => null;

    public Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        object? outputs = null;

        try
        {
            // Pass-through: Lấy nguyên cái cục JSON mà API StartWorkflow truyền vào
            // Chuyển nó thành Dictionary để khi MassTransit / Engine lưu xuống DB, 
            // nó vẫn giữ được cấu trúc cây JSON chuẩn để FE đọc được.
            if (!string.IsNullOrWhiteSpace(context.Payload) && context.Payload != "{}")
            {
                outputs = JsonSerializer.Deserialize<Dictionary<string, object>>(context.Payload, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
        }
        catch
        {
            // Nếu người dùng lỡ truyền vào một chuỗi String bình thường không phải object JSON,
            // ta bọc nó lại thành một property tên là "RawInput" để hệ thống không bị crash.
            outputs = new { RawInput = context.Payload };
        }

        // Bắn Thành công ngay lập tức kèm theo dữ liệu khởi tạo
        return Task.FromResult(PluginResult.Success(outputs));
    }

    public Task<PluginResult> CompensateAsync(PluginContext context)
    {
        // Trigger thì không có gì để rollback cả
        return Task.FromResult(PluginResult.Success());
    }
}
