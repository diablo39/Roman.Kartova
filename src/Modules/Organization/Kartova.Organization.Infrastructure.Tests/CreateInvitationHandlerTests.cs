using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Kartova.Organization.Infrastructure.Tests;

/// <summary>
/// Behavioral tests for <see cref="CreateInvitationHandler"/> — slice 9
/// spec §6.7. Covers the three-way email conflict matrix, KeyCloak
/// compensation on role-assignment failure, validation rejections, and the
/// happy path with three-context persistence verification (a missing
/// SaveChangesAsync surfaces as an empty assert-context).
/// </summary>
[TestClass]
public sealed class CreateInvitationHandlerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-05-27T10:00:00Z");

    private static DbContextOptions<OrganizationDbContext> NewOptions() =>
        new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"inv-{Guid.NewGuid()}")
            .Options;

    private static (CreateInvitationHandler h, OrganizationDbContext db, IKeycloakAdminClient kc, TenantId tenant, Guid currentUserId)
        Make(FakeTimeProvider clock, DbContextOptions<OrganizationDbContext>? options = null)
    {
        var tenant = new TenantId(Guid.NewGuid());
        var opts = options ?? NewOptions();
        var db = new OrganizationDbContext(opts);

        var kc = Substitute.For<IKeycloakAdminClient>();
        var tenantCtx = Substitute.For<ITenantContext>();
        tenantCtx.Id.Returns(tenant);
        tenantCtx.IsTenantScoped.Returns(true);

        var currentUser = Substitute.For<ICurrentUser>();
        var currentUserId = Guid.NewGuid();
        currentUser.UserId.Returns(currentUserId);

        var koOptions = Options.Create(new KeycloakAdminOptions
        {
            BaseUrl = "x",
            Realm = "x",
            AdminClientId = "x",
            AdminClientSecret = "x",
            FrontendBaseUrl = "http://localhost:5173",
        });

        var h = new CreateInvitationHandler(db, kc, tenantCtx, currentUser, clock, koOptions);
        return (h, db, kc, tenant, currentUserId);
    }

    [TestMethod]
    public async Task Returns_Validation_when_email_is_empty()
    {
        var clock = new FakeTimeProvider(T0);
        var (h, db, _, _, _) = Make(clock);
        await using var _db = db;

        var result = await h.HandleAsync(new CreateInvitationRequest("", KartovaRoles.Member), CancellationToken.None);

        var failed = result as CreateInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(CreateInvitationError.Validation, failed!.Error);
    }

    [TestMethod]
    public async Task Returns_Validation_when_email_lacks_at_sign()
    {
        var clock = new FakeTimeProvider(T0);
        var (h, db, _, _, _) = Make(clock);
        await using var _db = db;

        var result = await h.HandleAsync(new CreateInvitationRequest("not-an-email", KartovaRoles.Member), CancellationToken.None);

        var failed = result as CreateInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(CreateInvitationError.Validation, failed!.Error);
    }

    [TestMethod]
    public async Task Returns_Validation_when_role_unknown()
    {
        var clock = new FakeTimeProvider(T0);
        var (h, db, kc, _, _) = Make(clock);
        await using var _db = db;

        var result = await h.HandleAsync(new CreateInvitationRequest("alice@example.com", "BogusRole"), CancellationToken.None);

        var failed = result as CreateInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(CreateInvitationError.Validation, failed!.Error);
        // KC must NOT be touched when input validation fails — proves the role
        // allow-list is enforced before any side-effect.
        await kc.DidNotReceive().CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Returns_EmailAlreadyInTenant_when_users_row_exists()
    {
        var clock = new FakeTimeProvider(T0);
        var (h, db, kc, tenant, _) = Make(clock);
        await using var _db = db;

        // Seed an existing User row in the same tenant — simulates a previously
        // accepted invitation under the same email.
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            Email = "alice@example.com",
            DisplayName = "Alice",
            CreatedAt = T0,
        });
        await db.SaveChangesAsync();

        var result = await h.HandleAsync(new CreateInvitationRequest("alice@example.com", KartovaRoles.Member), CancellationToken.None);

        var failed = result as CreateInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(CreateInvitationError.EmailAlreadyInTenant, failed!.Error);
        await kc.DidNotReceive().CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Returns_EmailAlreadyInvited_when_pending_invitation_exists_case_insensitive()
    {
        var clock = new FakeTimeProvider(T0);
        var (h, db, kc, tenant, _) = Make(clock);
        await using var _db = db;

        // Invitation.Create lowercases the stored email; the handler also
        // lowercases the incoming request — so a request with mixed-case input
        // matches a previously-stored pending invitation.
        var existing = Invitation.Create("alice@example.com", KartovaRoles.Member,
            invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
            tenantId: tenant, clock: clock);
        db.Invitations.Add(existing);
        await db.SaveChangesAsync();

        var result = await h.HandleAsync(new CreateInvitationRequest("Alice@Example.COM", KartovaRoles.Member), CancellationToken.None);

        var failed = result as CreateInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(CreateInvitationError.EmailAlreadyInvited, failed!.Error);
        await kc.DidNotReceive().CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task Returns_EmailAlreadyOnPlatform_when_keycloak_returns_conflict()
    {
        var clock = new FakeTimeProvider(T0);
        var (h, db, kc, _, _) = Make(clock);
        await using var _db = db;

        kc.CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.EmailAlreadyExists, "email taken"));

        var result = await h.HandleAsync(new CreateInvitationRequest("alice@example.com", KartovaRoles.Member), CancellationToken.None);

        var failed = result as CreateInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(CreateInvitationError.EmailAlreadyOnPlatform, failed!.Error);
    }

    [TestMethod]
    public async Task Happy_path_creates_kc_user_and_db_invitation_and_user_row()
    {
        var opts = NewOptions();
        var clock = new FakeTimeProvider(T0);
        var kcId = Guid.NewGuid();

        // Three-context persistence pattern: a missing SaveChangesAsync would
        // leave the assertDb empty, surfacing the mutation.
        var (h, db, kc, tenant, inviterId) = Make(clock, opts);
        await using (db)
        {
            kc.CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>())
                .Returns(kcId);

            var result = await h.HandleAsync(new CreateInvitationRequest("alice@example.com", KartovaRoles.Member), CancellationToken.None);

            var created = result as CreateInvitationResult.Created;
            Assert.IsNotNull(created);
            // Spec §9.2 step 8: `?invitation=1` is a deliberate sentinel
            // (auto-accept is keyed off the authenticated email, not a token).
            // Slice-9 H4 API-1 fix added an `email` hint so the invitee sees
            // the target address in the link and the SPA can pass it to KC
            // as `login_hint` in a follow-up — invariant: the URL must end
            // with the sentinel + the percent-encoded email of the invitation.
            Assert.AreEqual(
                $"http://localhost:5173/?invitation=1&email={Uri.EscapeDataString("alice@example.com")}",
                created!.Response.InviteUrl);
            Assert.AreEqual("alice@example.com", created.Response.Invitation.Email);
            Assert.AreEqual(KartovaRoles.Member, created.Response.Invitation.Role);
            Assert.AreEqual("Pending", created.Response.Invitation.Status);
            Assert.AreEqual(inviterId, created.Response.Invitation.InvitedByUserId);

            await kc.Received(1).CreateUserAsync(
                Arg.Is<CreateKeycloakUserRequest>(r => r.Email == "alice@example.com" && r.TenantId == tenant.Value.ToString()),
                Arg.Any<CancellationToken>());
            await kc.Received(1).AssignRealmRoleAsync(kcId, KartovaRoles.Member, Arg.Any<CancellationToken>());
            await kc.DidNotReceive().DeleteUserAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        }

        await using (var assertDb = new OrganizationDbContext(opts))
        {
            var invitation = await assertDb.Invitations.SingleAsync();
            Assert.AreEqual("alice@example.com", invitation.Email);
            Assert.AreEqual(KartovaRoles.Member, invitation.Role);
            Assert.AreEqual(InvitationStatus.Pending, invitation.Status);
            Assert.AreEqual(kcId, invitation.KeycloakUserId);

            var user = await assertDb.Users.SingleAsync();
            Assert.AreEqual(kcId, user.Id);
            Assert.AreEqual("alice@example.com", user.Email);
            Assert.AreEqual("alice@example.com", user.DisplayName);
        }
    }

    [TestMethod]
    public async Task Compensates_by_deleting_kc_user_when_role_assign_fails()
    {
        var clock = new FakeTimeProvider(T0);
        var (h, db, kc, _, _) = Make(clock);
        await using var _db = db;

        var kcId = Guid.NewGuid();
        kc.CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(kcId);
        kc.AssignRealmRoleAsync(kcId, KartovaRoles.Member, Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.Unexpected, "role assign failed"));

        var result = await h.HandleAsync(new CreateInvitationRequest("alice@example.com", KartovaRoles.Member), CancellationToken.None);

        var failed = result as CreateInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(CreateInvitationError.Upstream, failed!.Error);
        // Compensation: KC user MUST be deleted exactly once when role-assign blows up.
        await kc.Received(1).DeleteUserAsync(kcId, Arg.Any<CancellationToken>());
        // And nothing is persisted to the DB.
        Assert.AreEqual(0, await db.Invitations.CountAsync());
        Assert.AreEqual(0, await db.Users.CountAsync());
    }

    [TestMethod]
    public async Task Returns_EmailAlreadyInvited_and_compensates_when_unique_index_throws_23505()
    {
        // Race-condition contract closed by slice-9 carry-forward #10
        // (migration MakeInvitationsPendingIndexUnique): a concurrent invite
        // that slips past the AnyAsync pre-check now races the partial UNIQUE
        // index `idx_invitations_email_pending` and surfaces a 23505. The
        // handler must translate that to EmailAlreadyInvited + best-effort
        // delete the orphan KeyCloak user (matches the role-assign
        // compensation pattern).
        var clock = new FakeTimeProvider(T0);

        // Synthesize the minimum signal the handler matches on
        // (InnerException is PostgresException with SqlState == "23505").
        // Npgsql 10 exposes a public ctor that lets us seed sqlState directly.
        var pg = new PostgresException(
            messageText: "duplicate key value violates unique constraint",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: "23505");

        // Throw on the first SaveChangesAsync — wrap in DbUpdateException so
        // EF Core's contract is preserved (the production code matches on
        // DbUpdateException + InnerException is PostgresException).
        var interceptor = new ThrowOnSaveInterceptor(new DbUpdateException("duplicate", pg));
        var opts = new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseInMemoryDatabase($"inv-{Guid.NewGuid()}")
            .AddInterceptors(interceptor)
            .Options;

        var (h, db, kc, _, _) = Make(clock, opts);
        await using var _db = db;

        var kcId = Guid.NewGuid();
        kc.CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(kcId);

        var result = await h.HandleAsync(new CreateInvitationRequest("alice@example.com", KartovaRoles.Member), CancellationToken.None);

        var failed = result as CreateInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(CreateInvitationError.EmailAlreadyInvited, failed!.Error);
        // Compensation: the orphan KC user must be deleted so the realm doesn't
        // carry an unreachable shadow account when the DB rejects the commit.
        await kc.Received(1).DeleteUserAsync(kcId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// EF Core save interceptor that throws a configured exception on the
    /// first SaveChanges call — drives the 23505 contract test without
    /// requiring a real PostgreSQL container in a unit-tier suite.
    /// </summary>
    private sealed class ThrowOnSaveInterceptor(Exception ex) : ISaveChangesInterceptor
    {
        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken ct = default)
            => throw ex;
    }

    [TestMethod]
    public async Task Compensation_swallows_secondary_KC_delete_failure()
    {
        // Ensures that if compensation itself fails (e.g. KC is down completely),
        // the caller still sees the original Upstream error rather than a leaked
        // KeycloakAdminException — the catch-all in the handler covers this.
        var clock = new FakeTimeProvider(T0);
        var (h, db, kc, _, _) = Make(clock);
        await using var _db = db;

        var kcId = Guid.NewGuid();
        kc.CreateUserAsync(Arg.Any<CreateKeycloakUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(kcId);
        kc.AssignRealmRoleAsync(kcId, KartovaRoles.Member, Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.Unexpected, "role assign failed"));
        kc.DeleteUserAsync(kcId, Arg.Any<CancellationToken>())
            .ThrowsAsync(new KeycloakAdminException(KeycloakAdminError.Unexpected, "KC unreachable"));

        var result = await h.HandleAsync(new CreateInvitationRequest("alice@example.com", KartovaRoles.Member), CancellationToken.None);

        var failed = result as CreateInvitationResult.Failed;
        Assert.IsNotNull(failed);
        Assert.AreEqual(CreateInvitationError.Upstream, failed!.Error);
    }
}
