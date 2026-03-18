using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Infrastructure.Plugins;
using AWE.Sdk;
using AWE.Shared.Consts;
using AWE.WorkflowEngine.Interfaces;
using MassTransit;

namespace AWE.Worker;

public class PluginConsumer : IConsumer<ExecutePluginCommand>
{
    private readonly ILogger<PluginConsumer> _logger;
    private readonly IExecutionPointerRepository _pointerRepo;
    private readonly IUnitOfWork _uow;
    private readonly IServiceProvider _serviceProvider;
    private readonly IPluginRegistry _registry;
    private readonly PluginLoader _pluginLoader;

    // Worker ID
    private readonly string _workerId = $"Worker-{Environment.MachineName}-{Guid.NewGuid().ToString()[..4]}";

    public PluginConsumer(
        ILogger<PluginConsumer> logger,     
        IExecutionPointerRepository pointerRepo,
        IUnitOfWork uow,
        IServiceProvider serviceProvider,
        IPluginRegistry registry,
        PluginLoader pluginLoader
        )
    {
        _logger = logger;
        _pointerRepo = pointerRepo;
        _uow = uow;
        _serviceProvider = serviceProvider;
        _registry = registry;
        _pluginLoader = pluginLoader;
    }

    public async Task Consume(ConsumeContext<ExecutePluginCommand> context)
    {
        var cmd = context.Message;
        _logger.LogInformation("🛠️ [{Worker}] START Step {Node} ({Type})", _workerId, cmd.NodeId, cmd.StepType);
        // 1. LOG: Node bắt đầu chạy
        await context.Publish(new WriteAuditLogCommand(
            InstanceId: cmd.InstanceId,
            Event: "StepStarted",
            Message: $"Bắt đầu thực thi Node: {cmd.NodeId}",
            Level: Domain.Enums.LogLevel.Information,
            //ExecutionPointerId: cmd.PointerId,
            NodeId: cmd.NodeId,
            WorkerId: Environment.MachineName
        ));

        // IDEMPOTENCY CHECK
        var pointer = await _pointerRepo.GetPointerByIdAsync(cmd.ExecutionPointerId);

        if (pointer == null)
        {
            _logger.LogError("❌ Pointer {Id} not found.", cmd.ExecutionPointerId);
            return;
        }

        if (pointer.Status == ExecutionPointerStatus.Completed)
        {
            _logger.LogWarning("⚠️ Step {Node} already completed.", cmd.NodeId);
            return;
        }

        // setup heartbeat running worker
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);

        // run Heartbeat in separate thread  
        var heartbeatTask = StartHeartbeatLoopAsync(pointer.Id, _workerId, cts.Token);

