namespace AWE.Application.UseCases.Workflows.ScheduleDefinition;

public class CreateScheduleRequest
{
    public string CronExpression { get; set; } = string.Empty;
}

public record CreateScheduleCommand(
    Guid DefinitionId,
    string CronExpression
);
