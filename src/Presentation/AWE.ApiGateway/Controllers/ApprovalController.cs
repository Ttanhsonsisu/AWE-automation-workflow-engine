using AWE.Application.UseCases.Approvals.SubmitApproval;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[Route("api/v1/approvals")]
public class ApprovalController : ApiController
{
    private readonly ISubmitApprovalUseCase _submitApprovalUseCase;

    public ApprovalController(ISubmitApprovalUseCase submitApprovalUseCase)
    {
        _submitApprovalUseCase = submitApprovalUseCase;
    }

    [HttpGet("submit")]
    [AllowAnonymous]
    public async Task<IActionResult> SubmitApproval(
        [FromQuery] string token,
        [FromQuery] bool isApproved = true,
        [FromQuery] string? reason = null,
        [FromQuery] string? approverName = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Tạo payload từ query parameters (GET request không gửi body)
        var payload = new SubmitApprovalRequest
        {
            Token = token,
            IsApproved = isApproved,
            Reason = reason,
            ApproverName = approverName
        };

        // 2. Thực thi Use Case
        var result = await _submitApprovalUseCase.ExecuteAsync(payload, cancellationToken);

        // 3. Trả về HTML giao diện đẹp mắt thay vì JSON
        if (!result.IsSuccess)
        {
            var err = result.Error;
            return RenderHtml(
                iconClass: "icon-error",
                iconHtml: "✕",
                title: "Thao tác thất bại",
                description: "Không thể xử lý quyết định phê duyệt này hoặc Token của bạn đã hết hạn/được sử dụng.",
                decision: "Không hợp lệ",
                reason: err?.Message ?? "Token không hợp lệ hoặc hết hạn",
                approverName: "Hệ thống"
            );
        }

        return RenderHtml(
            iconClass: isApproved ? "icon-success" : "icon-warning",
            iconHtml: isApproved ? "✓" : "!",
            title: isApproved ? "Phê duyệt thành công!" : "Từ chối thành công!",
            description: isApproved 
                ? "Quyết định phê duyệt của bạn đã được ghi nhận. Quy trình workflow đang được tiếp tục."
                : "Quyết định từ chối của bạn đã được ghi nhận. Quy trình workflow sẽ rẽ sang nhánh tương ứng.",
            decision: isApproved ? "Đồng ý (Approve)" : "Từ chối (Reject)",
            reason: string.IsNullOrWhiteSpace(reason) ? "Không có" : reason,
            approverName: string.IsNullOrWhiteSpace(approverName) ? "Người xét duyệt" : approverName
        );
    }

