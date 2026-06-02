using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure.Admin;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Kartova.Organization.Infrastructure.Tests;

/// <summary>
/// Behavioral tests for <see cref="ExpireInvitationsHostedService"/> — slice 9
/// spec §6.9. Drives the public <c>ExpireDueAsync</c> work method directly with
/// a constructed <see cref="IServiceProvider"/>, avoiding <see cref="PeriodicTimer"/>
/// timing in unit tests.
/// </summary>
[TestClass]
public sealed class ExpireInvitationsHostedServiceTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-27T10:00:00Z");

    private static DbContextOptions<AdminOrganizationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AdminOrganizationDbContext>()
            .UseInMemoryDatabase($"expire-{Guid.NewGuid()}")
            .Options;

    private static ServiceProvider BuildServices(
        DbContextOptions<AdminOrganizationDbContext> opts,
        IKeycloakAdminClient kc,
        TimeProvider clock)
    {
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton(kc);
        services.AddScoped(_ => new AdminOrganizationDbContext(opts));
        return services.BuildServiceProvider();
    }

    private static (ExpireInvitationsHostedService Sut, ServiceProvider HostSp) BuildSut(TimeProvider clock)
    {
        // The base ctor requires IServiceScopeFactory and IDistributedLock; neither is
        // touched by ExpireDueAsync when invoked directly. Use a real DI container so
        // the base class type-checks cleanly. Caller is responsible for disposing the
        // returned ServiceProvider (covered by the per-test `await using` blocks below).
        var hostSp = new ServiceCollection().BuildServiceProvider();
        var sut = new ExpireInvitationsHostedService(
            hostSp.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IDistributedLock>(),
            clock,
            NullLogger<ExpireInvitationsHostedService>.Instance);
        return (sut, hostSp);
    }

    [TestMethod]
    public async Task ExpireDueAsync_marks_pending_invitations_past_due_and_deletes_kc_users()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kc = Substitute.For<IKeycloakAdminClient>();

        Guid kcUserId;
        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            // Invitation.Create sets ExpiresAt = now + 7 days. Advancing the clock by 8 days
            // moves "now" past ExpiresAt without needing to fabricate a backdated invitation.
            var seeded = Invitation.Create("alice@example.com", KartovaRoles.Member,
                invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
                tenantId: tenant, clock: clock, tokenHash: InvitationToken.Hash("seed-token"));
            kcUserId = seeded.KeycloakUserId!.Value;
            seedDb.Invitations.Add(seeded);
            await seedDb.SaveChangesAsync();
        }

        clock.Advance(TimeSpan.FromDays(8));
        await using var sp = BuildServices(opts, kc, clock);
        var (sut, hostSp) = BuildSut(clock);
        await using var _hostSp = hostSp;

        await sut.ExpireDueAsync(sp, CancellationToken.None);

        await using (var assertDb = new AdminOrganizationDbContext(opts))
        {
            var reloaded = await assertDb.Invitations.SingleAsync();
            Assert.AreEqual(InvitationStatus.Expired, reloaded.Status);
        }
        await kc.Received(1).DeleteUserAsync(kcUserId, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExpireDueAsync_leaves_non_expired_pending_invitations_alone()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kc = Substitute.For<IKeycloakAdminClient>();

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            // Pending invitation with ExpiresAt 7 days in the future — sweep should skip.
            var seeded = Invitation.Create("alice@example.com", KartovaRoles.Member,
                invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
                tenantId: tenant, clock: clock, tokenHash: InvitationToken.Hash("seed-token"));
            seedDb.Invitations.Add(seeded);
            await seedDb.SaveChangesAsync();
        }

        // Clock NOT advanced — invitation is still within its TTL window.
        await using var sp = BuildServices(opts, kc, clock);
        var (sut, hostSp) = BuildSut(clock);
        await using var _hostSp = hostSp;

        await sut.ExpireDueAsync(sp, CancellationToken.None);

        await using (var assertDb = new AdminOrganizationDbContext(opts))
        {
            var reloaded = await assertDb.Invitations.SingleAsync();
            Assert.AreEqual(InvitationStatus.Pending, reloaded.Status);
        }
        await kc.DidNotReceive().DeleteUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExpireDueAsync_skips_invitations_that_are_already_accepted()
    {
        // Even with ExpiresAt in the past, Accepted invitations must not be touched
        // (the Status filter on the query enforces this; this test pins that contract).
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kc = Substitute.For<IKeycloakAdminClient>();

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            var seeded = Invitation.Create("alice@example.com", KartovaRoles.Member,
                invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
                tenantId: tenant, clock: clock, tokenHash: InvitationToken.Hash("seed-token"));
            seeded.MarkAccepted(clock);
            seedDb.Invitations.Add(seeded);
            await seedDb.SaveChangesAsync();
        }

        clock.Advance(TimeSpan.FromDays(8));
        await using var sp = BuildServices(opts, kc, clock);
        var (sut, hostSp) = BuildSut(clock);
        await using var _hostSp = hostSp;

        await sut.ExpireDueAsync(sp, CancellationToken.None);

        await using (var assertDb = new AdminOrganizationDbContext(opts))
        {
            var reloaded = await assertDb.Invitations.SingleAsync();
            Assert.AreEqual(InvitationStatus.Accepted, reloaded.Status);
        }
        await kc.DidNotReceive().DeleteUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExpireDueAsync_propagates_non_NotFound_KC_error_and_aborts_before_save()
    {
        // MT4 (= slice-9 carry-forward item 12): the loop must propagate any
        // non-NotFound KeycloakAdminException AND abort before SaveChangesAsync
        // runs. The two seeded invitations are processed in EF iteration order;
        // throwing on the FIRST KC delete means BOTH rows must still be Pending
        // when ops inspects the table — no partial commit (the surviving row
        // is critical; a partial flip would silently mask the failure on the
        // next sweep tick).
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kc = Substitute.For<IKeycloakAdminClient>();

        Guid firstKcId, secondKcId;
        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            var first = Invitation.Create("alice@example.com", KartovaRoles.Member,
                invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
                tenantId: tenant, clock: clock, tokenHash: InvitationToken.Hash("seed-token"));
            var second = Invitation.Create("bob@example.com", KartovaRoles.Member,
                invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
                tenantId: tenant, clock: clock, tokenHash: InvitationToken.Hash("seed-token"));
            firstKcId = first.KeycloakUserId!.Value;
            secondKcId = second.KeycloakUserId!.Value;
            seedDb.Invitations.Add(first);
            seedDb.Invitations.Add(second);
            await seedDb.SaveChangesAsync();
        }

        // Throw on the FIRST id — the second is never reached. Use
        // KeycloakAdminError.Unexpected to exercise the non-NotFound branch
        // (the swallowed-NotFound branch is covered by the sibling test).
        kc.DeleteUserAsync(firstKcId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.Unexpected, "KC down"));

        clock.Advance(TimeSpan.FromDays(8));
        await using var sp = BuildServices(opts, kc, clock);
        var (sut, hostSp) = BuildSut(clock);
        await using var _hostSp = hostSp;

        // The exception must surface to the caller — the base class's
        // LeaderElectedPeriodicService swallows + logs at the periodic tick,
        // but ExpireDueAsync itself (the unit under test) propagates.
        await Assert.ThrowsExactlyAsync<KeycloakAdminException>(
            () => sut.ExpireDueAsync(sp, CancellationToken.None));

        // Critical invariant: NEITHER row was flipped. SaveChangesAsync runs
        // AFTER the loop, so a throw in the loop aborts before the save,
        // leaving both rows Pending.
        await using (var assertDb = new AdminOrganizationDbContext(opts))
        {
            var rows = await assertDb.Invitations.OrderBy(i => i.Email).ToListAsync();
            Assert.AreEqual(2, rows.Count);
            Assert.IsTrue(rows.All(r => r.Status == InvitationStatus.Pending),
                "No partial commit: both invitations must still be Pending after the abort.");
        }

        // The KC delete was called exactly once for the first id; the second
        // never ran because the loop aborted.
        await kc.Received(1).DeleteUserAsync(firstKcId, Arg.Any<CancellationToken>());
        await kc.DidNotReceive().DeleteUserAsync(secondKcId, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ExpireDueAsync_swallows_KC_NotFound_and_still_marks_expired()
    {
        // Idempotency contract: if the KC user was already deleted (or never existed),
        // expiration must still transition the invitation to Expired.
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kc = Substitute.For<IKeycloakAdminClient>();

        Guid kcUserId;
        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            var seeded = Invitation.Create("alice@example.com", KartovaRoles.Member,
                invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
                tenantId: tenant, clock: clock, tokenHash: InvitationToken.Hash("seed-token"));
            kcUserId = seeded.KeycloakUserId!.Value;
            seedDb.Invitations.Add(seeded);
            await seedDb.SaveChangesAsync();
        }

        kc.DeleteUserAsync(kcUserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.NotFound, "user already gone"));

        clock.Advance(TimeSpan.FromDays(8));
        await using var sp = BuildServices(opts, kc, clock);
        var (sut, hostSp) = BuildSut(clock);
        await using var _hostSp = hostSp;

        await sut.ExpireDueAsync(sp, CancellationToken.None);

        await using (var assertDb = new AdminOrganizationDbContext(opts))
        {
            var reloaded = await assertDb.Invitations.SingleAsync();
            Assert.AreEqual(InvitationStatus.Expired, reloaded.Status);
        }
    }
}
