using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Organization.Contracts;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>PUT /api/v1/organizations/users/{id}/role</c>
/// (slice-10 Task 5, spec §6.7 / ADR-0102). Verifies the 204 happy path,
/// last-OrgAdmin 409 guard, unknown-role 422, and permission 403.
/// The 204 happy path requires a real KeyCloak user (ChangeRealmRoleAsync
/// must succeed) — provisioned via the invitation flow (same pattern as
/// <see cref="InvitationTests"/>).
/// </summary>
[TestClass]
public sealed class ChangeMemberRoleTests : OrganizationIntegrationTestBase
{
    // ----- cleanup helpers ---------------------------------------------------

    /// <summary>Best-effort teardown — mirrors InvitationTests.CleanupTenantInvitationsAsync.</summary>
#pragma warning disable CA1031
    private static async Task CleanupAsync(Guid tenantId, params Guid[] userIds)
    {
        foreach (var uid in userIds)
        {
            try { await Fx.DeleteUserInOrganizationAsync(uid); }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"[cleanup] delete user {uid}: {ex.Message}");
            }
        }
        try { await Fx.DeleteOrganizationsForTenantAsync(tenantId); }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"[cleanup] delete org for tenant {tenantId}: {ex.Message}");
        }
    }
#pragma warning restore CA1031

    /// <summary>
    /// Deletes a KC user via the IKeycloakAdminClient (best-effort test teardown).
    /// Same idiom as InvitationTests.TryDeleteKeycloakUserAsync.
    /// </summary>
    private static async Task TryDeleteKeycloakUserAsync(Guid? kcUserId)
    {
        if (kcUserId is null) return;
        using var scope = Fx.Services.CreateScope();
        var kc = scope.ServiceProvider.GetRequiredService<IKeycloakAdminClient>();
        try { await kc.DeleteUserAsync(kcUserId.Value, CancellationToken.None); }
#pragma warning disable CA1031
        catch { }
#pragma warning restore CA1031
    }

    // ----- Test 1: last OrgAdmin guard (409) ---------------------------------

    /// <summary>
    /// Seeding exactly ONE OrgAdmin user and requesting a demotion to Member
    /// must return 409 Conflict with <c>ProblemTypes.LastOrgAdmin</c>.
    /// The guard fires before the KeyCloak call, so no real KC user is needed.
    /// </summary>
    [TestMethod]
    public async Task Demoting_last_orgadmin_returns_409_last_orgadmin()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("role-last-admin");
        var unique = Guid.NewGuid().ToString("N")[..8];

        // Seed exactly ONE OrgAdmin — the guard must fire.
        var orgAdminId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId),
            $"Solo Admin-{unique}",
            $"solo-admin-{unique}@example.com",
            KartovaRoles.OrgAdmin);

        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });
            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/users/{orgAdminId}/role",
                new UpdateMemberRoleRequest(KartovaRoles.Member));

            Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode,
                $"Expected 409. Body: {await resp.Content.ReadAsStringAsync()}");

            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.AreEqual(
                ProblemTypes.LastOrgAdmin,
                doc.RootElement.GetProperty("type").GetString(),
                "Problem type must be LastOrgAdmin.");
        }
        finally
        {
            await CleanupAsync(tenantId, orgAdminId);
        }
    }

    // ----- Test 2: invalid role (422) ----------------------------------------

    /// <summary>
    /// Supplying an unrecognised role string must return 422 Unprocessable Entity.
    /// The validation fires before the DB lookup, so no real KC user is needed.
    /// </summary>
    [TestMethod]
    public async Task Unknown_role_returns_422()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("role-bad-role");
        var unique = Guid.NewGuid().ToString("N")[..8];

        var userId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId),
            $"Any User-{unique}",
            $"any-{unique}@example.com",
            KartovaRoles.Member);

        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });
            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/users/{userId}/role",
                new UpdateMemberRoleRequest("Nope"));

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode,
                $"Expected 422. Body: {await resp.Content.ReadAsStringAsync()}");

            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.AreEqual(
                ProblemTypes.ValidationFailed,
                doc.RootElement.GetProperty("type").GetString(),
                "Problem type must be ValidationFailed.");
        }
        finally
        {
            await CleanupAsync(tenantId, userId);
        }
    }

    // ----- Test 3: Member JWT returns 403 ------------------------------------

    /// <summary>
    /// A Member-role JWT lacks <c>org.users.role.change</c> permission and must
    /// receive 403 Forbidden — auth guard fires before handler invocation.
    /// </summary>
    [TestMethod]
    public async Task Member_without_permission_returns_403()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("role-no-perm");
        var unique = Guid.NewGuid().ToString("N")[..8];

        var targetId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId),
            $"Target User-{unique}",
            $"target-{unique}@example.com",
            KartovaRoles.Viewer);

        try
        {
            // Member token (not OrgAdmin) → lacks org.users.role.change.
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                new TenantId(tenantId), new[] { KartovaRoles.Member });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/users/{targetId}/role",
                new UpdateMemberRoleRequest(KartovaRoles.OrgAdmin));

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode,
                $"Expected 403 for Member JWT. Body: {await resp.Content.ReadAsStringAsync()}");
        }
        finally
        {
            await CleanupAsync(tenantId, targetId);
        }
    }

    // ----- Test 4: happy path (204 + write-through) --------------------------

    /// <summary>
    /// OrgAdmin promotes a Member to OrgAdmin: returns 204, calls KC
    /// <c>ChangeRealmRoleAsync</c> (real round-trip), and updates the
    /// <c>users.realm_role</c> projection column.
    ///
    /// A real KC user is required because <see cref="IKeycloakAdminClient.ChangeRealmRoleAsync"/>
    /// must succeed. Strategy: use the invitation flow (same as
    /// <see cref="InvitationTests"/>) to provision the KC user, then seed the
    /// projection row with that user's sub/id as a Member. A second OrgAdmin is
    /// seeded so the last-admin guard cannot fire during the Member→OrgAdmin promotion.
    /// </summary>
    [TestMethod]
    public async Task OrgAdmin_promotes_member_to_orgadmin_returns_204_and_updates_projection()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("role-happy");
        var unique = Guid.NewGuid().ToString("N")[..8];

        // Seed a second OrgAdmin (the JWT-auth admin) so the last-admin guard
        // can never fire in this test (the promoted user is a Member, not demoting
        // an OrgAdmin — but the second OrgAdmin is good defensive hygiene).
        var existingAdminId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId),
            $"Existing Admin-{unique}",
            $"existing-admin-{unique}@example.com",
            KartovaRoles.OrgAdmin);

        // Provision a real KC user via the invitation flow.
        var memberEmail = $"promote-me-{unique}@role-happy-{unique}.kartova.local";

        Guid? kcUserId = null;
        try
        {
            var adminClient = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });

            // Create invitation → KC user is provisioned, projection stub inserted.
            var inviteResp = await adminClient.PostAsJsonAsync(
                "/api/v1/organizations/invitations",
                new CreateInvitationRequest(memberEmail, KartovaRoles.Member));
            Assert.AreEqual(HttpStatusCode.Created, inviteResp.StatusCode,
                $"Expected 201 from CreateInvitation. Body: {await inviteResp.Content.ReadAsStringAsync()}");

            var inviteBody = await inviteResp.Content.ReadFromJsonAsync<CreateInvitationResponse>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(inviteBody);

            // Read the KC user id from the invitations row (same DB-peek as InvitationTests).
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                kcUserId = await db.Invitations
                    .Where(i => EF.Property<Guid>(i, "_id") == inviteBody!.Invitation.Id)
                    .Select(i => i.KeycloakUserId)
                    .SingleAsync();
            }
            Assert.IsNotNull(kcUserId, "Freshly-created invitation must have a KC user id.");

            // The invitation handler inserts a users projection row. The RealmRole is set
            // to the invited role (Member) at invite time by CreateInvitationHandler — the
            // realm_role fix (slice-10) removed the Viewer default. We only need the row
            // to exist with a non-OrgAdmin role so the PUT can change it.
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                var row = await db.Users.SingleOrDefaultAsync(u => u.Id == kcUserId!.Value);
                Assert.IsNotNull(row, "Invitation flow must have inserted a users projection row.");
                Assert.AreNotEqual(KartovaRoles.OrgAdmin, row!.RealmRole,
                    "Projection row must start as a non-OrgAdmin role before the PUT.");
            }

            // PUT /users/{kcUserId}/role  {role: "OrgAdmin"}
            var resp = await adminClient.PutAsJsonAsync(
                $"/api/v1/organizations/users/{kcUserId}/role",
                new UpdateMemberRoleRequest(KartovaRoles.OrgAdmin));

            Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode,
                $"Expected 204. Body: {await resp.Content.ReadAsStringAsync()}");

            // Verify write-through: projection row must now show OrgAdmin.
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                var row = await db.Users.SingleOrDefaultAsync(u => u.Id == kcUserId!.Value);
                Assert.IsNotNull(row, "Projection row must still exist after role change.");
                Assert.AreEqual(KartovaRoles.OrgAdmin, row!.RealmRole,
                    "Projection row must reflect the new role after write-through.");
            }
        }
        finally
        {
            // Clean up: invitation row, users rows, KC user, org row.
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await CleanupAsync(tenantId, existingAdminId);
            if (kcUserId is not null)
            {
                await Fx.DeleteUserInOrganizationAsync(kcUserId.Value);
                await TryDeleteKeycloakUserAsync(kcUserId);
            }
        }
    }
}