        try
        {
            // ACQUIRE LEASE
            if (!pointer.TryAcquireLease(_workerId, TimeSpan.FromMinutes(5))) return;
            await _uow.SaveChangesAsync();

            // DYNAMIC ROUTER: Phân luồng thực thi (Hybrid Architecture)
            PluginResult pluginResult;
            var pluginContext = new PluginContext(cmd.Payload, context.CancellationToken);

            switch (cmd.ExecutionMode)
            {
                case PluginExecutionMode.DynamicDll:
                    pluginResult = await _pluginLoader.ExecutePluginAsync(cmd.DllPath!, cmd.Payload, context.CancellationToken);
                    break;

                case PluginExecutionMode.RemoteGrpc:
                    // TODO: Mở rộng sau. Tạm thời văng lỗi để chặn.
                    throw new NotImplementedException("gRPC Remote Runner is coming soon!");

                case PluginExecutionMode.BuiltIn:
                default:
                    var builtInPlugin = _registry.GetPlugin(cmd.StepType);
                    pluginResult = await builtInPlugin.ExecuteAsync(pluginContext);
                    break;
            }

            // Xử lý Lỗi nghiệp vụ từ Plugin
            if (!pluginResult.IsSuccess)
            {
                throw new Exception(pluginResult.ErrorMessage ?? "Plugin failed without message.");
            }

            // PERSISTENCE & OUTBOX EVENT
            var outputDoc = JsonSerializer.SerializeToDocument(pluginResult.Outputs);
            pointer.Complete(_workerId, outputDoc);

            var routingKey = $"{MessagingConstants.PatternEvent.TrimEnd('#')}completed";
            await context.Publish(new StepCompletedEvent(
                  cmd.InstanceId, cmd.ExecutionPointerId, cmd.NodeId, outputDoc, DateTime.UtcNow
            ), ctx => ctx.SetRoutingKey(routingKey));

            await _uow.SaveChangesAsync(); // Double-Save: Lưu Entity + Outbox Message

            _logger.LogInformation("✅ [{Worker}] Step {Node} Completed.", _workerId, cmd.NodeId);

            await context.Publish(new WriteAuditLogCommand(
                InstanceId: cmd.InstanceId,
                Event: "StepCompleted",
                Message: $"Node {cmd.NodeId} hoàn thành thành công.",
                Level: Domain.Enums.LogLevel.Information,
                ExecutionPointerId: cmd.ExecutionPointerId,
                NodeId: cmd.NodeId,
                WorkerId: Environment.MachineName,
                MetadataJson: JsonSerializer.Serialize(outputDoc) 
            ));

            await cts.CancelAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [{Worker}] Error on Step {Node}", _workerId, cmd.NodeId);
            await context.Publish(new WriteAuditLogCommand(
                InstanceId: cmd.InstanceId,
                Event: "StepError",
                Message: $"Lỗi khi thực thi Node: {ex.Message}",
                Level: Domain.Enums.LogLevel.Error,
                ExecutionPointerId: cmd.ExecutionPointerId,
                NodeId: cmd.NodeId,
                WorkerId: Environment.MachineName,
                MetadataJson: JsonSerializer.Serialize(new { Exception = ex.ToString() })
            ));
            await HandleFailureAsync(pointer, cmd, ex.Message, context);
            await cts.CancelAsync();
            //throw;
        }
        finally
        {
            try { await heartbeatTask; } catch (OperationCanceledException) { }
        }
    }

    private async Task HandleFailureAsync(ExecutionPointer pointer, ExecutePluginCommand cmd, string errorMsg, ConsumeContext context)
    {
        var errorDoc = JsonDocument.Parse($"{{\"Error\": \"{errorMsg}\"}}");
        pointer.MarkAsFailed(_workerId, errorDoc);
        await _uow.SaveChangesAsync();

        await context.Publish(new StepFailedEvent(
            InstanceId: cmd.InstanceId,
            ExecutionPointerId: pointer.Id,
            StepId: cmd.NodeId,
            ErrorMessage: errorMsg
        ));
    }

    // Logic Heartbeat Loop
    private async Task StartHeartbeatLoopAsync(Guid pointerId, string workerId, CancellationToken ct)
    {
        // Cứ 10 giây báo cáo 1 lần. Gia hạn thêm 30 giây.
        // Nếu Worker crash, sau 30 giây lease sẽ hết hạn -> Engine biết đường reset.
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                // Tạo Scope mới vì Scope cũ của Consumer có thể đang bận
                using var scope = _serviceProvider.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IExecutionPointerRepository>();

                var success = await repo.RenewLeaseAsync(pointerId, workerId, TimeSpan.FromSeconds(30), ct);

                if (!success)
                {
                    // Nếu gia hạn thất bại (vd: Engine đã reset mất rồi),
                    // ta nên tự sát (dừng plugin) để tránh lãng phí resource.
                    _logger.LogWarning("⚠️ Lost ownership of step {Id}. Stopping heartbeat.", pointerId);
                    return;
                }
            }
            catch (Exception ex)
            {
                // Lỗi mạng tạm thời, không throw để vòng lặp tiếp tục chạy lần sau
                _logger.LogWarning("⚠️ Heartbeat failed: {Message}", ex.Message);
            }
        }
    }
}
