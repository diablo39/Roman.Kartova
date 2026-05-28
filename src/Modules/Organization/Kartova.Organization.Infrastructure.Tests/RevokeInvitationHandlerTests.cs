using Kartova.Organization.Domain;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Kartova.Organization.Infrastructure.Tests;

/// <summary>
/// Behavioral tests for <see cref="RevokeInvitationHandler"/> — slice 9
/// spec §6.7. Three-state outcome (NotFound / NotPending / Ok), plus the
/// idempotent KC cleanup case where the user was already deleted.
/// </summary>
[TestClass]
public sealed class RevokeInvitationHandlerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-27T10:00:00Z");

    private static DbContextOptions<OrganizationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"revoke-{Guid.NewGuid()}")
            .Options;

    [TestMethod]
    public async Task Returns_NotFound_when_invitation_does_not_exist()
    {
        await using var db = new OrganizationDbContext(NewOptions());
        var clock = new FakeTimeProvider(T0);
        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = new RevokeInvitationHandler(db, kc, clock);

        var result = await sut.HandleAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.AreEqual(RevokeResult.NotFound, result);
        await kc.DidNotReceive().DeleteUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Returns_NotPending_when_invitation_already_accepted()
    {
        await using var db = new OrganizationDbContext(NewOptions());
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());

        var invitation = Invitation.Create("alice@example.com", KartovaRoles.Member,
            invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
            tenantId: tenant, clock: clock);
        invitation.MarkAccepted(clock);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = new RevokeInvitationHandler(db, kc, clock);

        var result = await sut.HandleAsync(invitation.Id.Value, CancellationToken.None);

        Assert.AreEqual(RevokeResult.NotPending, result);
        await kc.DidNotReceive().DeleteUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Happy_path_revokes_pending_invitation_and_deletes_kc_user()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kcUserId = Guid.NewGuid();

        Guid invitationId;
        await using (var seedDb = new OrganizationDbContext(opts))
        {
            var invitation = Invitation.Create("alice@example.com", KartovaRoles.Member,
                invitedByUserId: Guid.NewGuid(), keycloakUserId: kcUserId,
                tenantId: tenant, clock: clock);
            seedDb.Invitations.Add(invitation);
            await seedDb.SaveChangesAsync();
            invitationId = invitation.Id.Value;
        }

        var kc = Substitute.For<IKeycloakAdminClient>();
        await using (var actDb = new OrganizationDbContext(opts))
        {
            var sut = new RevokeInvitationHandler(actDb, kc, clock);
            var result = await sut.HandleAsync(invitationId, CancellationToken.None);
            Assert.AreEqual(RevokeResult.Ok, result);
        }

        await kc.Received(1).DeleteUserAsync(kcUserId, Arg.Any<CancellationToken>());

        await using (var assertDb = new OrganizationDbContext(opts))
        {
            var reloaded = await assertDb.Invitations.SingleAsync();
            Assert.AreEqual(InvitationStatus.Revoked, reloaded.Status);
            Assert.AreEqual(clock.GetUtcNow(), reloaded.RevokedAt);
        }
    }

    [TestMethod]
    public async Task Swallows_KC_NotFound_during_user_cleanup_and_still_revokes()
    {
        // Idempotency contract: the KC user may have been deleted by an out-of-band
        // process. The handler must still transition the invitation to Revoked.
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kcUserId = Guid.NewGuid();

        Guid invitationId;
        await using (var seedDb = new OrganizationDbContext(opts))
        {
            var invitation = Invitation.Create("alice@example.com", KartovaRoles.Member,
                invitedByUserId: Guid.NewGuid(), keycloakUserId: kcUserId,
                tenantId: tenant, clock: clock);
            seedDb.Invitations.Add(invitation);
            await seedDb.SaveChangesAsync();
            invitationId = invitation.Id.Value;
        }

        var kc = Substitute.For<IKeycloakAdminClient>();
        kc.DeleteUserAsync(kcUserId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.NotFound, "user already gone"));

        await using (var actDb = new OrganizationDbContext(opts))
        {
            var sut = new RevokeInvitationHandler(actDb, kc, clock);
            var result = await sut.HandleAsync(invitationId, CancellationToken.None);
            Assert.AreEqual(RevokeResult.Ok, result);
        }

        await using (var assertDb = new OrganizationDbContext(opts))
        {
            var reloaded = await assertDb.Invitations.SingleAsync();
            Assert.AreEqual(InvitationStatus.Revoked, reloaded.Status);
        }
    }
}
