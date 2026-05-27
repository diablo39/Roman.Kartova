using System.Security.Claims;
using Kartova.Organization.Domain;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Organization.Infrastructure.Tests;

/// <summary>
/// Behavioral tests for <see cref="OrganizationPostAuthSyncHook"/> (slice 9 / task C4).
/// Exercises both the user-projection upsert and the invitation-acceptance side
/// effect via the InMemory provider — same pattern as <see cref="UserProjectionUpdaterTests"/>.
/// The hook is <c>internal sealed</c>; the test project accesses it via
/// <c>InternalsVisibleTo</c>.
/// </summary>
[TestClass]
public sealed class OrganizationPostAuthSyncHookTests
{
    private static readonly Guid UserId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static OrganizationDbContext NewInMemory()
    {
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"post-auth-hook-{Guid.NewGuid()}")
            .Options;
        return new OrganizationDbContext(opts);
    }

    private static ClaimsPrincipal BuildPrincipal(Guid userId, string email, string? given, string? family)
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

    private static ITenantContext NewScopedContext(TenantId tenant)
    {
        var ctx = new TenantContextAccessor();
        ctx.Populate(tenant, Array.Empty<string>());
        return ctx;
    }

    [TestMethod]
    public async Task Upserts_user_and_marks_pending_invitation_accepted_when_match_exists()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var tenant = new TenantId(Guid.NewGuid());
        var ctx = NewScopedContext(tenant);

        // Seed: a Pending invitation matching this user, not yet expired (Create
        // sets ExpiresAt = now + 7d, well in the future of the same `now`).
        var invitation = Invitation.Create(
            "alice@example.com",
            KartovaRoles.Member,
            invitedByUserId: Guid.NewGuid(),
            keycloakUserId: UserId,
            tenantId: tenant,
            clock: clock);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        var projection = new UserProjectionUpdater(clock);
        var sut = new OrganizationPostAuthSyncHook(db, projection, ctx, clock);
        var principal = BuildPrincipal(UserId, "alice@example.com", "Alice", "Smith");

        await sut.ExecuteAsync(principal, CancellationToken.None);

        var user = await db.Users.SingleAsync();
        Assert.AreEqual("alice@example.com", user.Email);
        Assert.AreEqual("Alice Smith", user.DisplayName);

        var accepted = await db.Invitations.SingleAsync();
        Assert.AreEqual(InvitationStatus.Accepted, accepted.Status);
        Assert.AreEqual(clock.GetUtcNow(), accepted.AcceptedAt);
        Assert.AreEqual(invitation.Id.Value, ctx.JustAcceptedInvitationId);
    }

    [TestMethod]
    public async Task Skips_invitation_when_no_pending_match_exists()
    {
        await using var db = NewInMemory();
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-27T10:00:00Z"));
        var tenant = new TenantId(Guid.NewGuid());
        var ctx = NewScopedContext(tenant);

        var projection = new UserProjectionUpdater(clock);
        var sut = new OrganizationPostAuthSyncHook(db, projection, ctx, clock);
        var principal = BuildPrincipal(UserId, "alice@example.com", "Alice", "Smith");

        await sut.ExecuteAsync(principal, CancellationToken.None);

        Assert.AreEqual(1, await db.Users.CountAsync(), "User row must still be upserted.");
        Assert.AreEqual(0, await db.Invitations.CountAsync(), "No invitation rows expected.");
        Assert.IsNull(ctx.JustAcceptedInvitationId);
    }

    [TestMethod]
    public async Task Skips_invitation_when_pending_match_has_expired()
    {
        await using var db = NewInMemory();
        // Construct an invitation at T-8d so its ExpiresAt = (T-8d) + 7d = T-1d (expired).
        var clock = new FakeTimeProvider(DateTimeOffset.Parse("2026-05-19T10:00:00Z"));
        var tenant = new TenantId(Guid.NewGuid());
        var ctx = NewScopedContext(tenant);

        var invitation = Invitation.Create(
            "alice@example.com",
            KartovaRoles.Member,
            invitedByUserId: Guid.NewGuid(),
            keycloakUserId: UserId,
            tenantId: tenant,
            clock: clock);
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync();

        // Advance the clock past the 7-day expiry window.
        clock.Advance(TimeSpan.FromDays(8));

        var projection = new UserProjectionUpdater(clock);
        var sut = new OrganizationPostAuthSyncHook(db, projection, ctx, clock);
        var principal = BuildPrincipal(UserId, "alice@example.com", "Alice", "Smith");

        await sut.ExecuteAsync(principal, CancellationToken.None);

        Assert.AreEqual(1, await db.Users.CountAsync(), "User row must still be upserted.");
        var stillPending = await db.Invitations.SingleAsync();
        Assert.AreEqual(InvitationStatus.Pending, stillPending.Status,
            "Expired invitation must remain Pending — the background expirer handles it.");
        Assert.IsNull(ctx.JustAcceptedInvitationId);
    }
}
