using System;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.Application.UseCases.Executions.ResumeExecution;

public class ResumeExecutionRequest
{
    public Guid InstanceId { get; set; }
}

public interface IResumeExecutionUseCase
{
    Task<Result> ExecuteAsync(ResumeExecutionRequest request, CancellationToken cancellationToken = default);
}

public class ResumeExecutionUseCase(
    IWorkflowInstanceRepository instanceRepo,
    IExecutionPointerRepository pointerRepo,
    IWorkflowOrchestrator orchestrator, 
    IUnitOfWork uow,
    IPublishEndpoint publishEndpoint,
    ILogger<ResumeExecutionUseCase> logger) : IResumeExecutionUseCase
{
    private readonly IWorkflowInstanceRepository _instanceRepo = instanceRepo;
    private readonly IExecutionPointerRepository _pointerRepo = pointerRepo;
    private readonly IWorkflowOrchestrator _orchestrator = orchestrator;
    private readonly IUnitOfWork _uow = uow;
    private readonly IPublishEndpoint _publishEndpoint = publishEndpoint;
    private readonly ILogger<ResumeExecutionUseCase> _logger = logger;

    public async Task<Result> ExecuteAsync(ResumeExecutionRequest request, CancellationToken ct = default)
    {
        var instance = await _instanceRepo.GetInstanceByIdAsync(request.InstanceId, ct);
        if (instance == null)
            return Result.Failure(Error.NotFound("Execution.NotFound", "Workflow execution not found"));

        // Debug log để kiểm tra trạng thái trước khi Resume
        var pointers = await _pointerRepo.GetPointersByInstanceIdAsync(instance.Id, ct);
        var stuckPointers = pointers.Where(p =>
            p.Status == ExecutionPointerStatus.Completed &&
            !p.Routed).ToList();

        if (instance.Status != WorkflowInstanceStatus.Suspended && stuckPointers.Count == 0)
        {
            return Result.Failure(Error.BusinessRule("Execution.CannotResume", "Luồng không bị tạm dừng hoặc không có Node nào đang chờ rẽ nhánh."));
        }

        // 3. Đổi trạng thái DB thành Running (nếu chưa phải)
        if (instance.Status != WorkflowInstanceStatus.Running)
        {
            instance.Resume();
            await _instanceRepo.UpdateInstanceAsync(instance, ct);
            await _uow.SaveChangesAsync(ct);

            // Notify UI
            await _publishEndpoint.Publish(new UiWorkflowStatusChangedEvent(
                instance.Id,
                "Running",
                DateTime.UtcNow
            ), ct);

            await _publishEndpoint.Publish(new WriteAuditLogCommand(
                InstanceId: instance.Id,
                Event: "WorkflowResumed",
                Message: "Luồng đã được tiếp tục thực thi bởi người dùng.",
                Level: AWE.Domain.Enums.LogLevel.Information,
                NodeId: "System"
            ), ct);
        }

        // 4. RẼ NHÁNH CÁC NODE KẸT
        // Không block API quá lâu để chờ "hết sạch" node kẹt, vì routing là eventual.
        // Chỉ thử vài vòng ngắn để kick-off lại luồng, sau đó trả Success cho client.
        const int maxGlobalRetries = 3;
        var hadProgress = false;
        for (var round = 1; round <= maxGlobalRetries; round++)
        {
            pointers = await _pointerRepo.GetPointersByInstanceIdAsync(instance.Id, ct);
            stuckPointers = pointers.Where(p => p.Status == ExecutionPointerStatus.Completed && !p.Routed).ToList();

            if (stuckPointers.Count == 0)
            {
                return Result.Success();
            }

            var hasJoinLockBusy = false;
            var routedAnyThisRound = false;
            var beforeCount = stuckPointers.Count;

            foreach (var pointer in stuckPointers)
            {
                try
                {
                    _logger.LogInformation("🚀 Resuming routing for stuck pointer {PointerId}", pointer.Id);
                    var routeResult = await _orchestrator.HandleStepCompletionAsync(instance.Id, pointer.Id, null);
                    if (routeResult.IsSuccess)
                    {
                        routedAnyThisRound = true;
                    }

                    if (routeResult.IsFailure)
                    {
                        var msg = routeResult.Error?.Message ?? string.Empty;
                        if (msg.Contains("acquire lock", StringComparison.OrdinalIgnoreCase)
                            || msg.Contains("join", StringComparison.OrdinalIgnoreCase))
                        {
                            hasJoinLockBusy = true;
                            _logger.LogInformation("⏳ Join lock busy for pointer {PointerId}. Round {Round}/{Max}. Reason: {Msg}", pointer.Id, round, maxGlobalRetries, msg);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ Resume routing returned failure for pointer {PointerId}: {Code} - {Message}",
                                pointer.Id,
                                routeResult.Error?.Code,
                                routeResult.Error?.Message);
                        }
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("acquire lock", StringComparison.OrdinalIgnoreCase)
                                                           || ex.Message.Contains("join", StringComparison.OrdinalIgnoreCase))
                {
                    hasJoinLockBusy = true;
                    _logger.LogInformation("⏳ Join lock busy for pointer {PointerId}. Round {Round}/{Max}. Reason: {Msg}", pointer.Id, round, maxGlobalRetries, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "🔥 Critical error while routing pointer {PointerId} during Resume.", pointer.Id);
                }
            }

            if (!hasJoinLockBusy)
            {
                pointers = await _pointerRepo.GetPointersByInstanceIdAsync(instance.Id, ct);
                var afterCount = pointers.Count(p => p.Status == ExecutionPointerStatus.Completed && !p.Routed);

                if (afterCount == 0)
                    return Result.Success();

                if (afterCount < beforeCount || routedAnyThisRound)
                    hadProgress = true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(120), ct);
        }

        pointers = await _pointerRepo.GetPointersByInstanceIdAsync(instance.Id, ct);
        stuckPointers = pointers.Where(p => p.Status == ExecutionPointerStatus.Completed && !p.Routed).ToList();

        if (stuckPointers.Count > 0)
        {
            _logger.LogInformation("Resume accepted for workflow {InstanceId}. Remaining stuck pointers: {Count}. Routing will continue asynchronously.",
                instance.Id,
                stuckPointers.Count);

            if (!hadProgress)
            {
                return Result.Failure(Error.Conflict(
                    "Execution.ResumeNoProgress",
                    "Resume không tạo được tiến triển xử lý. Vui lòng thử lại hoặc kiểm tra log lỗi định tuyến."
                ));
            }
        }

        return Result.Success();


    }
}
