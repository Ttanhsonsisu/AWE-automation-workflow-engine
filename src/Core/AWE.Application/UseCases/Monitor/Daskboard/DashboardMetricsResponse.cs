using AWE.Domain.Enums;

namespace AWE.Application.UseCases.Monitor.Daskboard;

public record DashboardMetricsResponse(
    int TotalWorkflows,
    int ActiveWorkflows,
    int TotalExecutionsLast30Days,
    List<ExecutionStatusCount> StatusBreakdown,
    List<DailyExecutionTrend> ExecutionTrend,
    List<RecentFailedExecution> RecentFailures
);

public record ExecutionStatusCount(WorkflowInstanceStatus Status, int Count);

public record DailyExecutionTrend(DateTime Date, int Count);

public record RecentFailedExecution(Guid InstanceId, Guid DefinitionId, DateTime FailedAt);
