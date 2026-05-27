using Kartova.SharedKernel.Postgres;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Kartova.SharedKernel.Postgres.IntegrationTests;

[TestClass]
public sealed class PostgresAdvisoryLockTests
{
    private static PostgreSqlContainer? _pg;
    private static NpgsqlDataSource? _dataSource;

    [ClassInitialize]
    public static async Task InitAsync(TestContext _)
    {
        _pg = new PostgreSqlBuilder().WithImage("postgres:18-alpine").Build();
        await _pg.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_pg.GetConnectionString());
    }

    [ClassCleanup]
    public static async Task TeardownAsync()
    {
        _dataSource?.Dispose();
        if (_pg is not null) await _pg.DisposeAsync();
    }

    [TestMethod]
    public async Task Concurrent_acquire_only_one_wins()
    {
        var sut = new PostgresAdvisoryLock(_dataSource!, NullLogger<PostgresAdvisoryLock>.Instance);
        const string name = "concurrent-test";

        var handle1 = await sut.TryAcquireAsync(name, CancellationToken.None);
        var handle2 = await sut.TryAcquireAsync(name, CancellationToken.None);

        Assert.IsNotNull(handle1);
        Assert.IsNull(handle2);
        await handle1!.DisposeAsync();
    }

    [TestMethod]
    public async Task After_dispose_lock_is_available_to_next_acquirer()
    {
        var sut = new PostgresAdvisoryLock(_dataSource!, NullLogger<PostgresAdvisoryLock>.Instance);
        const string name = "release-test";

        var h1 = await sut.TryAcquireAsync(name, CancellationToken.None);
        Assert.IsNotNull(h1);
        await h1!.DisposeAsync();

        var h2 = await sut.TryAcquireAsync(name, CancellationToken.None);
        Assert.IsNotNull(h2);
        await h2!.DisposeAsync();
    }

    [TestMethod]
    public void StableHash64_is_deterministic_across_calls()
    {
        var a = PostgresAdvisoryLock.StableHash64("expire-invitations");
        var b = PostgresAdvisoryLock.StableHash64("expire-invitations");
        var c = PostgresAdvisoryLock.StableHash64("different");
        Assert.AreEqual(a, b);
        Assert.AreNotEqual(a, c);
    }
}
