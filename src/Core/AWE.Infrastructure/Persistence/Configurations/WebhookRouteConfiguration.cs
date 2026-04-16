using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AWE.Infrastructure.Persistence.Configurations;

public class WebhookRouteConfiguration : IEntityTypeConfiguration<WebhookRoute>
{
    public void Configure(EntityTypeBuilder<WebhookRoute> builder)
    {
        builder.ToTable("WebhookRoutes");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RoutePath)
            .IsRequired()
            .HasMaxLength(255);

        // Unique Index cực kỳ quan trọng để đảm bảo 1 RoutePath chỉ map với 1 luồng active
        builder.HasIndex(x => x.RoutePath)
            .IsUnique();

        builder.Property(x => x.WorkflowDefinitionId)
            .IsRequired();
    }
}
