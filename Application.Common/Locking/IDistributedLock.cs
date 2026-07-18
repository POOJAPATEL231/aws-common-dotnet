namespace Application.Common.Locking
{
    /// <summary>
    /// A held distributed lock. Dispose to release; if the holder crashes, the
    /// lease expires on its own after the requested duration.
    /// </summary>
    public interface IDistributedLockHandle : IAsyncDisposable
    {
        string LockName { get; }
        string OwnerId { get; }
        DateTimeOffset LeaseExpiresAtUtc { get; }
    }

    /// <summary>
    /// Distributed mutual-exclusion abstraction (implemented on DynamoDB conditional
    /// writes by Persistence.Common.AWS.Locking.DynamoDbDistributedLock). Used for
    /// leader election and run-once job guards across service instances.
    /// </summary>
    public interface IDistributedLock
    {
        /// <summary>
        /// Attempts to acquire the named lock for <paramref name="leaseDuration"/>.
        /// Returns a handle when acquired, or null when another owner holds an
        /// unexpired lease.
        /// </summary>
        Task<IDistributedLockHandle?> TryAcquireAsync(string lockName, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    }
}
