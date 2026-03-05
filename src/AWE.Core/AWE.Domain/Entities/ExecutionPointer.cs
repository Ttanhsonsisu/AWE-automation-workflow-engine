using AWE.Domain.Common;
using AWE.Domain.Enums;
using System.Text.Json;

namespace AWE.Domain.Entities;

public class ExecutionPointer : Entity
{
    public Guid InstanceId { get; private set; }
    public string StepId { get; private set; } = string.Empty;

    // --- Token Identity (Cho Atomic Join sau này) ---
    public Guid? ParentTokenId { get; private set; }
    public string BranchId { get; private set; }

    public ExecutionPointerStatus Status { get; set; }
    public bool Active { get; private set; }

    // --- Leasing (Zombie Detection) ---
    public DateTime? LeasedUntil { get; private set; }
    public string? LeasedBy { get; private set; }

    public int RetryCount { get; private set; }
    public Guid? PredecessorId { get; private set; }
    public JsonDocument Scope { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // [CHANGE] Đổi tên StepContext -> Output cho đúng ngữ nghĩa "Kết quả trả về"
    public JsonDocument? Output { get; private set; }

    public virtual WorkflowInstance Instance { get; private set; } = null!;
    public virtual ICollection<ExecutionLog> ExecutionLogs { get; private set; } = new List<ExecutionLog>();

    private ExecutionPointer() { }

    public ExecutionPointer(
        Guid instanceId,
        string stepId,
        Guid? predecessorId = null,
        JsonDocument? scope = null,
        Guid? parentTokenId = null,
        string branchId = "ROOT")
    {
        if (string.IsNullOrWhiteSpace(stepId))
            throw new ArgumentException("Step ID cannot be empty", nameof(stepId));

        InstanceId = instanceId;
        StepId = stepId;
        ParentTokenId = parentTokenId;
        BranchId = branchId;
        PredecessorId = predecessorId;

        Status = ExecutionPointerStatus.Pending; // Mặc định là Pending đợi Worker nhận
        Active = true;
        RetryCount = 0;
        Scope = scope ?? JsonDocument.Parse("[]");
        CreatedAt = DateTime.UtcNow;
    }

    // --- State Machine & Leasing Logic ---

    public bool TryAcquireLease(string workerId, TimeSpan leaseDuration)
    {
        if (string.IsNullOrWhiteSpace(workerId))
            throw new ArgumentException("Worker ID required");

        var now = DateTime.UtcNow;

        // Case 1: Nhận việc mới
        if (Status == ExecutionPointerStatus.Pending)
        {
            Status = ExecutionPointerStatus.Running;
            LeasedUntil = now.Add(leaseDuration);
            LeasedBy = workerId;
            return true;
        }

        // Case 2: Cướp lại việc của Zombie (Worker cũ bị chết)
        if (Status == ExecutionPointerStatus.Running && LeasedUntil.HasValue && LeasedUntil.Value < now)
        {
            LeasedUntil = now.Add(leaseDuration);
            LeasedBy = workerId;
            RetryCount++; // Tính là 1 lần retry
            return true;
        }

        return false;
    }

    public void RenewLease(string workerId, TimeSpan leaseDuration)
    {
        ValidateLeaseOwnership(workerId);
        LeasedUntil = DateTime.UtcNow.Add(leaseDuration);
    }

    public void Complete(string workerId, JsonDocument? output)
    {
        ValidateLeaseOwnership(workerId);

        Status = ExecutionPointerStatus.Completed;
        Active = false; // Đánh dấu pointer này đã xong nhiệm vụ

        // Clear Lease để không ai pickup nhầm nữa
        LeasedUntil = null;
        LeasedBy = null;

        // [CHANGE] Lưu output
        Output = output;
    }

    public void MarkAsFailed(string workerId, JsonDocument? errorContext)
    {
        ValidateLeaseOwnership(workerId);

        Status = ExecutionPointerStatus.Failed;
        Active = false;
        LeasedUntil = null;
        LeasedBy = null;

        // Có thể lưu lỗi vào Output hoặc một cột ErrorData riêng (Ở đây tạm lưu vào Output)
        Output = errorContext;
    }

    public void Skip()
    {
        Status = ExecutionPointerStatus.Skipped;
        Active = false;
        LeasedUntil = null;
        LeasedBy = null;
    }

    public void ResetToPending()
    {
        if (Status == ExecutionPointerStatus.Completed || Status == ExecutionPointerStatus.Skipped)
            throw new InvalidOperationException($"Cannot reset terminal state: {Status}");

        Status = ExecutionPointerStatus.Pending;
        LeasedUntil = null;
        LeasedBy = null;
        RetryCount++;
    }

    public bool IsZombie()
    {
        return Status == ExecutionPointerStatus.Running
            && LeasedUntil.HasValue
            && LeasedUntil.Value < DateTime.UtcNow;
    }

    private void ValidateLeaseOwnership(string workerId)
    {
        // Logic kiểm tra rất chặt chẽ, tốt!
        if (LeasedBy != workerId)
            throw new InvalidOperationException($"Lease conflict: Held by {LeasedBy}, request by {workerId}");

        if (Status != ExecutionPointerStatus.Running)
            throw new InvalidOperationException($"Invalid status transition from {Status}");
    }

    /// <summary>
    /// Hàm dùng riêng cho việc đánh thức Node "Wait" từ API bên ngoài
    /// </summary>
    /// <param name="output"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void CompleteFromWait(JsonDocument? output)
    {
        if (Status != ExecutionPointerStatus.WaitingForEvent)
            throw new InvalidOperationException($"Cannot resume step from status: {Status}");

        Status = ExecutionPointerStatus.Completed;
        Active = false; // Đã xong nhiệm vụ
        Output = output; // Lưu data từ Webhook gửi vào
    }
}
