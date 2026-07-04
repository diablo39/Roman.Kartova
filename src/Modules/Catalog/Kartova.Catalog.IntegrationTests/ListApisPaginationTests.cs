using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>Cursor-pagination and sort-order integration tests for the APIs list endpoint (ADR-0095).</summary>
[TestClass]
public sealed class ListApisPaginationTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static async Task Seed(HttpClient client, Guid teamId, string name, string version = "v1") =>
        await SeedWithStyle(client, teamId, name, ApiStyle.Rest, version);

    private static async Task SeedWithStyle(HttpClient client, Guid teamId, string name, ApiStyle style, string version)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", new
        {
            displayName = name, description = "seed.", style, version, specUrl = (string?)null, teamId,
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
    }

    // NOTE: CatalogIntegrationTestBase's Fx fixture is assembly-scoped (one shared tenant DB
    // for the whole IntegrationTests assembly, no per-class reset). /apis has no teamId/attribute
    // filter this slice (FU-9), so — mirroring ListServicesPaginationTests.GET_default_sort_is_displayName_ascending
    // and GET_returns_cursor_page_envelope_and_paginates_forward — the envelope/paging shape is
    // asserted separately (counts + NextCursor only, tolerant of other tenants' rows) from the
    // exact ordering (unique-prefixed names, fetched at limit=200 and filtered down before the
    // exact-order assertion) rather than asserting page.Items[0] directly off an unscoped call.
    [TestMethod]
    public async Task List_paginates_forward_with_cursor()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api List Team");
        var unique = $"page-api-{Guid.NewGuid():N}";
        await Seed(client, teamId, $"{unique}-1");
        await Seed(client, teamId, $"{unique}-2");
        await Seed(client, teamId, $"{unique}-3");

        var firstResp = await client.GetAsync("/api/v1/catalog/apis?sortBy=displayName&sortOrder=asc&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, firstResp.StatusCode);
        var first = await firstResp.Content.ReadFromJsonAsync<CursorPage<ApiResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(2, first!.Items.Count);
        Assert.IsNotNull(first.NextCursor);

        var nextResp = await client.GetAsync(
            $"/api/v1/catalog/apis?sortBy=displayName&sortOrder=asc&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, nextResp.StatusCode);
        var next = await nextResp.Content.ReadFromJsonAsync<CursorPage<ApiResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(next!.Items.Count >= 1);
    }

    [TestMethod]
    public async Task List_default_sort_is_displayName_ascending()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Default Sort Team");
        var unique = Guid.NewGuid().ToString("N");
        var names = new[] { $"dsort-{unique}-zzz", $"dsort-{unique}-aaa", $"dsort-{unique}-mmm" };
        foreach (var name in names)
        {
            await Seed(client, teamId, name);
        }

        // No sortBy/sortOrder ⇒ endpoint default must be displayName asc.
        var resp = await client.GetAsync("/api/v1/catalog/apis?limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApiResponse>>(KartovaApiFixtureBase.WireJson);
        var seeded = page!.Items.Select(i => i.DisplayName)
            .Where(n => names.Contains(n)).ToList();
        CollectionAssert.AreEqual(
            new[] { $"dsort-{unique}-aaa", $"dsort-{unique}-mmm", $"dsort-{unique}-zzz" },
            seeded,
            "default order must be ascending displayName");
    }

    [TestMethod]
    public async Task List_honors_sortBy_version_and_style()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Sort Team");
        var unique = Guid.NewGuid().ToString("N");
        await SeedWithStyle(client, teamId, $"vsort-{unique}-1", ApiStyle.Grpc, "1.0");
        await SeedWithStyle(client, teamId, $"vsort-{unique}-2", ApiStyle.Rest, "3.0");
        await SeedWithStyle(client, teamId, $"vsort-{unique}-3", ApiStyle.GraphQL, "2.0");

        // sortBy=version asc: real order must be 1.0, 2.0, 3.0 — fails if the Version
        // sort spec is swapped or broken (e.g. sorting by displayName/style instead).
        var byVersionResp = await client.GetAsync("/api/v1/catalog/apis?sortBy=version&sortOrder=asc&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, byVersionResp.StatusCode);
        var byVersion = await byVersionResp.Content.ReadFromJsonAsync<CursorPage<ApiResponse>>(KartovaApiFixtureBase.WireJson);
        var byVersionSeeded = byVersion!.Items
            .Where(i => i.DisplayName.StartsWith($"vsort-{unique}-", StringComparison.Ordinal))
            .Select(i => i.Version).ToList();
        CollectionAssert.AreEqual(new[] { "1.0", "2.0", "3.0" }, byVersionSeeded, "sortBy=version asc must order by version");

        // sortBy=style desc: styles are Rest(0) < Grpc(1) < GraphQL(2) by enum declaration
        // order (see ApiStyle) — desc must yield GraphQL, Grpc, Rest. Fails if the Style
        // sort spec is swapped or broken.
        var byStyleResp = await client.GetAsync("/api/v1/catalog/apis?sortBy=style&sortOrder=desc&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, byStyleResp.StatusCode);
        var byStyle = await byStyleResp.Content.ReadFromJsonAsync<CursorPage<ApiResponse>>(KartovaApiFixtureBase.WireJson);
        var byStyleSeeded = byStyle!.Items
            .Where(i => i.DisplayName.StartsWith($"vsort-{unique}-", StringComparison.Ordinal))
            .Select(i => i.Style).ToList();
        CollectionAssert.AreEqual(
            new[] { ApiStyle.GraphQL, ApiStyle.Grpc, ApiStyle.Rest }, byStyleSeeded, "sortBy=style desc must order by style");
    }

    [TestMethod]
    public async Task List_rejects_unknown_sortBy_with_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync("/api/v1/catalog/apis?sortBy=bogus");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // Sibling-envelope adjustment: ListApplicationsPaginationTests.LimitOutOfRange_returns_400
    // asserts 400 + RFC 7807 "invalid-limit" body for out-of-range ?limit values (the shared
    // CursorListBinding never returns 422 for this case) — mirrored here instead of the plan's
    // draft 422 expectation.
    [TestMethod]
    public async Task List_rejects_out_of_range_limit_with_400_invalid_limit()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync("/api/v1/catalog/apis?limit=99999");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "invalid-limit");
    }

    [TestMethod]
    public async Task List_is_tenant_isolated()
    {
        var clientA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Iso Team");
        var uniqueName = $"orga-only-api-{Guid.NewGuid():N}";
        await Seed(clientA, teamId, uniqueName);

        var clientB = await Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var pageB = await clientB.GetFromJsonAsync<CursorPage<ApiResponse>>(
            "/api/v1/catalog/apis?limit=200", KartovaApiFixtureBase.WireJson);
        Assert.IsFalse(pageB!.Items.Any(a => a.DisplayName == uniqueName));
    }
}
