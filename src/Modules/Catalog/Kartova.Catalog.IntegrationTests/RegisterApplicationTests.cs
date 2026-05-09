using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class RegisterApplicationTests : CatalogIntegrationTestBase
{
    [TestMethod]
    public async Task POST_with_valid_payload_creates_row_and_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("payments-api", "Payments API", "Payments REST surface."));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        StringAssert.StartsWith(resp.Headers.Location!.ToString(), "/api/v1/catalog/applications/");

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(body);
        Assert.AreEqual("payments-api", body!.Name);
        Assert.AreEqual("Payments API", body.DisplayName);
        Assert.AreEqual("Payments REST surface.", body.Description);
        Assert.AreNotEqual(Guid.Empty, body.Id);

        // Slice 5 wire-shape pin: lifecycle defaults to Active, no sunset on
        // create, Version is base64(4-byte xmin) and round-trips via VersionEncoding.
        Assert.AreEqual(Lifecycle.Active, body.Lifecycle);
        Assert.IsNull(body.SunsetDate);
        Assert.IsFalse(string.IsNullOrWhiteSpace(body.Version));
        Assert.IsTrue(VersionEncoding.TryDecode(body.Version, out _));
    }

    [TestMethod]
    public async Task POST_persists_owner_user_id_from_jwt_sub_claim()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var subFromToken = await Fx.GetSubClaimAsync("admin@orga.kartova.local");

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("svc-x", "Svc X", "x"));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(subFromToken, body!.OwnerUserId);
    }

    [TestMethod]
    public async Task POST_persists_tenant_id_from_scope_not_payload()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var tenantFromToken = await Fx.GetTenantIdClaimAsync("admin@orga.kartova.local");

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("svc-y", "Svc Y", "y"));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(tenantFromToken, body!.TenantId);
    }

    [TestMethod]
    [DataRow("", "Display", "desc")]
    [DataRow("   ", "Display", "desc")]
    [DataRow("name", "", "desc")]
    [DataRow("name", "  ", "desc")]
    [DataRow("name", "Display", "")]
    [DataRow("name", "Display", "  ")]
    [DataRow("BadName", "Display", "desc")]      // kebab-case: uppercase
    [DataRow("bad_name", "Display", "desc")]     // underscore
    [DataRow("bad name", "Display", "desc")]     // space
    [DataRow("9digit", "Display", "desc")]       // leading digit
    public async Task POST_with_invalid_payload_returns_400(string name, string displayName, string description)
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, displayName, description));

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType!.MediaType);
    }

    [TestMethod]
    public async Task GET_by_id_returns_row_in_same_tenant()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var post = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("svc-z", "Svc Z", "z"));
        var created = await post.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);

        var get = await client.GetAsync($"/api/v1/catalog/applications/{created!.Id}");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        var fetched = await get.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(created.Id, fetched!.Id);
        Assert.AreEqual("svc-z", fetched.Name);
    }

    [TestMethod]
    public async Task GET_by_id_returns_404_for_unknown_id()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.GetAsync($"/api/v1/catalog/applications/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType!.MediaType);
    }

    [TestMethod]
    public async Task GET_by_id_emits_ETag_header_matching_Version_field()
    {
        // Slice 5 §13.5 — the single-resource GET emits an RFC 7232 quoted ETag
        // whose value is the base64-encoded xmin row version. Clients capture
        // it for a future PUT If-Match request (Task 8+).
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var registered = await CreateApp(client, "etag-app");

        var resp = await client.GetAsync($"/api/v1/catalog/applications/{registered.Id}");
        Assert.IsTrue(resp.IsSuccessStatusCode);

        var etag = resp.Headers.ETag?.Tag;
        Assert.IsFalse(string.IsNullOrWhiteSpace(etag));
        Assert.AreEqual($"\"{registered.Version}\"", etag);        // RFC 7232 quoted
    }

    [TestMethod]
    public async Task GET_list_returns_apps_in_current_tenant_sorted_by_createdAt()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var first = await CreateApp(client, "first-app-list");
        var second = await CreateApp(client, "second-app-list");

        var resp = await client.GetAsync("/api/v1/catalog/applications");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);

        Assert.IsNotNull(page);
        var ids = page!.Items.Select(x => x.Id).ToList();
        CollectionAssert.Contains(ids, first.Id);
        CollectionAssert.Contains(ids, second.Id);
        // Default sort is createdAt desc — both newly created apps appear at the front.
        Assert.IsTrue(page!.Items.Any());
    }

    private static async Task<ApplicationResponse> CreateApp(HttpClient c, string name)
    {
        var post = await c.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, name, $"desc for {name}"));
        return (await post.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    [TestMethod]
    public async Task POST_with_invalid_displayName_returns_field_level_problem_details()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("svc-fl", "", "desc"));

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var doc = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var errors = doc.GetProperty("errors");
        StringAssert.Contains(
            errors.GetProperty("displayName").EnumerateArray().Single().GetString(),
            "must not be empty");
    }

    [TestMethod]
    public async Task POST_without_token_returns_401()
    {
        using var client = Fx.CreateAnonymousClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("name", "Name", "desc"));

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_by_id_returns_404_for_other_tenants_row()
    {
        // OrgA creates a row, OrgB tries to fetch it by id. Must 404 — never leak
        // existence (no 403, no 200). Pins the cross-tenant isolation guarantee.
        var clientA = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var post = await clientA.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("orga-private", "Orga Private", "owned by orga"));
        Assert.AreEqual(HttpStatusCode.Created, post.StatusCode);
        var orgaApp = await post.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);

        var clientB = await Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var resp = await clientB.GetAsync($"/api/v1/catalog/applications/{orgaApp!.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_list_excludes_other_tenants_rows()
    {
        // OrgA seeds a row; OrgB's list must not include it. RLS + tenant scope
        // together must shield orgb from any orga rows.
        var clientA = await Fx.CreateAuthenticatedClientAsync("admin@orga.kartova.local");
        var orgaApp = await CreateApp(clientA, "orga-isolation-probe");

        var clientB = await Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var resp = await clientB.GetAsync("/api/v1/catalog/applications");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);

        Assert.IsNotNull(page);
        var rows = page!.Items;
        CollectionAssert.DoesNotContain(rows.Select(x => x.Id).ToList(), orgaApp.Id);

        var orgbTenantId = await Fx.GetTenantIdClaimAsync("admin@orgb.kartova.local");
        Assert.IsTrue(rows.All(x => x.TenantId == orgbTenantId));
    }
}
