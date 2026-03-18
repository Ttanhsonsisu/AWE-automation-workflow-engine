using System;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Primitives;
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
    ILogger<ResumeExecutionUseCase> logger) : IResumeExecutionUseCase
{
    private readonly IWorkflowInstanceRepository _instanceRepo = instanceRepo;
    private readonly IExecutionPointerRepository _pointerRepo = pointerRepo;
    private readonly IWorkflowOrchestrator _orchestrator = orchestrator;
    private readonly IUnitOfWork _uow = uow;
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
        }

        // 4. RẼ NHÁNH CÁC NODE KẸT VỚI ÁO GIÁP BẢO VỆ (Try-Catch)
        foreach (var pointer in stuckPointers)
        {
            try
            {
                _logger.LogInformation("🚀 Resuming routing for stuck pointer {PointerId}", pointer.Id);
                await _orchestrator.HandleStepCompletionAsync(instance.Id, pointer.Id, null);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("lock"))
            {
                // BẮT RIÊNG LỖI LOCK TẠI CỬA JOIN!
                // Cố tình nuốt lỗi này để API không bị Crash 500. 
                // Pointer này vẫn giữ trạng thái Completed & Routed = false.
                // Người dùng chỉ cần bấm Resume lần nữa là nó sẽ chạy qua mượt mà!
                _logger.LogWarning("⏳ Pointer {PointerId} is waiting for Join Lock during Resume. Please click Resume again. Reason: {Msg}", pointer.Id, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "🔥 Critical error while routing pointer {PointerId} during Resume.", pointer.Id);
            }
        }

        return Result.Success();
    }
}
