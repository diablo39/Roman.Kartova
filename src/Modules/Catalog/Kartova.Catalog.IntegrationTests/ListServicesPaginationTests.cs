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
}
