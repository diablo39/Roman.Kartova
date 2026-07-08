using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Kartova.SharedKernel.Tests;

[TestClass]
public sealed class LeaderElectedPeriodicServiceTests
{
    private sealed class TestService(
        IServiceScopeFactory scopes, IDistributedLock locks, TimeProvider clock,
        Action<IServiceProvider> work)
        : LeaderElectedPeriodicService(scopes, locks, clock, NullLogger.Instance)
    {
        protected override string LockName => "test";
        protected override TimeSpan Interval => TimeSpan.FromMinutes(1);
        protected override Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct)
        {
            work(services);
            return Task.CompletedTask;
        }
    }

    // Deterministic replacement for fixed `await Task.Delay(100)` waits. The service runs a
    // background loop that awaits `Task.Delay(Interval, clock)` against the FakeTimeProvider,
    // so a single up-front `clock.Advance` is racy on a contended CI runner in two ways:
    //   (1) the advance can fire before the loop has registered its first timer (advance lost);
    //   (2) the post-advance work continuation may not be scheduled within a fixed 100 ms.
    // Repeatedly advancing (each advance releases at most one loop iteration, since the loop
    // re-registers its next delay only after running) and yielding real time until the observed
    // effect holds — bounded by a deadline — removes both races without changing semantics:
    // the loop still runs exactly once per released interval.
    private static async Task PumpUntilAsync(Func<bool> condition, FakeTimeProvider clock, TimeSpan step)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            clock.Advance(step);
            await Task.Delay(25);
        }
    }

    [TestMethod]
    public async Task Runs_leader_work_when_lock_acquired()
    {
        var locks = Substitute.For<IDistributedLock>();
        var handle = Substitute.For<IAsyncDisposable>();
        locks.TryAcquireAsync("test", Arg.Any<CancellationToken>()).Returns(handle);
        var clock = new FakeTimeProvider();
        await using var sp = new ServiceCollection().BuildServiceProvider();
        var ran = 0;
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks, clock,
            _ => Interlocked.Increment(ref ran));

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await PumpUntilAsync(() => Volatile.Read(ref ran) >= 1, clock, TimeSpan.FromMinutes(1));
        cts.Cancel();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        // Exactly one interval was released before the condition was observed, so work ran once.
        Assert.AreEqual(1, Volatile.Read(ref ran));
    }

    [TestMethod]
    public async Task Skips_when_lock_unavailable()
    {
        var locks = Substitute.For<IDistributedLock>();
        locks.TryAcquireAsync("test", Arg.Any<CancellationToken>()).Returns((IAsyncDisposable?)null);
        var clock = new FakeTimeProvider();
        await using var sp = new ServiceCollection().BuildServiceProvider();
        var ran = 0;
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks, clock,
            _ => Interlocked.Increment(ref ran));

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        // Release several intervals; the loop acquires the (unavailable) lock each time and must
        // never invoke the work. Advancing repeatedly gives it ample opportunity to run — if the
        // skip logic regressed, `ran` would climb above 0.
        for (var i = 0; i < 4; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(25);
        }
        cts.Cancel();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        Assert.AreEqual(0, Volatile.Read(ref ran));
    }

    [TestMethod]
    public async Task Exception_in_work_does_not_stop_the_loop()
    {
        var locks = Substitute.For<IDistributedLock>();
        var handle = Substitute.For<IAsyncDisposable>();
        locks.TryAcquireAsync("test", Arg.Any<CancellationToken>()).Returns(handle);
        var clock = new FakeTimeProvider();
        await using var sp = new ServiceCollection().BuildServiceProvider();
        var calls = 0;
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks, clock,
            _ => { if (Interlocked.Increment(ref calls) == 1) throw new InvalidOperationException("boom"); });

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        // The first invocation throws; the loop must recover and run again on the next interval.
        await PumpUntilAsync(() => Volatile.Read(ref calls) >= 2, clock, TimeSpan.FromMinutes(1));
        cts.Cancel();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        var observed = Volatile.Read(ref calls);
        Assert.IsTrue(observed >= 2, $"Expected at least 2 invocations after exception recovery, got {observed}.");
    }
}
