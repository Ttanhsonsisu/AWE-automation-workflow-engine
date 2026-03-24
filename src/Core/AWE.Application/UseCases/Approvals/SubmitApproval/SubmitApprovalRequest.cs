using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Approvals.SubmitApproval;

public class SubmitApprovalRequest
{
    public string Token { get; set; } = string.Empty;
    public bool IsApproved { get; set; }
    public string? Reason { get; set; }
    public string? ApproverName { get; set; }
}
