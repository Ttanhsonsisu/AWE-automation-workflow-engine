using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using MassTransit;

namespace AWE.Wokrer.Engine.Consumers;

public class AuditLogConsumer : IConsumer<WriteAuditLogCommand>
{
    private readonly ILogger<AuditLogConsumer> _logger;
    private readonly IUnitOfWork _uow;
    private readonly IExecutionLogRepository _executionLogRepository;
    private readonly IExecutionPointerRepository _executionPointerRepository;
    private readonly IWorkflowInstanceRepository _workflowInstanceRepository;

    public AuditLogConsumer(ILogger<AuditLogConsumer> logger,
                            IUnitOfWork uow,
                            IExecutionLogRepository executionLogRepository,
                            IExecutionPointerRepository executionPointerRepository,
                            IWorkflowInstanceRepository workflowInstanceRepository)
    {
        _uow = uow;
        _executionLogRepository = executionLogRepository;
        _executionPointerRepository = executionPointerRepository;
        _workflowInstanceRepository = workflowInstanceRepository;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WriteAuditLogCommand> context)
    {
        var msg = context.Message;

        // 1. In ra Console của con Worker chuyên ghi Log
        _logger.LogInformation("[AUDIT] [{Level}] {Event} - Node: {NodeId} - Msg: {Msg}",
            msg.Level, msg.Event, msg.NodeId, msg.Message);

        // 2. Chuyển đổi Metadata từ string sang JsonDocument (nếu có)
        JsonDocument? metadataDoc = null;
        if (!string.IsNullOrWhiteSpace(msg.MetadataJson))
        {
            try
            {
                metadataDoc = JsonDocument.Parse(msg.MetadataJson);
            }
            catch
            {
                /* Bỏ qua nếu parse JSON lỗi để không làm chết quá trình ghi log */
            }
        }

        if (msg.InstanceId == Guid.Empty)
        {
            _logger.LogWarning("[AUDIT] Invalid empty InstanceId for log event {Event}. Skip insert.", msg.Event);
            return;
        }

        var instance = await _workflowInstanceRepository.GetInstanceByIdAsync(msg.InstanceId, context.CancellationToken);
        if (instance == null)
        {
            _logger.LogWarning("[AUDIT] Instance {InstanceId} not found for log event {Event}. Skip insert to avoid FK violation.", msg.InstanceId, msg.Event);
            return;
        }

        Guid? executionPointerId = msg.ExecutionPointerId;
        if (executionPointerId.HasValue)
        {
            var pointer = await _executionPointerRepository.GetPointerByIdAsync(executionPointerId.Value, context.CancellationToken);
            if (pointer == null)
            {
                _logger.LogWarning("[AUDIT] Pointer {PointerId} not found for log event {Event}. Fallback to null pointer reference.", executionPointerId.Value, msg.Event);
                executionPointerId = null;
            }
        }

        // 3. Tạo Entity
        var logEntry = new ExecutionLog(
            instanceId: msg.InstanceId,
            eventType: msg.Event,
            message: msg.Message,
            level: msg.Level, // Enum Microsoft.Extensions.Logging.LogLevel
            executionPointerId: executionPointerId,
            nodeId: msg.NodeId,
            workerId: msg.WorkerId,
            metadata: metadataDoc
        );

        // 4. Lưu vào Database
        await _executionLogRepository.AddLogAsync(logEntry, context.CancellationToken);

        // Vì PrefetchCount = 100, nếu có nhiều message đến cùng lúc, 
        // nó sẽ chạy nhiều hàm Consume song song, và Entity Framework sẽ gặp vấn đề nếu không thiết kế khéo.
        // NHƯNG với Scoped UnitOfWork của MassTransit, mỗi ConsumeContext là 1 Scope riêng biệt, nên an toàn 100%.
        await _uow.SaveChangesAsync(context.CancellationToken);
    }
}
