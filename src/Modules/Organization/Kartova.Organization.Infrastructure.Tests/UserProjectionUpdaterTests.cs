using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

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

    [TestMethod]
    public async Task UpsertAsync_throws_OneEmailPerTenantViolationException_when_unique_index_throws_23505()
    {
        // ADR-0100: the AddUsersTenantEmailUnique migration installs a functional
        // UNIQUE index on users(tenant_id, lower(email)). When two distinct KC
        // sub claims share an email within the same tenant, the second INSERT
        // throws Postgres 23505 — the catch arm at UserProjectionUpdater.cs:52-68
        // translates that into a typed OneEmailPerTenantViolationException so ops
        // can investigate. This test pins that translation.
        //
        // Mirrors the ThrowOnSaveInterceptor pattern from
        // CreateInvitationHandlerTests' 23505 test (same Npgsql 10 ctor signature
        // for seeding sqlState directly without touching a real PostgreSQL).
        // TODO: lift ThrowOnSaveInterceptor to a shared helper if a third caller appears.
        var pg = new PostgresException(
            messageText: "duplicate key value violates unique constraint \"ix_users_tenant_email\"",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: "23505");
        var inner = new DbUpdateException("duplicate", pg);
        var interceptor = new ThrowOnSaveInterceptor(inner);
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"users-projection-{Guid.NewGuid()}")
            .AddInterceptors(interceptor)
            .Options;

        await using var db = new OrganizationDbContext(opts);
        var sut = new UserProjectionUpdater(new FakeTimeProvider(), NullLogger<UserProjectionUpdater>.Instance);
        var tenant = new TenantId(Guid.NewGuid());
        var userId = Guid.NewGuid();

        var ex = await Assert.ThrowsExactlyAsync<OneEmailPerTenantViolationException>(
            () => sut.UpsertAsync(db, userId, "user@example.com", null, null, tenant, CancellationToken.None));

        Assert.AreEqual(tenant.Value, ex.TenantId);
        Assert.AreEqual("user@example.com", ex.Email);
        Assert.IsInstanceOfType<DbUpdateException>(ex.InnerException);
        Assert.AreSame(inner, ex.InnerException);
    }

    /// <summary>
    /// EF Core save interceptor that throws a configured exception on the
    /// first SaveChanges call — drives the 23505 contract test without
    /// requiring a real PostgreSQL container in a unit-tier suite. Mirrors the
    /// sibling helper in <c>CreateInvitationHandlerTests</c>.
    /// </summary>
    private sealed class ThrowOnSaveInterceptor(Exception ex) : ISaveChangesInterceptor
    {
        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken ct = default)
            => throw ex;
    }
}
