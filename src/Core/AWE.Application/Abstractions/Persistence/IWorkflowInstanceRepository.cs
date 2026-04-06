using AWE.Domain.Entities;
using AWE.Domain.Enums;

namespace AWE.Application.Abstractions.Persistence;

public interface IWorkflowInstanceRepository
{
    Task<WorkflowInstance?> GetInstanceByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowInstanceStatus?> GetInstanceStatusAsync(Guid id, CancellationToken cancellationToken = default);
    Task<WorkflowInstance?> GetInstanceWithPointersAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowInstance>> GetInstancesByStatusAsync(WorkflowInstanceStatus status, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowInstance>> GetInstancesAsync(
        int page = 1,
        int size = 10,
        IReadOnlyCollection<Guid>? definitionIds = null,
        WorkflowInstanceStatus? status = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        CancellationToken cancellationToken = default);
    Task<long> CountInstancesAsync(
        IReadOnlyCollection<Guid>? definitionIds = null,
        WorkflowInstanceStatus? status = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WorkflowInstance>> GetChildInstancesAsync(
        IReadOnlyCollection<Guid> parentInstanceIds,
        CancellationToken cancellationToken = default);
    Task AddInstanceAsync(WorkflowInstance instance, CancellationToken cancellationToken = default);
    Task UpdateInstanceAsync(WorkflowInstance instance, CancellationToken cancellationToken = default);
}
