using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>Cursor-pagination and sort/filter integration tests for <c>GET /systems</c>
/// (ADR-0095/ADR-0107, E-03.F-03.S-01 Task 12). Mirrors <see cref="ListApisPaginationTests"/>
/// minus the style filter (Systems have no style dimension).</summary>
[TestClass]
public sealed class ListSystemsPaginationTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private static async Task Seed(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems",
            new { displayName = name, description = "seed.", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"Seed '{name}' failed: {resp.StatusCode}");
    }

    [TestMethod]
    public async Task List_paginates_forward_with_cursor()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "System List Team");
        var unique = $"page-sys-{Guid.NewGuid():N}";
        await Seed(client, teamId, $"{unique}-1");
        await Seed(client, teamId, $"{unique}-2");
        await Seed(client, teamId, $"{unique}-3");

        var firstResp = await client.GetAsync("/api/v1/catalog/systems?sortBy=displayName&sortOrder=asc&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, firstResp.StatusCode);
        var first = await firstResp.Content.ReadFromJsonAsync<CursorPage<SystemResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(2, first!.Items.Count);
        Assert.IsNotNull(first.NextCursor);

        var nextResp = await client.GetAsync(
            $"/api/v1/catalog/systems?sortBy=displayName&sortOrder=asc&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, nextResp.StatusCode);
        var next = await nextResp.Content.ReadFromJsonAsync<CursorPage<SystemResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(next!.Items.Count >= 1);
    }

    [TestMethod]
    public async Task List_default_sort_is_displayName_ascending()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "System Default Sort Team");
        var unique = Guid.NewGuid().ToString("N");
        var names = new[] { $"dsort-{unique}-zzz", $"dsort-{unique}-aaa", $"dsort-{unique}-mmm" };
        foreach (var name in names)
        {
            await Seed(client, teamId, name);
        }

        // No sortBy/sortOrder ⇒ endpoint default must be displayName asc.
        var resp = await client.GetAsync("/api/v1/catalog/systems?limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<SystemResponse>>(KartovaApiFixtureBase.WireJson);
        var seeded = page!.Items.Select(i => i.DisplayName).Where(n => names.Contains(n)).ToList();
        CollectionAssert.AreEqual(
            new[] { $"dsort-{unique}-aaa", $"dsort-{unique}-mmm", $"dsort-{unique}-zzz" },
            seeded,
            "default order must be ascending displayName");
    }

    [TestMethod]
    public async Task List_honors_sortBy_createdAt()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "System CreatedAt Sort Team");
        var unique = Guid.NewGuid().ToString("N");
        var origin = DateTimeOffset.UtcNow.AddMinutes(-10);
        // Seed via the bypass-RLS helper with explicit, spread-apart createdAt so the sort
        // order is deterministic (HTTP-registered rows would all share ~now).
        await Fx.SeedSystemAsync(tenantId, teamId, $"csort-{unique}-late", createdAt: origin.AddMinutes(2));
        await Fx.SeedSystemAsync(tenantId, teamId, $"csort-{unique}-early", createdAt: origin);
        await Fx.SeedSystemAsync(tenantId, teamId, $"csort-{unique}-mid", createdAt: origin.AddMinutes(1));

        var resp = await client.GetAsync("/api/v1/catalog/systems?sortBy=createdAt&sortOrder=asc&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<SystemResponse>>(KartovaApiFixtureBase.WireJson);
        var seeded = page!.Items.Select(i => i.DisplayName)
            .Where(n => n.StartsWith($"csort-{unique}-", StringComparison.Ordinal)).ToList();
        CollectionAssert.AreEqual(
            new[] { $"csort-{unique}-early", $"csort-{unique}-mid", $"csort-{unique}-late" },
            seeded, "sortBy=createdAt asc must order by createdAt");
    }

    [TestMethod]
    public async Task List_rejects_unknown_sortBy_with_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync("/api/v1/catalog/systems?sortBy=bogus");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task List_rejects_out_of_range_limit_with_400_invalid_limit()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync("/api/v1/catalog/systems?limit=99999");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "invalid-limit");
    }

    [TestMethod]
    public async Task List_is_tenant_isolated()
    {
        var clientA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "System Iso Team");
        var uniqueName = $"orga-only-sys-{Guid.NewGuid():N}";
        await Seed(clientA, teamId, uniqueName);

        var clientB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var pageB = await clientB.GetFromJsonAsync<CursorPage<SystemResponse>>(
            "/api/v1/catalog/systems?limit=200", KartovaApiFixtureBase.WireJson);
        Assert.IsFalse(pageB!.Items.Any(a => a.DisplayName == uniqueName));
    }

    [TestMethod]
    public async Task List_filters_by_displayNameContains()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "System Name Filter Team");
        var unique = Guid.NewGuid().ToString("N");
        await Seed(client, teamId, $"filt-{unique}-orders");
        await Seed(client, teamId, $"filt-{unique}-payments");

        var resp = await client.GetAsync($"/api/v1/catalog/systems?displayNameContains={unique}-ORDERS&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<SystemResponse>>(KartovaApiFixtureBase.WireJson);
        var names = page!.Items.Select(i => i.DisplayName).Where(n => n.StartsWith($"filt-{unique}", StringComparison.Ordinal)).ToList();
        CollectionAssert.AreEqual(new[] { $"filt-{unique}-orders" }, names, "case-insensitive substring must match only the one row");
    }

    [TestMethod]
    public async Task List_filters_by_teamId()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamA = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "System TeamId Filter A");
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "System TeamId Filter B");
        var unique = Guid.NewGuid().ToString("N");
        await Seed(client, teamA, $"tidfilt-{unique}-a");
        await Seed(client, teamB, $"tidfilt-{unique}-b");

        var resp = await client.GetAsync($"/api/v1/catalog/systems?teamId={teamB}&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<SystemResponse>>(KartovaApiFixtureBase.WireJson);
        var seeded = page!.Items.Select(i => i.DisplayName)
            .Where(n => n.StartsWith($"tidfilt-{unique}", StringComparison.Ordinal)).ToList();
        CollectionAssert.AreEqual(new[] { $"tidfilt-{unique}-b" }, seeded, "teamId filter must return only TeamB's system");
    }

    [TestMethod]
    public async Task List_filter_is_tenant_isolated()
    {
        var clientA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamA = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "System Filter Iso Team");
        var uniqueName = $"iso-filt-sys-{Guid.NewGuid():N}";
        await Seed(clientA, teamA, uniqueName);

        var clientB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await clientB.GetAsync($"/api/v1/catalog/systems?displayNameContains={uniqueName}&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var pageB = await resp.Content.ReadFromJsonAsync<CursorPage<SystemResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsFalse(pageB!.Items.Any(a => a.DisplayName == uniqueName), "RLS must isolate OrgA's system even with active filters");
    }

    [TestMethod]
    public async Task List_displayNameContains_treats_wildcards_literally()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "System Wildcard Escape Team");
        var unique = Guid.NewGuid().ToString("N");
        var literalName = $"wc-{unique}-a_b";
        var lookalikeName = $"wc-{unique}-axb";
        await Seed(client, teamId, literalName);
        await Seed(client, teamId, lookalikeName);

        var resp = await client.GetAsync($"/api/v1/catalog/systems?displayNameContains={literalName}&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<SystemResponse>>(KartovaApiFixtureBase.WireJson);
        var seeded = page!.Items.Select(i => i.DisplayName)
            .Where(n => n.StartsWith($"wc-{unique}", StringComparison.Ordinal)).ToList();
        CollectionAssert.AreEqual(
            new[] { literalName }, seeded, "'_' in displayNameContains must be escaped as a literal, not a LIKE single-char wildcard");
    }
}
