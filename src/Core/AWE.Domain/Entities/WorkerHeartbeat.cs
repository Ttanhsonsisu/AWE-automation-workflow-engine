namespace AWE.Domain.Entities;

public class WorkerHeartbeat
{
    public string WorkerId { get; private set; } = string.Empty;
    public string WorkerType { get; private set; } = string.Empty;
    public string MachineName { get; private set; } = string.Empty;
    public int ProcessId { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }

    private WorkerHeartbeat()
    {
    }

    public WorkerHeartbeat(
        string workerId,
        string workerType,
        string machineName,
        int processId,
        DateTime nowUtc)
    {
        WorkerId = workerId;
        WorkerType = workerType;
        MachineName = machineName;
        ProcessId = processId;
        StartedAtUtc = nowUtc;
        LastSeenAtUtc = nowUtc;
    }

    public void MarkSeen(DateTime nowUtc)
    {
        LastSeenAtUtc = nowUtc;
    }
}
