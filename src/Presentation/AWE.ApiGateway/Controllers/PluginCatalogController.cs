using System.Text.Json;
using AWE.Shared.Primitives;
using AWE.WorkflowEngine.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/v1/plugins")]
public class PluginCatalogController(IPluginRegistry pluginRegistry) : ApiController
{
    private readonly IPluginRegistry _pluginRegistry = pluginRegistry;

    [HttpGet]
    public IActionResult GetPluginCatalog()
    {
        var builtInPlugins = _pluginRegistry.GetAllPlugins();

        var catalog = builtInPlugins.Select(p => new
        {
            name = p.Name,
            displayName = p.DisplayName,
            description = p.Description,
            category = p.Category,
            icon = p.Icon,
            inputSchema = ParseSchema(p.InputSchema),
            outputSchema = ParseSchema(p.OutputSchema)
        })
        .OrderBy(p => p.category)
        .ThenBy(p => p.displayName)
        .ToList();

        //return HandleResult(Result(catalog));
        return Ok(catalog);
    }

    private object ParseSchema(string schemaString)
    {
        if (string.IsNullOrWhiteSpace(schemaString) || schemaString == "{}")
            return new object();

        try
        {
            return JsonSerializer.Deserialize<object>(schemaString) ?? new object();
        }
        catch
        {
            return new object(); // Trả về object rỗng nếu chuỗi JSON cấu hình lỗi
        }
    }
}
