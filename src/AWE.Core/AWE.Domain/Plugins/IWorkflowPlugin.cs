using System;
using System.Collections.Generic;
using System.Text;

namespace AWE.Domain.Plugins;

/// <summary>
/// Hợp đồng giao tiếp giữa Engine và các file DLL bên ngoài.
/// </summary>
public interface IWorkflowPlugin
{
    /// <summary>
    /// Tên định danh duy nhất (VD: "AWE.HttpPlugin", "AWE.SendEmail")
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Hàm thực thi chính.
    /// </summary>
    /// <param name="context">Dữ liệu đầu vào (Input variables)</param>
    /// <returns>Kết quả thực thi (Output variables)</returns>
    Task<PluginResult> ExecuteAsync(PluginContext context);
}
