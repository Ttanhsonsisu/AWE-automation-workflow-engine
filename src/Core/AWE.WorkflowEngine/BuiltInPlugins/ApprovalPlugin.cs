using AWE.Application.Abstractions.Persistence;
using AWE.Application.Services;
using AWE.Domain.Entities;
using AWE.Application.ConfigOptions;
using AWE.Sdk.v2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AWE.WorkflowEngine.BuiltInPlugins;

/// <summary>
/// Input của node Approval.
/// Toàn bộ cấu hình — bao gồm SMTP — đều configurable trực tiếp trong workflow node.
/// Hỗ trợ biến động: {{workflow.input.xxx}}, {{steps.xxx.Output.yyy}}.
/// </summary>
public class ApprovalInput
{
    // ── Notification channels ─────────────────────────────────────────────────

    /// <summary>
    /// Danh sách kênh gửi thông báo. Giá trị hợp lệ: "Email", "Telegram".
    /// Nếu rỗng và ApproverEmail không rỗng → tự động gửi Email.
    /// </summary>
    public List<string>? Channels { get; set; }

    /// <summary>Email của người cần phê duyệt (giảng viên, quản lý, ...).</summary>
    public string? ApproverEmail { get; set; }

    /// <summary>Chat ID Telegram của người cần phê duyệt.</summary>
    public string? TelegramChatId { get; set; }

    // ── Notification content ──────────────────────────────────────────────────

    /// <summary>Tiêu đề thông báo / email subject.</summary>
    public string? Title { get; set; }

    /// <summary>
    /// Nội dung chi tiết cho người phê duyệt.
    /// Có thể dùng biến: {{steps.write_back_results.Output.Summary}}
    /// </summary>
    public string? Message { get; set; }

    // ── Approval API URL ──────────────────────────────────────────────────────

    /// <summary>
    /// URL công khai của API Gateway — dùng để build link phê duyệt trong email.
    ///   Local dev:  http://localhost:8080
    ///   Self-host:  https://your-domain.com
    /// Link cuối: {ApiBaseUrl}/api/v1/approvals/submit?token={token}
    /// Có thể dùng biến: {{workflow.input.apiBaseUrl}}
    /// </summary>
    public string? ApiBaseUrl { get; set; }

    /// <summary>Số ngày token còn hiệu lực. Mặc định 3 ngày.</summary>
    public int TokenExpiryDays { get; set; } = 3;

    // ── SMTP config (node-level — ưu tiên hơn appsettings) ───────────────────

    /// <summary>
    /// SMTP server host.
    /// Ví dụ: smtp.gmail.com, smtp.mailgun.org, sandbox.smtp.mailtrap.io
    /// Nếu rỗng → dùng SmtpEmail:Host trong appsettings.
    /// </summary>
    public string? SmtpHost { get; set; }

    /// <summary>
    /// SMTP port. Thông thường: 587 (STARTTLS), 465 (SSL), 25 (plain).
    /// Nếu 0 → dùng SmtpEmail:Port trong appsettings (default 587).
    /// </summary>
    public int SmtpPort { get; set; } = 0;

    /// <summary>
    /// SMTP username / email đăng nhập.
    /// Gmail: địa chỉ Gmail. Mailtrap: username được cấp.
    /// Có thể dùng biến bí mật: {{workflow.input.smtpUsername}}
    /// </summary>
    public string? SmtpUsername { get; set; }

    /// <summary>
    /// SMTP password / App Password.
    /// Gmail: bật 2FA → tạo App Password tại https://myaccount.google.com/apppasswords
    /// Có thể dùng biến bí mật: {{workflow.input.smtpPassword}}
    /// </summary>
    public string? SmtpPassword { get; set; }

    /// <summary>
    /// Tên hiển thị của người gửi email.
    /// Ví dụ: "AWE Workflow System", "Hệ thống xét duyệt ĐATN"
    /// </summary>
    public string? SmtpFromName { get; set; }

    /// <summary>
    /// Địa chỉ email người gửi.
    /// Phải trùng với tài khoản SMTP hoặc được authorize (SPF/DKIM).
    /// </summary>
    public string? SmtpFromAddress { get; set; }

    /// <summary>
    /// Bật STARTTLS (true) hoặc kết nối plain (false). Mặc định: true.
    /// Gmail và hầu hết providers yêu cầu true với port 587.
    /// </summary>
    public bool SmtpUseSsl { get; set; } = true;
}

public class ApprovalOutput
{
    public bool IsApproved { get; set; }
    public string? Reason { get; set; }
    public string? ApproverName { get; set; }
}

public class ApprovalPlugin : IWorkflowPlugin
{
    private readonly IApprovalTokenRepository _tokenRepo;
    private readonly ILogger<ApprovalPlugin> _logger;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailNotificationService _emailService;
    private readonly ITelegramNotificationService _telegramService;
    private readonly SmtpEmailConfig _fallbackSmtp;

    public ApprovalPlugin(
        IApprovalTokenRepository tokenRepo,
        ILogger<ApprovalPlugin> logger,
        IEmailNotificationService emailService,
        ITelegramNotificationService telegramService,
        IUnitOfWork unitOfWork,
        IOptions<SmtpEmailConfig> fallbackSmtpOptions)
    {
        _tokenRepo       = tokenRepo;
        _logger          = logger;
        _emailService    = emailService;
        _telegramService = telegramService;
        _unitOfWork      = unitOfWork;
        _fallbackSmtp    = fallbackSmtpOptions.Value;
    }

    // =========================================================================
    // METADATA & SCHEMA
    // =========================================================================

    public string Name        => "Approval";
    public string DisplayName => "Phê duyệt (Human Task)";
    public string Description => "Gửi yêu cầu phê duyệt qua Email/Telegram và tạm dừng quy trình để chờ phản hồi.";
    public string Category    => "Human Interaction";
    public string Icon        => "UserCheck";

