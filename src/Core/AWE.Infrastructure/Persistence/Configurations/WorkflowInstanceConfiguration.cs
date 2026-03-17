using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for WorkflowInstance entity
/// </summary>
public class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("WorkflowInstance");

        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        // Properties
        builder.Property(x => x.DefinitionId)
            .IsRequired();

        builder.Property(x => x.DefinitionVersion)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(20)
            .HasConversion<string>() // Store enum as string
            .IsRequired();

        builder.Property(x => x.ContextData)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.StartTime)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.LastUpdated);

        // Indexes
        builder.HasIndex(x => x.DefinitionId)
            .HasDatabaseName("ix_workflow_instances_definition_id");

        builder.HasIndex(x => x.Status)
            .HasDatabaseName("ix_workflow_instances_status");

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_workflow_instances_created_at");

        builder.HasIndex(x => new { x.Status, x.CreatedAt })
            .HasDatabaseName("ix_workflow_instances_status_created_at");

        // Relationships
        builder.HasOne(x => x.Definition)
            .WithMany(x => x.Instances)
            .HasForeignKey(x => x.DefinitionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.ExecutionPointers)
            .WithOne(x => x.Instance)
            .HasForeignKey(x => x.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.JoinBarriers)
            .WithOne(x => x.Instance)
            .HasForeignKey(x => x.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.ExecutionLogs)
            .WithOne(x => x.Instance)
            .HasForeignKey(x => x.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
