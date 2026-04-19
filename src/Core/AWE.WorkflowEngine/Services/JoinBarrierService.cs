using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using Medallion.Threading;
using Microsoft.Extensions.Logging;

namespace AWE.WorkflowEngine.Services;

public class JoinBarrierService(
    IDistributedLockProvider lockProvider,
    IExecutionPointerRepository pointerRepo,
    ILogger<JoinBarrierService> logger) : IJoinBarrierService
{
    private readonly IDistributedLockProvider _lockProvider = lockProvider;
    private readonly IExecutionPointerRepository _pointerRepo = pointerRepo;
    private readonly ILogger<JoinBarrierService> _logger = logger;

    public async Task<JoinBarrierResult> EvaluateBarrierAsync(WorkflowInstance instance, string joinNodeId, int totalIncomingEdges)
    {
        var lockKey = $"workflow:{instance.Id}:join:{joinNodeId}";
        await using var handle = await _lockProvider.TryAcquireLockAsync(lockKey, TimeSpan.FromSeconds(5));

        if (handle == null)
        {
            _logger.LogInformation("⏳ Join {JoinId} locked by another thread.", joinNodeId);
            // Fallback an toàn: lock contention kéo dài không được phép làm kẹt flow.
            // Dùng optimistic evaluation để tiếp tục tiến trình Resume.
            return await EvaluateCoreAsync(instance, joinNodeId, totalIncomingEdges);
        }

        return await EvaluateCoreAsync(instance, joinNodeId, totalIncomingEdges);
    }

    private async Task<JoinBarrierResult> EvaluateCoreAsync(WorkflowInstance instance, string joinNodeId, int totalIncomingEdges)
    {
        int arrivedPointersCount = await _pointerRepo.CountArrivedPointersByStepIdAsync(instance.Id, joinNodeId);

        // Chưa đủ nhánh -> Barrier chưa vỡ
        if (arrivedPointersCount < totalIncomingEdges)
            return new JoinBarrierResult(false, false, null);

        var joinPointers = await _pointerRepo.GetPointersByStepIdAsync(instance.Id, joinNodeId);

        // Đã có nhánh nào chạy qua cái Join này chưa? (Chống duplicate)
        if (joinPointers.Any(p => p.Status == ExecutionPointerStatus.Completed))
            return new JoinBarrierResult(true, false, null);

        // DEAD-PATH PROPAGATION: Tất cả các nhánh đổ vào đều bị Skipped
        if (joinPointers.All(p => p.Status == ExecutionPointerStatus.Skipped))
        {
            _logger.LogInformation("All paths to {JoinId} skipped. Propagating Dead-Path.", joinNodeId);
            return new JoinBarrierResult(true, true, null);
        }

        // CHỌN ĐẠI DIỆN ĐỂ DISPATCH
        var pointerToDispatch = joinPointers.FirstOrDefault(p => p.Status == ExecutionPointerStatus.Pending);

        // Dọn rác: Đánh dấu các pointer thừa thành Completed
        foreach (var p in joinPointers.Where(x => x.Id != pointerToDispatch?.Id && x.Status == ExecutionPointerStatus.Pending))
        {
            p.Status = ExecutionPointerStatus.Completed;
        }

        return new JoinBarrierResult(true, false, pointerToDispatch);
    }

}
