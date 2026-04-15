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

    [HttpPost("submit")]
    [AllowAnonymous]
    public async Task<IActionResult> SubmitApproval([FromQuery] string token, [FromBody] SubmitApprovalRequest payload, CancellationToken cancellationToken)
    {
        // 1. Map token sang payload
        payload.Token = token;

        // 2. Thực thi Use Case
        var result = await _submitApprovalUseCase.ExecuteAsync(payload, cancellationToken);

        // 3. Trả về kết quả
        return HandleResult(result);
    }
}
