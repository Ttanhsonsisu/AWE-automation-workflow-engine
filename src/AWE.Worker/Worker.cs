using System.Text.Json;
using AWE.Application.Abstractions.Persistence;
using AWE.Contracts.Messages;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
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

            var routingKey = "workflow.event.completed"; // fix 

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
            throw;
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
        for (int i = 0; i < 24; i++) // 24 * 5s = 120s
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




//using System.Reflection;
//using System.Text.Json;
//using AWE.Application.Abstractions.Persistence;
//using AWE.Application.Services;
//using AWE.Contracts.Messages;
//using AWE.Domain.Entities;
//using AWE.Domain.Enums;
//using AWE.Infrastructure.Plugins;
//using MassTransit;

//namespace AWE.Worker;

//public class PluginConsumer : IConsumer<ExecutePluginCommand>
//{
//    private readonly ILogger<PluginConsumer> _logger;
//    private readonly PluginLoader _pluginLoader;
//    private readonly IStorageService _storageService;
//    private readonly IExecutionPointerRepository _pointerRepo;
//    private readonly IUnitOfWork _uow;

//    // Thư mục cache DLL trên máy Worker
//    private readonly string _localCacheDir;

//    // Định danh Worker (Trong thực tế lấy từ Environment Variable hoặc HostName)
//    private readonly string _workerId = $"Worker-{Environment.MachineName}-{Guid.NewGuid().ToString()[..4]}";

//    public PluginConsumer(
//        ILogger<PluginConsumer> logger,
//        PluginLoader pluginLoader,
//        IStorageService storageService,
//        IExecutionPointerRepository pointerRepo,
//        IUnitOfWork uow)
//    {
//        _logger = logger;
//        _pluginLoader = pluginLoader;
//        _storageService = storageService;
//        _pointerRepo = pointerRepo;
//        _uow = uow;

//        // Setup thư mục cache: /app/plugin-cache
//        _localCacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugin-cache");
//        if (!Directory.Exists(_localCacheDir)) Directory.CreateDirectory(_localCacheDir);
//    }

//    public async Task Consume(ConsumeContext<ExecutePluginCommand> context)
//    {
//        var cmd = context.Message;
//        _logger.LogInformation("🛠️ [{Worker}] Receive Step {Node} ({Type})", _workerId, cmd.NodeId, cmd.StepType);

//        // =================================================================
//        // 1. IDEMPOTENCY CHECK [SRS FR-08]
//        // =================================================================
//        // Load trạng thái thật từ DB ("Ground Truth")

//        var pointer = await _pointerRepo.GetPointerByIdAsync(cmd.ExecutionPointerId);

//        if (pointer == null)
//        {
//            _logger.LogError("❌ Pointer {Id} not found in DB. Aborting.", cmd.ExecutionPointerId);
//            return;
//        }

//        // Nếu đã xong hoặc đang chạy bởi người khác (mà lease chưa hết hạn) -> Bỏ qua
//        if (pointer.Status == ExecutionPointerStatus.Completed)
//        {
//            _logger.LogWarning("⚠️ Step {Node} already completed. Ack.", cmd.NodeId);
//            return;
//        }

//        try
//        {

//            // =================================================================
//            // 2. ACQUIRE LEASE (Basic for Week 3)
//            // =================================================================
//            // Đánh dấu "Tôi đang làm việc này" để các Worker khác không tranh
//            if (!pointer.TryAcquireLease(_workerId, TimeSpan.FromMinutes(5)))
//            {
//                _logger.LogWarning("🔒 Step {Node} locked by {Owner}. Skip.", cmd.NodeId, pointer.LeasedBy);
//                return;
//            }

//            await _uow.SaveChangesAsync(); // Commit Lease ngay lập tức

//            // =================================================================
//            // 3. PREPARE & EXECUTE [SRS FR-07]
//            // =================================================================

//            // A. Tải DLL (Nếu chưa có)
//            string dllPath = Path.Combine(_localCacheDir, $"{cmd.StepType}Plugin.dll");
//            await EnsurePluginDownloadedAsync(cmd.StepType, dllPath, context.CancellationToken);

//            // B. Chạy Plugin (Isolated ALC)
//            // Payload lúc này là JSON String đã được Engine resolve biến {{...}}
//            var result = await _pluginLoader.ExecutePluginAsync(
//                dllPath,
//                cmd.Payload,
//                context.CancellationToken);

