using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Shared.Primitives;
using MassTransit;

namespace AWE.Application.UseCases.Approvals.SubmitApproval;

public class SubmitApprovalUseCase : ISubmitApprovalUseCase
{
    private readonly IApprovalTokenRepository _tokenRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublishEndpoint _publishEndpoint;

    public SubmitApprovalUseCase(
        IApprovalTokenRepository tokenRepo,
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint)
    {
        _tokenRepo = tokenRepo;
        _unitOfWork = unitOfWork;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<Result<SubmitApprovalResponse>> ExecuteAsync(SubmitApprovalRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Truy vấn Token (Cực nhanh vì đã có Index)
        var approvalToken = await _tokenRepo.GetByTokenStringAsync(request.Token, cancellationToken);

        if (approvalToken == null || approvalToken.IsUsed || approvalToken.ExpiredAt < DateTime.UtcNow)
        {
            return Result.Failure<SubmitApprovalResponse>(Error.Validation("Token.Invalid", "Token không hợp lệ, đã sử dụng hoặc hết hạn!"));
        }

        // 2. Chốt Token đã dùng (Để tránh sếp bấm đúp 2 lần)
        approvalToken.IsUsed = true;
        await _tokenRepo.UpdateApprovalTokenAsync(approvalToken, cancellationToken);

        // 3. Đóng gói dữ liệu duyệt thành chuỗi String
        var payload = new 
        {
            IsApproved = request.IsApproved,
            Reason = request.Reason,
            ApproverName = request.ApproverName
        };
        string resumeDataJson = JsonSerializer.Serialize(payload);

        // 4. [BÍ QUYẾT TỐC ĐỘ] Bắn thẳng lệnh đánh thức vào RabbitMQ qua MassTransit Outbox
        await _publishEndpoint.Publish(new ResumeWorkflowCommand(
            PointerId: approvalToken.PointerId,
            ResumeDataJson: resumeDataJson
        ), cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 5. Trả về ngay lập tức (Không cần đợi Engine chạy!)
        return Result.Success(new SubmitApprovalResponse
        {
            Message = "Đã ghi nhận phê duyệt. Hệ thống đang xử lý bước tiếp theo!"
        });
    }
}
