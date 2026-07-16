using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Kartova.SharedKernel.Tests;

[TestClass]
public sealed class LeaderElectedPeriodicServiceTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private sealed class TestService(
        IServiceScopeFactory scopes, IDistributedLock locks, TimeProvider clock,
        Action<IServiceProvider> work)
        : LeaderElectedPeriodicService(scopes, locks, clock, NullLogger.Instance)
    {
        protected override string LockName => "test";
        protected override TimeSpan Interval => LeaderElectedPeriodicServiceTests.Interval;
        protected override Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct)
        {
            work(services);
            return Task.CompletedTask;
        }
    }

    // The service's loop awaits a `PeriodicTimer(Interval, clock)`. Two facts drive these tests:
    //
    //  1. The loop starts on a background task, so at the moment StartAsync returns it has NOT yet
    //     reached its first await — the PeriodicTimer may not exist, so an immediate `clock.Advance`
    //     is a no-op that is silently lost (this is what made a single up-front advance flaky).
    //  2. Once the PeriodicTimer is constructed it arms a *single auto-repeating* timer; from then on
    //     each `clock.Advance(Interval)` releases exactly one tick.
    //
    // PeriodicTimer arms by calling `TimeProvider.CreateTimer` in its constructor, so wrapping the
    // clock in `ArmSignallingTimeProvider` gives a deterministic barrier: `WaitForArmed` returns only
    // once the loop has constructed its timer. After that, one advance per intended tick is exact —
    // no arming race, and (because we advance the next interval only after the previous tick's work
    // signal, which fires from inside the delegate after WaitForNextTickAsync already returned) no
    // coalescing loss and no double-fire. This replaces the earlier poll-advance-on-a-fixed-delay
    // helper, whose extra advances under load queued a second tick and over-counted.
    private sealed class ArmSignallingTimeProvider(TimeProvider inner, SemaphoreSlim armed) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
        public override long GetTimestamp() => inner.GetTimestamp();
        public override long TimestampFrequency => inner.TimestampFrequency;
        public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = inner.CreateTimer(callback, state, dueTime, period);
            armed.Release();
            return timer;
        }
    }

    private static async Task AwaitSignal(SemaphoreSlim signal, string because) =>
        Assert.IsTrue(await signal.WaitAsync(SignalTimeout), because);

    [TestMethod]
    public async Task Runs_leader_work_when_lock_acquired()
    {
        var locks = Substitute.For<IDistributedLock>();
        var handle = Substitute.For<IAsyncDisposable>();
        locks.TryAcquireAsync("test", Arg.Any<CancellationToken>()).Returns(handle);
        var clock = new FakeTimeProvider();
        using var armed = new SemaphoreSlim(0);
        await using var sp = new ServiceCollection().BuildServiceProvider();
        var ran = 0;
        using var worked = new SemaphoreSlim(0);
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks,
            new ArmSignallingTimeProvider(clock, armed), _ => { Interlocked.Increment(ref ran); worked.Release(); });

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await AwaitSignal(armed, "loop did not arm its timer");
        // Release exactly one interval and wait for the work to run. We never advance again, so the
        // next tick (due one interval later) can never fire — work is guaranteed to run precisely once.
        clock.Advance(Interval);
        await AwaitSignal(worked, "leader work did not run within the deadline");
        cts.Cancel();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        Assert.AreEqual(1, Volatile.Read(ref ran));
    }

    [TestMethod]
    public async Task Skips_when_lock_unavailable()
    {
        var locks = Substitute.For<IDistributedLock>();
        using var attempted = new SemaphoreSlim(0);
        locks.TryAcquireAsync("test", Arg.Any<CancellationToken>())
            .Returns(_ => { attempted.Release(); return Task.FromResult<IAsyncDisposable?>(null); });
        var clock = new FakeTimeProvider();
        using var armed = new SemaphoreSlim(0);
        await using var sp = new ServiceCollection().BuildServiceProvider();
        var ran = 0;
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks,
            new ArmSignallingTimeProvider(clock, armed), _ => Interlocked.Increment(ref ran));

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await AwaitSignal(armed, "loop did not arm its timer");
        // Release two ticks, waiting on each lock attempt, so we know the loop actually reached the
        // lock and took the skip branch both times — yet the (unavailable) lock kept work from running.
        for (var i = 0; i < 2; i++)
        {
            clock.Advance(Interval);
            await AwaitSignal(attempted, "lock was not attempted");
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
        using var armed = new SemaphoreSlim(0);
        await using var sp = new ServiceCollection().BuildServiceProvider();
        var calls = 0;
        using var invoked = new SemaphoreSlim(0);
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks,
            new ArmSignallingTimeProvider(clock, armed),
            _ =>
            {
                var n = Interlocked.Increment(ref calls);
                invoked.Release();                                   // signal every invocation, before any throw
                if (n == 1) throw new InvalidOperationException("boom");
            });

        using var cts = new CancellationTokenSource();
        await sut.StartAsync(cts.Token);
        await AwaitSignal(armed, "loop did not arm its timer");
        // First tick throws; wait until it is observed before releasing the next interval so the second
        // advance lands as a fresh tick. The loop must catch the exception and run again on that tick.
        clock.Advance(Interval);
        await AwaitSignal(invoked, "first tick did not run");
        clock.Advance(Interval);
        await AwaitSignal(invoked, "loop did not recover after the exception");
        cts.Cancel();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        var observed = Volatile.Read(ref calls);
        Assert.IsTrue(observed >= 2, $"Expected at least 2 invocations after exception recovery, got {observed}.");
    }
}
