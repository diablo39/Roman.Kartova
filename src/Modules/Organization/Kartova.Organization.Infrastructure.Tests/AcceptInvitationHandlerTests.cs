using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure.Admin;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Kartova.Organization.Infrastructure.Tests;

/// <summary>
/// Behavioral tests for <see cref="AcceptInvitationHandler"/> — slice 9 Task 7.
/// Covers token resolution (NotFound / state guards), GetContextAsync context data,
/// AcceptAsync happy path (KC calls, persistence), validation rejection, and
/// KC-NotFound translation to GoneAlreadyUsed.
/// </summary>
[TestClass]
public sealed class AcceptInvitationHandlerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-27T10:00:00Z");
    private const string Token = "test-acceptance-token";

    private static DbContextOptions<AdminOrganizationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<AdminOrganizationDbContext>()
            .UseInMemoryDatabase($"accept-{Guid.NewGuid()}")
            .Options;

    private static AcceptInvitationHandler MakeSut(
        AdminOrganizationDbContext db,
        IKeycloakAdminClient kc,
        FakeTimeProvider clock) =>
        new(db, kc, clock, NullLogger<AcceptInvitationHandler>.Instance);

    // ------------------------------------------------------------------ helpers

    private static async Task<Invitation> SeedPendingInvitationAsync(
        AdminOrganizationDbContext db,
        TenantId tenant,
        FakeTimeProvider clock,
        Guid? kcUserId = null,
        string email = "alice@example.com",
        string token = Token)
    {
        var inv = Invitation.Create(
            email, KartovaRoles.Member,
            invitedByUserId: Guid.NewGuid(),
            keycloakUserId: kcUserId ?? Guid.NewGuid(),
            tenantId: tenant,
            clock: clock,
            tokenHash: InvitationToken.Hash(token));
        db.Invitations.Add(inv);
        await db.SaveChangesAsync();
        return inv;
    }

    // =================================================================
    // ResolveAsync — token/state guards (affects both GetContextAsync and AcceptAsync)
    // =================================================================

    [TestMethod]
    public async Task Unknown_token_GetContextAsync_returns_NotFound()
    {
        var opts = NewOptions();
        await using var db = new AdminOrganizationDbContext(opts);
        var kc = Substitute.For<IKeycloakAdminClient>();
        var clock = new FakeTimeProvider(T0);
        var sut = MakeSut(db, kc, clock);

        var result = await sut.GetContextAsync("no-such-token", CancellationToken.None);

        var failed = result as GetAcceptContextResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.NotFound, failed!.Error);
    }

    [TestMethod]
    public async Task Unknown_token_AcceptAsync_returns_NotFound()
    {
        var opts = NewOptions();
        await using var db = new AdminOrganizationDbContext(opts);
        var kc = Substitute.For<IKeycloakAdminClient>();
        var clock = new FakeTimeProvider(T0);
        var sut = MakeSut(db, kc, clock);

        var result = await sut.AcceptAsync("no-such-token", "ValidP@ssword1!", "Alice", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.NotFound, failed!.Error);
        await kc.DidNotReceive().SetPasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Revoked_invitation_returns_GoneRevoked()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            var inv = await SeedPendingInvitationAsync(seedDb, tenant, clock);
            inv.Revoke(clock);
            await seedDb.SaveChangesAsync();
        }

        await using var db = new AdminOrganizationDbContext(opts);
        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = MakeSut(db, kc, clock);

        var result = await sut.AcceptAsync(Token, "ValidP@ssword1!", "Alice", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.GoneRevoked, failed!.Error);
        await kc.DidNotReceive().SetPasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Expired_status_invitation_returns_GoneExpired()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            var inv = await SeedPendingInvitationAsync(seedDb, tenant, clock);
            inv.MarkExpired(clock);
            await seedDb.SaveChangesAsync();
        }

        await using var db = new AdminOrganizationDbContext(opts);
        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = MakeSut(db, kc, clock);

        var result = await sut.AcceptAsync(Token, "ValidP@ssword1!", "Alice", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.GoneExpired, failed!.Error);
    }

    [TestMethod]
    public async Task Pending_invitation_past_ExpiresAt_returns_GoneExpired()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            // Invitation.Create sets ExpiresAt = now + 7 days; advance clock past that.
            await SeedPendingInvitationAsync(seedDb, tenant, clock);
        }

        clock.Advance(TimeSpan.FromDays(8));   // now past ExpiresAt

        await using var db = new AdminOrganizationDbContext(opts);
        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = MakeSut(db, kc, clock);

        var result = await sut.AcceptAsync(Token, "ValidP@ssword1!", "Alice", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.GoneExpired, failed!.Error);
        await kc.DidNotReceive().SetPasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    // =================================================================
    // GetContextAsync — happy path
    // =================================================================

    [TestMethod]
    public async Task GetContextAsync_valid_token_returns_populated_context()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var inviterId = Guid.NewGuid();
        const string inviterDisplayName = "Bob Smith";
        const string orgDisplayName = "Acme Corp";

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            // Seed inviter user row
            seedDb.Users.Add(new Kartova.Organization.Domain.User
            {
                Id = inviterId,
                TenantId = tenant,
                Email = "bob@example.com",
                DisplayName = inviterDisplayName,
                CreatedAt = T0,
            });

            // Seed org row
            var org = Kartova.Organization.Domain.Organization.Create(orgDisplayName, clock);
            // Organization.Create creates a new TenantId equal to org.Id — we need an org
            // for the same TenantId as the invitation. Use the seeded tenant's Guid
            // for the org so o.TenantId == inv.TenantId works in the query.
            seedDb.Organizations.Add(org);

            // Seed the invitation referencing the tenant that matches the org.TenantId
            var inv = Invitation.Create(
                "alice@example.com", KartovaRoles.Member,
                invitedByUserId: inviterId,
                keycloakUserId: Guid.NewGuid(),
                tenantId: org.TenantId,   // use the org's own TenantId
                clock: clock,
                tokenHash: InvitationToken.Hash(Token));
            seedDb.Invitations.Add(inv);
            await seedDb.SaveChangesAsync();
        }

        await using var db = new AdminOrganizationDbContext(opts);
        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = MakeSut(db, kc, clock);

        var result = await sut.GetContextAsync(Token, CancellationToken.None);

        var ok = result as GetAcceptContextResult.Ok;
        Assert.IsNotNull(ok);
        Assert.AreEqual(orgDisplayName, ok!.Context.OrgDisplayName);
        Assert.AreEqual("alice@example.com", ok.Context.Email);
        Assert.AreEqual(KartovaRoles.Member, ok.Context.Role);
        Assert.AreEqual(inviterDisplayName, ok.Context.InvitedByDisplayName);
        Assert.AreEqual("alice", ok.Context.DefaultDisplayName);   // localPart of email
        Assert.AreEqual(T0.AddDays(7), ok.Context.ExpiresAt);
    }

    // =================================================================
    // AcceptAsync — happy path
    // =================================================================

    [TestMethod]
    public async Task AcceptAsync_valid_returns_Ok_calls_KC_and_burns_token()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kcUserId = Guid.NewGuid();

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            await SeedPendingInvitationAsync(seedDb, tenant, clock, kcUserId: kcUserId);
        }

        var kc = Substitute.For<IKeycloakAdminClient>();
        await using (var actDb = new AdminOrganizationDbContext(opts))
        {
            var sut = MakeSut(actDb, kc, clock);
            var result = await sut.AcceptAsync(Token, "ValidP@ssword1!", "  Alice  ", CancellationToken.None);

            var ok = result as AcceptInvitationResult.Ok;
            Assert.IsNotNull(ok);
            Assert.AreEqual("alice@example.com", ok!.Email);
        }

        // KC: SetPasswordAsync called once with correct arguments (temporary=false)
        await kc.Received(1).SetPasswordAsync(kcUserId, "ValidP@ssword1!", false, Arg.Any<CancellationToken>());

        // KC: UpdateUserAsync called once — EmailVerified=true, FirstName trimmed, LastName null, RequiredActions empty
        await kc.Received(1).UpdateUserAsync(
            kcUserId,
            Arg.Is<UpdateKeycloakUserRequest>(r =>
                r.EmailVerified == true &&
                r.FirstName == "Alice" &&            // whitespace trimmed
                r.LastName == null &&
                r.RequiredActions.Count == 0),
            Arg.Any<CancellationToken>());

        // Persistence: token burned (null), CredentialSetAt set, Status still Pending
        await using (var assertDb = new AdminOrganizationDbContext(opts))
        {
            var saved = await assertDb.Invitations.SingleAsync();
            Assert.IsNull(saved.TokenHash, "Token hash must be null after credential set (single-use burned).");
            Assert.IsNotNull(saved.CredentialSetAt, "CredentialSetAt must be stamped.");
            Assert.AreEqual(T0, saved.CredentialSetAt!.Value);
            Assert.AreEqual(InvitationStatus.Pending, saved.Status, "Status must stay Pending — flips to Accepted on first login.");
        }
    }

    // =================================================================
    // AcceptAsync — validation rejections
    // =================================================================

    [TestMethod]
    public async Task AcceptAsync_password_too_short_returns_Validation_and_kc_not_called()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());

        await using var db = new AdminOrganizationDbContext(opts);
        await SeedPendingInvitationAsync(db, tenant, clock);

        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = MakeSut(db, kc, clock);

        var result = await sut.AcceptAsync(Token, "short", "Alice", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.Validation, failed!.Error);
        await kc.DidNotReceive().SetPasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await kc.DidNotReceive().UpdateUserAsync(Arg.Any<Guid>(), Arg.Any<UpdateKeycloakUserRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AcceptAsync_empty_display_name_returns_Validation_and_kc_not_called()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());

        await using var db = new AdminOrganizationDbContext(opts);
        await SeedPendingInvitationAsync(db, tenant, clock);

        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = MakeSut(db, kc, clock);

        // Whitespace-only trims to empty — must reject
        var result = await sut.AcceptAsync(Token, "ValidP@ssword1!", "   ", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.Validation, failed!.Error);
        await kc.DidNotReceive().SetPasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await kc.DidNotReceive().UpdateUserAsync(Arg.Any<Guid>(), Arg.Any<UpdateKeycloakUserRequest>(), Arg.Any<CancellationToken>());
    }

    // =================================================================
    // AcceptAsync — KC NotFound translates to GoneAlreadyUsed
    // =================================================================

    [TestMethod]
    public async Task AcceptAsync_kc_SetPassword_NotFound_returns_GoneAlreadyUsed()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kcUserId = Guid.NewGuid();

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            await SeedPendingInvitationAsync(seedDb, tenant, clock, kcUserId: kcUserId);
        }

        var kc = Substitute.For<IKeycloakAdminClient>();
        kc.SetPasswordAsync(kcUserId, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.NotFound, "user gone"));

        await using var db = new AdminOrganizationDbContext(opts);
        var sut = MakeSut(db, kc, clock);

        var result = await sut.AcceptAsync(Token, "ValidP@ssword1!", "Alice", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.GoneAlreadyUsed, failed!.Error);
    }

    [TestMethod]
    public async Task AcceptAsync_kc_UpdateUser_NotFound_returns_GoneAlreadyUsed()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kcUserId = Guid.NewGuid();

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            await SeedPendingInvitationAsync(seedDb, tenant, clock, kcUserId: kcUserId);
        }

        var kc = Substitute.For<IKeycloakAdminClient>();
        // SetPasswordAsync succeeds; UpdateUserAsync throws NotFound
        kc.SetPasswordAsync(kcUserId, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        kc.UpdateUserAsync(kcUserId, Arg.Any<UpdateKeycloakUserRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.NotFound, "user gone"));

        await using var db = new AdminOrganizationDbContext(opts);
        var sut = MakeSut(db, kc, clock);

        var result = await sut.AcceptAsync(Token, "ValidP@ssword1!", "Alice", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.GoneAlreadyUsed, failed!.Error);
    }

    // =================================================================
    // AcceptAsync — Status==Accepted (token still present) → GoneAlreadyUsed
    // =================================================================

    [TestMethod]
    public async Task AcceptAsync_status_Accepted_returns_GoneAlreadyUsed()
    {
        // Seed an invitation that has been accepted (Status=Accepted) but whose
        // TokenHash is still set (simulates MarkAccepted called without burning the token,
        // e.g., the status was transitioned independently of MarkCredentialSet).
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            var inv = await SeedPendingInvitationAsync(seedDb, tenant, clock, token: "ACC");
            inv.MarkAccepted(clock);
            await seedDb.SaveChangesAsync();
        }

        await using var db = new AdminOrganizationDbContext(opts);
        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = MakeSut(db, kc, clock);

        var result = await sut.AcceptAsync("ACC", "ValidP@ssword1!", "Alice", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.GoneAlreadyUsed, failed!.Error);
        await kc.DidNotReceive().SetPasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    // =================================================================
    // AcceptAsync — KC Unexpected error translates to Upstream
    // =================================================================

    [TestMethod]
    public async Task AcceptAsync_kc_SetPassword_Unexpected_returns_Upstream()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kcUserId = Guid.NewGuid();

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            await SeedPendingInvitationAsync(seedDb, tenant, clock, kcUserId: kcUserId);
        }

        var kc = Substitute.For<IKeycloakAdminClient>();
        kc.SetPasswordAsync(kcUserId, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.Unexpected, "x"));

        await using var db = new AdminOrganizationDbContext(opts);
        var sut = MakeSut(db, kc, clock);

        var result = await sut.AcceptAsync(Token, "ValidP@ssword1!", "Alice", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.Upstream, failed!.Error);
    }

    // =================================================================
    // AcceptAsync — boundary validation (password length limits, null password, displayName limit)
    // =================================================================

    [TestMethod]
    public async Task AcceptAsync_password_length_129_returns_Validation_kc_not_called()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());

        await using var db = new AdminOrganizationDbContext(opts);
        await SeedPendingInvitationAsync(db, tenant, clock);

        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = MakeSut(db, kc, clock);

        // 129 chars — one over the 128-char maximum.
        var result = await sut.AcceptAsync(Token, new string('a', 129), "Alice", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.Validation, failed!.Error);
        await kc.DidNotReceive().SetPasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AcceptAsync_null_password_returns_Validation_kc_not_called()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());

        await using var db = new AdminOrganizationDbContext(opts);
        await SeedPendingInvitationAsync(db, tenant, clock);

        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = MakeSut(db, kc, clock);

        var result = await sut.AcceptAsync(Token, null!, "Alice", CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.Validation, failed!.Error);
        await kc.DidNotReceive().SetPasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task AcceptAsync_displayName_length_129_returns_Validation_kc_not_called()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());

        await using var db = new AdminOrganizationDbContext(opts);
        await SeedPendingInvitationAsync(db, tenant, clock);

        var kc = Substitute.For<IKeycloakAdminClient>();
        var sut = MakeSut(db, kc, clock);

        // 129 chars — one over the 128-char maximum for display name.
        var result = await sut.AcceptAsync(Token, "ValidP@ssword1!", new string('a', 129), CancellationToken.None);

        var failed = result as AcceptInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(AcceptInvitationError.Validation, failed!.Error);
        await kc.DidNotReceive().SetPasswordAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    // =================================================================
    // Burned-token single-use guard
    // =================================================================

    [TestMethod]
    public async Task Burned_token_second_AcceptAsync_call_returns_NotFound()
    {
        // After a successful AcceptAsync, the TokenHash is null.
        // A second attempt with the same token must return NotFound
        // (null TokenHash ≠ computed hash → row not found).
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var tenant = new TenantId(Guid.NewGuid());
        var kcUserId = Guid.NewGuid();

        await using (var seedDb = new AdminOrganizationDbContext(opts))
        {
            await SeedPendingInvitationAsync(seedDb, tenant, clock, kcUserId: kcUserId);
        }

        var kc = Substitute.For<IKeycloakAdminClient>();

        // First call — should succeed
        await using (var db1 = new AdminOrganizationDbContext(opts))
        {
            var sut = MakeSut(db1, kc, clock);
            var first = await sut.AcceptAsync(Token, "ValidP@ssword1!", "Alice", CancellationToken.None);
            Assert.IsInstanceOfType<AcceptInvitationResult.Ok>(first);
        }

        // Second call with the same token — burned, must return NotFound
        await using (var db2 = new AdminOrganizationDbContext(opts))
        {
            var sut = MakeSut(db2, kc, clock);
            var second = await sut.AcceptAsync(Token, "ValidP@ssword1!", "Alice", CancellationToken.None);
            var failed = second as AcceptInvitationResult.Failed;
            Assert.IsNotNull(failed);
            Assert.AreEqual(AcceptInvitationError.NotFound, failed!.Error);
        }
    }
}
