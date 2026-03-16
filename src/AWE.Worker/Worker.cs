using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using AWE.Shared.Consts;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace AWE.Worker;

public class PluginConsumer : IConsumer<ExecutePluginCommand>
{
    private readonly ILogger<PluginConsumer> _logger;
    private readonly IExecutionPointerRepository _pointerRepo;
    private readonly IUnitOfWork _uow;
    private readonly IServiceProvider _serviceProvider;

    // Worker ID
    private readonly string _workerId = $"Worker-{Environment.MachineName}-{Guid.NewGuid().ToString()[..4]}";

    public PluginConsumer(
        ILogger<PluginConsumer> logger,     
        IExecutionPointerRepository pointerRepo,
        IUnitOfWork uow,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _pointerRepo = pointerRepo;
        _uow = uow;
        _serviceProvider = serviceProvider;
        // Đã bỏ: PluginLoader, IStorageService (Chưa cần thiết cho giai đoạn test Core)
    }

    public async Task Consume(ConsumeContext<ExecutePluginCommand> context)
    {
        var cmd = context.Message;
        _logger.LogInformation("🛠️ [{Worker}] START Step {Node} ({Type})", _workerId, cmd.NodeId, cmd.StepType);

        // =================================================================
        // 1. IDEMPOTENCY CHECK
        // =================================================================
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
            // =================================================================
            // 2. ACQUIRE LEASE
            // =================================================================
            if (!pointer.TryAcquireLease(_workerId, TimeSpan.FromMinutes(5)))
            {
                _logger.LogWarning("🔒 Step {Node} locked by {Owner}.", cmd.NodeId, pointer.LeasedBy);
                return;
            }
            await _uow.SaveChangesAsync();

            // =================================================================
            // 3. EXECUTE (INTERNAL LOGIC - HARDCODED FOR TESTING)
            // =================================================================
            // Thay vì tải DLL, ta gọi hàm xử lý nội bộ giả lập

            var outputs = await ExecuteInternalLogicAsync(cmd.StepType, cmd.Payload, context.CancellationToken);

            // =================================================================
            // 4. PERSISTENCE (Lưu kết quả)
            // =================================================================

            // Serialize kết quả
            var outputDoc = JsonSerializer.SerializeToDocument(outputs);

            // Cập nhật DB
            pointer.Complete(_workerId, outputDoc);

            //var routingKey = "workflow.event.completed";
            // change: use const for routingKey
            var routingKey = $"{MessagingConstants.PatternEvent.TrimEnd('#')}completed";

            // Bắn Event báo xong
            await context.Publish(new StepCompletedEvent(
                  WorkflowInstanceId: cmd.InstanceId,
                  ExecutionPointerId: cmd.ExecutionPointerId,
                  StepId: cmd.NodeId,
                  Output: outputDoc,
                  CompletedAt: DateTime.UtcNow
            ), context =>
            {
                context.SetRoutingKey(routingKey);
            });

            await _uow.SaveChangesAsync();

            _logger.LogInformation("✅ [{Worker}] Step {Node} Completed.", _workerId, cmd.NodeId);
            await cts.CancelAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [{Worker}] Error on Step {Node}", _workerId, cmd.NodeId);
            await HandleFailureAsync(pointer, cmd, ex.Message, context);
            await cts.CancelAsync();
            //throw;
        }
        finally
        {
            try { await heartbeatTask; } catch (OperationCanceledException) { }
        }
    }

    /// <summary>
    /// Hàm giả lập Logic của Plugin để test luồng Engine
    /// </summary>
    private async Task<Dictionary<string, object>> ExecuteInternalLogicAsync(string stepType, string payloadJson, CancellationToken ct)
    {
        // Giả lập độ trễ xử lý (như đang chạy plugin thật)
        await Task.Delay(500, ct);

        var inputs = string.IsNullOrEmpty(payloadJson)
            ? new Dictionary<string, object>()
            : JsonSerializer.Deserialize<Dictionary<string, object>>(payloadJson) ?? new();

        // Xử lý cứng dựa trên StepType (Dispatcher)
        return stepType switch
        {
            "HttpRequest" => new Dictionary<string, object>
            {
                { "Status", 200 },
                { "Body", "{\"message\": \"Fake API Response\"}" },
                { "Time", DateTime.UtcNow }
            },

            "Log" => new Dictionary<string, object>
            {
                { "LogStatus", "Written to Console" }
            },

            "Delay" => new Dictionary<string, object>
            {
                { "Waited", "500ms" }
            },
            "CrashTest" => await RunCrashTestAsync(ct),
            "SagaTest" => throw new Exception("BÙM! Giả lập lỗi hệ thống để test Rollback!"),
            "Join" => new Dictionary<string, object>
            {
                { "JoinStatus", "Barrier Passed successfully" }
            },
            


            // Nếu gặp loại chưa định nghĩa -> Lỗi
            _ => throw new NotImplementedException($"Internal Plugin '{stepType}' not implemented yet.")
        };
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

    /// <summary>
    /// test for hearbeat Leasing, Recovery
    /// </summary>
    private async Task<Dictionary<string, object>> RunCrashTestAsync(CancellationToken ct)
    {
        _logger.LogWarning("💀 [TEST] Starting CrashTest plugin. I will sleep for 2 minutes.");
        _logger.LogWarning("👉 NOW IS THE TIME TO KILL THIS WORKER PROCESS TO TEST RECOVERY!");

        // Giả lập tác vụ chạy rất lâu (2 phút)
        // Trong thời gian này, Heartbeat sẽ chạy mỗi 10s.
        // Nếu bạn Kill Process lúc này, Heartbeat sẽ tắt -> Lease hết hạn.
        for (int i = 0; i < 3; i++) // 3 * 5s = 15s
        {
            if (ct.IsCancellationRequested) break;

            await Task.Delay(5000, ct);
            _logger.LogInformation("⏳ [TEST] Working... ({Seconds}s elapsed)", (i + 1) * 5);
        }

        return new Dictionary<string, object>
    {
        { "Result", "I survived!" },
        { "Status", "Success" }
    };
    }
}
