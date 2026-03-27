using System;
using System.Collections.Generic;
using System.Text;

namespace AWE.Sdk.v2;

public interface IWorkflowPlugin
{
    string Name { get; }
    string DisplayName { get; }
    string Description { get; }
    string Category { get; }
    string Icon { get; }

    // Trả về Type thay vì chuỗi JSON
    Type? InputType { get; }
    Type? OutputType { get; }

    Task<PluginResult> ExecuteAsync(PluginContext context);
    Task<PluginResult> CompensateAsync(PluginContext context);
}
