using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Abstractions.Persistence;
using AWE.Shared.Primitives;

namespace AWE.Application.UseCases.Executions.RetryExecution;

public class RetryExecutionRequest
{
    public Guid InstanceId { get; set; }
}

public interface IRetryExecutionUseCase
{
    Task<Result> ExecuteAsync(RetryExecutionRequest request, CancellationToken cancellationToken = default);
}

public class RetryExecutionUseCase(
    IWorkflowInstanceRepository instanceRepo,
    IExecutionPointerRepository pointerRepo,
    IWorkflowDefinitionRepository defRepo,
    IPointerDispatcher dispatcher,
    IUnitOfWork uow) : IRetryExecutionUseCase
{
    private readonly IWorkflowInstanceRepository _instanceRepo = instanceRepo;
    private readonly IExecutionPointerRepository _pointerRepo = pointerRepo;
    private readonly IWorkflowDefinitionRepository _defRepo = defRepo;
    private readonly IPointerDispatcher _dispatcher = dispatcher;
    private readonly IUnitOfWork _uow = uow;

    public async Task<Result> ExecuteAsync(RetryExecutionRequest request, CancellationToken ct = default)
    {
        var instance = await _instanceRepo.GetInstanceByIdAsync(request.InstanceId, ct);
        if (instance == null)
            return Result.Failure(Error.NotFound("Execution.NotFound", "Workflow execution not found"));

        if (instance.Status != Domain.Enums.WorkflowInstanceStatus.Failed &&
            instance.Status != Domain.Enums.WorkflowInstanceStatus.Compensating)
        {
            return Result.Failure(Error.BusinessRule("Execution.NotFailed", "Chỉ có thể Retry luồng bị Failed."));
        }

        // 1. Tìm tất cả các Pointer đang bị Failed của luồng này
        var pointers = await _pointerRepo.GetPointersByInstanceIdAsync(instance.Id, ct);
        var failedPointers = pointers.Where(p => p.Status == Domain.Enums.ExecutionPointerStatus.Failed).ToList();

        if (!failedPointers.Any())
            return Result.Failure(Error.BusinessRule("Execution.NoFailedNodes", "Không tìm thấy Node nào bị lỗi để Retry."));

        var def = await _defRepo.GetDefinitionByIdAsync(instance.DefinitionId, ct);

        // 2. Cập nhật lại trạng thái Instance
        instance.Status = Domain.Enums.WorkflowInstanceStatus.Running;

        // 3. Reset các Node lỗi và đẩy lại vào Message Broker
        foreach (var pointer in failedPointers)
        {
            pointer.ResetToPending(); // Hàm bạn đã viết trong Entity (reset Status, RetryCount)
            await _dispatcher.DispatchAsync(instance, pointer, def!.DefinitionJson);
        }

        // 4. Lưu DB (Atomic)
        await _uow.SaveChangesAsync(ct);

        return Result.Success();
    }
}
