using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

public class WorkflowSchedulerSyncTaskConfiguration : IEntityTypeConfiguration<WorkflowSchedulerSyncTask>
{
    public void Configure(EntityTypeBuilder<WorkflowSchedulerSyncTask> builder)
    {
        builder.ToTable("WorkflowSchedulerSyncTask");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DefinitionId)
            .IsRequired();

        builder.Property(x => x.Operation)
            .IsRequired();

        builder.Property(x => x.RetryCount)
            .IsRequired();

        builder.Property(x => x.NextAttemptAtUtc)
            .IsRequired();

        builder.Property(x => x.IsCompleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.LastError)
            .HasMaxLength(2000);

        builder.HasIndex(x => new { x.IsCompleted, x.NextAttemptAtUtc })
            .HasDatabaseName("ix_scheduler_sync_task_due");

        builder.HasIndex(x => new { x.DefinitionId, x.Operation, x.IsCompleted })
            .HasDatabaseName("ix_scheduler_sync_task_pending_by_def");
    }
}
