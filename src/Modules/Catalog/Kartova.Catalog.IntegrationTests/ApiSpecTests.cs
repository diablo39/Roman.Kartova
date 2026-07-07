using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>Real-seam tests for PUT/GET /apis/{id}/spec (ADR-0112).</summary>
[TestClass]
public class ApiSpecTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string SpecJson = "{\"openapi\":\"3.0.0\",\"info\":{\"title\":\"Orders\",\"version\":\"1\"}}";

    private static object RegisterBody(Guid teamId) => new
    {
        displayName = "orders-api",
        description = "Orders REST API.",
        style = ApiStyle.Rest,
        version = "v1",
        specUrl = (string?)null,
        teamId,
    };

    private async Task<Guid> RegisterApiAsync(HttpClient client, Guid teamId)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", RegisterBody(teamId));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static HttpRequestMessage SpecRequest(HttpMethod method, Guid apiId, string? content = null,
        string mediaType = "application/json")
    {
        var req = new HttpRequestMessage(method, $"/api/v1/catalog/apis/{apiId}/spec");
        if (content is not null)
        {
            req.Content = new StringContent(content);
            req.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        }

        return req;
    }

    [TestMethod]
    public async Task PUT_with_valid_json_returns_201_and_roundtrips_via_GET()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team A");
        var apiId = await RegisterApiAsync(client, teamId);

        var putResp = await client.SendAsync(SpecRequest(HttpMethod.Put, apiId, SpecJson));
        Assert.AreEqual(HttpStatusCode.Created, putResp.StatusCode);

        var getResp = await client.GetAsync($"/api/v1/catalog/apis/{apiId}/spec");
        Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);
        Assert.AreEqual("application/json", getResp.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual(SpecJson, await getResp.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task PUT_with_charset_suffixed_content_type_returns_201_and_echoes_bare_media_type()
    {
        // Real clients (StringContent(s, Encoding.UTF8, "application/json"), browsers, most tooling)
        // send `application/json; charset=utf-8`. The delegate must normalize to the bare media type
        // before the allow-list check (else 415 on the happy path) and store/echo it bare.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team Charset");
        var apiId = await RegisterApiAsync(client, teamId);

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/apis/{apiId}/spec")
        {
            Content = new StringContent(SpecJson, Encoding.UTF8, "application/json"),
        };
        Assert.AreEqual("utf-8", req.Content.Headers.ContentType?.CharSet,
            "Test precondition: StringContent must emit a charset param to exercise the regression.");

        var putResp = await client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.Created, putResp.StatusCode);

        var getResp = await client.GetAsync($"/api/v1/catalog/apis/{apiId}/spec");
        Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);
        Assert.AreEqual("application/json", getResp.Content.Headers.ContentType?.MediaType);
        Assert.IsNull(getResp.Content.Headers.ContentType?.CharSet,
            "Stored/echoed media type must be bare (no charset).");
        Assert.AreEqual(SpecJson, await getResp.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task PUT_with_empty_body_returns_400()
    {
        // Whitespace-only content trips ApiSpec's IsNullOrWhiteSpace reject → ArgumentException → 400.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team Empty");
        var apiId = await RegisterApiAsync(client, teamId);

        var resp = await client.SendAsync(SpecRequest(HttpMethod.Put, apiId, "   "));
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_second_write_replaces_and_returns_204()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team B");
        var apiId = await RegisterApiAsync(client, teamId);

        var firstPut = await client.SendAsync(SpecRequest(HttpMethod.Put, apiId, SpecJson));
        Assert.AreEqual(HttpStatusCode.Created, firstPut.StatusCode);

        const string replacement = "{\"openapi\":\"3.1.0\"}";
        var secondPut = await client.SendAsync(SpecRequest(HttpMethod.Put, apiId, replacement));
        Assert.AreEqual(HttpStatusCode.NoContent, secondPut.StatusCode);

        var getResp = await client.GetAsync($"/api/v1/catalog/apis/{apiId}/spec");
        Assert.AreEqual(replacement, await getResp.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task PUT_accepts_yaml_media_type()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team Yaml");
        var apiId = await RegisterApiAsync(client, teamId);

        const string yaml = "openapi: 3.0.0\ninfo:\n  title: Orders\n  version: '1'\n";
        var putResp = await client.SendAsync(SpecRequest(HttpMethod.Put, apiId, yaml, "application/yaml"));
        Assert.AreEqual(HttpStatusCode.Created, putResp.StatusCode);

        var getResp = await client.GetAsync($"/api/v1/catalog/apis/{apiId}/spec");
        Assert.AreEqual("application/yaml", getResp.Content.Headers.ContentType?.MediaType);
        Assert.AreEqual(yaml, await getResp.Content.ReadAsStringAsync());
    }

    [TestMethod]
    public async Task PUT_sets_HasSpec_true_on_the_api_resource()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team HasSpec");
        var apiId = await RegisterApiAsync(client, teamId);

        var before = await client.GetAsync($"/api/v1/catalog/apis/{apiId}");
        var beforeBody = await before.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsFalse(beforeBody!.HasSpec);

        var putResp = await client.SendAsync(SpecRequest(HttpMethod.Put, apiId, SpecJson));
        Assert.AreEqual(HttpStatusCode.Created, putResp.StatusCode);

        var after = await client.GetAsync($"/api/v1/catalog/apis/{apiId}");
        var afterBody = await after.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(afterBody!.HasSpec);
    }

    [TestMethod]
    public async Task GET_returns_404_when_no_spec_stored()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team NoSpec");
        var apiId = await RegisterApiAsync(client, teamId);

        var getResp = await client.GetAsync($"/api/v1/catalog/apis/{apiId}/spec");
        Assert.AreEqual(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_for_unknown_api_id_returns_404()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.SendAsync(SpecRequest(HttpMethod.Put, Guid.NewGuid(), SpecJson));
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_for_unknown_api_id_returns_404()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/apis/{Guid.NewGuid()}/spec");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_by_member_not_in_target_team_returns_403()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var ownerClient = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Spec Team Forbidden");
        var apiId = await RegisterApiAsync(ownerClient, teamId);

        var memberClient = await Fx.CreateAuthenticatedClientAsync(
            "member@orga.kartova.local", new[] { KartovaRoles.Member });

        var resp = await memberClient.SendAsync(SpecRequest(HttpMethod.Put, apiId, SpecJson));
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_from_other_tenant_returns_404()
    {
        var ownerClient = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team XT");
        var apiId = await RegisterApiAsync(ownerClient, teamId);

        var otherTenantClient = await Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var resp = await otherTenantClient.SendAsync(SpecRequest(HttpMethod.Put, apiId, SpecJson));
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_from_other_tenant_returns_404()
    {
        var ownerClient = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team XT2");
        var apiId = await RegisterApiAsync(ownerClient, teamId);
        var putResp = await ownerClient.SendAsync(SpecRequest(HttpMethod.Put, apiId, SpecJson));
        Assert.AreEqual(HttpStatusCode.Created, putResp.StatusCode);

        var otherTenantClient = await Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var resp = await otherTenantClient.GetAsync($"/api/v1/catalog/apis/{apiId}/spec");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_with_unsupported_content_type_returns_415()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team 415");
        var apiId = await RegisterApiAsync(client, teamId);

        var resp = await client.SendAsync(SpecRequest(HttpMethod.Put, apiId, "plain text", "text/plain"));
        Assert.AreEqual(HttpStatusCode.UnsupportedMediaType, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_with_oversized_content_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Spec Team 400");
        var apiId = await RegisterApiAsync(client, teamId);

        var oversized = new string('a', ApiSpec.MaxContentBytes + 1);
        var resp = await client.SendAsync(SpecRequest(HttpMethod.Put, apiId, oversized));
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_without_token_returns_401()
    {
        var anon = Fx.CreateAnonymousClient();
        var resp = await anon.SendAsync(SpecRequest(HttpMethod.Put, Guid.NewGuid(), SpecJson));
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_without_token_returns_401()
    {
        var anon = Fx.CreateAnonymousClient();
        var resp = await anon.GetAsync($"/api/v1/catalog/apis/{Guid.NewGuid()}/spec");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
