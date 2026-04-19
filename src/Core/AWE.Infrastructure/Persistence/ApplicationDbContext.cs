using System.Text.Json;
using AWE.Domain.Entities;
using AWE.Infrastructure.Extensions;
using AWE.Infrastructure.Persistence.Interceptors;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AWE.Infrastructure.Persistence;

/// <summary>
/// Main database context for workflow engine
/// Configured for PostgreSQL with JSONB support
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{

    // Define Dbset
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowInstance> WorkflowInstances => Set<WorkflowInstance>();
    public DbSet<ExecutionPointer> ExecutionPointers => Set<ExecutionPointer>();
    public DbSet<JoinBarrier> JoinBarriers => Set<JoinBarrier>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ExecutionLog> ExecutionLogs => Set<ExecutionLog>();
    public DbSet<PluginPackage> PluginPackages => Set<PluginPackage>();
    public DbSet<PluginVersion> PluginVersions => Set<PluginVersion>();
    public DbSet<WorkflowSchedule> WorkflowSchedules => Set<WorkflowSchedule>();
    public DbSet<SystemAuditLog> SystemAuditLogs => Set<SystemAuditLog>();
    public DbSet<ApprovalToken> ApprovalTokens => Set<ApprovalToken>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<WorkflowSchedulerSyncTask> WorkflowSchedulerSyncTasks => Set<WorkflowSchedulerSyncTask>();

    public DbSet<WebhookRoute> WebhookRoutes => Set<WebhookRoute>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        ConfigureJsonDocuments(modelBuilder);
        // MASSTRANSIT OUTBOX MAPPING 
        // Auto create table OutboxMessage, OutboxState, InboxState in database
        modelBuilder.AddInboxStateEntity(cfg => cfg.ToTable("InboxState"));
        modelBuilder.AddOutboxMessageEntity(cfg => cfg.ToTable("OutboxMessage"));
        modelBuilder.AddOutboxStateEntity(cfg => cfg.ToTable("OutboxState"));

        modelBuilder.ApplySnakeCaseColumnNames();

    }

    private static void ConfigureJsonDocuments(ModelBuilder modelBuilder)
    {
        // Configure all JsonDocument properties to use JSONB column type in PostgreSQL
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(JsonDocument))
                {
                    property.SetColumnType("jsonb");
                }
            }
        }
    }

    /// <summary>
    /// Override SaveChanges to automatically set audit fields
    /// </summary>
    public override int SaveChanges()
    {
        UpdateAuditFields();
        return base.SaveChanges();
    }

    /// <summary>
    /// Override SaveChangesAsync to automatically set audit fields
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateAuditFields()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is Domain.Common.AuditableEntity &&
                       (e.State == EntityState.Modified));

        foreach (var entry in entries)
        {
            if (entry.Entity is Domain.Common.AuditableEntity auditableEntity)
            {
                auditableEntity.MarkAsUpdated();
            }
        }
    }
}
