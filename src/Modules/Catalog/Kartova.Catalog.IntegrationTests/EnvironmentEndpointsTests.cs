using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Real-seam (gate-3) integration tests for the Environment slice-1 endpoints (E-02.F-05.S-01):
/// <c>POST /catalog/environments</c>, <c>GET /catalog/environments</c>,
/// <c>GET /catalog/environments/{id}</c>. Exercises the real Postgres/RLS + JWT seam via
/// <see cref="KartovaApiFixtureBase"/> — proves the register/list/get wiring, the per-tenant
/// display-name uniqueness (pre-check + DB backstop), resource-details validation, the type/region
/// list filters, and tenant isolation (Environment has no owning team — see
/// <see cref="InfrastructureVmEndpointsTests"/> for the equivalent team-owned slice).
/// </summary>
[TestClass]
public sealed class EnvironmentEndpointsTests : CatalogIntegrationTestBase
{
    // ADR-0109: enums travel on the wire as camelCase strings.
    private static object Body(
        string displayName, EnvironmentType type = EnvironmentType.Production,
        string description = "desc", string? region = "eu-west-1", string? cluster = "c1")
        => new
        {
            displayName,
            description,
            type = type.ToString().ToLowerInvariant(),
            region,
            cluster,
            resourceDetails = new Dictionary<string, string>(),
        };

    [TestMethod]
    public async Task RegisterEnvironment_returns_201_and_roundtrips()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-a.test");
        var post = await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Prod EU"));
        Assert.AreEqual(HttpStatusCode.Created, post.StatusCode);

        var created = await post.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(created);
        Assert.AreEqual("Prod EU", created!.DisplayName);
        Assert.AreEqual(EnvironmentType.Production, created.Type);

        var get = await client.GetAsync($"/api/v1/catalog/environments/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        var fetched = await get.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(created.Id, fetched!.Id);
        Assert.AreEqual("eu-west-1", fetched.Region);
    }

    [TestMethod]
    public async Task RegisterEnvironment_without_permission_returns_403()
    {
        var viewer = await Fx.CreateAuthenticatedClientAsync("viewer@env-a.test", roles: new[] { KartovaRoles.Viewer });
        var post = await viewer.PostAsJsonAsync("/api/v1/catalog/environments", Body("No Perm"));
        Assert.AreEqual(HttpStatusCode.Forbidden, post.StatusCode);
    }

    [TestMethod]
    public async Task RegisterEnvironment_duplicate_name_returns_409_with_type()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-dup.test");
        Assert.AreEqual(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Staging"))).StatusCode);

        var dup = await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Staging"));
        Assert.AreEqual(HttpStatusCode.Conflict, dup.StatusCode);
        var body = await dup.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "environment-name-conflict");
    }

    [TestMethod]
    public async Task RegisterEnvironment_invalid_resource_details_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-a.test");
        var bad = new
        {
            displayName = "Bad",
            description = "d",
            type = "development",
            region = (string?)null,
            cluster = (string?)null,
            resourceDetails = new Dictionary<string, string> { [new string('k', 200)] = "v" },
        };
        var post = await client.PostAsJsonAsync("/api/v1/catalog/environments", bad);
        Assert.AreEqual(HttpStatusCode.BadRequest, post.StatusCode);
    }

    [TestMethod]
    public async Task ListEnvironments_default_sort_is_display_name_asc()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-list.test");
        foreach (var n in new[] { "Charlie", "Alpha", "Bravo" })
            await client.PostAsJsonAsync("/api/v1/catalog/environments", Body(n));

        var page = await client.GetFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(
            "/api/v1/catalog/environments", KartovaApiFixtureBase.WireJson);
        var names = page!.Items.Select(i => i.DisplayName).ToList();
        CollectionAssert.AreEqual(new[] { "Alpha", "Bravo", "Charlie" }, names);
    }

    [TestMethod]
    public async Task ListEnvironments_filters_by_type_and_region()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-filter.test");
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Prod A", EnvironmentType.Production, region: "eu"));
        await client.PostAsJsonAsync("/api/v1/catalog/environments", Body("Dev A", EnvironmentType.Development, region: "us"));

        var byType = await client.GetFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(
            "/api/v1/catalog/environments?type=production", KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(byType!.Items.All(i => i.Type == EnvironmentType.Production));

        var byRegion = await client.GetFromJsonAsync<CursorPage<EnvironmentListItemResponse>>(
            "/api/v1/catalog/environments?region=us", KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(byRegion!.Items.All(i => i.Region == "us"));
    }

    [TestMethod]
    public async Task ListEnvironments_rejects_invalid_type_filter_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-a.test");
        var resp = await client.GetAsync("/api/v1/catalog/environments?type=bogus");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GetEnvironment_unknown_id_returns_404()
    {
        var client = await Fx.CreateAuthenticatedClientAsync("admin@env-a.test");
        var resp = await client.GetAsync($"/api/v1/catalog/environments/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Environment_is_tenant_isolated()
    {
        var a = await Fx.CreateAuthenticatedClientAsync("admin@tenant-one.test");
        var post = await a.PostAsJsonAsync("/api/v1/catalog/environments", Body("Isolated"));
        var created = await post.Content.ReadFromJsonAsync<EnvironmentDetailResponse>(KartovaApiFixtureBase.WireJson);

        var b = await Fx.CreateAuthenticatedClientAsync("admin@tenant-two.test");
        var crossGet = await b.GetAsync($"/api/v1/catalog/environments/{created!.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, crossGet.StatusCode);

        // Same display name in a different tenant must be allowed (uniqueness is per-tenant).
        var bPost = await b.PostAsJsonAsync("/api/v1/catalog/environments", Body("Isolated"));
        Assert.AreEqual(HttpStatusCode.Created, bPost.StatusCode);
    }
}
