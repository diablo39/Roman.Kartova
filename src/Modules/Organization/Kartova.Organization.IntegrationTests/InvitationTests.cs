using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.Organization.Infrastructure.Admin;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for the slice-9 invitation lifecycle (spec §6.7 + §6.9 / §11.3):
/// <list type="bullet">
/// <item><c>POST /api/v1/organizations/invitations</c> (CreateInvitationAsync)</item>
/// <item><c>POST /api/v1/organizations/invitations/{id}/revoke</c> (RevokeInvitationAsync)</item>
/// <item><see cref="ExpireInvitationsHostedService.ExpireDueAsync"/> direct invocation</item>
/// </list>
/// All six tests run against the real Keycloak Testcontainer wired by the
/// Organization fixture's <c>UsesKeycloakContainer = true</c> opt-in — so the KC
/// admin client makes real REST calls and DB persistence rides the normal
/// per-request tenant scope. DB verification uses the BYPASSRLS connection
/// because assertion code runs outside an inbound HTTP request (no
/// <c>SET LOCAL app.current_tenant_id</c>).
/// </summary>
[TestClass]
public sealed class InvitationTests : OrganizationIntegrationTestBase
{
    private static DbContextOptions<OrganizationDbContext> BypassOptions() =>
        new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql(Fx.BypassConnectionString)
            .Options;

    /// <summary>
    /// Best-effort KC cleanup for a user provisioned by a test. The KC admin
    /// client treats <c>DeleteUserAsync</c> on a missing id as idempotent
    /// (see <see cref="RevokeInvitationHandler"/>), so calling this on a user
    /// the test already revoked is harmless.
    /// </summary>
    private static async Task TryDeleteKeycloakUserAsync(Guid? kcUserId)
    {
        if (kcUserId is null) return;
        using var scope = Fx.Services.CreateScope();
        var kc = scope.ServiceProvider.GetRequiredService<IKeycloakAdminClient>();
        try { await kc.DeleteUserAsync(kcUserId.Value, CancellationToken.None); }
#pragma warning disable CA1031 // best-effort test teardown — swallow any KC failure
        catch { }
#pragma warning restore CA1031
    }

    // ---------- Scenario #2 (spec §11.3): happy path -------------------------

