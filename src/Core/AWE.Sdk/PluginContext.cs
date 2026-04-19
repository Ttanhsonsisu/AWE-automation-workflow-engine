//using System.Text.Json;

//namespace AWE.Sdk;

//public class PluginContext
//{
//    private readonly JsonElement _root;

//    public CancellationToken CancellationToken { get; }

//    public PluginContext(string jsonPayload, CancellationToken ct)
//    {
//        // Parse payload 1 lần, lưu root element
//        // Lưu ý: Người gọi (Loader) phải đảm bảo JsonDocument không bị Dispose trước khi Plugin chạy xong
//        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(jsonPayload) ? "{}" : jsonPayload);
//        _root = doc.RootElement.Clone(); // Clone để tách khỏi vòng đời của doc gốc nếu cần
//        CancellationToken = ct;
//    }

//    // Constructor hỗ trợ truyền JsonElement trực tiếp
//    public PluginContext(JsonElement root, CancellationToken ct)
//    {
//        _root = root;
//        CancellationToken = ct;
//    }

//    /// <summary>
//    /// Helper lấy dữ liệu an toàn, tự convert sang type mong muốn
//    /// </summary>
//    public T? Get<T>(string key)
//    {
//        if (_root.ValueKind != JsonValueKind.Object) return default;

//        if (_root.TryGetProperty(key, out var element))
//        {
//            try
//            {
//                return element.Deserialize<T>();
//            }
//            catch
//            {
//                return default;
//            }
//        }
//        return default;
//    }

//    /// <summary>
//    /// Lấy raw JsonElement nếu plugin muốn xử lý phức tạp
//    /// </summary>
//    public JsonElement GetRaw(string key)
//    {
//        if (_root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(key, out var element))
//        {
//            return element;
//        }
//        return default;
//    }
//}
