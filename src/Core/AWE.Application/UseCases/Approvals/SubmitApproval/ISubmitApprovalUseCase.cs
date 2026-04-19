using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Approvals.SubmitApproval;

public interface ISubmitApprovalUseCase
{
    Task<Result<SubmitApprovalResponse>> ExecuteAsync(SubmitApprovalRequest request, CancellationToken cancellationToken = default);
}
