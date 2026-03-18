using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;

namespace AWE.Infrastructure.Persistence.Repositories;

public class WorkflowScheduleRepository(ApplicationDbContext context) : IWorkflowScheduleRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task AddWorkflowScheduleAsync(WorkflowSchedule schedule, CancellationToken cancellation = default)
    {
        await _context.WorkflowSchedules.AddAsync(schedule, cancellation);
    }
}