    public Type? InputType  => typeof(ApprovalInput);
    public Type? OutputType => typeof(ApprovalOutput);

    // =========================================================================
    // EXECUTE
    // =========================================================================

    public async Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        // Engine/Worker nhét "PointerId" vào context trước khi gọi plugin
        var pointerIdStr = context.Get<string>("PointerId");
        if (!Guid.TryParse(pointerIdStr, out var pointerId))
            return PluginResult.Failure("Hệ thống lỗi: Không tìm thấy PointerId để tạo Token phê duyệt.");

        try
        {
            // ── 1. Đọc inputs từ node config (engine đã resolve biến {{...}}) ──

            var title         = context.Get<string>("Title")          ?? "Yêu cầu phê duyệt";
            var message       = context.Get<string>("Message")         ?? string.Empty;
            var approverEmail = context.Get<string>("ApproverEmail")   ?? string.Empty;
            var telegramChat  = context.Get<string>("TelegramChatId")  ?? string.Empty;
            var channels      = context.Get<List<string>>("Channels")  ?? new List<string>();
            var expiryDays    = context.Get<int>("TokenExpiryDays");
            if (expiryDays <= 0) expiryDays = 3;

            // ── 2. Build Approval URL từ node input ──
            var apiBaseUrl  = (context.Get<string>("ApiBaseUrl") ?? "http://localhost:8080").TrimEnd('/');
            var tokenString = Guid.NewGuid().ToString("N");
            var approvalUrl = $"{apiBaseUrl}/api/v1/approvals/submit?token={tokenString}";

            // ── 3. Build SMTP config từ node input (fallback về appsettings) ──
            var nodeSmtp = new SmtpEmailConfig
            {
                Host        = context.Get<string>("SmtpHost")        ?? string.Empty,
                Port        = context.Get<int>("SmtpPort"),
                Username    = context.Get<string>("SmtpUsername")    ?? string.Empty,
                Password    = context.Get<string>("SmtpPassword")    ?? string.Empty,
                FromName    = context.Get<string>("SmtpFromName")    ?? string.Empty,
                FromAddress = context.Get<string>("SmtpFromAddress") ?? string.Empty,
                UseSsl      = context.Get<bool?>("SmtpUseSsl") ?? true,
            };

            // ── 4. Tạo Token bảo mật, lưu DB ──
            var token = new ApprovalToken
            {
                Id          = Guid.NewGuid(),
                PointerId   = pointerId,
                TokenString = tokenString,
                ExpiredAt   = DateTime.UtcNow.AddDays(expiryDays)
            };
            await _tokenRepo.CreateToken(token);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation(
                "[APPROVAL] Token tạo thành công. Pointer={PointerId}, URL={Url}",
                pointerId, approvalUrl);

            // ── 5. Xác định kênh gửi ──
            // Fallback: nếu channels rỗng nhưng có email → gửi Email
            bool sendEmail    = channels.Contains("Email",    StringComparer.OrdinalIgnoreCase)
                             || (channels.Count == 0 && !string.IsNullOrWhiteSpace(approverEmail));
            bool sendTelegram = channels.Contains("Telegram", StringComparer.OrdinalIgnoreCase);

            // ── 6. Gửi Email ──
            if (sendEmail)
            {
                if (!string.IsNullOrWhiteSpace(approverEmail))
                {
                    await _emailService.SendApprovalEmailAsync(
                        smtpConfig:      nodeSmtp,
                        toEmail:         approverEmail,
                        subject:         title,
                        approvalUrl:     approvalUrl,
                        workflowTitle:   title,
                        workflowMessage: message,
                        expiryDays:      expiryDays,
                        ct:              default);
                }
                else
                {
                    _logger.LogWarning("[EMAIL] Channel Email được chọn nhưng ApproverEmail rỗng. Bỏ qua.");
                }
            }

            // ── 7. Gửi Telegram ──
            if (sendTelegram)
            {
                if (!string.IsNullOrWhiteSpace(telegramChat))
                {
                    var telegramMsg = $"🔔 *{title}*\n\n{message}\n\n✅ Approval link:\n{approvalUrl}";
                    await _telegramService.SendAlertAsync(telegramMsg);
                    _logger.LogInformation(
                        "[TELEGRAM] Gửi tới ChatId={ChatId}. URL={Url}", telegramChat, approvalUrl);
                }
                else
                {
                    _logger.LogWarning("[TELEGRAM] Channel Telegram được chọn nhưng TelegramChatId rỗng. Bỏ qua.");
                }
            }

            return PluginResult.Suspend(
                $"Đã gửi thông báo. Token={tokenString}. URL={approvalUrl}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xử lý yêu cầu phê duyệt.");
            return PluginResult.Failure($"Lỗi khi gửi thông báo phê duyệt: {ex.Message}");
        }
    }

    // =========================================================================
    // COMPENSATE (Rollback)
    // =========================================================================

    public async Task<PluginResult> CompensateAsync(PluginContext context)
    {
        var pointerIdStr = context.Get<string>("PointerId");
        if (Guid.TryParse(pointerIdStr, out var pointerId))
        {
            try
            {
                var token = await _tokenRepo.GetByPointerIdAsync(pointerId);
                if (token != null && !token.IsUsed)
                {
                    token.ExpiredAt = DateTime.UtcNow;
                    await _tokenRepo.UpdateApprovalTokenAsync(token);
                    await _unitOfWork.SaveChangesAsync();
                    _logger.LogInformation(
                        "Đã HỦY Approval Token. Pointer={PointerId}", pointerId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Không thể hủy Token trong quá trình Rollback.");
            }
        }

        return PluginResult.Success();
    }
}
