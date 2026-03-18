using System;
using System.Collections.Generic;
using System.Text;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

public class WorkflowScheduleConfiguration : IEntityTypeConfiguration<WorkflowSchedule>
{
    public void Configure(EntityTypeBuilder<WorkflowSchedule> builder)
    {
        builder.ToTable("WorkflowSchedule");

        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedOnAdd();

        // Properties
        builder.Property(x => x.DefinitionId)
            .IsRequired();

        builder.Property(x => x.CronExpression)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.LastRunAt);

        builder.Property(x => x.NextRunAt);

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.Version)
            .IsConcurrencyToken()
            .IsRequired();

        // Indexes
        builder.HasIndex(x => new { x.IsActive, x.NextRunAt })
            .HasDatabaseName("ix_workflow_schedule_active_next_run");

        // 2. Index để tìm kiếm nhanh tất cả lịch của một Workflow cụ thể
        builder.HasIndex(x => x.DefinitionId)
            .HasDatabaseName("ix_workflow_schedule_definition_id");

        // Relationships
        builder.HasOne(x => x.Definition)
            .WithMany() 
            .HasForeignKey(x => x.DefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
