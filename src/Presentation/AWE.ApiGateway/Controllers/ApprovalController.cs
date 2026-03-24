using System.Text.Json;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace AWE.ApiGateway.Controllers;

[ApiController]
[Route("api/v1/approvals")]
public class ApprovalController(IWorkflowOrchestrator orchestrator, IApprovalTokenRepository tokenRepo) : ControllerBase
{
    [HttpPost("submit")]
    public async Task<IActionResult> SubmitApproval([FromQuery] string token, [FromBody] ApprovalDecision payload)
    {
        // authen
        var approvalToken = await tokenRepo.GetByTokenStringAsync(token);

        if (approvalToken == null) return NotFound("Token không tồn tại!");
        if (approvalToken.IsUsed) return BadRequest("Token này đã được sử dụng!");
        if (approvalToken.ExpiredAt < DateTime.UtcNow) return BadRequest("Token đã hết hạn!");

        // set token đã dùng
        approvalToken.IsUsed = true;
        await tokenRepo.UpdateApprovalTokenAsync(approvalToken); 

        // 3. Gọi Engine đi tiếp (Bơm quyết định duyệt vào Output)
        var resumeData = JsonSerializer.SerializeToDocument(payload);
        var result = await orchestrator.ResumeStepAsync(approvalToken.PointerId, resumeData);

        if (result.IsFailure) return BadRequest(result.Error);

        return Ok(new { Message = "Đã ghi nhận phê duyệt thành công!", Data = payload });
    }
}

public class ApprovalDecision
{
    public bool IsApproved { get; set; }
    public string? Reason { get; set; }
    public string? ApproverName { get; set; }
}
