using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Organization.Infrastructure.Tests;

[TestClass]
public sealed class UserProjectionUpdaterTests
{
    private static OrganizationDbContext NewInMemory()
    {
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"users-projection-{Guid.NewGuid()}")
            .Options;
        return new OrganizationDbContext(opts);
    }

    [TestMethod]
    public async Task Upsert_inserts_new_user_with_computed_display_name()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var sut = new UserProjectionUpdater(clock, NullLogger<UserProjectionUpdater>.Instance);
        var tenant = new TenantId(Guid.NewGuid());

        await sut.UpsertAsync(db, new Guid("11111111-1111-1111-1111-111111111111"),
            "alice@example.com", "Alice", "Smith", tenant, CancellationToken.None);

        var u = await db.Users.SingleAsync();
        Assert.AreEqual("Alice Smith", u.DisplayName);
        Assert.AreEqual(clock.GetUtcNow(), u.LastSeenAt);
        Assert.AreEqual(clock.GetUtcNow(), u.CreatedAt);
    }

    [TestMethod]
    public async Task Upsert_falls_back_to_email_when_names_missing()
    {
        await using var db = NewInMemory();
        var sut = new UserProjectionUpdater(new FakeTimeProvider(), NullLogger<UserProjectionUpdater>.Instance);
        var tenant = new TenantId(Guid.NewGuid());

        await sut.UpsertAsync(db, Guid.NewGuid(), "noname@example.com", null, null, tenant, CancellationToken.None);

        var u = await db.Users.SingleAsync();
        Assert.AreEqual("noname@example.com", u.DisplayName);
    }

    [TestMethod]
    public async Task Upsert_updates_existing_row_and_advances_last_seen()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var sut = new UserProjectionUpdater(clock, NullLogger<UserProjectionUpdater>.Instance);
        var tenant = new TenantId(Guid.NewGuid());
        var id = Guid.NewGuid();

        await sut.UpsertAsync(db, id, "alice@example.com", "Alice", "Smith", tenant, CancellationToken.None);
        clock.Advance(TimeSpan.FromMinutes(30));
        await sut.UpsertAsync(db, id, "alice@example.com", "Alice", "JONES", tenant, CancellationToken.None);

        var u = await db.Users.SingleAsync();
        Assert.AreEqual("Alice JONES", u.DisplayName);
        Assert.AreEqual(DateTimeOffset.Parse("2026-05-27T10:30:00Z"), u.LastSeenAt);
    }
}
