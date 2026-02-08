namespace AWE.Sdk;

/// <summary>
/// Hợp đồng giao tiếp (Contract) giữa Engine và Plugin.
/// Mọi Plugin muốn chạy được phải Implement interface này.
/// </summary>
public interface IWorkflowPlugin
{
    /// <summary>
    /// Tên định danh của Plugin (VD: "ExportExcel", "SendEmail").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Hàm thực thi logic chính.
    /// </summary>
    Task<PluginResult> ExecuteAsync(PluginContext context);
}
