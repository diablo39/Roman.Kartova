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
/// Integration tests for <c>DELETE /api/v1/organizations/users/{id}</c> (slice-10 Task 6,
/// spec §6.7). Verifies the 204 happy path (cross-module owner reassignment + KC delete +
/// projection removal), the cannot-offboard-self 409, last-OrgAdmin 409, invalid-successor 422,
/// and permission 403. The happy path requires a real KeyCloak user (DeleteUserAsync must
/// succeed) — provisioned via the invitation flow (same pattern as <see cref="ChangeMemberRoleTests"/>).
/// </summary>
[TestClass]
public sealed class OffboardMemberTests : OrganizationIntegrationTestBase
{
#pragma warning disable CA1031
    private static async Task CleanupAsync(Guid tenantId, params Guid[] userIds)
    {
        try { await Fx.DeleteCatalogApplicationsForTenantAsync(tenantId); }
        catch (Exception ex) { await Console.Error.WriteLineAsync($"[cleanup] catalog apps {tenantId}: {ex.Message}"); }
        try { await Fx.DeleteInvitationsForTenantAsync(tenantId); }
        catch (Exception ex) { await Console.Error.WriteLineAsync($"[cleanup] invitations {tenantId}: {ex.Message}"); }
        foreach (var uid in userIds)
        {
            try { await Fx.DeleteUserInOrganizationAsync(uid); }
            catch (Exception ex) { await Console.Error.WriteLineAsync($"[cleanup] delete user {uid}: {ex.Message}"); }
        }
        try { await Fx.DeleteOrganizationsForTenantAsync(tenantId); }
        catch (Exception ex) { await Console.Error.WriteLineAsync($"[cleanup] delete org {tenantId}: {ex.Message}"); }
    }

    private static async Task TryDeleteKeycloakUserAsync(Guid? kcUserId)
    {
        if (kcUserId is null) return;
        using var scope = Fx.Services.CreateScope();
        var kc = scope.ServiceProvider.GetRequiredService<IKeycloakAdminClient>();
        try { await kc.DeleteUserAsync(kcUserId.Value, CancellationToken.None); }
        catch { }
    }
#pragma warning restore CA1031

    // ----- Test 1: happy path (204 + reassignment + projection removal) ------

    [TestMethod]
    public async Task OrgAdmin_offboards_member_reassigns_apps_returns_204()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("offboard-happy");
        var unique = Guid.NewGuid().ToString("N")[..8];

