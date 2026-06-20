using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class GetServiceByIdTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private static async Task<ServiceResponse> RegisterAsync(HttpClient client, string email)
    {
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(email), "Get Team");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = "get-svc", description = "d", teamId,
            endpoints = new[] { new { url = "https://api.example.com", protocol = Protocol.Rest } },
        });
        Assert.IsTrue(resp.IsSuccessStatusCode);
        return (await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    [TestMethod]
    public async Task GET_returns_200_for_service_in_same_tenant()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var created = await RegisterAsync(client, OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/services/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(created.Id, body!.Id);
    }

    [TestMethod]
    public async Task GET_returns_404_for_nonexistent_id()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/services/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_returns_404_for_other_tenants_id()
    {
        var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var created = await RegisterAsync(orgA, OrgAUser);
        var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await orgB.GetAsync($"/api/v1/catalog/services/{created.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
