namespace AWE.Sdk.v2;

public class PluginResult
{
    public bool IsSuccess { get; set; }

    // THAY ĐỔI: Dùng 'object' thay vì Dictionary để chứa được TOutput (vd: SendEmailOutput)
    public object? Outputs { get; set; }

    public string? ErrorMessage { get; set; }
    public string? Message { get; set; } = string.Empty;

    // FR-11: HIBERNATE (WAIT & DELAY)
    public bool? IsSuspended { get; set; } = default;

    // 🔥 THAY ĐỔI: Nhận vào object thay vì Dictionary
    public static PluginResult Success(object? outputs = null)
        => new() { IsSuccess = true, Outputs = outputs };

    public static PluginResult Failure(string message)
        => new() { IsSuccess = false, ErrorMessage = message };

    public static PluginResult Suspend(string msg = "Đang chờ tác động từ bên ngoài...")
        => new() { IsSuccess = false, IsSuspended = true, Message = msg };
}
