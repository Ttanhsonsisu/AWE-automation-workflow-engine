using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class WorkflowDefinitionRepository(ApplicationDbContext _context) : IWorkflowDefinitionRepository
{
    public async Task<WorkflowDefinition?> GetDefinitionByIdAsync(
       Guid id,
       CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<WorkflowDefinition?> GetDefinitionByNameAndVersionAsync(
        string name,
        int version,
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowDefinitions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Name == name && x.Version == version, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetAllDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowDefinitions
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .ThenByDescending(x => x.Version)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowDefinition>> GetPublishedDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.WorkflowDefinitions
            .AsNoTracking()
            .Where(x => x.IsPublished)
            .OrderBy(x => x.Name)
            .ThenByDescending(x => x.Version)
            .ToListAsync(cancellationToken);
    }

    public async Task AddDefinitionAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        await _context.WorkflowDefinitions.AddAsync(definition, cancellationToken);
    }

    public Task UpdateDefinitionAsync(
        WorkflowDefinition definition,
        CancellationToken cancellationToken = default)
    {
        _context.WorkflowDefinitions.Update(definition);
        return Task.CompletedTask;
    }
}
