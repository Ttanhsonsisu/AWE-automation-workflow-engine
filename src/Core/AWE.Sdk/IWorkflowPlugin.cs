//namespace AWE.Sdk;

///// <summary>
///// Hợp đồng giao tiếp (Contract) giữa Engine và Plugin.
///// Mọi Plugin muốn chạy được phải Implement interface này.
///// </summary>
//public interface IWorkflowPlugin
//{
//    /// <summary>
//    /// Tên định danh của Plugin (VD: "ExportExcel", "SendEmail").
//    /// </summary>
//    string Name { get; }
//    /// <summary>
//    /// Metadata hiển thị (Dùng cho Frontend React)
//    /// </summary>
//    string DisplayName { get; }
//    string Description { get; }
//    string Category { get; }
//    string Icon { get; }

//    /// <summary>
//    /// Schema Cấu hình (JSON Schema định nghĩa Form nhập liệu và Output)
//    /// </summary>
//    string InputSchema { get; }
//    string OutputSchema { get; }

//    /// <summary>
//    /// Hàm thực thi logic chính.
//    /// </summary>
//    Task<PluginResult> ExecuteAsync(PluginContext context);

//    // hỗ trợ rollback nếu ExecuteAsync thất bại hoặc có lỗi xảy ra trong quá trình thực thi.
//    Task<PluginResult> CompensateAsync(PluginContext context);
//}
