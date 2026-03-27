using AWE.Sdk.v2;

namespace AWE.Plugins.Samples;

public class TextProcessorInput
{
    public string? Text { get; set; }
    public string? Operation { get; set; }
}

public class TextProcessorOutput
{
    public string? Result { get; set; }
}

public class TextProcessorPlugin : IWorkflowPlugin
{
    // ==========================================
    // 1. METADATA CHO FRONTEND
    // ==========================================
    public string Name => "AWE.Samples.TextProcessor";
    public string DisplayName => "Xử lý Văn bản (Text Processor)";
    public string Description => "Biến đổi chuỗi đầu vào (In hoa, In thường, Đảo ngược chữ).";
    public string Category => "Data Manipulation";
    public string Icon => "lucide-type";

    // ==========================================
    // 2. AUTO-DISCOVERY SCHEMA (Cho UI Designer render Form)
    // ==========================================
    public Type? InputType => typeof(TextProcessorInput);
    public Type? OutputType => typeof(TextProcessorOutput);

    // ==========================================
    // 3. LOGIC THỰC THI CHÍNH
    // ==========================================
    public Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        try
        {
            // Lấy dữ liệu từ Payload thông qua PluginContext
            var text = context.Get<string>("text");
            var operation = context.Get<string>("operation");

            if (string.IsNullOrWhiteSpace(text))
            {
                // Nếu dữ liệu thiếu, trả về Failure (Engine sẽ bắt được và log ra)
                return Task.FromResult(PluginResult.Failure("Đầu vào 'text' không được để trống."));
            }

            // Xử lý nghiệp vụ
            string resultText = operation?.ToUpper() switch
            {
                "UPPER" => text.ToUpper(),
                "LOWER" => text.ToLower(),
                "REVERSE" => new string(text.Reverse().ToArray()),
                _ => text.ToUpper() // Mặc định
            };

            // Đóng gói kết quả trả về
            var outputs = new Dictionary<string, object>
            {
                { "result", resultText },
                { "originalLength", text.Length } // Trả thêm data phụ nếu thích
            };

            return Task.FromResult(PluginResult.Success(outputs));
        }
        catch (Exception ex)
        {
            return Task.FromResult(PluginResult.Failure($"Lỗi xử lý text: {ex.Message}"));
        }
    }

    // ==========================================
    // 4. LOGIC ROLLBACK (Saga Pattern)
    // ==========================================
    public Task<PluginResult> CompensateAsync(PluginContext context)
    {
        // Với Plugin đổi text thì không có gì để rollback (không đổi state của hệ thống)
        // Ta chỉ cần trả về Success.
        return Task.FromResult(PluginResult.Success());
    }
}
