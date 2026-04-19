using System.Text.Json;
using System.Text.Json.Nodes;

namespace AWE.Shared.Extensions;

/// <summary>
/// Extensions for working with JsonDocument and JsonElement
/// </summary>
public static class JsonExtensions
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };



    #region JsonDocument Extensions

    /// <summary>
    /// Tìm kiếm Property trong JSON không phân biệt chữ hoa chữ thường.
    /// </summary>
    public static bool TryGetPropertyCaseInsensitive(this JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;

        // 1. Thử tìm chính xác trước (Tốc độ nhanh nhất O(1))
        if (element.TryGetProperty(propertyName, out value))
            return true;

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

    /// <summary>
    /// Get value from JsonDocument by path (supports nested properties)
    /// </summary>
    public static T? GetValue<T>(this JsonDocument document, string jsonPath)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        if (string.IsNullOrWhiteSpace(jsonPath))
            throw new ArgumentException("JSON path cannot be empty", nameof(jsonPath));

        try
        {
            var element = GetElementByPath(document.RootElement, jsonPath);
            return element.ValueKind != JsonValueKind.Undefined
                ? element.Deserialize<T>(_jsonSerializerOptions)
                : default;
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>
    /// Get value from JsonDocument by path with fallback default value
    /// </summary>
    public static T GetValueOrDefault<T>(this JsonDocument document, string jsonPath, T defaultValue = default!)
    {
        try
        {
            var value = document.GetValue<T>(jsonPath);
            return value ?? defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Set or update value at specified path
    /// </summary>
    public static JsonDocument SetValue(this JsonDocument document, string jsonPath, object value)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var jsonObject = JsonObject.Create(document.RootElement.Clone());
        SetValueInJsonObject(jsonObject, jsonPath, value);

        return JsonDocument.Parse(jsonObject.ToJsonString());
    }

    /// <summary>
    /// Merge two JsonDocuments (deep merge)
    /// </summary>
    public static JsonDocument MergeWith(this JsonDocument source, JsonDocument other, bool overwriteArrays = false)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (other == null) throw new ArgumentNullException(nameof(other));

        var sourceObject = JsonObject.Create(source.RootElement.Clone());
        var otherObject = JsonObject.Create(other.RootElement.Clone());

        MergeJsonObjects(sourceObject, otherObject, overwriteArrays);

        return JsonDocument.Parse(sourceObject.ToJsonString());
    }

    /// <summary>
    /// Merge multiple JsonDocuments
    /// </summary>
    public static JsonDocument MergeAll(this IEnumerable<JsonDocument> documents, bool overwriteArrays = false)
    {
        if (documents == null || !documents.Any())
            return JsonDocument.Parse("{}");

        var result = documents.First();
        foreach (var doc in documents.Skip(1))
        {
            result = result.MergeWith(doc, overwriteArrays);
        }

        return result;
    }

    /// <summary>
    /// Convert JsonDocument to dictionary
    /// </summary>
    public static Dictionary<string, object?> ToDictionary(this JsonDocument document)
    {
        return ConvertToDictionary(document.RootElement);
    }

    /// <summary>
    /// Convert JsonDocument to typed object
    /// </summary>
    public static T? ToObject<T>(this JsonDocument document)
    {
        if (document == null)
            return default;

        return document.RootElement.Deserialize<T>(_jsonSerializerOptions);
    }

    /// <summary>
    /// Clone JsonDocument (creates new instance)
    /// </summary>
    public static JsonDocument Clone(this JsonDocument document)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        return JsonDocument.Parse(document.RootElement.GetRawText());
    }

    /// <summary>
    /// Check if JsonDocument contains a specific path
    /// </summary>
    public static bool ContainsPath(this JsonDocument document, string jsonPath)
    {
        if (document == null)
            return false;

        var element = GetElementByPath(document.RootElement, jsonPath);
        return element.ValueKind != JsonValueKind.Undefined;
    }

    /// <summary>
    /// Remove property at specified path
    /// </summary>
    public static JsonDocument RemoveProperty(this JsonDocument document, string jsonPath)
    {
        if (document == null)
            throw new ArgumentNullException(nameof(document));

        var jsonObject = JsonObject.Create(document.RootElement.Clone());
        RemovePropertyFromJsonObject(jsonObject, jsonPath);

        return JsonDocument.Parse(jsonObject.ToJsonString());
    }

    /// <summary>
    /// Flatten JsonDocument to key-value pairs
    /// </summary>
    public static Dictionary<string, string> Flatten(this JsonDocument document, string prefix = "")
    {
        var result = new Dictionary<string, string>();
        FlattenElement(document.RootElement, prefix, result);
        return result;
    }

    #endregion

    #region JsonElement Extensions

    /// <summary>
    /// Safely get property value as string
    /// </summary>
    public static string? GetStringValue(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return element.TryGetProperty(propertyName, out var prop)
            ? prop.GetString()
            : null;
    }

    /// <summary>
    /// Safely get property value as int
    /// </summary>
    public static int? GetIntValue(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.Number)
        {
            return prop.GetInt32();
        }

        return null;
    }

    /// <summary>
    /// Safely get property value as bool
    /// </summary>
    public static bool? GetBoolValue(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        if (element.TryGetProperty(propertyName, out var prop) &&
            prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
        {
            return prop.GetBoolean();
        }

        return null;
    }

    /// <summary>
    /// Get child element by path
    /// </summary>
    public static JsonElement? GetElement(this JsonElement element, string jsonPath)
    {
        try
        {
            var result = GetElementByPath(element, jsonPath);
            return result.ValueKind != JsonValueKind.Undefined ? result : (JsonElement?)null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Check if element is null or empty
    /// </summary>
    public static bool IsNullOrEmpty(this JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Null ||
               element.ValueKind == JsonValueKind.Undefined ||
               (element.ValueKind == JsonValueKind.Object && !element.EnumerateObject().Any()) ||
               (element.ValueKind == JsonValueKind.Array && !element.EnumerateArray().Any()) ||
               (element.ValueKind == JsonValueKind.String && string.IsNullOrEmpty(element.GetString()));
    }

    /// <summary>
    /// Convert JsonElement to dictionary
    /// </summary>
    public static Dictionary<string, object?> ToDictionary(this JsonElement element)
    {
        return ConvertToDictionary(element);
    }

    /// <summary>
    /// Convert JsonElement to typed object
    /// </summary>
    public static T? ToObject<T>(this JsonElement element)
    {
        return element.Deserialize<T>(_jsonSerializerOptions);
    }

    /// <summary>
    /// Get all property names
    /// </summary>
    public static IEnumerable<string> GetPropertyNames(this JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return Enumerable.Empty<string>();

        return element.EnumerateObject().Select(p => p.Name);
    }

    #endregion

    #region Helper Methods

    private static JsonElement GetElementByPath(JsonElement element, string jsonPath)
    {
        var segments = jsonPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var current = element;

        foreach (var segment in segments)
        {
            if (current.ValueKind != JsonValueKind.Object)
                return default;

            // Handle array index: items[0]
            if (segment.Contains('[') && segment.EndsWith(']'))
            {
                var arrayName = segment.Substring(0, segment.IndexOf('['));
                var indexStr = segment.Substring(segment.IndexOf('[') + 1, segment.IndexOf(']') - segment.IndexOf('[') - 1);

                if (!int.TryParse(indexStr, out int index))
                    return default;

                if (!current.TryGetProperty(arrayName, out var arrayProp) ||
                    arrayProp.ValueKind != JsonValueKind.Array)
                    return default;

                var array = arrayProp.EnumerateArray().ToArray();
                if (index < 0 || index >= array.Length)
                    return default;

                current = array[index];
            }
            else
            {
                if (!current.TryGetProperty(segment, out current))
                    return default;
            }
        }

        return current;
    }

    private static void SetValueInJsonObject(JsonObject jsonObject, string jsonPath, object value)
    {
        var segments = jsonPath.Split('.');
        var current = jsonObject;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];

            // Handle array index
            if (segment.Contains('[') && segment.EndsWith(']'))
            {
                var arrayName = segment.Substring(0, segment.IndexOf('['));
                var indexStr = segment.Substring(segment.IndexOf('[') + 1, segment.IndexOf(']') - segment.IndexOf('[') - 1);

                if (!int.TryParse(indexStr, out int index))
                    throw new ArgumentException($"Invalid array index in path: {segment}");

                if (!current.TryGetPropertyValue(arrayName, out var node) || node is not JsonArray jsonArray)
                {
                    jsonArray = new JsonArray();
                    current[arrayName] = jsonArray;
                }

                // Ensure array is large enough
                while (jsonArray.Count <= index)
                    jsonArray.Add(null);

                if (jsonArray[index] is not JsonObject nextObject)
                {
                    nextObject = new JsonObject();
                    jsonArray[index] = nextObject;
                }

                current = nextObject;
            }
            else
            {
                if (!current.TryGetPropertyValue(segment, out var node) || node is not JsonObject nextObject)
                {
                    nextObject = new JsonObject();
                    current[segment] = nextObject;
                }

                current = nextObject;
            }
        }

        var lastSegment = segments.Last();

        // Remove array index from last segment if present
        if (lastSegment.Contains('[') && lastSegment.EndsWith(']'))
        {
            var arrayName = lastSegment.Substring(0, lastSegment.IndexOf('['));
            var indexStr = lastSegment.Substring(lastSegment.IndexOf('[') + 1, lastSegment.IndexOf(']') - lastSegment.IndexOf('[') - 1);

            if (!int.TryParse(indexStr, out int index))
                throw new ArgumentException($"Invalid array index in path: {lastSegment}");

            if (!current.TryGetPropertyValue(arrayName, out var node) || node is not JsonArray jsonArray)
            {
                jsonArray = new JsonArray();
                current[arrayName] = jsonArray;
            }

            // Ensure array is large enough
            while (jsonArray.Count <= index)
                jsonArray.Add(null);

            var jsonValue = ConvertToJsonNode(value);
            jsonArray[index] = jsonValue;
        }
        else
        {
            var jsonValue = ConvertToJsonNode(value);
            current[lastSegment] = jsonValue;
        }
    }

    private static JsonNode? ConvertToJsonNode(object value)
    {
        if (value == null)
            return null;

        return value switch
        {
            JsonNode node => node,
            string str => JsonValue.Create(str),
            int num => JsonValue.Create(num),
            long num => JsonValue.Create(num),
            decimal num => JsonValue.Create(num),
            double num => JsonValue.Create(num),
            bool flag => JsonValue.Create(flag),
            DateTime date => JsonValue.Create(date),
            DateTimeOffset date => JsonValue.Create(date),
            Guid guid => JsonValue.Create(guid.ToString()),
            _ => JsonSerializer.SerializeToNode(value, _jsonSerializerOptions)
        };
    }

    private static void MergeJsonObjects(JsonObject target, JsonObject source, bool overwriteArrays)
    {
        foreach (var sourceProperty in source)
        {
            if (!target.ContainsKey(sourceProperty.Key))
            {
                target[sourceProperty.Key] = sourceProperty.Value?.DeepClone();
            }
            else
            {
                var targetValue = target[sourceProperty.Key];
                var sourceValue = sourceProperty.Value;

                if (targetValue is JsonObject targetObject && sourceValue is JsonObject sourceObject)
                {
                    MergeJsonObjects(targetObject, sourceObject, overwriteArrays);
                }
                else if (targetValue is JsonArray targetArray && sourceValue is JsonArray sourceArray)
                {
                    if (overwriteArrays)
                    {
                        target[sourceProperty.Key] = sourceArray.DeepClone();
                    }
                    else
                    {
                        foreach (var item in sourceArray)
                        {
                            targetArray.Add(item?.DeepClone());
                        }
                    }
                }
                else
                {
                    // Overwrite with source value
                    target[sourceProperty.Key] = sourceValue?.DeepClone();
                }
            }
        }
    }

    private static void RemovePropertyFromJsonObject(JsonObject jsonObject, string jsonPath)
    {
        var segments = jsonPath.Split('.');
        var current = jsonObject;

        for (int i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];

            if (!current.TryGetPropertyValue(segment, out var node) || node is not JsonObject nextObject)
                return; // Path doesn't exist

            current = nextObject;
        }

        var lastSegment = segments.Last();
        current.Remove(lastSegment);
    }

    private static Dictionary<string, object?> ConvertToDictionary(JsonElement element)
    {
        var result = new Dictionary<string, object?>();

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                result[property.Name] = ConvertJsonElement(property.Value);
            }
        }

        return result;
    }

    private static object? ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetDecimal(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Object => ConvertToDictionary(element),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            _ => element.ToString()
        };
    }

    private static void FlattenElement(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix)
                        ? property.Name
                        : $"{prefix}.{property.Name}";
                    FlattenElement(property.Value, key, result);
                }
                break;

            case JsonValueKind.Array:
                var array = element.EnumerateArray().ToArray();
                for (int i = 0; i < array.Length; i++)
                {
                    var key = $"{prefix}[{i}]";
                    FlattenElement(array[i], key, result);
                }
                break;

            case JsonValueKind.String:
                result[prefix] = element.GetString()!;
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
                result[prefix] = element.ToString();
                break;

            case JsonValueKind.Null:
                result[prefix] = "null";
                break;
        }
    }

    #endregion

    #region String Extensions for JSON

    /// <summary>
    /// Parse string to JsonDocument safely
    /// </summary>
    public static JsonDocument? ToJsonDocument(this string jsonString)
    {
        if (string.IsNullOrWhiteSpace(jsonString))
            return JsonDocument.Parse("{}");

        try
        {
            return JsonDocument.Parse(jsonString);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Parse string to JsonElement safely
    /// </summary>
    public static JsonElement? ToJsonElement(this string jsonString)
    {
        if (string.IsNullOrWhiteSpace(jsonString))
            return JsonDocument.Parse("{}").RootElement;

        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Check if string is valid JSON
    /// </summary>
    public static bool IsValidJson(this string jsonString)
    {
        if (string.IsNullOrWhiteSpace(jsonString))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(jsonString);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Pretty print JSON string
    /// </summary>
    public static string FormatJson(this string jsonString)
    {
        if (!IsValidJson(jsonString))
            return jsonString;

        using var doc = JsonDocument.Parse(jsonString);
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    #endregion
}
