using Kartova.Organization.Domain;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Kartova.Organization.Infrastructure.Tests;

/// <summary>
/// Behavioral tests for <see cref="SessionStartHandler"/> (slice 9 task D7).
/// The handler is a composition handler — its observable behavior is the wire
/// shape of <see cref="Contracts.SessionStartResponse"/>. Tests use NSubstitute
/// for the seam dependencies (<see cref="IUserDirectory"/>, <see cref="ICurrentUser"/>,
/// <see cref="ITenantContext"/>) and a real <see cref="OrgProfileQueries"/>
/// against the InMemory provider — same pattern as the D2 sibling
/// (<see cref="OrgProfileQueriesTests"/>) and the slice-8 handler tests.
/// </summary>
[TestClass]
public sealed class SessionStartHandlerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-27T10:00:00Z");

    private static OrganizationDbContext NewInMemory(out TenantId tenant)
    {
        tenant = new TenantId(Guid.NewGuid());
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"session-{Guid.NewGuid()}").Options;
        return new OrganizationDbContext(opts);
    }

    private static ITenantContext NewTenantCtx(
        TenantId tenant,
        IReadOnlyCollection<string> roles,
        Guid? justAcceptedInvitationId = null)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.Id.Returns(tenant);
        ctx.IsTenantScoped.Returns(true);
        ctx.Roles.Returns(roles);
        ctx.JustAcceptedInvitationId.Returns(justAcceptedInvitationId);
        return ctx;
    }

    private static ICurrentUser NewCurrentUser(
        Guid userId,
        IReadOnlyList<TeamMembershipInfo>? memberships = null,
        Guid? justAcceptedInvitationId = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.TeamMemberships.Returns(memberships ?? Array.Empty<TeamMembershipInfo>());
        user.JustAcceptedInvitationId.Returns(justAcceptedInvitationId);
        return user;
    }

    private static Domain.Organization SeedOrg(OrganizationDbContext db, string displayName = "Acme Inc")
    {
        var clock = new FakeTimeProvider(T0);
        var org = Domain.Organization.Create(displayName, clock);
        db.Organizations.Add(org);
        db.SaveChanges();
        return org;
    }

    [TestMethod]
    public async Task HandleAsync_returns_response_with_caller_identity_role_permissions_teams()
    {
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db, "Acme Inc");

        var userId = Guid.NewGuid();
        var teamA = Guid.NewGuid();
        var teamB = Guid.NewGuid();
        var memberships = new List<TeamMembershipInfo>
        {
            new(teamA, TeamRoleKind.Admin),
            new(teamB, TeamRoleKind.Member),
        };

        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Alice Smith", "alice@example.com"));

        var currentUser = NewCurrentUser(userId, memberships);
        var tenantCtx = NewTenantCtx(tenant, new[] { KartovaRoles.OrgAdmin });

        var sut = new SessionStartHandler(
            db,
            directory,
            new OrgProfileQueries(db),
            currentUser,
            tenantCtx);

        var response = await sut.HandleAsync(CancellationToken.None);

        Assert.AreEqual(userId, response.Me.Id);
        Assert.AreEqual("Alice Smith", response.Me.DisplayName);
        Assert.AreEqual("alice@example.com", response.Me.Email);
        Assert.AreEqual(KartovaRoles.OrgAdmin, response.Role);

        var expectedPerms = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
        CollectionAssert.AreEquivalent(expectedPerms.ToArray(), response.Permissions.ToArray());

        Assert.AreEqual(2, response.Teams.Count);
        // TeamRoleKind.ToString() is the canonical wire form — matches
        // TeamMemberResponse + GetMePermissions.
        var byTeam = response.Teams.ToDictionary(t => t.TeamId, t => t.Role);
        Assert.AreEqual("Admin", byTeam[teamA]);
        Assert.AreEqual("Member", byTeam[teamB]);

        Assert.AreEqual("Acme Inc", response.Organization.DisplayName);
        Assert.IsNull(response.AcceptedInvitation);
    }

    [TestMethod]
    public async Task HandleAsync_falls_back_to_user_id_string_when_directory_returns_null()
    {
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db);

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns((UserDisplayInfo?)null);

        var sut = new SessionStartHandler(
            db,
            directory,
            new OrgProfileQueries(db),
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.Member }));

        var response = await sut.HandleAsync(CancellationToken.None);

        Assert.AreEqual(userId, response.Me.Id);
        Assert.AreEqual(userId.ToString(), response.Me.DisplayName);
        Assert.AreEqual("", response.Me.Email);
    }

    [TestMethod]
    public async Task HandleAsync_falls_back_to_Viewer_when_tenant_roles_is_empty()
    {
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db);

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Bob", "bob@example.com"));

        var sut = new SessionStartHandler(
            db,
            directory,
            new OrgProfileQueries(db),
            NewCurrentUser(userId),
            NewTenantCtx(tenant, Array.Empty<string>()));

        var response = await sut.HandleAsync(CancellationToken.None);

        Assert.AreEqual(KartovaRoles.Viewer, response.Role);
        var expectedPerms = KartovaRolePermissions.ForRole(KartovaRoles.Viewer);
        CollectionAssert.AreEquivalent(expectedPerms.ToArray(), response.Permissions.ToArray());
    }

    [TestMethod]
    public async Task HandleAsync_returns_empty_permissions_when_role_outside_tenant_map()
    {
        // PlatformAdmin is deliberately absent from KartovaRolePermissions.Map
        // (orthogonal to tenants). ForRole returns EmptySet rather than throwing,
        // so a PlatformAdmin who somehow reaches the tenant-scoped endpoint
        // surfaces as a Role string + empty Permissions list — never a 500.
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db);

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Carol", "carol@example.com"));

        var sut = new SessionStartHandler(
            db,
            directory,
            new OrgProfileQueries(db),
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.PlatformAdmin }));

        var response = await sut.HandleAsync(CancellationToken.None);

        Assert.AreEqual(KartovaRoles.PlatformAdmin, response.Role);
        Assert.AreEqual(0, response.Permissions.Count);
    }

    [TestMethod]
    public async Task HandleAsync_throws_InvalidOperationException_when_no_organization()
    {
        await using var db = NewInMemory(out var tenant);
        // No SeedOrg — RLS would normally hide cross-tenant rows; here the DbSet
        // is genuinely empty.

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Dan", "dan@example.com"));

        var sut = new SessionStartHandler(
            db,
            directory,
            new OrgProfileQueries(db),
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.OrgAdmin }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await sut.HandleAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task HandleAsync_returns_AcceptedInvitation_when_JustAcceptedInvitationId_is_set()
    {
        await using var db = NewInMemory(out var tenant);
        var org = SeedOrg(db, "Welcome Co");

        var clock = new FakeTimeProvider(T0);
        var inviterId = Guid.NewGuid();
        var invitation = Invitation.Create(
            "newcomer@example.com",
            KartovaRoles.Member,
            invitedByUserId: inviterId,
            keycloakUserId: Guid.NewGuid(),
            tenantId: tenant,
            clock: clock);
        clock.Advance(TimeSpan.FromMinutes(5));
        invitation.MarkAccepted(clock);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Newcomer", "newcomer@example.com"));
        directory.GetAsync(inviterId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(inviterId, "Inviter Jane", "jane@example.com"));

        var sut = new SessionStartHandler(
            db,
            directory,
            new OrgProfileQueries(db),
            NewCurrentUser(userId, justAcceptedInvitationId: invitation.Id.Value),
            NewTenantCtx(tenant, new[] { KartovaRoles.Member }));

        var response = await sut.HandleAsync(CancellationToken.None);

        Assert.IsNotNull(response.AcceptedInvitation);
        Assert.AreEqual("Welcome Co", response.AcceptedInvitation!.OrgDisplayName);
        Assert.AreEqual(inviterId, response.AcceptedInvitation.InvitedBy.Id);
        Assert.AreEqual("Inviter Jane", response.AcceptedInvitation.InvitedBy.DisplayName);
        Assert.AreEqual(invitation.InvitedAt, response.AcceptedInvitation.InvitedAt);
        Assert.AreEqual(invitation.AcceptedAt!.Value, response.AcceptedInvitation.AcceptedAt);
        // Sanity: OrgDisplayName came from the OrgProfile read path, not a
        // stale snapshot. Verify by mutating the seed and re-running would be
        // excessive — equality with org.DisplayName is enough.
        Assert.AreEqual(org.DisplayName, response.AcceptedInvitation.OrgDisplayName);
    }

    [TestMethod]
    public async Task HandleAsync_returns_null_AcceptedInvitation_when_invitation_id_not_found()
    {
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db);

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Alice", "alice@example.com"));

        // JustAcceptedInvitationId set to a Guid that doesn't exist in DB —
        // e.g. a stale claim or a race against an out-of-band delete. The
        // handler must surface a normal session (no welcome block) instead of
        // 500-ing.
        var sut = new SessionStartHandler(
            db,
            directory,
            new OrgProfileQueries(db),
            NewCurrentUser(userId, justAcceptedInvitationId: Guid.NewGuid()),
            NewTenantCtx(tenant, new[] { KartovaRoles.Member }));

        var response = await sut.HandleAsync(CancellationToken.None);

        Assert.IsNull(response.AcceptedInvitation);
    }

    [TestMethod]
    public async Task HandleAsync_returns_null_AcceptedInvitation_when_inviter_user_not_found()
    {
        // Defensive null-check path: invitation row exists + is Accepted, but
        // the inviter has been hard-deleted from the local projection (edge
        // case — would normally never happen because we soft-delete). The
        // handler must NOT emit an AcceptedInvitation with a null InvitedBy.
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db);

        var clock = new FakeTimeProvider(T0);
        var inviterId = Guid.NewGuid();
        var invitation = Invitation.Create(
            "newcomer@example.com",
            KartovaRoles.Member,
            invitedByUserId: inviterId,
            keycloakUserId: Guid.NewGuid(),
            tenantId: tenant,
            clock: clock);
        clock.Advance(TimeSpan.FromMinutes(5));
        invitation.MarkAccepted(clock);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Newcomer", "newcomer@example.com"));
        directory.GetAsync(inviterId, Arg.Any<CancellationToken>())
            .Returns((UserDisplayInfo?)null);

        var sut = new SessionStartHandler(
            db,
            directory,
            new OrgProfileQueries(db),
            NewCurrentUser(userId, justAcceptedInvitationId: invitation.Id.Value),
            NewTenantCtx(tenant, new[] { KartovaRoles.Member }));

        var response = await sut.HandleAsync(CancellationToken.None);

        Assert.IsNull(response.AcceptedInvitation);
    }
}
