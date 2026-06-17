using Medallion.Threading;

namespace AWE.WorkflowEngine.Tests.Fakes;

internal sealed class ContendedDistributedLockProvider : IDistributedLockProvider
{
    public IDistributedLock CreateLock(string name) => new ContendedDistributedLock(name);

    private sealed class ContendedDistributedLock(string name) : IDistributedLock
    {
        public string Name { get; } = name;

        public IDistributedSynchronizationHandle? TryAcquire(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) => null;

        public IDistributedSynchronizationHandle Acquire(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            throw new TimeoutException("The fake lock is always contended.");

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(null);

        public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default) =>
            throw new TimeoutException("The fake lock is always contended.");
    }
}