    [TestMethod]
    public async Task Invitation_create_persists_keycloak_user_and_db_row()
    {
        // Each test uses a unique email domain so its derived tenant id is
        // disjoint from every other test — keeps the suite parallel-safe even
        // though MSTest serializes test classes.
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"admin@create-happy-{unique}.kartova.local";
        var inviteeEmail = $"newuser-{unique}@create-happy-{unique}.kartova.local";
        var tenantId = KartovaApiFixtureBase.TenantFor(adminEmail).Value;
        await Fx.SeedOrganizationAsync(tenantId, "Org-CreateHappy");

        Guid? kcUserId = null;
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });
            var resp = await client.PostAsJsonAsync(
                "/api/v1/organizations/invitations",
                new CreateInvitationRequest(inviteeEmail, KartovaRoles.Member));

            Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<CreateInvitationResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.AreNotEqual(Guid.Empty, body!.Invitation.Id);
            // Handler lowercases the email before persistence — verify the wire
            // shape reflects that, not the raw request casing.
            Assert.AreEqual(inviteeEmail.ToLowerInvariant(), body.Invitation.Email);
            Assert.AreEqual(KartovaRoles.Member, body.Invitation.Role);
            Assert.AreEqual("Pending", body.Invitation.Status);
            Assert.IsTrue(
                body.Invitation.ExpiresAt > body.Invitation.InvitedAt,
                $"ExpiresAt ({body.Invitation.ExpiresAt:o}) must be after InvitedAt ({body.Invitation.InvitedAt:o}).");
            Assert.IsFalse(string.IsNullOrWhiteSpace(body.InviteUrl), "InviteUrl must be non-empty.");
            Assert.IsTrue(
                body.InviteUrl.EndsWith("/?invitation=1", StringComparison.Ordinal),
                $"InviteUrl must end with '/?invitation=1'. Actual: {body.InviteUrl}");

            // BYPASSRLS verification: the invitations row + users projection row
            // both committed under the request's per-tenant scope.
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                // The Invitation aggregate maps its id via the `_id` backing field
                // (see InvitationEntityTypeConfiguration). EF cannot translate
                // `i.Id.Value == ...` — go through EF.Property to reach the column.
                var invitation = await db.Invitations.SingleAsync(
                    i => EF.Property<Guid>(i, "_id") == body.Invitation.Id);
                Assert.AreEqual(inviteeEmail.ToLowerInvariant(), invitation.Email);
                Assert.AreEqual(InvitationStatus.Pending, invitation.Status);
                Assert.IsNotNull(invitation.KeycloakUserId);
                kcUserId = invitation.KeycloakUserId;

                var userRow = await db.Users.SingleAsync(u => u.Email == inviteeEmail.ToLowerInvariant());
                Assert.AreEqual(kcUserId, userRow.Id);
                Assert.AreEqual(tenantId, userRow.TenantId.Value);
            }

            // KC verification — the live admin client must surface the new user.
            using var kcScope = Fx.Services.CreateScope();
            var kc = kcScope.ServiceProvider.GetRequiredService<IKeycloakAdminClient>();
            var kcUser = await kc.GetUserAsync(kcUserId!.Value, CancellationToken.None);
            Assert.IsNotNull(kcUser, "KeyCloak user must exist after a successful invitation create.");
            Assert.AreEqual(inviteeEmail.ToLowerInvariant(), kcUser!.Email, ignoreCase: true);
        }
        finally
        {
            // Order: users-row cleanup BEFORE invitations cleanup — the
            // users row is direct-id-leak-prone (no prefix sweep). Mirrors the
            // slice-9 e5aaf73 / 4715c87 convention.
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM users WHERE tenant_id = {0}", tenantId);
            }
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await TryDeleteKeycloakUserAsync(kcUserId);
        }
    }

    // ---------- Scenario #3: 409 EmailAlreadyInTenant ------------------------

    [TestMethod]
    public async Task Invitation_create_returns_409_when_email_already_in_tenant()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"admin@inv-409-intenant-{unique}.kartova.local";
        var existingEmail = $"existing-{unique}@inv-409-intenant-{unique}.kartova.local";
        var tenantId = KartovaApiFixtureBase.TenantFor(adminEmail).Value;
        await Fx.SeedOrganizationAsync(tenantId, "Org-409-InTenant");
        var seededUserId = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: existingEmail, email: existingEmail);
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });
            var resp = await client.PostAsJsonAsync(
                "/api/v1/organizations/invitations",
                new CreateInvitationRequest(existingEmail, KartovaRoles.Member));

            Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
            await using (var problemStream = await resp.Content.ReadAsStreamAsync())
            {
                using var problemDoc = await JsonDocument.ParseAsync(problemStream);
                // Specific type assertion — would fail if the handler reported any of
                // the other three 409 flavors (EmailAlreadyInvited / EmailAlreadyOnPlatform).
                Assert.AreEqual(
                    ProblemTypes.EmailAlreadyInTenant,
                    problemDoc.RootElement.GetProperty("type").GetString());
            }

            // Short-circuit before the KC round-trip: no new invitations row, no
            // new KC user (handler returns before kc.CreateUserAsync is called).
            await using var db = new OrganizationDbContext(BypassOptions());
            var invitationCount = await db.Invitations.CountAsync(i => i.TenantId == new TenantId(tenantId));
            Assert.AreEqual(0, invitationCount, "Handler must short-circuit before persisting any invitation.");
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(seededUserId);
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
        }
    }

    // ---------- Scenario #4: 409 EmailAlreadyInvited -------------------------

    [TestMethod]
    public async Task Invitation_create_returns_409_when_email_already_pending_in_tenant()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"admin@inv-409-invited-{unique}.kartova.local";
        var inviteeEmail = $"target-{unique}@inv-409-invited-{unique}.kartova.local";
        var tenantId = KartovaApiFixtureBase.TenantFor(adminEmail).Value;
        await Fx.SeedOrganizationAsync(tenantId, "Org-409-Invited");

        Guid? kcUserId = null;
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });

            // First invite succeeds — produces a Pending row + KC user + a User
            // projection row (CreateInvitationHandler adds both on success).
            var first = await client.PostAsJsonAsync(
                "/api/v1/organizations/invitations",
                new CreateInvitationRequest(inviteeEmail, KartovaRoles.Member));
            Assert.AreEqual(HttpStatusCode.Created, first.StatusCode);
            var firstBody = await first.Content.ReadFromJsonAsync<CreateInvitationResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(firstBody);

            // Capture the KC user id before we mutate the projection — we still need
            // it for KC cleanup in the finally block.
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                kcUserId = await db.Invitations
                    .Where(i => EF.Property<Guid>(i, "_id") == firstBody!.Invitation.Id)
                    .Select(i => i.KeycloakUserId)
                    .SingleAsync();
            }

            // Spec §6.7 step 2 (users-row guard) fires before step 3 (pending-
            // invitation guard). To exercise the EmailAlreadyInvited branch
            // specifically — and not the EmailAlreadyInTenant branch — we have
            // to clear the User projection row between invites. Without this
            // step the second invite would be caught by step 2 and report
            // EmailAlreadyInTenant, masking the EmailAlreadyInvited path the
            // spec lists as a distinct 409 flavor (spec §11.3 scenario #4).
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM users WHERE tenant_id = {0} AND email = {1}",
                    tenantId, inviteeEmail.ToLowerInvariant());
            }

            // Second invite for the same email + same tenant must now reject with
            // EmailAlreadyInvited (the Pending invitations row is still there).
            var second = await client.PostAsJsonAsync(
                "/api/v1/organizations/invitations",
                new CreateInvitationRequest(inviteeEmail, KartovaRoles.Member));
            Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode);
            await using (var problemStream = await second.Content.ReadAsStreamAsync())
            {
                using var problemDoc = await JsonDocument.ParseAsync(problemStream);
                Assert.AreEqual(
                    ProblemTypes.EmailAlreadyInvited,
                    problemDoc.RootElement.GetProperty("type").GetString());
            }

            // Exactly one invitations row + zero users projection rows for the
            // invitee (we deleted the projection above — the second invite must
            // not have re-created it because the handler short-circuits at the
            // pending-invitation guard before reaching the persistence step).
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                var invitationCount = await db.Invitations.CountAsync(
                    i => i.TenantId == new TenantId(tenantId) && i.Email == inviteeEmail.ToLowerInvariant());
                Assert.AreEqual(1, invitationCount);
                var userCount = await db.Users.CountAsync(
                    u => u.TenantId == new TenantId(tenantId) && u.Email == inviteeEmail.ToLowerInvariant());
                Assert.AreEqual(0, userCount);
            }
        }
        finally
        {
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM users WHERE tenant_id = {0}", tenantId);
            }
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await TryDeleteKeycloakUserAsync(kcUserId);
        }
    }

    // ---------- Scenario #5: 409 EmailAlreadyOnPlatform ----------------------

    [TestMethod]
    public async Task Invitation_create_returns_409_when_email_exists_in_other_tenant()
    {
        // Two distinct admin emails → two distinct deterministic tenants (the
        // derivation hashes the domain, not the local-part). Both admins invite
        // the SAME body email so the second hit reaches KC, which sees a
        // platform-wide collision and returns EmailAlreadyExists → 409
        // EmailAlreadyOnPlatform. The in-tenant pre-check passes for tenant B
        // because RLS hides tenant A's users row — only KC's platform view
        // catches the conflict.
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminAEmail = $"admin@otherten-a-{unique}.kartova.local";
        var adminBEmail = $"admin@otherten-b-{unique}.kartova.local";
        var sharedInvitee = $"shared-{unique}@cross-tenant-{unique}.kartova.local";
        var tenantA = KartovaApiFixtureBase.TenantFor(adminAEmail).Value;
        var tenantB = KartovaApiFixtureBase.TenantFor(adminBEmail).Value;
        Assert.AreNotEqual(tenantA, tenantB, "Distinct admin domains MUST yield distinct deterministic tenants.");

        await Fx.SeedOrganizationAsync(tenantA, "Org-Other-A");
        await Fx.SeedOrganizationAsync(tenantB, "Org-Other-B");

        Guid? kcUserId = null;
        try
        {
            var clientA = await Fx.CreateAuthenticatedClientAsync(adminAEmail, new[] { KartovaRoles.OrgAdmin });
            var respA = await clientA.PostAsJsonAsync(
                "/api/v1/organizations/invitations",
                new CreateInvitationRequest(sharedInvitee, KartovaRoles.Member));
            Assert.AreEqual(HttpStatusCode.Created, respA.StatusCode);
            var bodyA = await respA.Content.ReadFromJsonAsync<CreateInvitationResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(bodyA);

            // Capture the KC user id for cleanup before any later assertion can throw.
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                kcUserId = await db.Invitations
                    .Where(i => EF.Property<Guid>(i, "_id") == bodyA!.Invitation.Id)
                    .Select(i => i.KeycloakUserId)
                    .SingleAsync();
            }

            // Tenant B's admin invites the SAME email — KC will surface
            // EmailAlreadyExists from its platform-wide directory.
            var clientB = await Fx.CreateAuthenticatedClientAsync(adminBEmail, new[] { KartovaRoles.OrgAdmin });
            var respB = await clientB.PostAsJsonAsync(
                "/api/v1/organizations/invitations",
                new CreateInvitationRequest(sharedInvitee, KartovaRoles.Member));
            Assert.AreEqual(HttpStatusCode.Conflict, respB.StatusCode);
            await using (var problemStream = await respB.Content.ReadAsStreamAsync())
            {
                using var problemDoc = await JsonDocument.ParseAsync(problemStream);
                // MC/DC discipline: assert the SPECIFIC 409 flavor — would also pass
                // for the other two 409s if we only checked the status code.
                Assert.AreEqual(
                    ProblemTypes.EmailAlreadyOnPlatform,
                    problemDoc.RootElement.GetProperty("type").GetString());
            }

            // Tenant B has no invitations row + no users row for the shared email.
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                var bInvitationCount = await db.Invitations.CountAsync(
                    i => i.TenantId == new TenantId(tenantB) && i.Email == sharedInvitee.ToLowerInvariant());
                Assert.AreEqual(0, bInvitationCount,
                    "Tenant B must not commit anything when KC rejects the create.");
                var bUserCount = await db.Users.CountAsync(
                    u => u.TenantId == new TenantId(tenantB) && u.Email == sharedInvitee.ToLowerInvariant());
                Assert.AreEqual(0, bUserCount);
            }
        }
        finally
        {
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM users WHERE tenant_id = {0}", tenantA);
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM users WHERE tenant_id = {0}", tenantB);
            }
            await Fx.DeleteInvitationsForTenantAsync(tenantA);
            await Fx.DeleteInvitationsForTenantAsync(tenantB);
            await TryDeleteKeycloakUserAsync(kcUserId);
        }
    }

    // ---------- Scenario #6: revoke deletes KC user --------------------------

    [TestMethod]
    public async Task Invitation_revoke_deletes_keycloak_user_and_flips_status()
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"admin@revoke-happy-{unique}.kartova.local";
        var inviteeEmail = $"revoked-{unique}@revoke-happy-{unique}.kartova.local";
        var tenantId = KartovaApiFixtureBase.TenantFor(adminEmail).Value;
        await Fx.SeedOrganizationAsync(tenantId, "Org-RevokeHappy");

        Guid? kcUserId = null;
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });
            var create = await client.PostAsJsonAsync(
                "/api/v1/organizations/invitations",
                new CreateInvitationRequest(inviteeEmail, KartovaRoles.Member));
            Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
            var created = await create.Content.ReadFromJsonAsync<CreateInvitationResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(created);
            var invitationId = created!.Invitation.Id;

            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                kcUserId = await db.Invitations
                    .Where(i => EF.Property<Guid>(i, "_id") == invitationId)
                    .Select(i => i.KeycloakUserId)
                    .SingleAsync();
            }
            Assert.IsNotNull(kcUserId, "Sanity: a freshly-created Pending invitation has a KC user id.");

            // Revoke the invitation.
            var revoke = await client.PostAsync(
                $"/api/v1/organizations/invitations/{invitationId}/revoke",
                content: null);
            Assert.AreEqual(HttpStatusCode.NoContent, revoke.StatusCode);

            // DB: status flipped to Revoked, RevokedAt populated; users
            // projection row is NOT deleted (revoke clears KC only).
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                var reloaded = await db.Invitations.SingleAsync(
                    i => EF.Property<Guid>(i, "_id") == invitationId);
                Assert.AreEqual(InvitationStatus.Revoked, reloaded.Status);
                Assert.IsNotNull(reloaded.RevokedAt);
                var userRow = await db.Users.SingleOrDefaultAsync(u => u.Id == kcUserId!.Value);
                Assert.IsNotNull(userRow,
                    "Revoke must NOT delete the users projection — only the KC directory entry.");
            }

            // KC: the user is gone (GetUserAsync returns null).
            using var kcScope = Fx.Services.CreateScope();
            var kc = kcScope.ServiceProvider.GetRequiredService<IKeycloakAdminClient>();
            var kcUser = await kc.GetUserAsync(kcUserId!.Value, CancellationToken.None);
            Assert.IsNull(kcUser, "Revoke must delete the KeyCloak directory user.");
        }
        finally
        {
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM users WHERE tenant_id = {0}", tenantId);
            }
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            // Idempotent: the revoke above already deleted the KC user.
            await TryDeleteKeycloakUserAsync(kcUserId);
        }
    }

    // ---------- Scenario #9: hosted-service expire sweep ---------------------

    [TestMethod]
    public async Task Expire_invitations_sweep_deletes_keycloak_user_and_flips_status()
    {
        // Test name deviates from spec §11.3 #9 ("disables_keycloak_user") because
        // the implementation in ExpireInvitationsHostedService.ExpireDueAsync
        // (Infrastructure.Admin/ExpireInvitationsHostedService.cs:57) DELETES the
        // KC user rather than disabling it. Test mirrors what the code does.
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"admin@expire-{unique}.kartova.local";
        var inviteeEmail = $"expired-{unique}@expire-{unique}.kartova.local";
        var tenantId = KartovaApiFixtureBase.TenantFor(adminEmail).Value;
        await Fx.SeedOrganizationAsync(tenantId, "Org-Expire");

        Guid? kcUserId = null;
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });
            var create = await client.PostAsJsonAsync(
                "/api/v1/organizations/invitations",
                new CreateInvitationRequest(inviteeEmail, KartovaRoles.Member));
            Assert.AreEqual(HttpStatusCode.Created, create.StatusCode);
            var created = await create.Content.ReadFromJsonAsync<CreateInvitationResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(created);
            var invitationId = created!.Invitation.Id;

            // Backdate ExpiresAt to one hour before "now" so the sweep filter
            // (ExpiresAt < clock.GetUtcNow()) selects this row. Column name per
            // InvitationEntityTypeConfiguration is "expires_at".
            using (var prepScope = Fx.Services.CreateScope())
            {
                var clock = prepScope.ServiceProvider.GetRequiredService<TimeProvider>();
                var backdate = clock.GetUtcNow().AddHours(-1);
                await using var db = new OrganizationDbContext(BypassOptions());
                await db.Database.ExecuteSqlRawAsync(
                    "UPDATE invitations SET expires_at = {0} WHERE id = {1}",
                    backdate, invitationId);
            }

            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                kcUserId = await db.Invitations
                    .Where(i => EF.Property<Guid>(i, "_id") == invitationId)
                    .Select(i => i.KeycloakUserId)
                    .SingleAsync();
            }

            // Invoke the sweep directly through the public-for-testing entry
            // point — bypasses the LeaderElectedPeriodicService timer + lock
            // setup which is integration-tested separately. We resolve the live
            // hosted-service instance from the WAF root's IHostedService set so
            // the test exercises exactly the registered singleton (and not a
            // hand-constructed twin that could drift from the real wiring).
            using var sweepScope = Fx.Services.CreateScope();
            var sweep = Fx.Services
                .GetServices<Microsoft.Extensions.Hosting.IHostedService>()
                .OfType<ExpireInvitationsHostedService>()
                .Single();
            await sweep.ExpireDueAsync(sweepScope.ServiceProvider, CancellationToken.None);

            // DB: status flipped to Expired.
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                var reloaded = await db.Invitations.SingleAsync(
                    i => EF.Property<Guid>(i, "_id") == invitationId);
                Assert.AreEqual(InvitationStatus.Expired, reloaded.Status);
            }

            // KC: user has been deleted by the sweep.
            using var kcScope = Fx.Services.CreateScope();
            var kc = kcScope.ServiceProvider.GetRequiredService<IKeycloakAdminClient>();
            var kcUser = await kc.GetUserAsync(kcUserId!.Value, CancellationToken.None);
            Assert.IsNull(kcUser, "Expire sweep must delete the KeyCloak directory user.");
        }
        finally
        {
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM users WHERE tenant_id = {0}", tenantId);
            }
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await TryDeleteKeycloakUserAsync(kcUserId);
        }
    }
}
