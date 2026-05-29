using System.Net;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>GET /api/v1/organizations/users/{id}</c>
/// (slice-9 spec §6.7 — user detail with team memberships). The H4 Playwright
/// verification surfaced a 500 from this endpoint: <c>UserQueries.GetDetailAsync</c>
/// joined <c>TeamMembership.TeamId.Value</c> (value-object getter) on the outer
/// side of an EF <c>Join</c>, which Npgsql refused to translate. The unit-test
/// suite uses the EF InMemory provider, which happily evaluated the expression
/// client-side and masked the bug. These tests run against the real Postgres
/// container + RLS so the EF translation path is exercised end-to-end.
/// </summary>
[TestClass]
public sealed class UserDetailTests : OrganizationIntegrationTestBase
{
    private const byte AdminRole = 2;
    private const byte MemberRole = 1;

    // ---------- Happy path: user with team memberships ----------------------

    /// <summary>
    /// Regression: the H4-surfaced 500 must NOT recur. Seed a user, two teams,
    /// and two memberships, then call <c>GET /users/{id}</c>. Pre-fix this
    /// returned 500 with <c>"could not be translated"</c> in the API logs; the
    /// fix is a client-side dictionary join that keeps the value-object getter
    /// out of EF's expression tree.
    /// </summary>
    [TestMethod]
    public async Task GET_user_detail_returns_user_with_team_memberships()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("user-detail-happy");

        var unique = Guid.NewGuid().ToString("N")[..8];
        var userId = await Fx.SeedUserInOrganizationAsync(
            tenantId, "Alice Wonder", $"alice-{unique}@example.com");
        var platformTeamId = await Fx.SeedTeamAsync(tenantId, $"Platform-{unique}");
        var frontendTeamId = await Fx.SeedTeamAsync(tenantId, $"Frontend-{unique}");
        await Fx.SeedTeamMembershipAsync(platformTeamId, userId, AdminRole);
        await Fx.SeedTeamMembershipAsync(frontendTeamId, userId, MemberRole);

        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            var resp = await client.GetAsync($"/api/v1/organizations/users/{userId}");

            // Pre-fix: 500. Post-fix: 200.
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
                $"GET /users/{{id}} must return 200 OK (was 500 before the H4 API-2 fix). "
                + $"Body: {await resp.Content.ReadAsStringAsync()}");

            var body = await resp.Content.ReadFromJsonAsync<UserDetailResponse>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.AreEqual(userId, body!.Id);
            Assert.AreEqual("Alice Wonder", body.DisplayName);
            Assert.AreEqual($"alice-{unique}@example.com", body.Email);

            // Both memberships must come back with the team's display name
            // (the field that triggered the EF Join translation failure).
            Assert.AreEqual(2, body.Teams.Count,
                "Both seeded memberships must surface in the response.");
            var platform = body.Teams.SingleOrDefault(t => t.TeamId == platformTeamId);
            Assert.IsNotNull(platform, "Platform membership row must be present.");
            Assert.AreEqual($"Platform-{unique}", platform!.TeamName,
                "TeamName must be the joined team's display name — the field whose "
                + "projection broke pre-fix.");
            Assert.AreEqual("Admin", platform.Role);

            var frontend = body.Teams.SingleOrDefault(t => t.TeamId == frontendTeamId);
            Assert.IsNotNull(frontend, "Frontend membership row must be present.");
            Assert.AreEqual($"Frontend-{unique}", frontend!.TeamName);
            Assert.AreEqual("Member", frontend.Role);
        }
        finally
        {
            await CleanupAsync(tenantId, userId);
        }
    }

    // ---------- User with no memberships ------------------------------------

    /// <summary>
    /// The bug surfaced only when the user had at least one membership (the
    /// Join had nothing to translate when the outer source was empty). This
    /// test guards the zero-membership branch to confirm the client-side
    /// dictionary lookup short-circuits cleanly when there is nothing to look
    /// up — same shape, empty Teams collection.
    /// </summary>
    [TestMethod]
    public async Task GET_user_detail_returns_user_with_empty_teams_when_no_memberships()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("user-detail-no-teams");
        var unique = Guid.NewGuid().ToString("N")[..8];
        var userId = await Fx.SeedUserInOrganizationAsync(
            tenantId, "Bob Loner", $"bob-{unique}@example.com");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            var resp = await client.GetAsync($"/api/v1/organizations/users/{userId}");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<UserDetailResponse>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.AreEqual(userId, body!.Id);
            Assert.AreEqual(0, body.Teams.Count,
                "User with no memberships must surface an empty Teams collection.");
        }
        finally
        {
            await CleanupAsync(tenantId, userId);
        }
    }

    // ---------- 404 for unknown id ------------------------------------------

    /// <summary>
    /// Sanity check on the 404 envelope — the fix must not regress the
    /// existing not-found contract. Same shape pre/post-fix.
    /// </summary>
    [TestMethod]
    public async Task GET_user_detail_returns_404_for_unknown_id()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("user-detail-404");
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });

            var resp = await client.GetAsync(
                $"/api/v1/organizations/users/{Guid.NewGuid()}");
            Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }

    // ---------- helpers ------------------------------------------------------

    /// <summary>
    /// Tear down per-test rows in the strict order required by slice-9 H1
    /// cleanup convention (e5aaf73 / 4715c87): direct-id deletes ride first,
    /// teams (with FK-cascading membership wipes) ride second, then the
    /// organization row. Each step is best-effort so a throw in one does not
    /// strand rows planted by another.
    /// </summary>
    private static async Task CleanupAsync(Guid tenantId, Guid userId)
    {
#pragma warning disable CA1031 // best-effort test teardown — log and continue
        try { await Fx.DeleteUserInOrganizationAsync(userId); }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] users delete failed: {ex.Message}");
        }
        try { await Fx.DeleteTeamsForTenantAsync(tenantId); }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] teams delete failed: {ex.Message}");
        }
        try { await Fx.DeleteOrganizationsForTenantAsync(tenantId); }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(
                $"[cleanup] organizations delete failed: {ex.Message}");
        }
#pragma warning restore CA1031
    }
}