//            // =================================================================
//            // 4. PERSISTENCE FIRST [ADD 7.2.2]
//            // =================================================================
//            if (result.IsSuccess)
//            {
//                // Serialize kết quả ra JsonDocument
//                using var outputDoc = JsonSerializer.SerializeToDocument(result.Outputs);

//                // A. Ghi xuống DB trước
//                pointer.Complete(_workerId, outputDoc);

//                await context.Publish(new StepCompletedEvent(
//                      WorkflowInstanceId: cmd.InstanceId,
//                      ExecutionPointerId: cmd.ExecutionPointerId,
//                      StepId: cmd.NodeId,
//                      Output: outputDoc, // Gửi kèm để Engine tối ưu (đỡ query lại)
//                      CompletedAt: DateTime.UtcNow
//                ));

//                await _uow.SaveChangesAsync(); // POINT OF NO RETURN

//                _logger.LogInformation("✅ [{Worker}] Step {Node} Persisted.", _workerId, cmd.NodeId);

//            }
//            else
//            {
//                // Xử lý lỗi nghiệp vụ (Plugin trả về false)
//                var errorDoc = JsonDocument.Parse($"{{\"Error\": \"{result.ErrorMessage}\"}}");
//                pointer.MarkAsFailed(_workerId, errorDoc);

//                await _uow.SaveChangesAsync();

//                await context.Publish(new StepFailedEvent(
//                    InstanceId: cmd.InstanceId,
//                    StepId: cmd.NodeId,
//                    ErrorMessage: result.ErrorMessage ?? "Unknown Plugin Error"
//                ));
//            }
//        }
//        catch (Exception ex)
//        {
//            // Lỗi hệ thống (Crash, Mất mạng, Lỗi code Worker...)
//            _logger.LogError(ex, "🔥 [{Worker}] System Crash on Step {Node}", _workerId, cmd.NodeId);

//            await HandleFailureAsync(pointer, cmd, $"System Error: {ex.Message}", context);
//            // MassTransit sẽ tự Retry nếu throw exception này (Transient Fault)
//            // Nếu retry hết số lần -> Pointer sẽ treo ở trạng thái Running (Zombie) -> Recovery Job sẽ xử lý tuần sau
//            throw;
//        }
//    }

//    private async Task HandleFailureAsync(
//    ExecutionPointer pointer,
//    ExecutePluginCommand cmd,
//    string errorMessage,
//    ConsumeContext context)
//    {
//        var errorDoc = JsonDocument.Parse($"{{\"Error\": \"{errorMessage}\"}}");

//        // Cập nhật DB: Running -> Failed
//        pointer.MarkAsFailed(_workerId, errorDoc);
//        await _uow.SaveChangesAsync();

//        // Bắn Event báo lỗi về Engine
//        await context.Publish(new StepFailedEvent(
//            InstanceId: cmd.InstanceId,
//            StepId: cmd.NodeId,
//            ErrorMessage: errorMessage
//        ));

//        _logger.LogInformation("❌ [{Worker}] Step {Node} marked as FAILED in DB.", _workerId, cmd.NodeId);
//    }

//    /// <summary>
//    /// Helper tải DLL từ Storage Service (MinIO) về Local Cache
//    /// </summary>
//    private async Task EnsurePluginDownloadedAsync(string stepType, string localPath, CancellationToken ct)
//    {
//        if (File.Exists(localPath)) return;

//        _logger.LogInformation("📥 Downloading plugin {Type}...", stepType);

//        // Quy ước đường dẫn trên MinIO: plugins/{Type}/latest.dll
//        // (Trong thực tế nên dùng VersionID từ Command)
//        string objectKey = $"plugins/{stepType}/latest.dll";
//        string bucket = "awe-plugins";

//        try
//        {
//            // Dùng FileShare.None để lock file đang ghi, tránh 2 thread ghi cùng lúc
//            using var remoteStream = await _storageService.GetObjectAsync(bucket, objectKey, ct);
//            using var localStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);

//            await remoteStream.CopyToAsync(localStream, ct);
//        }
//        catch (Exception ex)
//        {
//            // Nếu lỗi download, xóa file rác nếu có
//            if (File.Exists(localPath)) File.Delete(localPath);
//            throw new Exception($"Failed to download plugin {stepType}: {ex.Message}", ex);
//        }
//    }
//}
