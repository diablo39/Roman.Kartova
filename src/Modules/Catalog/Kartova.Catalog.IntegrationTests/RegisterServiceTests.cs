using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class RegisterServiceTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static object Body(Guid teamId, params (string Url, Protocol Protocol)[] eps) => new
    {
        displayName = "orders-svc",
        description = "Order service.",
        teamId,
        endpoints = eps.Select(e => new { url = e.Url, protocol = e.Protocol }).ToArray(),
    };

    [TestMethod]
    public async Task POST_with_valid_payload_returns_201_and_echoes_endpoints_and_unknown_health()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services",
            Body(teamId, ("https://api.example.com/v1", Protocol.Rest), ("grpc://api.example.com", Protocol.Grpc)));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("orders-svc", body!.DisplayName);
        Assert.AreEqual(HealthStatus.Unknown, body.Health);
        Assert.AreEqual(2, body.Endpoints.Count);
        Assert.AreEqual(Protocol.Grpc, body.Endpoints[1].Protocol);
        Assert.AreEqual(teamId, body.TeamId);
    }

    [TestMethod]
    public async Task POST_with_zero_endpoints_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team 0");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", Body(teamId));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(0, body!.Endpoints.Count);
    }

    [TestMethod]
    public async Task POST_without_token_returns_401()
    {
        using var anon = Fx.CreateAnonymousClient();
        var resp = await anon.PostAsJsonAsync("/api/v1/catalog/services", Body(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_empty_display_name_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team 400");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services",
            new { displayName = "", description = "d", teamId, endpoints = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_relative_endpoint_url_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team Url");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = "svc", description = "d", teamId,
            endpoints = new[] { new { url = "/relative/path", protocol = Protocol.Rest } },
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_unknown_team_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", Body(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_51_endpoints_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team 51");
        var eps = Enumerable.Range(0, 51).Select(i => ($"https://h{i}.example.com", Protocol.Rest)).ToArray();
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", Body(teamId, eps));
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // F7 (gate8 Imp-1 test): null element in endpoints array must not cause HTTP 500
    [TestMethod]
    public async Task POST_with_null_endpoint_element_does_not_return_500()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team NullEp");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = "null-ep-svc",
            description = "testing null endpoint element.",
            teamId,
            endpoints = new object?[] { null },
        });
        Assert.IsTrue((int)resp.StatusCode < 500,
            $"Expected < 500 when endpoints contains a null element, but got {(int)resp.StatusCode}.");
    }

    // F8 (gate9 SF-1): CreatedByUserId in the response must match the JWT sub of the registering user
    [TestMethod]
    public async Task POST_sets_CreatedByUserId_to_caller_sub()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Svc Team Identity");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services",
            Body(teamId, ("https://api.example.com/v1", Protocol.Rest)));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        var expectedSub = await Fx.GetSubClaimAsync(OrgAUser);
        Assert.AreEqual(expectedSub, body!.CreatedByUserId);
    }

    // F9 (gate8 Imp-3): Member not in target team is denied with 403
    [TestMethod]
    public async Task POST_by_member_not_in_target_team_returns_403()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Svc Team Restricted");
        // Member is NOT added to this team — the membership gate must fire.
        var memberClient = await Fx.CreateAuthenticatedClientAsync(
            "member@orga.kartova.local", new[] { KartovaRoles.Member });

        var resp = await memberClient.PostAsJsonAsync("/api/v1/catalog/services",
            Body(teamId, ("https://api.example.com/v1", Protocol.Rest)));

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
