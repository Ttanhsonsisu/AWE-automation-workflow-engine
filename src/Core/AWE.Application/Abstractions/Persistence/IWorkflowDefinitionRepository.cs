using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.Persistence;

public interface IWorkflowDefinitionRepository
{
    Task<WorkflowDefinition?> GetDefinitionByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowDefinition?> GetDefinitionByNameAndVersionAsync(string name, int version, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowDefinition>> GetAllDefinitionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowDefinition>> GetPublishedDefinitionsAsync(CancellationToken cancellationToken = default);
    Task AddDefinitionAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default);
    Task UpdateDefinitionAsync(WorkflowDefinition definition, CancellationToken cancellationToken = default);
}
