using AWE.Application.ConfigOptions;
using AWE.Application.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace AWE.Infrastructure.Services;

/// <summary>
/// Gửi email qua SMTP dùng MailKit.
/// SMTP config nhận per-call (từ node input) — appsettings là fallback khi node không cung cấp.
/// </summary>
public class EmailNotificationService : IEmailNotificationService
{
    private readonly SmtpEmailConfig _fallbackConfig;
    private readonly ILogger<EmailNotificationService> _logger;

    public EmailNotificationService(
        IOptions<SmtpEmailConfig> fallbackOptions,
        ILogger<EmailNotificationService> logger)
    {
        _fallbackConfig = fallbackOptions.Value;
        _logger = logger;
    }

    public async Task SendApprovalEmailAsync(
        SmtpEmailConfig smtpConfig,
        string toEmail,
        string subject,
        string approvalUrl,
        string workflowTitle,
        string workflowMessage,
        int expiryDays = 3,
        CancellationToken ct = default)
    {
        // Merge node-config với fallback: node ưu tiên, appsettings làm fallback
        var resolved = MergeWithFallback(smtpConfig);

        if (string.IsNullOrWhiteSpace(resolved.Host) || string.IsNullOrWhiteSpace(resolved.FromAddress))
        {
            _logger.LogWarning(
                "[EMAIL] SMTP chưa đủ cấu hình (Host={Host}, FromAddress={From}). " +
                "Hãy điền SmtpHost/SmtpFromAddress vào node config hoặc appsettings SmtpEmail. " +
                "Bỏ qua gửi email tới {ToEmail}.",
                resolved.Host, resolved.FromAddress, toEmail);
            return;
        }

        try
        {
            var approveLink = $"{approvalUrl.TrimEnd('/')}&isApproved=true";
            var rejectLink  = $"{approvalUrl.TrimEnd('/')}&isApproved=false";

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(resolved.FromName, resolved.FromAddress));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new TextPart(TextFormat.Html)
            {
                Text = BuildHtmlBody(workflowTitle, workflowMessage, approveLink, rejectLink, approvalUrl, expiryDays)
            };

            using var smtp = new SmtpClient();
            var secureOptions = resolved.UseSsl
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await smtp.ConnectAsync(resolved.Host, resolved.Port, secureOptions, ct);

            if (!string.IsNullOrWhiteSpace(resolved.Username))
                await smtp.AuthenticateAsync(resolved.Username, resolved.Password, ct);

            await smtp.SendAsync(message, ct);
            await smtp.DisconnectAsync(quit: true, ct);

            _logger.LogInformation(
                "[EMAIL] Gửi thành công tới {ToEmail} qua {Host}:{Port}. Approval URL: {Url}",
                toEmail, resolved.Host, resolved.Port, approvalUrl);
        }
        catch (Exception ex)
        {
            // Log lỗi nhưng KHÔNG throw — tránh crash workflow chỉ vì lỗi email
            _logger.LogError(ex,
                "[EMAIL] Gửi thất bại tới {ToEmail} qua {Host}:{Port}. Approval URL: {Url}",
                toEmail, resolved.Host, resolved.Port, approvalUrl);
        }
    }

    /// <summary>
    /// Merge config từ node (ưu tiên) với fallback từ appsettings.
    /// Bất kỳ field nào rỗng trong node sẽ dùng giá trị từ appsettings.
    /// </summary>
    private SmtpEmailConfig MergeWithFallback(SmtpEmailConfig nodeConfig)
    {
        return new SmtpEmailConfig
        {
            Host        = !string.IsNullOrWhiteSpace(nodeConfig.Host)        ? nodeConfig.Host        : _fallbackConfig.Host,
            Port        = nodeConfig.Port > 0                                 ? nodeConfig.Port        : _fallbackConfig.Port,
            Username    = !string.IsNullOrWhiteSpace(nodeConfig.Username)    ? nodeConfig.Username    : _fallbackConfig.Username,
            Password    = !string.IsNullOrWhiteSpace(nodeConfig.Password)    ? nodeConfig.Password    : _fallbackConfig.Password,
            FromName    = !string.IsNullOrWhiteSpace(nodeConfig.FromName)    ? nodeConfig.FromName    : _fallbackConfig.FromName,
            FromAddress = !string.IsNullOrWhiteSpace(nodeConfig.FromAddress) ? nodeConfig.FromAddress : _fallbackConfig.FromAddress,
            UseSsl      = nodeConfig.UseSsl,  // bool — luôn dùng giá trị node (default true)
        };
    }

    private static string BuildHtmlBody(
        string title,
        string message,
        string approveLink,
        string rejectLink,
        string rawApiUrl,
        int expiryDays)
    {
        return $"""
            <!DOCTYPE html>
            <html lang="vi">
            <head>
              <meta charset="UTF-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1.0" />
              <title>{System.Net.WebUtility.HtmlEncode(title)}</title>
            </head>
            <body style="margin:0;padding:0;background:#f4f6f8;font-family:Arial,sans-serif;">
              <table width="100%" cellpadding="0" cellspacing="0" style="background:#f4f6f8;padding:32px 0;">
                <tr>
                  <td align="center">
                    <table width="600" cellpadding="0" cellspacing="0"
                           style="background:#ffffff;border-radius:8px;box-shadow:0 2px 8px rgba(0,0,0,0.08);overflow:hidden;">

                      <!-- HEADER -->
                      <tr>
                        <td style="background:#1e40af;padding:24px 32px;">
                          <p style="margin:0;color:#fff;font-size:13px;letter-spacing:1px;text-transform:uppercase;">AWE Workflow System</p>
                          <h1 style="margin:8px 0 0;color:#fff;font-size:22px;font-weight:700;">Yêu cầu phê duyệt</h1>
                        </td>
                      </tr>

                      <!-- BODY -->
                      <tr>
                        <td style="padding:32px;">
                          <h2 style="margin:0 0 12px;color:#1e293b;font-size:18px;">{System.Net.WebUtility.HtmlEncode(title)}</h2>
                          <p style="margin:0 0 24px;color:#475569;font-size:15px;line-height:1.6;">
                            {System.Net.WebUtility.HtmlEncode(message)}
                          </p>

                          <!-- APPROVE BUTTON -->
                          <table cellpadding="0" cellspacing="0" style="margin-bottom:12px;">
                            <tr>
                              <td style="border-radius:6px;background:#16a34a;">
                                <a href="{approveLink}"
                                   style="display:inline-block;padding:14px 32px;color:#fff;font-size:15px;
                                          font-weight:700;text-decoration:none;border-radius:6px;">
                                  ✅ Phê duyệt (Approve)
                                </a>
                              </td>
                            </tr>
                          </table>

                          <!-- REJECT BUTTON -->
                          <table cellpadding="0" cellspacing="0" style="margin-bottom:32px;">
                            <tr>
                              <td style="border-radius:6px;background:#dc2626;">
                                <a href="{rejectLink}"
                                   style="display:inline-block;padding:14px 32px;color:#fff;font-size:15px;
                                          font-weight:700;text-decoration:none;border-radius:6px;">
                                  ❌ Từ chối (Reject)
                                </a>
                              </td>
                            </tr>
                          </table>

                          <p style="margin:0;color:#94a3b8;font-size:13px;">
                            Hoặc sao chép URL phê duyệt (API):<br/>
                            <code style="font-size:11px;word-break:break-all;color:#475569;">{System.Net.WebUtility.HtmlEncode(rawApiUrl)}</code>
                          </p>
                        </td>
                      </tr>

                      <!-- FOOTER -->
                      <tr>
                        <td style="background:#f8fafc;padding:16px 32px;border-top:1px solid #e2e8f0;">
                          <p style="margin:0;color:#94a3b8;font-size:12px;">
                            Email này được gửi tự động từ hệ thống AWE Automation Workflow Engine.
                            Token có hiệu lực trong {expiryDays} ngày.
                          </p>
                        </td>
                      </tr>

                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;
    }
}
