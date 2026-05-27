using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Kartova.SharedKernel;

public abstract class LeaderElectedPeriodicService(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    TimeProvider clock,
    ILogger logger) : BackgroundService
{
    protected abstract string LockName { get; }
    protected abstract TimeSpan Interval { get; }
    protected abstract Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (true)
        {
            if (!await timer.WaitForNextTickAsync(ct)) break;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await using var lockHandle = await locks.TryAcquireAsync(LockName, ct);
                if (lockHandle is null)
                {
                    logger.LogDebug("{Service}: lock held by another instance — skipping tick", GetType().Name);
                    continue;
                }
                await ExecuteLeaderWorkAsync(scope.ServiceProvider, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "{Service}: leader tick failed", GetType().Name);
            }
        }
    }
}
