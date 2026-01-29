using System;
using System.Collections.Generic;
using System.Text;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    // Declare db set here

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // MASSTRANSIT OUTBOX MAPPING 
        // Auto create table OutboxMessage, OutboxState, InboxState in database
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
