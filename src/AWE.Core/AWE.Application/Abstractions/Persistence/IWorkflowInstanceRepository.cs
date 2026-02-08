using AWE.Domain.Entities;
using AWE.Domain.Enums;

namespace AWE.Application.Abstractions.Persistence;

public interface IWorkflowInstanceRepository
{
    Task<WorkflowInstance?> GetInstanceByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetInstanceWithPointersAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowInstance>> GetInstancesByStatusAsync(WorkflowInstanceStatus status, CancellationToken cancellationToken = default);
    Task AddInstanceAsync(WorkflowInstance instance, CancellationToken cancellationToken = default);
    Task UpdateInstanceAsync(WorkflowInstance instance, CancellationToken cancellationToken = default);
}
