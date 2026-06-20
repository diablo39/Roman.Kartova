using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class ListServicesPaginationTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static async Task SeedAsync(HttpClient client, Guid teamId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
            {
                displayName = $"list-svc-{i:D2}", description = "d", teamId,
                endpoints = Array.Empty<object>(),
            });
            Assert.IsTrue(resp.IsSuccessStatusCode);
        }
    }

    [TestMethod]
    public async Task GET_returns_cursor_page_envelope_and_paginates_forward()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "List Team");
        await SeedAsync(client, teamId, 3);

        var firstResp = await client.GetAsync("/api/v1/catalog/services?sortBy=displayName&sortOrder=asc&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, firstResp.StatusCode);
        var first = await firstResp.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(2, first!.Items.Count);
        Assert.IsNotNull(first.NextCursor);

        var nextResp = await client.GetAsync(
            $"/api/v1/catalog/services?sortBy=displayName&sortOrder=asc&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, nextResp.StatusCode);
        var next = await nextResp.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(next!.Items.Count >= 1);
    }

    [TestMethod]
    public async Task GET_with_invalid_sortBy_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync("/api/v1/catalog/services?sortBy=bogusField");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // F11 (mutation 390/391): sortBy=displayName&sortOrder=asc is actually honored
    [TestMethod]
    public async Task GET_with_sortBy_displayName_asc_returns_items_in_ascending_order()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Sort Team");
        // Seed names in reverse alphabetical order so a creation-order sort would differ from name-order.
        foreach (var name in new[] { "sort-z-svc", "sort-a-svc", "sort-m-svc" })
        {
            var r = await client.PostAsJsonAsync("/api/v1/catalog/services", new
            {
                displayName = name,
                description = "sort test",
                teamId,
                endpoints = Array.Empty<object>(),
            });
            Assert.IsTrue(r.IsSuccessStatusCode);
        }

        var resp = await client.GetAsync("/api/v1/catalog/services?sortBy=displayName&sortOrder=asc&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
        var names = page!.Items.Select(i => i.DisplayName).ToList();
        // The seeded trio must appear in ascending order within the overall result.
        var sortIdx = names.Select((n, i) => (n, i))
            .Where(x => x.n is "sort-a-svc" or "sort-m-svc" or "sort-z-svc")
            .OrderBy(x => x.i)
            .Select(x => x.n)
            .ToList();
        CollectionAssert.AreEqual(
            new[] { "sort-a-svc", "sort-m-svc", "sort-z-svc" },
            sortIdx,
            $"Expected ascending displayName order; got [{string.Join(", ", sortIdx)}].");
    }

    // F12 (gate8 Imp-4): OrgB cannot see OrgA's services in the list endpoint
    [TestMethod]
    public async Task GET_list_does_not_expose_other_tenants_services()
    {
        const string OrgBUser = "admin@orgb.kartova.local";
        var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Isolation Team");
        var registerResp = await orgA.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = "orga-isolation-svc",
            description = "must not be visible to orgb.",
            teamId,
            endpoints = Array.Empty<object>(),
        });
        Assert.IsTrue(registerResp.IsSuccessStatusCode);
        var orgAService = (await registerResp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson))!;

        var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var listResp = await orgB.GetAsync("/api/v1/catalog/services?limit=200");
        Assert.AreEqual(HttpStatusCode.OK, listResp.StatusCode);
        var page = await listResp.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
        var orgBIds = page!.Items.Select(i => i.Id).ToHashSet();
        Assert.IsFalse(orgBIds.Contains(orgAService.Id),
            $"OrgA service {orgAService.Id} must not appear in OrgB's service list.");
    }
}
