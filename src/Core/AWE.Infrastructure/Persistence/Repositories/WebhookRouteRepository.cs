using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class WebhookRouteRepository(ApplicationDbContext context) : IWebhookRouteRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task<WebhookRoute?> GetByRoutePathAsync(string routePath, CancellationToken cancellationToken = default)
    {
        return await _context.WebhookRoutes
            .FirstOrDefaultAsync(x => x.RoutePath == routePath, cancellationToken);
    }

    public async Task<IReadOnlyList<WebhookRoute>> GetByWorkflowDefinitionIdAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default)
    {
        return await _context.WebhookRoutes
            .Where(x => x.WorkflowDefinitionId == workflowDefinitionId)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(WebhookRoute route, CancellationToken cancellationToken = default)
    {
        await _context.WebhookRoutes.AddAsync(route, cancellationToken);
    }

    public Task UpdateAsync(WebhookRoute route, CancellationToken cancellationToken = default)
    {
        if (_context.Entry(route).State == EntityState.Detached)
        {
            _context.WebhookRoutes.Update(route);
        }

        return Task.CompletedTask;
    }
}
