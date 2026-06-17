using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>DELETE /api/v1/organizations/teams/{id}</c> (slice 8, spec §10).
/// Covers the happy 204, the 409 "team-has-applications" branch with the
/// <c>applicationCount</c> extension (spec §6.5), and cross-tenant 404.
/// </summary>
[TestClass]
public sealed class DeleteTeamTests : OrganizationIntegrationTestBase
{
    // TeamRole.Admin == 2.
    private const byte AdminRole = 2;

    private static readonly TenantId Tenant =
        new(Guid.Parse("aaaaaaaa-0005-0005-0005-000000000005"));

    private static readonly TenantId OtherTenant =
        new(Guid.Parse("bbbbbbbb-0005-0005-0005-000000000005"));

    [TestMethod]
    public async Task Empty_team_OrgAdmin_returns_204()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Delete");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "ToDelete");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin },
                subject: "00000000-0000-0000-0005-000000000001");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.DeleteAsync($"/api/v1/organizations/teams/{teamId}");

            Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Team_with_assigned_applications_returns_409_with_applicationCount()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Delete-409");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "OwnsApps");
        // Two apps assigned to the team — the 409 envelope must surface the exact count.
        await Fx.SeedCatalogApplicationAssignedToTeamAsync(
            Tenant.Value, teamId, $"app-409-{Guid.NewGuid():N}".Substring(0, 16));
        await Fx.SeedCatalogApplicationAssignedToTeamAsync(
            Tenant.Value, teamId, $"app-409-{Guid.NewGuid():N}".Substring(0, 16));
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.DeleteAsync($"/api/v1/organizations/teams/{teamId}");

            Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
            Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType?.MediaType);

            // applicationCount is a top-level extension on the ProblemDetails body.
            await using var stream = await resp.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            Assert.AreEqual(
                ProblemTypes.TeamHasApplications,
                doc.RootElement.GetProperty("type").GetString());
            Assert.AreEqual(2, doc.RootElement.GetProperty("applicationCount").GetInt32());
        }
        finally
        {
            await Fx.DeleteCatalogApplicationsForTenantAsync(Tenant.Value);
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Member_who_is_team_admin_of_this_team_deletes_returns_204()
    {
        // ADR-0101 / spec §8: DELETE /teams/{id} shares the same resource-gate-only
        // posture as PUT — a realm-Member who holds an Admin membership on THIS team
        // may delete it. The team has NO assigned applications (a freshly-seeded team
        // has none by default — mirrors Empty_team_OrgAdmin_returns_204), so the
        // team-has-applications 409 branch is not hit.
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Delete-MemberAdmin");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "ToDelete");
        var userId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, userId, AdminRole);
        try
        {
            var client = Fx.CreateClient();
            // The sub claim must match the seeded Admin membership — the middleware
            // reads it to populate TeamMemberships, which the resource policy queries.
            // The realm role is Member; team-admin authority is the per-team Admin
            // membership alone (ADR-0101 — no claim gate).
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.Member },
                subject: userId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.DeleteAsync($"/api/v1/organizations/teams/{teamId}");

            Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Member_who_is_team_admin_of_other_team_delete_returns_403()
    {
        // Caller is a realm-Member who is Admin of team B but NOT of team A. The
        // resource gate (TeamAdminOfThis) is the sole authorization (ADR-0101); it
        // must short-circuit with 403 — proving team-admin authority does not leak
        // across teams for the delete verb.
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Delete-OtherTeam-403");
        var teamA = await Fx.SeedTeamAsync(Tenant.Value, "Team A");
        var teamB = await Fx.SeedTeamAsync(Tenant.Value, "Team B");
        var userId = Guid.NewGuid();
        // The user is Admin of teamB only — attempting to delete teamA must 403.
        await Fx.SeedTeamMembershipAsync(teamB, userId, AdminRole);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.Member },
                subject: userId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.DeleteAsync($"/api/v1/organizations/teams/{teamA}");

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Cross_tenant_returns_404()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Delete-Cross");
        await Fx.SeedOrganizationAsync(OtherTenant.Value, "OrgB-Delete-Cross");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Secret");
        try
        {
            var client = Fx.CreateClient();
            // Token scoped to OtherTenant — RLS hides the team in Tenant. 404, not 403.
            var token = Fx.Signer.IssueForTenant(OtherTenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.DeleteAsync($"/api/v1/organizations/teams/{teamId}");

            Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
            await Fx.DeleteTeamsForTenantAsync(OtherTenant.Value);
        }
    }
}
