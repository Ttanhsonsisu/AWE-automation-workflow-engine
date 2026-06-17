namespace AWE.Application.ConfigOptions;

/// <summary>
/// Cấu hình SMTP để gửi email.
/// Có thể được cấu hình tại:
///   1. Node level (ưu tiên cao nhất) — truyền trực tiếp khi gọi SendApprovalEmailAsync
///   2. Appsettings section "SmtpEmail" / env vars SmtpEmail__* (fallback)
/// </summary>
public class SmtpEmailConfig
{
    /// <summary>SMTP server host. Ví dụ: smtp.gmail.com, sandbox.smtp.mailtrap.io</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>SMTP port. 587 = STARTTLS (phổ biến), 465 = SSL, 25 = plain.</summary>
    public int Port { get; set; } = 587;

    /// <summary>SMTP username / email đăng nhập.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>SMTP password hoặc App Password (Gmail).</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Tên hiển thị của người gửi. Ví dụ: "AWE Workflow System".</summary>
    public string FromName { get; set; } = "AWE Workflow System";

    /// <summary>Địa chỉ email người gửi (phải khớp với tài khoản SMTP hoặc được SPF authorize).</summary>
    public string FromAddress { get; set; } = string.Empty;

    /// <summary>Bật STARTTLS. true = port 587 / TLS, false = plain text. Mặc định: true.</summary>
    public bool UseSsl { get; set; } = true;
}
