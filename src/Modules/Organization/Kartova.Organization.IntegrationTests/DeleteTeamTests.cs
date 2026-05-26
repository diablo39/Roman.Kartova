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
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
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
