namespace AWE.Application.UseCases.Workflows.ScheduleDefinition;

public record ScheduleResponse(Guid ScheduleId, DateTime? NextRunAt);
