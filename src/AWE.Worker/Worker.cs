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

    // Worker ID
    private readonly string _workerId = $"Worker-{Environment.MachineName}-{Guid.NewGuid().ToString()[..4]}";

    public PluginConsumer(
        ILogger<PluginConsumer> logger,
        IExecutionPointerRepository pointerRepo,
        IUnitOfWork uow)
    {
        _logger = logger;
        _pointerRepo = pointerRepo;
        _uow = uow;
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
            using var outputDoc = JsonSerializer.SerializeToDocument(outputs);

            // Cập nhật DB
            pointer.Complete(_workerId, outputDoc);
            await _uow.SaveChangesAsync();

            // Bắn Event báo xong
            await context.Publish(new StepCompletedEvent(
                  WorkflowInstanceId: cmd.InstanceId,
                  ExecutionPointerId: cmd.ExecutionPointerId,
                  StepId: cmd.NodeId,
                  Output: outputDoc,
                  CompletedAt: DateTime.UtcNow
            ));

            _logger.LogInformation("✅ [{Worker}] Step {Node} Completed.", _workerId, cmd.NodeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🔥 [{Worker}] Error on Step {Node}", _workerId, cmd.NodeId);
            await HandleFailureAsync(pointer, cmd, ex.Message, context);

            // Re-throw để MassTransit quản lý Retry (nếu cần)
            // throw; 
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
