namespace AWE.Application.UseCases.Audit;

public record AuditLogResponse(
    Guid Id,
    string Action,
    string UserName,
    string OldValues,
    string NewValues,
    DateTime Timestamp);
