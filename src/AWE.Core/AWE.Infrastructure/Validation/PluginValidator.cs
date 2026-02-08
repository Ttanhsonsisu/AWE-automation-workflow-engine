using System.Reflection;
using System.Runtime.Loader;
using AWE.Application.Abstractions.Validation;
using AWE.Application.Dtos.PluginDtos;
using AWE.Domain.Errors;
using AWE.Sdk; 
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
}
