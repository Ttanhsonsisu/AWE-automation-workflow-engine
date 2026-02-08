using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.ToTable("WorkflowDefinition");

        // Primary Key
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .ValueGeneratedNever(); // We generate GUIDs in domain

        // Properties
        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Version)
            .IsRequired();

        builder.Property(x => x.DefinitionJson)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(x => x.IsPublished)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.LastUpdated);

        // Indexes
        builder.HasIndex(x => new { x.Name, x.Version })
            .IsUnique()
            .HasDatabaseName("ix_workflow_definitions_name_version");

        builder.HasIndex(x => x.IsPublished)
            .HasDatabaseName("ix_workflow_definitions_is_published");

        builder.HasIndex(x => x.CreatedAt)
            .HasDatabaseName("ix_workflow_definitions_created_at");

        // Relationships
        builder.HasMany(x => x.Instances)
            .WithOne(x => x.Definition)
            .HasForeignKey(x => x.DefinitionId)
            .OnDelete(DeleteBehavior.Restrict); // Prevent deletion if instances exist
    }
}
