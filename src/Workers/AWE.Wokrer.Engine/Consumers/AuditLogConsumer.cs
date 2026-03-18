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

    public AuditLogConsumer(ILogger<AuditLogConsumer> logger,
                            IUnitOfWork uow,
                            IExecutionLogRepository executionLogRepository)
    {
        _uow = uow;
        _executionLogRepository = executionLogRepository;
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

        // 3. Tạo Entity
        var logEntry = new ExecutionLog(
            instanceId: msg.InstanceId,
            eventType: msg.Event,
            message: msg.Message,
            level: msg.Level, // Enum Microsoft.Extensions.Logging.LogLevel
            executionPointerId: msg.ExecutionPointerId,
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
