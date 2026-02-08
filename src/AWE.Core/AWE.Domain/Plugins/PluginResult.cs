using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AWE.Domain.Plugins;

public record PluginResult
{
    public bool IsSuccess { get; init; }
    public JsonDocument? Outputs { get; init; } 
    public string? ErrorMessage { get; init; }

    public static PluginResult Success(JsonDocument? outputs = null)
        => new() { IsSuccess = true, Outputs = outputs };

    public static PluginResult Fail(string message)
        => new() { IsSuccess = false, ErrorMessage = message };
}
