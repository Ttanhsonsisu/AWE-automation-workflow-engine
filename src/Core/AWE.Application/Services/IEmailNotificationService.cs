using AWE.Application.ConfigOptions;

namespace AWE.Application.Services;

/// <summary>
/// Service gửi email thông báo từ hệ thống AWE.
/// SMTP config được truyền vào per-call (từ node input), không bị lock vào appsettings.
/// </summary>
public interface IEmailNotificationService
{
    /// <summary>
    /// Gửi email yêu cầu phê duyệt workflow.
    /// SMTP config có thể lấy từ node input (ưu tiên) hoặc fallback từ appsettings.
    /// </summary>
    /// <param name="smtpConfig">
    /// Cấu hình SMTP được build từ node input.
    /// Các field rỗng sẽ được fallback về giá trị appsettings của server.
    /// </param>
    /// <param name="toEmail">Địa chỉ email người nhận.</param>
    /// <param name="subject">Tiêu đề email.</param>
    /// <param name="approvalUrl">
    /// URL API để submit quyết định phê duyệt.
    /// Ví dụ: https://domain.com/api/v1/approvals/submit?token=xxx
    /// </param>
    /// <param name="workflowTitle">Tiêu đề workflow / tên task cần duyệt.</param>
    /// <param name="workflowMessage">Nội dung mô tả chi tiết.</param>
    /// <param name="expiryDays">Số ngày token còn hiệu lực (để hiển thị trong email).</param>
    /// <param name="ct">Cancellation token.</param>
    Task SendApprovalEmailAsync(
        SmtpEmailConfig smtpConfig,
        string toEmail,
        string subject,
        string approvalUrl,
        string workflowTitle,
        string workflowMessage,
        int expiryDays = 3,
        CancellationToken ct = default);
}
