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

    [TestMethod]
    public async Task Runs_leader_work_when_lock_acquired()
    {
        var locks = Substitute.For<IDistributedLock>();
        var handle = Substitute.For<IAsyncDisposable>();
        locks.TryAcquireAsync("test", Arg.Any<CancellationToken>()).Returns(handle);
        var clock = new FakeTimeProvider();
        var sp = new ServiceCollection().BuildServiceProvider();
        var ran = 0;
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks, clock, _ => ran++);

        using var cts = new CancellationTokenSource();
        var task = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(100);
        cts.Cancel();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        Assert.AreEqual(1, ran);
    }

    [TestMethod]
    public async Task Skips_when_lock_unavailable()
    {
        var locks = Substitute.For<IDistributedLock>();
        locks.TryAcquireAsync("test", Arg.Any<CancellationToken>()).Returns((IAsyncDisposable?)null);
        var clock = new FakeTimeProvider();
        var sp = new ServiceCollection().BuildServiceProvider();
        var ran = 0;
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks, clock, _ => ran++);

        using var cts = new CancellationTokenSource();
        var task = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(100);
        cts.Cancel();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        Assert.AreEqual(0, ran);
    }

    [TestMethod]
    public async Task Exception_in_work_does_not_stop_the_loop()
    {
        var locks = Substitute.For<IDistributedLock>();
        var handle = Substitute.For<IAsyncDisposable>();
        locks.TryAcquireAsync("test", Arg.Any<CancellationToken>()).Returns(handle);
        var clock = new FakeTimeProvider();
        var sp = new ServiceCollection().BuildServiceProvider();
        var calls = 0;
        var sut = new TestService(sp.GetRequiredService<IServiceScopeFactory>(), locks, clock,
            _ => { calls++; if (calls == 1) throw new InvalidOperationException("boom"); });

        using var cts = new CancellationTokenSource();
        var task = sut.StartAsync(cts.Token);
        await Task.Delay(100);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(100);
        clock.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(100);
        cts.Cancel();
        try { await sut.StopAsync(CancellationToken.None); } catch (OperationCanceledException) { }

        Assert.IsTrue(calls >= 2, $"Expected at least 2 invocations after exception recovery, got {calls}.");
    }
}
