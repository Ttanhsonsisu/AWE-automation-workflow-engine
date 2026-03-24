namespace AWE.Sdk;

public class PluginResult
{
    public bool IsSuccess { get; set; }
    public Dictionary<string, object> Outputs { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? Message { get; set; } = string.Empty;

    // FR-11: HIBERNATE (WAIT & DELAY) - TRẠNG THÁI TẠM DỪNG CỦA PLUGIN
    public bool? IsSuspended { get; set; } = default;

    public static PluginResult Success(Dictionary<string, object>? outputs = null)
        => new() { IsSuccess = true, Outputs = outputs ?? new() };

    public static PluginResult Failure(string message)
        => new() { IsSuccess = false, ErrorMessage = message };

    public static PluginResult Suspend(string msg = "Đang chờ tác động từ bên ngoài...")
        => new() { IsSuccess = false, IsSuspended = true, Message = msg };
}
