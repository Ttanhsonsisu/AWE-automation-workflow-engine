namespace AWE.Application.Abstractions.Persistence;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // 2. Quản lý Transaction thủ công (Dành cho logic phức tạp cần commit nhiều lần)
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    // 3. Resiliency (Chống lỗi mạng chập chờn)
    // Giúp chạy lại logic (Retry) nếu gặp lỗi kết nối Database tạm thời
    Task ExecuteTransactionalAsync(Func<Task> action, CancellationToken cancellationToken = default);
}
