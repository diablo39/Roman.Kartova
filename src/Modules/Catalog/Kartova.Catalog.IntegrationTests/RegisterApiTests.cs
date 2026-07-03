using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class RegisterApiTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static object Body(Guid teamId, ApiStyle style = ApiStyle.Rest, string version = "v1",
        string? specUrl = "https://specs.example.com/openapi.json") => new
    {
        displayName = "orders-api",
        description = "Orders REST API.",
        style,
        version,
        specUrl,
        teamId,
    };

    [TestMethod]
    public async Task POST_with_valid_payload_returns_201_and_roundtrips()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId, ApiStyle.Grpc, "2.0"));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("orders-api", body!.DisplayName);
        Assert.AreEqual(ApiStyle.Grpc, body.Style);
        Assert.AreEqual("2.0", body.Version);
        Assert.AreEqual(teamId, body.TeamId);

        // Round-trips through GET-by-id.
        var get = await client.GetAsync($"/api/v1/catalog/apis/{body.Id}");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
    }

    [TestMethod]
    public async Task POST_allows_null_spec_url()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team Null");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId, specUrl: null));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNull(body!.SpecUrl);
    }

    [TestMethod]
    public async Task POST_without_token_returns_401()
    {
        using var anon = Fx.CreateAnonymousClient();
        var resp = await anon.PostAsJsonAsync("/api/v1/catalog/apis", Body(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_empty_display_name_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team 400");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis",
            new { displayName = "", description = "d", style = ApiStyle.Rest, version = "v1", specUrl = (string?)null, teamId });
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_relative_spec_url_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team Url");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId, specUrl: "/relative/openapi.json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_unknown_team_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", Body(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_by_member_not_in_target_team_returns_403()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Api Team Restricted");
        var memberClient = await Fx.CreateAuthenticatedClientAsync(
            "member@orga.kartova.local", new[] { KartovaRoles.Member });

        var resp = await memberClient.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId));
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_sets_CreatedByUserId_to_caller_sub()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team Identity");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        var expectedSub = await Fx.GetSubClaimAsync(OrgAUser);
        Assert.AreEqual(expectedSub, body!.CreatedByUserId);
    }

    [TestMethod]
    public async Task GET_by_id_unknown_returns_404()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/apis/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_by_id_from_other_tenant_returns_404()
    {
        // Register as OrgA, then attempt to read it as OrgB (RLS must hide it).
        var clientA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team XT");
        var created = await clientA.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId));
        var body = await created.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);

        var clientB = await Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var resp = await clientB.GetAsync($"/api/v1/catalog/apis/{body!.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
