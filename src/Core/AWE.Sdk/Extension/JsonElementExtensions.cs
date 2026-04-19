using System.Text.Json;

namespace AWE.Sdk.Extension;

public static class JsonElementExtensions
{
    /// <summary>
    /// Tìm kiếm Property trong JSON không phân biệt chữ hoa chữ thường.
    /// </summary>
    public static bool TryGetPropertyCaseInsensitive(this JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        // 1. Thử tìm chính xác trước (Tốc độ nhanh nhất O(1))
        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }
        // 2. Nếu không thấy, quét qua tất cả các key và so sánh bỏ qua Hoa/Thường (O(N))
        foreach (var prop in element.EnumerateObject())
        {
            if (string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        return false;
    }
}
