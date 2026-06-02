namespace Kartova.SharedKernel;

/// <summary>
/// Cluster-wide named exclusive lock. Implementations must guarantee that only one acquirer
/// across all instances can hold a given lockName at a time. The returned handle releases
/// the lock on Dispose. Implementations are expected to release on connection drop / process
/// death automatically to avoid stale locks.
/// </summary>
public interface IDistributedLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct);
}
