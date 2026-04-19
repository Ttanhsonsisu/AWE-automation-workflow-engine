using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for PluginPackage entity
/// </summary>
public class PluginPackageConfiguration : IEntityTypeConfiguration<PluginPackage>
{
    public void Configure(EntityTypeBuilder<PluginPackage> builder)
    {
        builder.ToTable("PluginPackage");

        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        // Properties
        builder.Property(x => x.UniqueName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(1000);

        // Các cột UI Metadata cho Frontend
        builder.Property(x => x.Category)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Icon)
            .HasMaxLength(100)
            .IsRequired();

        // Kiểu chạy của Plugin (Nên lưu dạng chuỗi để dễ đọc trong DB, hoặc lưu số nguyên mặc định)
        builder.Property(x => x.ExecutionMode)
            .HasConversion<string>() // Lưu thành "DynamicDll", "RemoteGrpc" thay vì số 1, 2
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.LastUpdated);

        // Indexes
        builder.HasIndex(x => x.UniqueName)
            .IsUnique()
            .HasDatabaseName("ix_plugin_packages_unique_name");

        // Relationships
        builder.HasMany(x => x.Versions)
            .WithOne(x => x.Package)
            .HasForeignKey(x => x.PackageId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
