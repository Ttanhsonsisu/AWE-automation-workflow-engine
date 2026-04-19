using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AWE.Infrastructure.Persistence.Repositories;

public class WorkflowScheduleRepository(ApplicationDbContext context) : IWorkflowScheduleRepository
{
    private readonly ApplicationDbContext _context = context;

    public async Task AddWorkflowScheduleAsync(WorkflowSchedule schedule, CancellationToken cancellation = default)
    {
        await _context.WorkflowSchedules.AddAsync(schedule, cancellation);
    }

    public async Task<IReadOnlyList<WorkflowSchedule>> GetActiveSchedulesAsync(CancellationToken cancellation = default)
    {
        return await _context.WorkflowSchedules
            .AsNoTracking()
            .Where(x => x.IsActive)
            .ToListAsync(cancellation);
    }

    public async Task<IReadOnlyList<WorkflowSchedule>> GetActiveSchedulesByDefinitionIdAsync(Guid definitionId, CancellationToken cancellation = default)
    {
        return await _context.WorkflowSchedules
            .AsNoTracking()
            .Where(x => x.IsActive && x.DefinitionId == definitionId)
            .ToListAsync(cancellation);
    }
}
