using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.Persistence;

public interface IWebhookRouteRepository
{
    Task<WebhookRoute?> GetByRoutePathAsync(string routePath, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WebhookRoute>> GetByWorkflowDefinitionIdAsync(Guid workflowDefinitionId, CancellationToken cancellationToken = default);
    Task AddAsync(WebhookRoute route, CancellationToken cancellationToken = default);
    Task UpdateAsync(WebhookRoute route, CancellationToken cancellationToken = default);
}
