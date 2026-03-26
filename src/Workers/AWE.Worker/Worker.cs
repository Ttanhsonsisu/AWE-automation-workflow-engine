using System.Text.Json;
using AWE.Application.Abstractions.CoreEngine;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Infrastructure.Plugins;
using AWE.Sdk;
using AWE.Shared.Consts;
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
    private readonly PluginCacheManager _pluginCacheManager;

    // Worker ID
    private readonly string _workerId = $"Worker-{Environment.MachineName}-{Guid.NewGuid().ToString()[..4]}";

    public PluginConsumer(
        ILogger<PluginConsumer> logger,
        IExecutionPointerRepository pointerRepo,
        IUnitOfWork uow,
        IServiceProvider serviceProvider,
        IPluginRegistry registry,
        PluginLoader pluginLoader,
        PluginCacheManager pluginCacheManager
        )
    {
        _logger = logger;
        _pointerRepo = pointerRepo;
        _uow = uow;
        _serviceProvider = serviceProvider;
        _registry = registry;
        _pluginLoader = pluginLoader;
        _pluginCacheManager = pluginCacheManager;
    }

    public async Task Consume(ConsumeContext<ExecutePluginCommand> context)
    {
        var cmd = context.Message;
        _logger.LogInformation("🛠️ [{Worker}] START Step {Node} ({Type})", _workerId, cmd.NodeId, cmd.StepType);

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

        if (!pointer.TryAcquireLease(_workerId, TimeSpan.FromMinutes(5)))
        {
            _logger.LogWarning("⚠️ Step {Node} is locked by another worker.", cmd.NodeId);
            return;
        }
        await _uow.SaveChangesAsync();

        // 👉 SỬA: CHỈ BẮN LOG KHI ĐÃ CẦM CHẮC LEASE TRONG TAY
        await context.Publish(new WriteAuditLogCommand(
            InstanceId: cmd.InstanceId,
            Event: "StepStarted",
            Message: $"Bắt đầu thực thi Node: {cmd.NodeId}",
            Level: Domain.Enums.LogLevel.Information,
            ExecutionPointerId: cmd.ExecutionPointerId, // Bỏ comment chỗ này
            NodeId: cmd.NodeId,
            WorkerId: Environment.MachineName
        ));

        // setup heartbeat running worker
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);

        // run Heartbeat in separate thread  
        var heartbeatTask = StartHeartbeatLoopAsync(pointer.Id, _workerId, cts.Token);

        try
        {
            // Bơm PointerId và InstanceId vào Payload để Plugin có thể dùng
            var payloadDict = JsonSerializer.Deserialize<Dictionary<string, object>>(cmd.Payload) ?? new();
            payloadDict["PointerId"] = cmd.ExecutionPointerId.ToString();
            payloadDict["InstanceId"] = cmd.InstanceId.ToString();
            var enrichedPayload = JsonSerializer.Serialize(payloadDict);

            // DYNAMIC ROUTER: Phân luồng thực thi (Hybrid Architecture)
            PluginResult pluginResult;
            var pluginContext = new PluginContext(enrichedPayload, context.CancellationToken);

            switch (cmd.ExecutionMode)
            {
                case PluginExecutionMode.DynamicDll:
                    if (string.IsNullOrWhiteSpace(cmd.ExecutionMetadataJson))
                    {
                        throw new ArgumentException("ExecutionMetadataJson is required for DynamicDll mode.");
                    }

                    pluginResult = await _pluginCacheManager.ExecutePluginAsync(cmd.ExecutionMetadataJson!, cmd.Payload, context.CancellationToken);
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

            // TẮT HEARTBEAT TRƯỚC KHI CHỐT DATA
            await cts.CancelAsync();
            try { await heartbeatTask; } catch { }

            // PERSISTENCE & OUTBOX EVENT
            if (pluginResult.IsSuspended == true)
            {
                // 1. NHÁNH SUSPEND: Plugin yêu cầu chờ (Approval / Payment)
                _logger.LogInformation("[{Worker}] Step {Node} SUSPENDED. Reason: {Msg}", _workerId, cmd.NodeId, pluginResult.Message);

                // Bắn lệnh về cho Core Engine để nó đổi trạng thái Database thành WaitingForEvent
                await context.Publish(new SuspendStepCommand(
                    InstanceId: cmd.InstanceId,
                    PointerId: cmd.ExecutionPointerId,
                    Reason: pluginResult.Message
                ));

                await context.Publish(new WriteAuditLogCommand(
                    InstanceId: cmd.InstanceId,
                    Event: "StepSuspended",
                    Message: $"Node {cmd.NodeId} chuyển sang trạng thái chờ: {pluginResult.Message}",
                    Level: Domain.Enums.LogLevel.Information,
                    ExecutionPointerId: cmd.ExecutionPointerId,
                    NodeId: cmd.NodeId,
                    WorkerId: Environment.MachineName
                ));

                await _uow.SaveChangesAsync();
            }
            else if (pluginResult.IsSuccess)
            {
                // logic success, complete step
                var outputDoc = JsonSerializer.SerializeToDocument(pluginResult.Outputs);
                pointer.Complete(_workerId, outputDoc);

                var routingKey = $"{MessagingConstants.PatternEvent.TrimEnd('#')}completed";
                await context.Publish(new StepCompletedEvent(
                      cmd.InstanceId, cmd.ExecutionPointerId, cmd.NodeId, outputDoc, DateTime.UtcNow
                ), ctx => ctx.SetRoutingKey(routingKey));

                _logger.LogInformation("[{Worker}] Step {Node} Completed.", _workerId, cmd.NodeId);

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

                await _uow.SaveChangesAsync();
            }
            else
            {
                // throw error plugin 
                throw new Exception(pluginResult.ErrorMessage ?? pluginResult.Message ?? "Plugin failed without message.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{Worker}] Error on Step {Node}", _workerId, cmd.NodeId);

            await cts.CancelAsync();
            try { await heartbeatTask; } catch { }

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
        //await _uow.SaveChangesAsync();

        await context.Publish(new StepFailedEvent(
            InstanceId: cmd.InstanceId,
            ExecutionPointerId: pointer.Id,
            StepId: cmd.NodeId,
            ErrorMessage: errorMsg
        ));

        await _uow.SaveChangesAsync();
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
