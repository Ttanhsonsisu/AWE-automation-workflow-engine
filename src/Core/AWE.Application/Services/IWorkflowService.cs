using AWE.Application.Dtos.WorkflowDto;
using AWE.Shared.Primitives;

namespace AWE.Application.Services;

public interface IWorkflowService
{
    Task<Result<WorkflowDetailDto>> GetWorkflowDetailsAsync(Guid id, CancellationToken ct = default);    
}
