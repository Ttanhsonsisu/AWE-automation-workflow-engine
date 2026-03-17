using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for JoinBarrier entity
/// </summary>
public class JoinBarrierConfiguration : IEntityTypeConfiguration<JoinBarrier>
{
    public void Configure(EntityTypeBuilder<JoinBarrier> builder)
    {
        builder.ToTable("JoinBarrier");

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

        builder.Property(x => x.RequiredCount)
            .IsRequired();

        builder.Property(x => x.ArrivedTokens)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.IsReleased)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        // Indexes
        // Unique constraint: one barrier per instance+step combination
        builder.HasIndex(x => new { x.InstanceId, x.StepId })
            .IsUnique()
            .HasDatabaseName("ix_join_barriers_instance_step");

        builder.HasIndex(x => x.IsReleased)
            .HasDatabaseName("ix_join_barriers_is_released");

        // Relationships
        builder.HasOne(x => x.Instance)
            .WithMany(x => x.JoinBarriers)
            .HasForeignKey(x => x.InstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
