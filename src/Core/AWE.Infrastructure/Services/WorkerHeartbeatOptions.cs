namespace AWE.Infrastructure.Services;

public sealed record WorkerHeartbeatOptions(
    string WorkerType,
    TimeSpan Interval,
    TimeSpan StaleAfter);
