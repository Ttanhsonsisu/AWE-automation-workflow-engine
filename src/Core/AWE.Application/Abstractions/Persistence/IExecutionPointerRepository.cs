using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.Persistence;

public interface IExecutionPointerRepository
{
    Task<ExecutionPointer?> GetPointerByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get pending pointers that are ready for execution (polling query)
    /// Includes lease expiry check for zombie recovery
    /// </summary>
    Task<IReadOnlyList<ExecutionPointer>> GetPendingPointersAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get zombie pointers (Running but lease expired)
    /// </summary>
    Task<IReadOnlyList<ExecutionPointer>> GetZombiePointersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get active pointers for a specific instance
    /// </summary>
    Task<IReadOnlyList<ExecutionPointer>> GetActivePointersByInstanceAsync(Guid instanceId, CancellationToken cancellationToken = default);

    Task AddPointerAsync(ExecutionPointer pointer, CancellationToken cancellationToken = default);
    Task UpdatePointerAsync(ExecutionPointer pointer, CancellationToken cancellationToken = default);

    // For Worker: Gia hạn thời gian sống
    Task<bool> RenewLeaseAsync(Guid pointerId, string workerId, TimeSpan extension, CancellationToken ct = default);

    // for cho Engine (Recovery): Tìm các Pointer đã chết (Zombie)
    Task<List<ExecutionPointer>> GetExpiredPointersAsync(DateTime utcNow, int count, CancellationToken ct = default);

    // Dành cho Engine (Recovery): Reset Pointer về Pending
    Task<int> ResetRawPointersAsync(List<Guid> pointerIds, CancellationToken ct = default);

    // Đếm số lượng pointer đã đến nút Join (bao gồm Pending, Completed, Skipped)
    Task<int> CountArrivedPointersByStepIdAsync(Guid instanceId, string stepId);

    // Lấy danh sách các pointer đang tụ tập ở nút Join
    Task<List<ExecutionPointer>> GetPointersByStepIdAsync(Guid instanceId, string stepId);

    // Lấy danh sách các pointer đã hoàn thành ở nút Join (để Engine quyết định có đủ điều kiện đi tiếp hay không)
    Task<List<ExecutionPointer>> GetCompletedPointersByInstanceIdAsync(Guid instanceId);
}
