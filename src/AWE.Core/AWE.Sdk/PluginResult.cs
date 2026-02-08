namespace AWE.Sdk;

public class PluginResult
{
    public bool IsSuccess { get; set; }
    public Dictionary<string, object> Outputs { get; set; } = new();
    public string? ErrorMessage { get; set; }

    public static PluginResult Success(Dictionary<string, object>? outputs = null)
        => new() { IsSuccess = true, Outputs = outputs ?? new() };

    public static PluginResult Failure(string message)
        => new() { IsSuccess = false, ErrorMessage = message };
}