    private ContentResult RenderHtml(
        string iconClass,
        string iconHtml,
        string title,
        string description,
        string decision,
        string reason,
        string approverName)
    {
        var html = $@"
<!DOCTYPE html>
<html lang=""vi"">
<head>
  <meta charset=""UTF-8"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
  <title>AWE Approval System</title>
  <link rel=""preconnect"" href=""https://fonts.googleapis.com"">
  <link rel=""preconnect"" href=""https://fonts.gstatic.com"" crossorigin>
  <link href=""https://fonts.googleapis.com/css2?family=Plus+Jakarta+Sans:wght@400;500;600;700&display=swap"" rel=""stylesheet"">
  <style>
    :root {{
      --background: 224 71% 4%;
      --foreground: 213 31% 91%;
      --card: 224 71% 4%;
      --card-foreground: 213 31% 91%;
      --popover: 224 71% 4%;
      --popover-foreground: 213 31% 91%;
      --primary: 263.4 70% 50.4%;
      --primary-foreground: 210 20% 98%;
      --secondary: 215 27.9% 16.9%;
      --secondary-foreground: 210 20% 98%;
      --muted: 215 27.9% 16.9%;
      --muted-foreground: 215.4 16.3% 56.9%;
      --accent: 215 27.9% 16.9%;
      --accent-foreground: 210 20% 98%;
      --destructive: 0 62.8% 30.6%;
      --destructive-foreground: 210 20% 98%;
      --border: 215 27.9% 16.9%;
      --input: 215 27.9% 16.9%;
      --ring: 263.4 70% 50.4%;
      --success: 142.1 70.6% 45.3%;
      --success-foreground: 144.4 61.2% 96.1%;
    }}

    body {{
      font-family: 'Plus Jakarta Sans', sans-serif;
      background: radial-gradient(circle at 50% 50%, #1e1b4b 0%, #0f172a 100%);
      color: hsl(var(--foreground));
      min-height: 100vh;
      display: flex;
      align-items: center;
      justify-content: center;
      margin: 0;
      padding: 16px;
      box-sizing: border-box;
    }}

    .container {{
      width: 100%;
      max-width: 480px;
      background: rgba(30, 41, 59, 0.45);
      backdrop-filter: blur(16px);
      -webkit-backdrop-filter: blur(16px);
      border: 1px solid rgba(255, 255, 255, 0.08);
      border-radius: 24px;
      padding: 40px 32px;
      text-align: center;
      box-shadow: 0 20px 40px rgba(0, 0, 0, 0.3);
      box-sizing: border-box;
      animation: fadeIn 0.6s ease-out;
    }}

    @keyframes fadeIn {{
      from {{ opacity: 0; transform: translateY(20px); }}
      to {{ opacity: 1; transform: translateY(0); }}
    }}

    .icon-wrapper {{
      width: 80px;
      height: 80px;
      border-radius: 50%;
      display: flex;
      align-items: center;
      justify-content: center;
      margin: 0 auto 24px;
      font-size: 36px;
      animation: scaleIn 0.5s cubic-bezier(0.175, 0.885, 0.32, 1.275) 0.2s both;
    }}

    @keyframes scaleIn {{
      from {{ transform: scale(0); }}
      to {{ transform: scale(1); }}
    }}

    .icon-success {{
      background: rgba(16, 185, 129, 0.1);
      border: 2px solid rgba(16, 185, 129, 0.3);
      color: #10b981;
    }}

    .icon-error {{
      background: rgba(239, 68, 68, 0.1);
      border: 2px solid rgba(239, 68, 68, 0.3);
      color: #ef4444;
    }}

    .icon-warning {{
      background: rgba(245, 158, 11, 0.1);
      border: 2px solid rgba(245, 158, 11, 0.3);
      color: #f59e0b;
    }}

    h1 {{
      font-size: 22px;
      font-weight: 700;
      margin: 0 0 12px;
      color: #ffffff;
      letter-spacing: -0.02em;
      line-height: 1.3;
    }}

    p {{
      font-size: 14px;
      line-height: 1.6;
      color: hsl(var(--muted-foreground));
      margin: 0 0 24px;
    }}

    .details {{
      background: rgba(255, 255, 255, 0.02);
      border: 1px solid rgba(255, 255, 255, 0.05);
      border-radius: 12px;
      padding: 16px;
      margin-bottom: 24px;
      text-align: left;
      font-size: 13px;
    }}

    .details-row {{
      display: flex;
      justify-content: space-between;
      margin-bottom: 8px;
      line-height: 1.5;
    }}

    .details-row:last-child {{
      margin-bottom: 0;
    }}

    .label {{
      color: hsl(var(--muted-foreground));
    }}

    .value {{
      font-weight: 600;
      color: #f1f5f9;
      max-width: 60%;
      word-break: break-word;
      text-align: right;
    }}

    .footer-text {{
      font-size: 12px;
      color: hsl(var(--muted-foreground));
      opacity: 0.8;
      margin-top: 32px;
    }}
  </style>
</head>
<body>
  <div class=""container"">
    <div class=""icon-wrapper {iconClass}"">
      {iconHtml}
    </div>
    <h1>{title}</h1>
    <p>{description}</p>
    
    <div class=""details"">
      <div class=""details-row"">
        <span class=""label"">Quyết định</span>
        <span class=""value"">{decision}</span>
      </div>
      <div class=""details-row"">
        <span class=""label"">Lý do</span>
        <span class=""value"">{reason}</span>
      </div>
      <div class=""details-row"">
        <span class=""label"">Người thực hiện</span>
        <span class=""value"">{approverName}</span>
      </div>
    </div>

    <div class=""footer-text"">
      Hệ thống tự động hóa quy trình AWE Automation Workflow Engine
    </div>
  </div>
</body>
</html>";

        return new ContentResult
        {
            Content = html,
            ContentType = "text/html; charset=utf-8",
            StatusCode = 200
        };
    }
}
