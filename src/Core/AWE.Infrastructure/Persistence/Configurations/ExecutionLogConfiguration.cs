using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for ExecutionLog entity
/// Append-only audit table
/// </summary>
public class ExecutionLogConfiguration : IEntityTypeConfiguration<ExecutionLog>
{
    public void Configure(EntityTypeBuilder<ExecutionLog> builder)
    {
        builder.ToTable("ExecutionLog");

        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        // Properties
        builder.Property(x => x.InstanceId)
            .IsRequired();

        builder.Property(x => x.Level)
            .HasMaxLength(20)
            .HasConversion<string>()
            .IsRequired();

        builder.Property(x => x.NodeId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Event)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Metadata)
            .HasColumnType("jsonb");

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        // Indexes
        builder.HasIndex(x => x.InstanceId)
            .HasDatabaseName("ix_execution_logs_instance_id");

        builder.HasIndex(x => new { x.InstanceId, x.CreatedAt })
            .HasDatabaseName("ix_execution_logs_instance_created");

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_execution_logs_created_at");

        // Relationships
        builder.HasOne(x => x.Instance)
            .WithMany(x => x.ExecutionLogs)
            .HasForeignKey(x => x.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ExecutionPointer)
            .WithMany(x => x.ExecutionLogs)
            .HasForeignKey(x => x.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
