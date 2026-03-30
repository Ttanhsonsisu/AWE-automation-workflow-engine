using AWE.Application.Dtos.WorkflowDto;
using AWE.Shared.Consts;
using AWE.Shared.Primitives;

namespace AWE.Application.Services;

public interface IWorkflowService
{
    Task<Result<WorkflowDetailDto>> GetWorkflowDetailsAsync(Guid id, CancellationToken ct = default);
    Task<Result<PagedResult<WorkflowDto>>> GetPagingWorkflowAsync(
        int pageSize = Consts.PAGE_SIZE_DEFAULT,
        int pageNo = 1,
        bool? isPublished = null,
        string? name = null,
        CancellationToken ct = default);
    Task<Result<PagedResult<WorkflowPagingDto>>> GetPagingGroupVersionWorkflowAsync(
        int pageSize = Consts.PAGE_SIZE_DEFAULT,
        int pageNo = 1,
        bool? isPublished = null,
        string? name = null,
        CancellationToken ct = default);
}
