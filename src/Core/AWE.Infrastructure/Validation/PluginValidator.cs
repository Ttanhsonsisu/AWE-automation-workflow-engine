using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Serialization;
using System.Text.Json;
using AWE.Application.Abstractions.Validation;
using AWE.Application.Dtos.PluginDtos;
using AWE.Application.Extensions;
using AWE.Domain.Errors;
using AWE.Sdk.v2;
using AWE.Shared.Primitives;

namespace AWE.Infrastructure.Validation;

public class PluginValidator : IPluginValidator
{
    public Result<PluginMetadataDto> ValidateAssembly(Stream dllStream)
    {
        // 1. Tạo Context riêng để load DLL (cho phép unload sau khi xong để không khóa file/RAM)
        var context = new AssemblyLoadContext("ValidationContext", isCollectible: true);

        try
        {
            dllStream.Position = 0;
            Assembly assembly;

            try
            {
                assembly = context.LoadFromStream(dllStream);
            }
            catch (BadImageFormatException)
            {
                return PluginErrors.Version.InvalidAssembly("Not a valid .NET assembly or wrong architecture.");
            }

            // 2. Kiểm tra xem DLL có class nào implement IWorkflowPlugin của SDK không?
            var pluginType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IWorkflowPlugin).IsAssignableFrom(t)
                                     && !t.IsInterface && !t.IsAbstract);

            if (pluginType == null)
            {
                return PluginErrors.Version.MissingInterface;
            }

            // 3. Trích xuất Version
            var assemblyName = assembly.GetName();
            var detectedVersion = assemblyName.Version?.ToString(3) ?? "1.0.0";

            // 4. (Optional) Trích xuất Schema nếu có logic đó trong SDK
            string? schemaJson = null;

            return new PluginMetadataDto(detectedVersion, schemaJson);
        }
        catch (Exception ex)
        {
            return Error.Unexpected("PluginValidator.Exception", ex.Message);
        }
        finally
        {
            // 5. Dọn dẹp context
            context.Unload();
        }
    }

    public Result<PluginExtractionResult> ValidateAndExtractSchema(Stream dllStream)
    {
        // 1. Tạo Context riêng để load DLL (isCollectible = true để chống Memory Leak)
        var context = new AssemblyLoadContext("ValidationAndExtractionContext", isCollectible: true);

        try
        {
            dllStream.Position = 0;
            Assembly assembly;

            try
            {
                assembly = context.LoadFromStream(dllStream);
            }
            catch (BadImageFormatException)
            {
                return PluginErrors.Version.InvalidAssembly("File không phải là .NET Assembly hợp lệ hoặc sai kiến trúc.");
            }

            // 2. Kiểm tra xem DLL có class nào implement IWorkflowPlugin không
            Type? pluginType = null;
            try
            {
                // Dùng GetTypes() trong Try-Catch để bắt mẻ lưới ReflectionTypeLoadException
                pluginType = assembly.GetTypes()
                    .FirstOrDefault(t => typeof(IWorkflowPlugin).IsAssignableFrom(t)
                                         && !t.IsInterface && !t.IsAbstract && t.IsClass);
            }
            catch (ReflectionTypeLoadException ex)
            {
                // Nếu có lỗi, .NET sẽ nhét các chi tiết lỗi vào mảng LoaderExceptions
                var loaderErrors = ex.LoaderExceptions
                    .Where(e => e != null)
                    .Select(e => e!.Message)
                    .Distinct()
                    .ToList();

                string detailedError = string.Join(" | ", loaderErrors);

                // Trả về thẳng cho màn hình Postman/UI biết tại sao DLL của họ rác
                return PluginErrors.Version.InvalidAssembly($"DLL chứa Type không hợp lệ. Chi tiết: {detailedError}");
            }

            if (pluginType == null)
            {
                return PluginErrors.Version.MissingInterface; 
            }

            // 3. AUTO-DISCOVERY: Trích xuất Schema ngay tại đây!
            // Bỏ qua Constructor để tránh lỗi thiếu Dependency (ILogger, DbContext...)
            var uninitializedObject = FormatterServices.GetUninitializedObject(pluginType);
            var pluginInstance = uninitializedObject as IWorkflowPlugin;

            string name = pluginInstance?.Name ?? "Unknown";
            string displayName = pluginInstance?.DisplayName ?? name;
            string description = pluginInstance?.Description ?? "";
            string category = pluginInstance?.Category ?? "Custom";
            string icon = pluginInstance?.Icon ?? "lucide-box";

            // Xử lý an toàn cho JSON
            string inSchemaStr = PluginSchemaGenerator.GenerateSchema(pluginInstance.InputType);
            string outSchemaStr = PluginSchemaGenerator.GenerateSchema(pluginInstance.OutputType);

            var resultData = new PluginExtractionResult(
                Name: name,
                DisplayName: displayName,
                Description: description,
                Category: category,
                Icon: icon,
                InputSchema: JsonDocument.Parse(inSchemaStr),
                OutputSchema: JsonDocument.Parse(outSchemaStr)
            );

            return Result.Success(resultData);
        }
        catch (Exception ex)
        {
            return Error.Unexpected("PluginValidator.Exception", ex.Message);
        }
        finally
        {
            // 5. Dọn dẹp context, trả lại RAM cho hệ thống
            context.Unload();
        }
    }
}
