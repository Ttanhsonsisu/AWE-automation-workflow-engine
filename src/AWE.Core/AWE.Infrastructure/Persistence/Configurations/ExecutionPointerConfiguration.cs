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
        builder.ToTable("ExecutionPointer");

        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        // Properties
        builder.Property(x => x.InstanceId)
            .IsRequired();

        builder.Property(x => x.StepId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.Active)
            .IsRequired();

        builder.Property(x => x.LeasedUntil);

        builder.Property(x => x.LeasedBy)
            
            .HasMaxLength(100);

        builder.Property(x => x.RetryCount)
            
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(x => x.PredecessorId);

        builder.Property(x => x.Scope)
            .HasColumnType("jsonb")
            .IsRequired();
        
        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.StepContext)
            .HasColumnType("jsonb");

        // CRITICAL INDEX: Worker polling query
        // Find pending work: Status=Pending AND (LeasedUntil IS NULL OR LeasedUntil < NOW())
        builder.HasIndex(x => new { x.Status, x.LeasedUntil, x.CreatedAt })
            .HasDatabaseName("ix_execution_pointers_polling")
            .HasFilter("active = true");

        // Index for zombie detection
        builder.HasIndex(x => new { x.Status, x.LeasedUntil })
            .HasDatabaseName("ix_execution_pointers_zombie")
            .HasFilter("status = 'Running'");

        // Index for instance queries
        builder.HasIndex(x => new { x.InstanceId, x.Active })
            .HasDatabaseName("ix_execution_pointers_instance_active");

        // Index for step tracking
        builder.HasIndex(x => new { x.InstanceId, x.StepId, x.Status })
            .HasDatabaseName("ix_execution_pointers_instance_step_status");

        // Partitioning support index (for future monthly partitioning)
        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_execution_pointers_created_at");

        // Relationships
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