        // A second OrgAdmin (the JWT-auth admin) is seeded so the last-admin guard never fires.
        var existingAdminId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Existing Admin-{unique}",
            $"existing-admin-{unique}@example.com", KartovaRoles.OrgAdmin);

        // The successor who will inherit the offboarded member's applications.
        var successorId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Successor-{unique}",
            $"successor-{unique}@example.com", KartovaRoles.Member);

        var memberEmail = $"offboard-me-{unique}@offboard-happy-{unique}.kartova.local";
        Guid? kcUserId = null;
        Guid? appId = null;
        try
        {
            var adminClient = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });

            // Provision a real KC user via the invitation flow → projection row + KC identity.
            var inviteResp = await adminClient.PostAsJsonAsync(
                "/api/v1/organizations/invitations",
                new CreateInvitationRequest(memberEmail, KartovaRoles.Member));
            Assert.AreEqual(HttpStatusCode.Created, inviteResp.StatusCode,
                $"Expected 201 from CreateInvitation. Body: {await inviteResp.Content.ReadAsStringAsync()}");
            var inviteBody = await inviteResp.Content.ReadFromJsonAsync<CreateInvitationResponse>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(inviteBody);

            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                kcUserId = await db.Invitations
                    .Where(i => EF.Property<Guid>(i, "_id") == inviteBody!.Invitation.Id)
                    .Select(i => i.KeycloakUserId)
                    .SingleAsync();
            }
            Assert.IsNotNull(kcUserId, "Freshly-created invitation must have a KC user id.");

            // Seed a Catalog application owned by the offboarded member.
            appId = await Fx.SeedCatalogApplicationOwnedByAsync(tenantId, kcUserId!.Value, $"Payments-{unique}");

            // DELETE /users/{kcUserId}  {successorUserId}
            var resp = await adminClient.SendAsync(new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/organizations/users/{kcUserId}")
            {
                Content = JsonContent.Create(new OffboardMemberRequest(successorId)),
            });

            Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode,
                $"Expected 204. Body: {await resp.Content.ReadAsStringAsync()}");

            // The application's owner must now be the successor.
            var owner = await Fx.ReadCatalogApplicationOwnerAsync(appId.Value);
            Assert.AreEqual(successorId, owner,
                "Application owner must be reassigned to the successor.");

            // The offboarded user must be gone from the projection.
            await using (var db = new OrganizationDbContext(BypassOptions()))
            {
                Assert.IsFalse(await db.Users.AnyAsync(u => u.Id == kcUserId!.Value),
                    "Offboarded user's projection row must be deleted.");
            }
        }
        finally
        {
            await CleanupAsync(tenantId, existingAdminId, successorId);
            if (kcUserId is not null)
            {
                await Fx.DeleteUserInOrganizationAsync(kcUserId.Value);
                await TryDeleteKeycloakUserAsync(kcUserId);
            }
        }
    }

    // ----- Test 2: self-offboard → 409 cannot-offboard-self ------------------

    [TestMethod]
    public async Task Self_offboard_returns_409_cannot_offboard_self()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("offboard-self");
        var unique = Guid.NewGuid().ToString("N")[..8];

        // The acting admin's own id is the deterministic sub claim CreateAuthenticatedClientAsync
        // mints for adminEmail. Seed a projection row for that same id so the not-found guard does
        // not fire before the self guard.
        var actingUserId = await Fx.GetSubClaimAsync(adminEmail);
        await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Acting Admin-{unique}", adminEmail, KartovaRoles.OrgAdmin, actingUserId);
        // Second OrgAdmin so the last-admin guard is irrelevant (self guard fires first anyway).
        var otherAdminId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Other Admin-{unique}",
            $"other-admin-{unique}@example.com", KartovaRoles.OrgAdmin);
        var successorId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Successor-{unique}",
            $"successor-{unique}@example.com", KartovaRoles.Member);

        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });

            var resp = await client.SendAsync(new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/organizations/users/{actingUserId}")
            {
                Content = JsonContent.Create(new OffboardMemberRequest(successorId)),
            });

            Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode,
                $"Expected 409. Body: {await resp.Content.ReadAsStringAsync()}");
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.AreEqual(ProblemTypes.CannotOffboardSelf,
                doc.RootElement.GetProperty("type").GetString());
        }
        finally
        {
            await CleanupAsync(tenantId, actingUserId, otherAdminId, successorId);
        }
    }

    // ----- Test 3: last OrgAdmin → 409 last-orgadmin -------------------------

    [TestMethod]
    public async Task Offboarding_last_orgadmin_returns_409_last_orgadmin()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("offboard-last-admin");
        var unique = Guid.NewGuid().ToString("N")[..8];

        // Exactly ONE OrgAdmin (projection-only — the guard returns before the KC call).
        var orgAdminId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Solo Admin-{unique}",
            $"solo-admin-{unique}@example.com", KartovaRoles.OrgAdmin);
        var successorId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Successor-{unique}",
            $"successor-{unique}@example.com", KartovaRoles.Member);

        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });
            var resp = await client.SendAsync(new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/organizations/users/{orgAdminId}")
            {
                Content = JsonContent.Create(new OffboardMemberRequest(successorId)),
            });

            Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode,
                $"Expected 409. Body: {await resp.Content.ReadAsStringAsync()}");
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.AreEqual(ProblemTypes.LastOrgAdmin,
                doc.RootElement.GetProperty("type").GetString());
        }
        finally
        {
            await CleanupAsync(tenantId, orgAdminId, successorId);
        }
    }

    // ----- Test 4: unknown successor → 422 invalid-successor -----------------

    [TestMethod]
    public async Task Unknown_successor_returns_422_invalid_successor()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("offboard-bad-succ");
        var unique = Guid.NewGuid().ToString("N")[..8];

        var targetId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Target-{unique}",
            $"target-{unique}@example.com", KartovaRoles.Member);

        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(adminEmail, new[] { KartovaRoles.OrgAdmin });
            var resp = await client.SendAsync(new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/organizations/users/{targetId}")
            {
                // Successor id does not resolve to any user in this tenant.
                Content = JsonContent.Create(new OffboardMemberRequest(Guid.NewGuid())),
            });

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode,
                $"Expected 422. Body: {await resp.Content.ReadAsStringAsync()}");
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.AreEqual(ProblemTypes.InvalidSuccessor,
                doc.RootElement.GetProperty("type").GetString());
        }
        finally
        {
            await CleanupAsync(tenantId, targetId);
        }
    }

    // ----- Test 5: Member JWT → 403 ------------------------------------------

    [TestMethod]
    public async Task Member_without_permission_returns_403()
    {
        var (_, tenantId) = await NewTenantAsync("offboard-no-perm");
        var unique = Guid.NewGuid().ToString("N")[..8];

        var targetId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Target-{unique}",
            $"target-{unique}@example.com", KartovaRoles.Viewer);
        var successorId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId), $"Successor-{unique}",
            $"successor-{unique}@example.com", KartovaRoles.Member);

        try
        {
            // Member token lacks org.users.remove.
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                new TenantId(tenantId), new[] { KartovaRoles.Member });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.SendAsync(new HttpRequestMessage(
                HttpMethod.Delete, $"/api/v1/organizations/users/{targetId}")
            {
                Content = JsonContent.Create(new OffboardMemberRequest(successorId)),
            });

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode,
                $"Expected 403 for Member JWT. Body: {await resp.Content.ReadAsStringAsync()}");
        }
        finally
        {
            await CleanupAsync(tenantId, targetId, successorId);
        }
    }
}
