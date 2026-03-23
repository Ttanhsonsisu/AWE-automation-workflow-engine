using System.Text.Json;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/v1/plugins")]
public class PluginCatalogController : ApiController
{
    private readonly IPluginRegistry _pluginRegistry;
    private readonly IPluginPackageRepository _packageRepo;

    public PluginCatalogController(IPluginRegistry pluginRegistry, IPluginPackageRepository packageRepo)
    {
        _pluginRegistry = pluginRegistry;
        _packageRepo = packageRepo;
    }

    [HttpGet("catalog")]
    public async Task<IActionResult> GetPluginCatalog(CancellationToken ct)
    {
        var catalog = new List<object>();

        // 1. LẤY BUILT-IN PLUGINS TỪ RAM
        var builtInPlugins = _pluginRegistry.GetAllPlugins();
        var builtInItems = builtInPlugins.Select(p => new
        {
            name = p.Name,
            displayName = p.DisplayName,
            description = p.Description,
            category = p.Category,
            icon = p.Icon,
            executionMode = PluginExecutionMode.BuiltIn.ToString(), 
            dllPath = (string?)null,
            inputSchema = ParseSchema(p.InputSchema),
            outputSchema = ParseSchema(p.OutputSchema)
        });
        catalog.AddRange(builtInItems);

        // Query toàn bộ Package có chứa Version đang Active
        var customPackages = await _packageRepo.ListAsync(ct);

        foreach (var pkg in customPackages)
        {
            var activeVersion = pkg.Versions.FirstOrDefault(v => v.IsActive);
            if (activeVersion == null) continue; 

            catalog.Add(new
            {
                name = pkg.UniqueName, 
                displayName = pkg.DisplayName,
                description = pkg.Description,
                category = "Custom", 
                icon = "lucide-box",
                executionMode = PluginExecutionMode.DynamicDll.ToString(),
                dllPath = activeVersion.ObjectKey, 
                inputSchema = ParseSchema(activeVersion.ConfigSchema?.RootElement.GetRawText() ?? "{}"),
                outputSchema = ParseSchema("{}") 
            });
        }

        // 3. SẮP XẾP VÀ TRẢ VỀ
        var sortedCatalog = catalog
            .OrderBy(p => ((dynamic)p).category)
            .ThenBy(p => ((dynamic)p).displayName)
            .ToList();

        return Ok(sortedCatalog);
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
            return new object();
        }
    }
}
