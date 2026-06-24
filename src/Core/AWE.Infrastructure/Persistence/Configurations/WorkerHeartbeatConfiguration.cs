using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

public class WorkerHeartbeatConfiguration : IEntityTypeConfiguration<WorkerHeartbeat>
{
    public void Configure(EntityTypeBuilder<WorkerHeartbeat> builder)
    {
        builder.ToTable("WorkerHeartbeat");

        builder.HasKey(x => x.WorkerId);

        builder.Property(x => x.WorkerId)
            .HasMaxLength(160)
            .IsRequired();

        builder.Property(x => x.WorkerType)
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(x => x.MachineName)
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.ProcessId)
            .IsRequired();

        builder.Property(x => x.StartedAtUtc)
            .IsRequired();

        builder.Property(x => x.LastSeenAtUtc)
            .IsRequired();

        builder.HasIndex(x => x.LastSeenAtUtc)
            .HasDatabaseName("ix_worker_heartbeat_last_seen");

        builder.HasIndex(x => new { x.WorkerType, x.LastSeenAtUtc })
            .HasDatabaseName("ix_worker_heartbeat_type_last_seen");
    }
}
