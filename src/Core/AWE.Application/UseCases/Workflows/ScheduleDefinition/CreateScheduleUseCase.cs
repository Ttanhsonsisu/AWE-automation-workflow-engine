using AWE.Application.Abstractions.Persistence;
using AWE.Domain.Entities;
using AWE.Shared.Primitives;
using Cronos;

namespace AWE.Application.UseCases.Workflows.ScheduleDefinition;

public interface ICreateScheduleUseCase
{
    public Task<Result<ScheduleResponse>> ExecuteAsync(CreateScheduleCommand request, CancellationToken cancellationToken);
}

public class CreateScheduleUseCase(
    IWorkflowScheduleRepository scheduleRepository,
    IWorkflowDefinitionRepository definitionRepository,
    IUnitOfWork unitOfWork
    ) : ICreateScheduleUseCase
{
    private readonly IWorkflowScheduleRepository _scheduleRepository = scheduleRepository;
    private readonly IWorkflowDefinitionRepository _definitionRepository = definitionRepository;
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<Result<ScheduleResponse>> ExecuteAsync(CreateScheduleCommand request, CancellationToken cancellationToken)
    {
        CronExpression parsedCron;
        try
        {
            parsedCron = CronExpression.Parse(request.CronExpression, CronFormat.Standard);
        }
        catch (CronFormatException)
        {
            // Sử dụng Error pattern của bạn
            return Result.Failure<ScheduleResponse>(Error.Failure(
                "Schedule.InvalidCron",
                "Chuỗi Cron không hợp lệ. Vui lòng kiểm tra lại định dạng."));
        }

        // 2. Validate Definition có tồn tại không
        var definitionExists = await _definitionRepository.ExistsDefinitionAsync(request.DefinitionId);

        if (!definitionExists)
        {
            return Result.Failure<ScheduleResponse>(Error.NotFound(
                "Workflow.NotFound",
                "Không tìm thấy Workflow tương ứng."));
        }

        // 3. Xử lý logic và lưu DB
        var nextRun = parsedCron.GetNextOccurrence(DateTime.UtcNow);
        var schedule = new WorkflowSchedule
        {
            Id = Guid.NewGuid(),
            DefinitionId = request.DefinitionId,
            CronExpression = request.CronExpression,
            NextRunAt = nextRun,
            IsActive = true
        };

        await _scheduleRepository.AddWorkflowScheduleAsync(schedule, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 4. Trả về thành công
        return Result.Success(new ScheduleResponse(schedule.Id, nextRun));
    }

}
