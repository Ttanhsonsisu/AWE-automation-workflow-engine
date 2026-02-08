using System;
using System.Collections.Generic;
using System.Text;
using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class WorkflowInstanceRepository(ApplicationDbContext _context) : IWorkflowInstanceRepository
{
    public async Task<WorkflowInstance?> GetInstanceByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<WorkflowInstance?> GetInstanceWithPointersAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowInstances
            .Include(x => x.ExecutionPointers.Where(p => p.Active))
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowInstance>> GetInstancesByStatusAsync(
        WorkflowInstanceStatus status,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowInstances
            .AsNoTracking()
            .Where(x => x.Status == status)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task AddInstanceAsync(
        WorkflowInstance instance,
        CancellationToken cancellationToken = default)
    {
        await _context.WorkflowInstances.AddAsync(instance, cancellationToken);
    }

    public Task UpdateInstanceAsync(
        WorkflowInstance instance,
        CancellationToken cancellationToken = default)
    {
        _context.WorkflowInstances.Update(instance);
        return Task.CompletedTask;
    }
}
