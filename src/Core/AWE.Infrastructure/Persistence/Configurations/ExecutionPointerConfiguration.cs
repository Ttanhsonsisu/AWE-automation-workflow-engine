using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for ExecutionPointer entity
/// HOT TABLE - Performance-critical indexes included
/// </summary>
public class ExecutionPointerConfiguration : IEntityTypeConfiguration<ExecutionPointer>
{
    public void Configure(EntityTypeBuilder<ExecutionPointer> builder)
    {
        // Sử dụng số nhiều theo chuẩn Database conventions
        builder.ToTable("ExecutionPointers");

        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        // --- Foreign Keys & Navigation ---
        builder.Property(x => x.InstanceId).IsRequired();

        // --- Identity & Structure ---
        builder.Property(x => x.StepId)
            .HasMaxLength(100)
            .IsRequired();

        // [NEW] Cấu hình cho Fork/Join
        builder.Property(x => x.BranchId)
            .HasMaxLength(100)
            .HasDefaultValue("ROOT") // Mặc định nhánh chính
            .IsRequired();

        builder.Property(x => x.ParentTokenId)
            .IsRequired(false); // Root pointer không có cha

        builder.Property(x => x.PredecessorId)
            .IsRequired(false);

        // --- Status & State ---
        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>() // Lưu Enum dưới dạng string cho dễ đọc
            .IsRequired();

        builder.Property(x => x.Active)
            .IsRequired();

        builder.Property(x => x.RetryCount)
            .HasDefaultValue(0)
            .IsRequired();

        // --- Leasing (Concurrency Control) ---
        builder.Property(x => x.LeasedUntil)
            .IsRequired(false);

        builder.Property(x => x.LeasedBy)
            .HasMaxLength(100)
            .IsRequired(false);

        // --- JSONB Data Columns (PostgreSQL Specific) ---

        // Context cục bộ của Token (Scope variables)
        builder.Property(x => x.Scope)
            .HasColumnType("jsonb")
            .IsRequired();

        // [UPDATED] Đổi tên từ StepContext -> Output
        // Lưu kết quả đầu ra của Plugin
        builder.Property(x => x.Output)
            .HasColumnType("jsonb")
            .IsRequired(false);

        builder.Property(x => x.InputData)
            .HasColumnType("jsonb")
            .IsRequired(false);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.StartTime)
            .IsRequired(false);

        // =====================================================================
        // PERFORMANCE INDEXES (CRITICAL)
        // =====================================================================

        // 1. Zombie Detection Index (Recovery Job)
        // Query: WHERE Status = 'Running' AND LeasedUntil < NOW()
        builder.HasIndex(x => new { x.Status, x.LeasedUntil })
            .HasDatabaseName("ix_execution_pointers_zombie")
            .HasFilter("\"status\" = 'Running'"); // Partial Index cho PostgreSQL

        // 2. Join Barrier Index (Atomic Join - Tuần 5)
        // Query: Tìm tất cả token con của cùng 1 cha tại node Join
        // WHERE InstanceId = ... AND ParentTokenId = ... AND StepId = ...
        builder.HasIndex(x => new { x.InstanceId, x.ParentTokenId, x.StepId })
            .HasDatabaseName("ix_execution_pointers_join_barrier");

        // 3. Active Pointers per Instance (UI/Debug)
        // Query: Lấy trạng thái hiện tại của Workflow
        builder.HasIndex(x => new { x.InstanceId, x.Active })
            .HasDatabaseName("ix_execution_pointers_instance_active");

        // 4. Cleanup/Partitioning Index
        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_execution_pointers_created_at");

        // --- Relationships ---
        builder.HasOne(x => x.Instance)
            .WithMany(x => x.ExecutionPointers)
            .HasForeignKey(x => x.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.ExecutionLogs)
            .WithOne(x => x.ExecutionPointer)
            .HasForeignKey(x => x.ExecutionPointerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
