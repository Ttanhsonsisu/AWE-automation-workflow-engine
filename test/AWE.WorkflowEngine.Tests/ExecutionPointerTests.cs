using System.Text.Json;
using AWE.Domain.Entities;
using AWE.Domain.Enums;

namespace AWE.WorkflowEngine.Tests;

public class ExecutionPointerTests
{
    [Fact]
    public void TryAcquireLease_WhenPending_MovesPointerToRunning()
    {
        var pointer = new ExecutionPointer(Guid.NewGuid(), "log");

        var acquired = pointer.TryAcquireLease("worker-a", TimeSpan.FromMinutes(5));

        Assert.True(acquired);
        Assert.Equal(ExecutionPointerStatus.Running, pointer.Status);
        Assert.True(pointer.StartTime.HasValue);
        Assert.Equal("worker-a", pointer.LeasedBy);
        Assert.True(pointer.LeasedUntil > DateTime.UtcNow);
    }

    [Fact]
    public void TryAcquireLease_WhenRunningLeaseExpired_AllowsAnotherWorkerToStealAndCountsRetry()
    {
        var pointer = new ExecutionPointer(Guid.NewGuid(), "log");

        Assert.True(pointer.TryAcquireLease("worker-a", TimeSpan.FromMilliseconds(-1)));
        Assert.True(pointer.IsZombie());

        var acquiredBySecondWorker = pointer.TryAcquireLease("worker-b", TimeSpan.FromMinutes(5));

        Assert.True(acquiredBySecondWorker);
        Assert.Equal(ExecutionPointerStatus.Running, pointer.Status);
        Assert.Equal("worker-b", pointer.LeasedBy);
        Assert.Equal(1, pointer.RetryCount);
    }

    [Fact]
    public void Complete_WhenLeaseOwnerMismatch_ThrowsAndKeepsPointerRunning()
    {
        var pointer = new ExecutionPointer(Guid.NewGuid(), "log");
        pointer.TryAcquireLease("worker-a", TimeSpan.FromMinutes(5));
        using var output = JsonDocument.Parse("""{"ok":true}""");

        var ex = Assert.Throws<InvalidOperationException>(() => pointer.Complete("worker-b", output));

        Assert.Contains("Lease conflict", ex.Message);
        Assert.Equal(ExecutionPointerStatus.Running, pointer.Status);
        Assert.True(pointer.Active);
    }

    [Fact]
    public void Complete_WhenLeaseOwnerMatches_MarksPointerTerminalAndClearsLease()
    {
        var pointer = new ExecutionPointer(Guid.NewGuid(), "log");
        pointer.TryAcquireLease("worker-a", TimeSpan.FromMinutes(5));
        using var output = JsonDocument.Parse("""{"ok":true}""");

        pointer.Complete("worker-a", output);

        Assert.Equal(ExecutionPointerStatus.Completed, pointer.Status);
        Assert.False(pointer.Active);
        Assert.Null(pointer.LeasedBy);
        Assert.Null(pointer.LeasedUntil);
        Assert.True(pointer.EndTime.HasValue);
    }

    [Fact]
    public void ResetToPending_WhenRunning_IncrementsRetryAndClearsLease()
    {
        var pointer = new ExecutionPointer(Guid.NewGuid(), "retry");
        pointer.TryAcquireLease("worker-a", TimeSpan.FromMinutes(5));

        pointer.ResetToPending();

        Assert.Equal(ExecutionPointerStatus.Pending, pointer.Status);
        Assert.Equal(1, pointer.RetryCount);
        Assert.Null(pointer.LeasedBy);
        Assert.Null(pointer.LeasedUntil);
    }

    [Fact]
    public void CompleteFromWait_WhenSuspended_CompletesPointerWithoutLease()
    {
        var pointer = new ExecutionPointer(Guid.NewGuid(), "approval");
        pointer.PauseForWebhook();
        using var resumeData = JsonDocument.Parse("""{"approved":true}""");

        pointer.CompleteFromWait(resumeData);

        Assert.Equal(ExecutionPointerStatus.Completed, pointer.Status);
        Assert.False(pointer.Active);
        Assert.Null(pointer.ResumeAt);
    }
}
