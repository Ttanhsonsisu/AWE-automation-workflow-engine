using System.Text.Json;
using AWE.Sdk.Extension;

namespace AWE.Sdk.v2;

public class PluginContext
{
    // 🔥 BỔ SUNG: Lưu lại chuỗi JSON gốc để Base Class có thể Deserialize thành TInput
    public string Payload { get; }

    public JsonElement Root { get; }
    public CancellationToken CancellationToken { get; }

    public PluginContext(string jsonPayload, CancellationToken ct)
    {
        Payload = string.IsNullOrWhiteSpace(jsonPayload) ? "{}" : jsonPayload;

        using var doc = JsonDocument.Parse(Payload);
        Root = doc.RootElement.Clone();
        CancellationToken = ct;
    }

    public PluginContext(JsonElement root, CancellationToken ct)
    {
        Root = root;
        // Tái tạo lại chuỗi JSON từ Element
        Payload = root.GetRawText();
        CancellationToken = ct;
    }

    /// <summary>
    /// Helper lấy dữ liệu an toàn (Dành cho Dev không dùng Base Class)
    /// </summary>
    public T? Get<T>(string key)
    {
        if (Root.ValueKind != JsonValueKind.Object) return default;

        if (Root.TryGetPropertyCaseInsensitive(key, out var element))
        {
            try
            {
                return element.Deserialize<T>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                return default;
            }
        }
        return default;
    }

    public JsonElement GetRaw(string key)
    {
        if (Root.ValueKind == JsonValueKind.Object && Root.TryGetProperty(key, out var element))
        {
            return element;
        }
        return default;
    }
}
