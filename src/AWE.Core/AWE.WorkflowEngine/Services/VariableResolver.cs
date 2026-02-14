using System.Text.Json;
using System.Text.RegularExpressions;
using AWE.WorkflowEngine.Interfaces;

namespace AWE.WorkflowEngine.Services;

//public class VariableResolver : IVariableResolver
//{
//    private static readonly Regex _regex = new Regex(@"\{\{(.*?)\}\}", RegexOptions.Compiled);

//    public string Resolve(string jsonTemplate, JsonDocument globalContext)
//    {
//        if (string.IsNullOrWhiteSpace(jsonTemplate)) return "{}";

//        return _regex.Replace(jsonTemplate, match =>
//        {
//            var rawPath = match.Groups[1].Value.Trim();
//            // Mapping cú pháp Docs -> Cấu trúc DB
//            // Docs: workflow.input.Id  -> DB: Inputs.Id
//            // Docs: steps.NodeA.output -> DB: Steps.NodeA.Output
//            string jsonPath = MapPathToEntity(rawPath);

//            var value = ExtractValue(globalContext.RootElement, jsonPath);
//            return value?.ToString() ?? "null";
//        });
//    }

//    private string MapPathToEntity(string path)
//    {
//        if (path.StartsWith("workflow.input.", StringComparison.OrdinalIgnoreCase))
//            return "Inputs." + path.Substring(15);

//        if (path.StartsWith("steps.", StringComparison.OrdinalIgnoreCase))
//        {
//            // steps.A.output.x -> Steps.A.Output.x
//            var parts = path.Split('.');
//            if (parts.Length >= 3 && parts[2].Equals("output", StringComparison.OrdinalIgnoreCase))
//            {
//                parts[0] = "Steps";
//                parts[2] = "Output";
//                return string.Join(".", parts);
//            }
//        }
//        return path;
//    }

//    private object? ExtractValue(JsonElement root, string path)
//    {
//        var current = root;
//        var segments = path.Split('.');

//        foreach (var segment in segments)
//        {
//            if (current.ValueKind != JsonValueKind.Object) return null;

//            // Case-insensitive property search
//            JsonProperty prop = default;
//            bool found = false;
//            foreach (var p in current.EnumerateObject())
//            {
//                if (p.Name.Equals(segment, StringComparison.OrdinalIgnoreCase))
//                {
//                    prop = p;
//                    found = true;
//                    break;
//                }
//            }

//            if (found) current = prop.Value;
//            else return null;
//        }

//        return current.ValueKind switch
//        {
//            JsonValueKind.String => current.GetString(),
//            JsonValueKind.Number => current.GetDouble(),
//            JsonValueKind.True => true,
//            JsonValueKind.False => false,
//            JsonValueKind.Null => null,
//            _ => current.GetRawText() // Object/Array trả về string JSON
//        };
//    }
//}

public class VariableResolver : IVariableResolver
{
    // Regex bắt chuỗi {{...}}
    private static readonly Regex _regex = new Regex(@"\{\{(.*?)\}\}", RegexOptions.Compiled);

    public string Resolve(string jsonTemplate, JsonDocument globalContext)
    {
        if (string.IsNullOrWhiteSpace(jsonTemplate)) return "{}";

        // Logic thay thế chuỗi
        return _regex.Replace(jsonTemplate, match =>
        {
            var rawPath = match.Groups[1].Value.Trim();

            // 1. Map Path: workflow.input.x -> Inputs.x
            string jsonPath = MapPathToEntity(rawPath);

            // 2. Lấy giá trị từ JSON gốc
            var value = ExtractValue(globalContext.RootElement, jsonPath);

            // 3. Xử lý format trả về (Quan trọng để không làm hỏng JSON)
            if (value is null) return "null";

            if (value is string s)
            {
                // Nếu template gốc đã có ngoặc kép bao quanh, vd: "{{var}}", thì ta chỉ trả về text
                // Nhưng Regex replace này thay thế cả cụm {{...}}, nên ta cần check ngữ cảnh.
                // ĐỂ AN TOÀN TUYỆT ĐỐI: Ta luôn trả về raw text, 
                // người dùng phải tự viết ngoặc kép trong JSON template nếu cần.
                // VD: "name": "{{...}}" -> "name": "Nguyen Van A"
                return s;
            }

            if (value is bool b) return b.ToString().ToLower(); // true/false (lowercase cho JSON)

            return value.ToString()!;
        });
    }

    private string MapPathToEntity(string path)
    {
        // Case 1: workflow.input -> Inputs
        if (path.StartsWith("workflow.input.", StringComparison.OrdinalIgnoreCase))
        {
            return "Inputs." + path.Substring(15); // Cắt bỏ "workflow.input."
        }

        // Case 2: steps.StepName.output -> Steps.StepName.Output
        if (path.StartsWith("steps.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = path.Split('.');
            if (parts.Length >= 3 && parts[2].Equals("output", StringComparison.OrdinalIgnoreCase))
            {
                // Giữ nguyên tên step (parts[1]), chỉ sửa output -> Output
                parts[0] = "Steps";
                parts[2] = "Output"; // Chuẩn hóa về PascalCase để khớp DB nếu cần
                return string.Join(".", parts);
            }
        }

        // Case 3: workflow.system -> System
        if (path.StartsWith("workflow.system.", StringComparison.OrdinalIgnoreCase))
        {
            return "System." + path.Substring(16);
        }

        return path;
    }

    private object? ExtractValue(JsonElement root, string path)
    {
        var current = root;
        var segments = path.Split('.');

        foreach (var segment in segments)
        {
            // Xử lý Array Index: VD "items[0]"
            var propertyName = segment;
            int? arrayIndex = null;

            if (segment.Contains('[') && segment.EndsWith(']'))
            {
                var openBracket = segment.IndexOf('[');
                propertyName = segment.Substring(0, openBracket);
                var indexStr = segment.Substring(openBracket + 1, segment.Length - openBracket - 2);
                if (int.TryParse(indexStr, out int idx))
                {
                    arrayIndex = idx;
                }
            }

            // 1. Tìm Property trong Object
            if (current.ValueKind == JsonValueKind.Object)
            {
                // Case-insensitive search
                JsonProperty prop = default;
                bool found = false;
                foreach (var p in current.EnumerateObject())
                {
                    if (p.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                    {
                        prop = p;
                        found = true;
                        break;
                    }
                }

                if (found) current = prop.Value;
                else return null; // Path không tồn tại
            }
            else
            {
                return null; // Không phải object mà đòi dot walk
            }

            // 2. Nếu có Index array thì truy cập vào phần tử
            if (arrayIndex.HasValue)
            {
                if (current.ValueKind == JsonValueKind.Array && arrayIndex.Value < current.GetArrayLength())
                {
                    current = current[arrayIndex.Value];
                }
                else
                {
                    return null; // Index out of range hoặc không phải array
                }
            }
        }

        // Return Native Type
        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Number => current.GetDouble(), // Hoặc GetDecimal tùy nhu cầu
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => current.GetRawText() // Object hoặc Array thì trả về nguyên cục JSON String
        };
    }
}

