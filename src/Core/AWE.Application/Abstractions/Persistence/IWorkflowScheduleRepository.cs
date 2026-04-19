using AWE.Domain.Entities;

namespace AWE.Application.Abstractions.Persistence;

public interface IWorkflowScheduleRepository
{
    // add new schedule
    public Task AddWorkflowScheduleAsync(WorkflowSchedule schedule, CancellationToken cancellation = default);

    public Task<IReadOnlyList<WorkflowSchedule>> GetActiveSchedulesAsync(CancellationToken cancellation = default);

    public Task<IReadOnlyList<WorkflowSchedule>> GetActiveSchedulesByDefinitionIdAsync(Guid definitionId, CancellationToken cancellation = default);

}
