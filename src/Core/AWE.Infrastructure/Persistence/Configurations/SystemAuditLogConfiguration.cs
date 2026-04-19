using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

public class SystemAuditLogConfiguration : IEntityTypeConfiguration<SystemAuditLog>
{
    public void Configure(EntityTypeBuilder<SystemAuditLog> builder)
    {
        // Sử dụng số nhiều theo chuẩn Database conventions
        builder.ToTable("SystemAuditLogs");

        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // --- Các thuộc tính ---
        builder.Property(x => x.UserId)
            .HasMaxLength(100)
            .IsRequired(false);  // Có thể là null

        builder.Property(x => x.UserName)
            .HasMaxLength(200)
            .IsRequired(false);  // Có thể là null

        builder.Property(x => x.TableName)
            .HasMaxLength(200)
            .IsRequired();  // Bắt buộc phải có

        builder.Property(x => x.Action)
            .HasMaxLength(50)
            .IsRequired();  // Bắt buộc phải có

        builder.Property(x => x.RecordId)
            .HasMaxLength(200)
            .IsRequired();  // Bắt buộc phải có

        // Cấu hình trường OldValues (jsonb) và NewValues (jsonb)
        builder.Property(x => x.OldValues)
            .HasColumnType("jsonb")
            .IsRequired(false);  // Có thể là null

        builder.Property(x => x.NewValues)
            .HasColumnType("jsonb")
            .IsRequired(false);  // Có thể là null

        builder.Property(x => x.CreatedAt)
            .IsRequired();  // CreatedAt sẽ tự động được gán khi khởi tạo đối tượng

        builder.Property(x => x.LastUpdated)
            .IsRequired(false);  // LastUpdated chỉ có giá trị khi gọi MarkAsUpdated

        // =======================================================================
        // Các chỉ mục nếu cần thiết
        // =======================================================================

        // Index for CreatedAt
        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_system_audit_logs_created_at");

        // Index for TableName and RecordId (để tìm kiếm hiệu quả hơn)
        builder.HasIndex(x => new { x.TableName, x.RecordId })
            .HasDatabaseName("ix_system_audit_logs_table_record");
    }
}
