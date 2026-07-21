using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>Real-seam <c>GET /systems/{id}</c> integration tests (E-03.F-03.S-01, Task 12).
/// Mirrors <see cref="GetServiceByIdTests"/>'s simple 200/404/cross-tenant-404 shape.</summary>
[TestClass]
public sealed class GetSystemSurfaceTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private static async Task<SystemResponse> RegisterAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems",
            new { displayName = name, description = "d", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"Register '{name}' failed: {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    [TestMethod]
    public async Task GET_returns_200_for_system_in_same_tenant()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Get System Team");
        var created = await RegisterAsync(client, teamId, "get-system");

        var resp = await client.GetAsync($"/api/v1/catalog/systems/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(created.Id, body!.Id);
        Assert.AreEqual("get-system", body.DisplayName);
    }

    [TestMethod]
    public async Task GET_returns_404_for_nonexistent_id()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/systems/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_returns_404_for_other_tenants_id()
    {
        var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Get System Team XT");
        var created = await RegisterAsync(orgA, teamId, "get-system-xt");

        var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await orgB.GetAsync($"/api/v1/catalog/systems/{created.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_returns_200_for_a_row_seeded_via_the_bypass_helper()
    {
        // Exercises KartovaApiFixture.SeedSystemAsync (Task 12) directly, independent of the
        // HTTP register path — proves the seeded row round-trips through the real read handler.
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Get System Seed Team");
        var systemId = await Fx.SeedSystemAsync(tenantId, teamId, $"seeded-system-{Guid.NewGuid():N}");

        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/systems/{systemId}");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(systemId, body!.Id);
        Assert.AreEqual(teamId, body.TeamId);
    }
}
