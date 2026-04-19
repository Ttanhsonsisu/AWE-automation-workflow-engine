using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Executions.CancelExecution;

public class CancelExecutionUseCase(
    IWorkflowInstanceRepository instanceRepo,
    IExecutionPointerRepository pointerRepo,
    IUnitOfWork uow) : ICancelExecutionUseCase
{
    private readonly IWorkflowInstanceRepository _instanceRepo = instanceRepo;
    private readonly IExecutionPointerRepository _pointerRepo = pointerRepo;
    private readonly IUnitOfWork _uow = uow;

    public async Task<Result> ExecuteAsync(CancelExecutionRequest request, CancellationToken ct = default)
    {
        var instance = await _instanceRepo.GetInstanceByIdAsync(request.InstanceId, ct);

        if (instance == null)
            return Result.Failure(Error.NotFound("Execution.NotFound", "Workflow execution not found"));

        // Nếu đã ở trạng thái kết thúc (Completed, Failed, Cancelled) thì không thể Cancel được nữa
        if (instance.Status == WorkflowInstanceStatus.Completed ||
            instance.Status == WorkflowInstanceStatus.Failed ||
            instance.Status == WorkflowInstanceStatus.Cancelled)
        {
            return Result.Failure(Error.BusinessRule("Execution.TerminalState", $"Không thể hủy luồng đang ở trạng thái {instance.Status}."));
        }

        // 1. Đổi trạng thái Instance thành Cancelled
        // (Nếu Entity của bạn chưa có hàm Cancel(), hãy thêm `public void Cancel() { Status = WorkflowInstanceStatus.Cancelled; }` nhé)
        instance.Status = WorkflowInstanceStatus.Cancelled;

        // 2. Đi "săn" và tiêu diệt các Pointer đang chờ để giải phóng tài nguyên
        var pointers = await _pointerRepo.GetPointersByInstanceIdAsync(instance.Id, ct);

        var activePointers = pointers.Where(p =>
            p.Status == ExecutionPointerStatus.Pending ||
            p.Status == ExecutionPointerStatus.Suspended).ToList();

        foreach (var pointer in activePointers)
        {
            // Ép nó vào trạng thái Skipped (bỏ qua) để Worker/WakeUp Service bỏ qua nó
            pointer.Skip();
        }

        // 3. Lưu toàn bộ thay đổi
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
