using System.Linq.Expressions;
using AWE.Application.UseCases.Monitor.Daskboard;
using AWE.Domain.Entities;
using AWE.Shared.Primitives;

namespace AWE.Application.Abstractions.Persistence;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetDefinitionByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetDefinitionByNameAndVersionAsync(string name, int version, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowDefinition>> GetAllDefinitionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowDefinition>> GetPublishedDefinitionsAsync(CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetLatestVersionByNameAsync(string name, CancellationToken cancellationToken = default);
    Task AddDefinitionAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default);
    Task UpdateDefinitionAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default);
    Task DeleteDefinitionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsDefinitionAsync(Guid id,CancellationToken cancellationToken = default);
    Task<long> CountAsync(Expression<Func<WorkflowDefinition, bool>>? predicate = null, CancellationToken cancellationToken = default);
    //DashboardMetrics
    Task<DashboardMetricsResponse> GetDashboardMetricsAsync(CancellationToken cancellationToken = default);
}
