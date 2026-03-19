using System.Text.Json;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AWE.Infrastructure.Persistence.Interceptors;

public class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context == null) return base.SavingChangesAsync(eventData, result, cancellationToken);

        var auditEntries = new List<SystemAuditLog>();

        // Lặp qua tất cả các Entity đang bị thay đổi trên RAM
        foreach (var entry in context.ChangeTracker.Entries())
        {
            // Bỏ qua nếu đó là Entity Audit (chống lặp vô hạn) hoặc Entity không có thay đổi
            if (entry.Entity is SystemAuditLog || entry.Entity is ExecutionLog ||
                entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
            {
                continue;
            }

            var auditEntry = new SystemAuditLog
            {
                TableName = entry.Metadata.GetTableName() ?? entry.Entity.GetType().Name,
                Action = entry.State.ToString(),
                UserId = "System", // TODO: Lấy từ HttpContext khi lắp Keycloak
                UserName = "Admin" // TODO: Lấy từ HttpContext
            };

            // Tìm khóa chính (Primary Key) của bảng đang bị sửa
            var primaryKey = entry.Properties.FirstOrDefault(p => p.Metadata.IsPrimaryKey());
            auditEntry.RecordId = primaryKey?.CurrentValue?.ToString() ?? "N/A";

            var oldValues = new Dictionary<string, object?>();
            var newValues = new Dictionary<string, object?>();

            // Quét từng cột dữ liệu để so sánh
            foreach (var property in entry.Properties)
            {
                if (property.IsTemporary) continue;
                string propertyName = property.Metadata.Name;

                switch (entry.State)
                {
                    case EntityState.Added:
                        newValues[propertyName] = property.CurrentValue;
                        break;
                    case EntityState.Deleted:
                        oldValues[propertyName] = property.OriginalValue;
                        break;
                    case EntityState.Modified:
                        if (property.IsModified) 
                        {
                            oldValues[propertyName] = property.OriginalValue;
                            newValues[propertyName] = property.CurrentValue;
                        }
                        break;
                }
            }

            if (oldValues.Count > 0) auditEntry.OldValues = JsonSerializer.Serialize(oldValues);
            if (newValues.Count > 0) auditEntry.NewValues = JsonSerializer.Serialize(newValues);

            auditEntries.Add(auditEntry);
        }

        // Thêm danh sách Audit vào context để lưu chung trong cùng 1 Transaction
        if (auditEntries.Any())
        {
            context.Set<SystemAuditLog>().AddRange(auditEntries);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
