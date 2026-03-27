using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Application.Services;
using AWE.Domain.Entities; 
using AWE.Sdk.v2;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.BuiltInPlugins;

public class ApprovalInput
{
    public List<string>? Channels { get; set; }
    public string? ApproverEmail { get; set; }
    public string? TelegramChatId { get; set; }
    public string? Title { get; set; }
    public string? Message { get; set; }
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
    private readonly ITelegramNotificationService _testSend;

    //private readonly IEmailNotificationService _emailService;
    private readonly ITelegramNotificationService _telegramService;

    public ApprovalPlugin(
        IApprovalTokenRepository tokenRepo,
        ILogger<ApprovalPlugin> logger,
        //IEmailNotificationService emailService,
        ITelegramNotificationService telegramService,
        IUnitOfWork unitOfWork,
        ITelegramNotificationService testSend)
    {
        _tokenRepo = tokenRepo;
        _logger = logger;
        //_emailService = emailService;
        _telegramService = telegramService;
        _unitOfWork = unitOfWork;
        _testSend = testSend;
    }

    // ========================================================
    // 1. METADATA & SCHEMA
    // ========================================================

    public string Name => "Approval";
    public string DisplayName => "Phê duyệt (Human Task)";
    public string Description => "Gửi yêu cầu phê duyệt qua Email/Telegram và tạm dừng quy trình để chờ phản hồi.";
    public string Category => "Human Interaction";
    public string Icon => "UserCheck";

    public Type? InputType => typeof(ApprovalInput);
    public Type? OutputType => typeof(ApprovalOutput);

    // ========================================================
    // 2. LOGIC THỰC THI CHÍNH (EXECUTE)
    // ========================================================

    public async Task<PluginResult> ExecuteAsync(PluginContext context)
    {
        // LƯU Ý: Engine/Worker khi gọi Plugin này PHẢI nhét "PointerId" vào JsonPayload
        var pointerIdStr = context.Get<string>("PointerId");
        if (!Guid.TryParse(pointerIdStr, out var pointerId))
        {
            return PluginResult.Failure("Hệ thống lỗi: Không tìm thấy PointerId để tạo Token phê duyệt.");
        }

        // Lấy thông tin cấu hình mà người dùng nhập từ giao diện (đã resolve biến {{...}})
        //var inputs = context.GetRaw("inputData");
        //if (inputs.ValueKind != JsonValueKind.Object)
        //{
        //    return PluginResult.Failure("Cấu hình Inputs không hợp lệ.");
        //}

        try
        {
            // 1. Tạo Token bảo mật lưu xuống DB
            var tokenString = Guid.NewGuid().ToString("N");
            var token = new ApprovalToken
            {
                Id = Guid.NewGuid(),
                PointerId = pointerId,
                TokenString = tokenString,
                ExpiredAt = DateTime.UtcNow.AddDays(3) // Hạn duyệt 3 ngày (có thể lấy từ UI)
            };
            await _tokenRepo.CreateToken(token);
            await _unitOfWork.SaveChangesAsync();

            // Chuẩn bị thông tin thông báo
            var title = context.Get<string>("Title") ?? "Yêu cầu phê duyệt";
            var message = context.Get<string>("Message") ?? "";
            // testing 
            var approvalUrl = $"https://app.awe.com/approve?token={tokenString}";


            var channels = context.Get<List<string>>("Channels") ?? new List<string>();

            // send message
            if (channels.Contains("Email"))
            {
                string email = context.Get<string>("ApproverEmail") ?? "";
                if (!string.IsNullOrWhiteSpace(email))
                {
                    // test
                    await _testSend.SendAlertAsync(approvalUrl);
                    _logger.LogInformation("[EMAIL] Đã gửi yêu cầu phê duyệt tới {Email}. Link: {Url}", email, approvalUrl);
                }
            }

            if (channels.Contains("Telegram"))
            {
                string chatId = context.Get<string>("TelegramChatId") ?? "";
                if (!string.IsNullOrWhiteSpace(chatId))
                {
                    await _testSend.SendAlertAsync(approvalUrl);
                    _logger.LogInformation("[TELEGRAM] Đã gửi yêu cầu phê duyệt tới ChatId {ChatId}. Link: {Url}", chatId, approvalUrl);
                }
            }


            return PluginResult.Suspend($"Đã gửi thông báo. Mã Token: {tokenString}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi gửi yêu cầu phê duyệt.");
            return PluginResult.Failure($"Lỗi khi gửi thông báo phê duyệt: {ex.Message}");
        }
    }

    // ========================================================
    // 3. LOGIC ROLLBACK (COMPENSATE)
    // ========================================================

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

                    _logger.LogInformation("Đã HỦY Approval Token cho Pointer {PointerId} do quy trình bị Rollback.", pointerId);
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
