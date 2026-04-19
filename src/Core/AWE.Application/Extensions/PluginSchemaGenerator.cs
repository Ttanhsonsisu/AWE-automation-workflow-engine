using System.Collections.Concurrent;
using AWE.Sdk.v2.Attributes;
using Namotion.Reflection;
using NJsonSchema.Generation;
using NJsonSchema;
using System.Collections.Generic;


namespace AWE.Application.Extensions;

public static class PluginSchemaGenerator
{
    private static readonly ConcurrentDictionary<Type, string> _schemaCache = new();

    /// <summary>
    /// Dịch một Type (Class/Record) của C# thành chuỗi JSON Schema chuẩn OpenAPI 3.
    /// </summary>
    public static string GenerateSchema(Type? type)
    {
        // 1. Nếu Plugin không định nghĩa Input/Output, trả về Object rỗng
        if (type == null) return "{}";

        // 2. Kiểm tra Cache (Tra cứu O(1) siêu nhanh)
        if (_schemaCache.TryGetValue(type, out var cachedSchema))
        {
            return cachedSchema;
        }

        // 3. Cấu hình bộ dịch (Generator Settings)
        var settings = new SystemTextJsonSchemaGeneratorSettings
        {
            SchemaType = SchemaType.OpenApi3, // Chuẩn phổ biến nhất cho React Json Schema Form
            GenerateExamples = true,          // Hỗ trợ hiển thị example data nếu có

            // Nhúng Processor tùy chỉnh để đọc [UiFieldAttribute]
            SchemaProcessors = { new UiFieldSchemaProcessor() }
        };

        // 4. Sinh ra Schema từ Type
        var schema = JsonSchema.FromType(type, settings);

        // Cấu hình không cho phép truyền thêm các trường rác ngoài schema
        schema.AllowAdditionalProperties = false;

        var json = schema.ToJson();

        // 5. Lưu vào Cache để lần sau dùng lại
        _schemaCache.TryAdd(type, json);

        return json;
    }

    /// <summary>
    /// Tiện ích: Dùng lúc App vừa khởi động để nạp sẵn Schema cho toàn bộ Built-in Plugins
    /// Tránh hiện tượng Request đầu tiên bị chậm (Cold Start).
    /// </summary>
    public static void PreWarmCache(IEnumerable<Type> typesToCache)
    {
        foreach (var type in typesToCache)
        {
            if (type != null) GenerateSchema(type);
        }
    }
}

/// <summary>
/// Trình xử lý can thiệp vào quá trình gen Schema của NJsonSchema.
/// Nhiệm vụ: Quét các Property có gắn [UiField] và chuyển nó thành metadata mở rộng (x-...) trong JSON.
/// </summary>
public class UiFieldSchemaProcessor : ISchemaProcessor
{
    public void Process(SchemaProcessorContext context)
    {
        // Lấy Attribute [UiField] nếu property hiện tại có gắn
        var uiAttr = context.ContextualType.GetContextAttributes(true)
            .OfType<UiFieldAttribute>()
            .FirstOrDefault();

        if (uiAttr != null)
        {
            // Đảm bảo object ExtensionData đã được khởi tạo
            context.Schema.ExtensionData ??= new Dictionary<string, object>();

            // Ép các thông số UI vào JSON Schema dưới dạng "x-*" (Quy ước chuẩn của JSON Schema mở rộng)
            if (!string.IsNullOrWhiteSpace(uiAttr.Widget))
            {
                context.Schema.ExtensionData["x-widget"] = uiAttr.Widget;
            }

            if (!string.IsNullOrWhiteSpace(uiAttr.Group))
            {
                context.Schema.ExtensionData["x-group"] = uiAttr.Group;
            }

            if (!string.IsNullOrWhiteSpace(uiAttr.ShowIf))
            {
                context.Schema.ExtensionData["x-show-if"] = uiAttr.ShowIf;
            }

            if (!string.IsNullOrWhiteSpace(uiAttr.Label))
            {
                context.Schema.ExtensionData["x-label"] = uiAttr.Label;
            }

            if (!string.IsNullOrWhiteSpace(uiAttr.DataSourceUrl))
            {
                context.Schema.ExtensionData["x-data-source-url"] = uiAttr.DataSourceUrl;
            }
        }
    }
}
