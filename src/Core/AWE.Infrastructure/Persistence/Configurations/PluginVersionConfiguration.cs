using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for PluginVersion entity
/// </summary>
public class PluginVersionConfiguration : IEntityTypeConfiguration<PluginVersion>
{
    public void Configure(EntityTypeBuilder<PluginVersion> builder)
    {
        builder.ToTable("PluginVersion");

        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        // Properties
        builder.Property(x => x.PackageId)
            .IsRequired();

        builder.Property(x => x.Version)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ObjectKey)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.Bucket)
           .HasMaxLength(100)
           .IsRequired();

        builder.Property(x => x.Sha256)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.StorageProvider)
            .HasMaxLength(50)
            .IsRequired(false);

        builder.Property(x => x.Size)
            .IsRequired();

        builder.Property(x => x.ConfigSchema)
            .HasColumnType("jsonb");

        builder.Property(x => x.IsActive)
            .IsRequired();

        builder.Property(x => x.ReleaseNotes)
            .HasMaxLength(2000);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.LastUpdated);

        // Indexes
        builder.HasIndex(x => new { x.PackageId, x.Version })
            .IsUnique()
            .HasDatabaseName("ix_plugin_versions_package_version");

        builder.HasIndex(x => x.IsActive)
            .HasDatabaseName("ix_plugin_versions_is_active");

        // Relationships
        builder.HasOne(x => x.Package)
            .WithMany(x => x.Versions)
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
