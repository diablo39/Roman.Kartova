using System.Security.Claims;
using Kartova.Organization.Domain;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Kartova.Organization.Infrastructure.Tests;

/// <summary>
/// Behavioral tests for <see cref="SessionStartHandler"/>. After the slice-9
/// post-auth-hook refactor the handler owns three responsibilities inline:
/// <list type="bullet">
///   <item>upsert the local users projection from the JWT's OIDC claims;</item>
///   <item>flip a Pending invitation to Accepted when one matches the caller's
///     KC user id and is not expired;</item>
///   <item>compose the <see cref="Contracts.SessionStartResponse"/> wire shape
///     (Me + role + permissions + teams + org + optional AcceptedInvitation).</item>
/// </list>
/// Tests use NSubstitute for the directory + tenant-context seams and a real
/// <see cref="OrgProfileQueries"/> + <see cref="UserProjectionUpdater"/> against
/// the InMemory provider — same pattern as the sibling
/// <see cref="OrgProfileQueriesTests"/> and the slice-8 handler tests.
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
        IReadOnlyCollection<string> roles)
    {
        var ctx = Substitute.For<ITenantContext>();
        ctx.Id.Returns(tenant);
        ctx.IsTenantScoped.Returns(true);
        ctx.Roles.Returns(roles);
        return ctx;
    }

    private static ICurrentUser NewCurrentUser(
        Guid userId,
        IReadOnlyList<TeamMembershipInfo>? memberships = null)
    {
        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(userId);
        user.TeamMemberships.Returns(memberships ?? Array.Empty<TeamMembershipInfo>());
        return user;
    }

    private static ClaimsPrincipal BuildPrincipal(
        Guid userId,
        string email = "alice@example.com",
        string? given = "Alice",
        string? family = "Smith")
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("email", email),
        };
        if (given is not null) claims.Add(new Claim("given_name", given));
        if (family is not null) claims.Add(new Claim("family_name", family));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "test"));
    }

    private static Domain.Organization SeedOrg(OrganizationDbContext db, string displayName = "Acme Inc")
    {
        var clock = new FakeTimeProvider(T0);
        var org = Domain.Organization.Create(displayName, clock);
        db.Organizations.Add(org);
        db.SaveChanges();
        return org;
    }

    private static SessionStartHandler NewSut(
        OrganizationDbContext db,
        IUserDirectory directory,
        ICurrentUser currentUser,
        ITenantContext tenantCtx,
        TimeProvider? clock = null)
    {
        clock ??= new FakeTimeProvider(T0);
        return new SessionStartHandler(
            db,
            directory,
            new OrgProfileQueries(db),
            currentUser,
            tenantCtx,
            new UserProjectionUpdater(clock, NullLogger<UserProjectionUpdater>.Instance),
            clock);
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

        var sut = NewSut(
            db,
            directory,
            NewCurrentUser(userId, memberships),
            NewTenantCtx(tenant, new[] { KartovaRoles.OrgAdmin }));

        var response = await sut.HandleAsync(BuildPrincipal(userId), CancellationToken.None);

        Assert.AreEqual(userId, response.Me.Id);
        Assert.AreEqual("Alice Smith", response.Me.DisplayName);
        Assert.AreEqual("alice@example.com", response.Me.Email);
        Assert.AreEqual(KartovaRoles.OrgAdmin, response.Role);

        var expectedPerms = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
        CollectionAssert.AreEquivalent(expectedPerms.ToArray(), response.Permissions.ToArray());

        Assert.AreEqual(2, response.Teams.Count);
        var byTeam = response.Teams.ToDictionary(t => t.TeamId, t => t.Role);
        Assert.AreEqual("Admin", byTeam[teamA]);
        Assert.AreEqual("Member", byTeam[teamB]);

        Assert.AreEqual("Acme Inc", response.Organization.DisplayName);
        Assert.IsNull(response.AcceptedInvitation);

        // The handler must have upserted the users projection from JWT claims —
        // verify directly so a refactor that drops the upsert (regression) fails
        // here rather than only at the integration tier.
        var row = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.AreEqual("alice@example.com", row.Email);
        Assert.AreEqual("Alice Smith", row.DisplayName);
        Assert.AreEqual(tenant, row.TenantId);
    }

    [TestMethod]
    public async Task HandleAsync_throws_when_email_claim_missing()
    {
        // Email is a required bootstrap input — the users-projection upsert
        // needs a non-empty value to materialize a row, and the SPA's
        // OidcCallbackHandler always presents one. A token missing it is a
        // misconfiguration, not a degraded session.
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db);

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        var sut = NewSut(
            db,
            directory,
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.Member }));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("sub", userId.ToString()) }, "test"));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await sut.HandleAsync(principal, CancellationToken.None));
    }

    [TestMethod]
    public async Task HandleAsync_reads_email_claim_via_ClaimTypes_Email_fallback()
    {
        // DefaultClaimsTransformation may rewrite "email" → ClaimTypes.Email in
        // some pipeline configurations. Dual-lookup keeps the handler robust to
        // that upstream remapping — pin both forms.
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db);

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Alice", "alice@example.com"));

        var sut = NewSut(
            db,
            directory,
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.Member }));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim("sub", userId.ToString()),
                new Claim(ClaimTypes.Email, "alice@example.com"),
            }, "test"));

        var response = await sut.HandleAsync(principal, CancellationToken.None);

        Assert.AreEqual("alice@example.com", response.Me.Email);
        var row = await db.Users.SingleAsync(u => u.Id == userId);
        Assert.AreEqual("alice@example.com", row.Email);
    }

    [TestMethod]
    public async Task HandleAsync_throws_when_directory_returns_null_after_upsert()
    {
        // After the upsert the users row is guaranteed to exist; a directory
        // miss here is an internal invariant violation, not a degraded session.
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db);

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns((UserDisplayInfo?)null);

        var sut = NewSut(
            db,
            directory,
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.Member }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await sut.HandleAsync(BuildPrincipal(userId), CancellationToken.None));
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

        var sut = NewSut(
            db,
            directory,
            NewCurrentUser(userId),
            NewTenantCtx(tenant, Array.Empty<string>()));

        var response = await sut.HandleAsync(BuildPrincipal(userId), CancellationToken.None);

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

        var sut = NewSut(
            db,
            directory,
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.PlatformAdmin }));

        var response = await sut.HandleAsync(BuildPrincipal(userId), CancellationToken.None);

        Assert.AreEqual(KartovaRoles.PlatformAdmin, response.Role);
        Assert.AreEqual(0, response.Permissions.Count);
    }

    [TestMethod]
    public async Task HandleAsync_throws_InvalidOperationException_when_no_organization()
    {
        await using var db = NewInMemory(out var tenant);
        // No SeedOrg — DbSet is genuinely empty.

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Dan", "dan@example.com"));

        var sut = NewSut(
            db,
            directory,
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.OrgAdmin }));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await sut.HandleAsync(BuildPrincipal(userId), CancellationToken.None));
    }

    [TestMethod]
    public async Task HandleAsync_flips_Pending_invitation_and_emits_AcceptedInvitation()
    {
        // End-to-end happy path for the invitation auto-accept side effect: a
        // Pending invitation matching the caller's KC user id exists and has
        // not expired, so HandleAsync flips it to Accepted and emits the
        // welcome block.
        await using var db = NewInMemory(out var tenant);
        var org = SeedOrg(db, "Welcome Co");

        var clock = new FakeTimeProvider(T0);
        var userId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var invitation = Invitation.Create(
            "newcomer@example.com",
            KartovaRoles.Member,
            invitedByUserId: inviterId,
            keycloakUserId: userId,
            tenantId: tenant,
            clock: clock);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        // Advance the clock so AcceptedAt > InvitedAt is observable; still
        // inside the 7-day expiry window.
        clock.Advance(TimeSpan.FromMinutes(5));

        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Newcomer", "newcomer@example.com"));
        directory.GetAsync(inviterId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(inviterId, "Inviter Jane", "jane@example.com"));

        var sut = NewSut(
            db,
            directory,
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.Member }),
            clock: clock);

        var response = await sut.HandleAsync(
            BuildPrincipal(userId, "newcomer@example.com", "Newcomer", null),
            CancellationToken.None);

        Assert.IsNotNull(response.AcceptedInvitation);
        Assert.AreEqual("Welcome Co", response.AcceptedInvitation!.OrgDisplayName);
        Assert.AreEqual(inviterId, response.AcceptedInvitation.InvitedBy.Id);
        Assert.AreEqual("Inviter Jane", response.AcceptedInvitation.InvitedBy.DisplayName);
        Assert.AreEqual(invitation.InvitedAt, response.AcceptedInvitation.InvitedAt);
        Assert.AreEqual(clock.GetUtcNow(), response.AcceptedInvitation.AcceptedAt);
        Assert.AreEqual(org.DisplayName, response.AcceptedInvitation.OrgDisplayName);

        // DB: status is now Accepted, AcceptedAt populated.
        var row = await db.Invitations.SingleAsync();
        Assert.AreEqual(InvitationStatus.Accepted, row.Status);
        Assert.AreEqual(clock.GetUtcNow(), row.AcceptedAt);
    }

    [TestMethod]
    public async Task HandleAsync_returns_null_AcceptedInvitation_when_no_pending_invitation()
    {
        // No invitation row → the welcome block is null; the rest of the
        // response is still well-formed.
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db);

        var userId = Guid.NewGuid();
        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Alice", "alice@example.com"));

        var sut = NewSut(
            db,
            directory,
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.Member }));

        var response = await sut.HandleAsync(BuildPrincipal(userId), CancellationToken.None);

        Assert.IsNull(response.AcceptedInvitation);
    }

    [TestMethod]
    public async Task HandleAsync_skips_invitation_flip_when_pending_match_is_expired()
    {
        // Expired Pending invitations are owned by the background expirer (the
        // handler must NOT flip them to Accepted). The welcome block stays
        // null and the row stays Pending so the expirer can transition it.
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db);

        var clock = new FakeTimeProvider(T0);
        var userId = Guid.NewGuid();
        var invitation = Invitation.Create(
            "stale@example.com",
            KartovaRoles.Member,
            invitedByUserId: Guid.NewGuid(),
            keycloakUserId: userId,
            tenantId: tenant,
            clock: clock);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        // Advance past the 7-day expiry window so ExpiresAt < now.
        clock.Advance(TimeSpan.FromDays(8));

        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Stale", "stale@example.com"));

        var sut = NewSut(
            db,
            directory,
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.Member }),
            clock: clock);

        var response = await sut.HandleAsync(
            BuildPrincipal(userId, "stale@example.com"),
            CancellationToken.None);

        Assert.IsNull(response.AcceptedInvitation);
        var row = await db.Invitations.SingleAsync();
        Assert.AreEqual(InvitationStatus.Pending, row.Status,
            "Expired invitation must remain Pending — the background expirer handles it.");
    }

    [TestMethod]
    public async Task HandleAsync_returns_null_AcceptedInvitation_when_inviter_user_not_found()
    {
        // Defensive null-check path: invitation row flipped to Accepted in this
        // request, but the inviter has been hard-deleted from the local
        // projection (edge case — soft-delete is the norm). The handler must
        // NOT emit an AcceptedInvitation with a null InvitedBy.
        await using var db = NewInMemory(out var tenant);
        SeedOrg(db);

        var clock = new FakeTimeProvider(T0);
        var userId = Guid.NewGuid();
        var inviterId = Guid.NewGuid();
        var invitation = Invitation.Create(
            "newcomer@example.com",
            KartovaRoles.Member,
            invitedByUserId: inviterId,
            keycloakUserId: userId,
            tenantId: tenant,
            clock: clock);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        var directory = Substitute.For<IUserDirectory>();
        directory.GetAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new UserDisplayInfo(userId, "Newcomer", "newcomer@example.com"));
        directory.GetAsync(inviterId, Arg.Any<CancellationToken>())
            .Returns((UserDisplayInfo?)null);

        var sut = NewSut(
            db,
            directory,
            NewCurrentUser(userId),
            NewTenantCtx(tenant, new[] { KartovaRoles.Member }),
            clock: clock);

        var response = await sut.HandleAsync(
            BuildPrincipal(userId, "newcomer@example.com"),
            CancellationToken.None);

        Assert.IsNull(response.AcceptedInvitation);
        // The Pending → Accepted flip still happened in DB — the welcome block
        // suppression is a presentation-layer concern, not a rollback.
        var row = await db.Invitations.SingleAsync();
        Assert.AreEqual(InvitationStatus.Accepted, row.Status);
    }
}
