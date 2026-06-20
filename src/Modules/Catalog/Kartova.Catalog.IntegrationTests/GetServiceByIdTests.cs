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

    // F13(a): multi-endpoint round-trip — url, protocol, and order are preserved
    [TestMethod]
    public async Task GET_returns_endpoints_with_correct_url_protocol_and_order()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Get Team Multi");
        var registerResp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = "multi-ep-svc", description = "endpoint round-trip test", teamId,
            endpoints = new[]
            {
                new { url = "https://api.example.com/rest", protocol = Protocol.Rest },
                new { url = "grpc://api.example.com:443", protocol = Protocol.Grpc },
            },
        });
        Assert.IsTrue(registerResp.IsSuccessStatusCode);
        var created = (await registerResp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson))!;

        var resp = await client.GetAsync($"/api/v1/catalog/services/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(2, body!.Endpoints.Count);
        Assert.AreEqual("https://api.example.com/rest", body.Endpoints[0].Url);
        Assert.AreEqual(Protocol.Rest, body.Endpoints[0].Protocol);
        Assert.AreEqual("grpc://api.example.com:443", body.Endpoints[1].Url);
        Assert.AreEqual(Protocol.Grpc, body.Endpoints[1].Protocol);
    }

    // F13(b): zero-endpoint round-trip — Endpoints is non-null and empty; Health is Unknown
    [TestMethod]
    public async Task GET_returns_empty_endpoints_and_unknown_health_for_zero_endpoint_service()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Get Team Zero");
        var registerResp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = "zero-ep-svc", description = "zero endpoint round-trip test", teamId,
            endpoints = Array.Empty<object>(),
        });
        Assert.IsTrue(registerResp.IsSuccessStatusCode);
        var created = (await registerResp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson))!;

        var resp = await client.GetAsync($"/api/v1/catalog/services/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(body!.Endpoints);
        Assert.AreEqual(0, body.Endpoints.Count);
        Assert.AreEqual(HealthStatus.Unknown, body.Health);
    }

    // F10 (gate9 SF-2): CreatedBy enrichment — GET by id returns non-null CreatedBy whose
    // display fields match the registering user (mirrors ApplicationOwnerEnrichmentTests).
    [TestMethod]
    public async Task GET_returns_CreatedBy_populated_when_user_row_exists()
    {
        var unique = $"svc-creator-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var creatorUserId = await Fx.SeedUserInOrganizationAsync(
            tenantId,
            displayName: "Service Creator",
            email: $"{unique}@orga.kartova.local");

        var client = await Fx.CreateAuthenticatedClientAsync(
            OrgAUser, subjectOverride: creatorUserId);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Get Team Enrich");
        var registerResp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = $"enrich-svc-{unique}",
            description = "CreatedBy enrichment test.",
            teamId,
            endpoints = Array.Empty<object>(),
        });
        Assert.IsTrue(registerResp.IsSuccessStatusCode);
        var created = (await registerResp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson))!;

        try
        {
            var resp = await client.GetAsync($"/api/v1/catalog/services/{created.Id}");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body!.CreatedBy,
                "CreatedBy must be populated when a matching users row exists.");
            Assert.AreEqual(creatorUserId, body.CreatedBy!.Id);
            Assert.AreEqual("Service Creator", body.CreatedBy.DisplayName);
            Assert.AreEqual($"{unique}@orga.kartova.local", body.CreatedBy.Email);
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(creatorUserId);
        }
    }
}
