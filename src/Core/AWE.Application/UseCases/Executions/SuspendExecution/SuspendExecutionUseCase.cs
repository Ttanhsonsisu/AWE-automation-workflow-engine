using System;
using System.Threading;
using System.Threading.Tasks;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Shared.Primitives;
using MassTransit;

namespace AWE.Application.UseCases.Executions.SuspendExecution;

public class SuspendExecutionRequest
{
    public Guid InstanceId { get; set; }
}

public interface ISuspendExecutionUseCase
{
    Task<Result> ExecuteAsync(SuspendExecutionRequest request, CancellationToken cancellationToken = default);
}

public class SuspendExecutionUseCase : ISuspendExecutionUseCase
{
    private readonly IWorkflowInstanceRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPublishEndpoint _publishEndpoint;

    public SuspendExecutionUseCase(
        IWorkflowInstanceRepository repository,
        IUnitOfWork unitOfWork,
        IPublishEndpoint publishEndpoint)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _publishEndpoint = publishEndpoint;
    }

    public async Task<Result> ExecuteAsync(SuspendExecutionRequest request, CancellationToken cancellationToken = default)
    {
        var instance = await _repository.GetInstanceByIdAsync(request.InstanceId, cancellationToken);

        if (instance == null)
            return Result.Failure(Error.NotFound("Execution.NotFound", "Workflow execution not found"));

        try
        {
            instance.Suspend();
            await _repository.UpdateInstanceAsync(instance, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Bắn sự kiện SignalR cập nhật UI nếu cần
            await _publishEndpoint.Publish(new UiWorkflowStatusChangedEvent(
                instance.Id,
                "Suspended",
                DateTime.UtcNow
            ), cancellationToken);

            await _publishEndpoint.Publish(new WriteAuditLogCommand(
                InstanceId: instance.Id,
                Event: "WorkflowSuspended",
                Message: "Luồng đã bị tạm dừng bởi người dùng.",
                Level: AWE.Domain.Enums.LogLevel.Warning,
                NodeId: "System"
            ), cancellationToken);

            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(Error.BusinessRule("Execution.InvalidState", ex.Message));
        }
    }
}
