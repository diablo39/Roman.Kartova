using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class ListRelationshipsTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = name, description = "x", teamId, endpoints = Array.Empty<object>(),
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static object Rel(EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid) =>
        new { sourceKind = sk, sourceId = sid, type = t, targetKind = tk, targetId = tid };

    [TestMethod]
    public async Task GET_incoming_returns_consumers_of_an_entity()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team");
        var a = await SeedServiceAsync(client, teamId, "list-svc-a");
        var b = await SeedServiceAsync(client, teamId, "list-svc-b");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));

        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={b}&direction=incoming");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(1, page!.Items.Count);
        Assert.AreEqual(a, page.Items[0].Source.Id);
    }

    [TestMethod]
    public async Task GET_outgoing_lists_only_edges_sourced_at_entity()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team Out");
        var a = await SeedServiceAsync(client, teamId, "list-svc-a2");
        var b = await SeedServiceAsync(client, teamId, "list-svc-b2");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));

        var outA = await (await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={a}&direction=outgoing"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        var outB = await (await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={b}&direction=outgoing"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);

        Assert.AreEqual(1, outA!.Items.Count);
        Assert.AreEqual(0, outB!.Items.Count);
    }

    [TestMethod]
    public async Task GET_all_direction_returns_both_incoming_and_outgoing()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team All");
        var a = await SeedServiceAsync(client, teamId, "list-svc-all-a");
        var b = await SeedServiceAsync(client, teamId, "list-svc-all-b");
        var c = await SeedServiceAsync(client, teamId, "list-svc-all-c");
        // a → b (b's outgoing, a's incoming target)
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));
        // b → c (b's outgoing)
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, b, RelationshipType.DependsOn, EntityKind.Service, c));

        var allB = await (await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={b}&direction=all"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);

        // b appears as target (a→b) and as source (b→c): 2 edges
        Assert.AreEqual(2, allB!.Items.Count);
    }

    [TestMethod]
    public async Task GET_is_tenant_isolated()
    {
        var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "B Team Iso");
        var b1 = await SeedServiceAsync(orgB, teamB, "b1-iso");
        var b2 = await SeedServiceAsync(orgB, teamB, "b2-iso");
        await orgB.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, b1, RelationshipType.DependsOn, EntityKind.Service, b2));

        var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var page = await (await orgA.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={b1}&direction=all"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(0, page!.Items.Count);
    }

    [TestMethod]
    public async Task GET_paginates_forward_and_sortBy_type_is_honoured()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Pag Team");
        var hub = await SeedServiceAsync(client, teamId, "list-rel-hub");
        var s1 = await SeedServiceAsync(client, teamId, "list-rel-s1");
        var s2 = await SeedServiceAsync(client, teamId, "list-rel-s2");
        var s3 = await SeedServiceAsync(client, teamId, "list-rel-s3");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, hub, RelationshipType.DependsOn, EntityKind.Service, s1));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, hub, RelationshipType.DependsOn, EntityKind.Service, s2));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, hub, RelationshipType.DependsOn, EntityKind.Service, s3));

        // Page 1: limit=2, sortBy=type
        var firstResp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={hub}&direction=outgoing&sortBy=type&sortOrder=asc&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, firstResp.StatusCode);
        var first = await firstResp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(2, first!.Items.Count);
        Assert.IsNotNull(first.NextCursor);

        // Page 2: follow cursor
        var nextResp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={hub}&direction=outgoing&sortBy=type&sortOrder=asc&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, nextResp.StatusCode);
        var next = await nextResp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(next!.Items.Count >= 1);
    }

    [TestMethod]
    public async Task GET_without_token_returns_401()
    {
        using var client = Fx.CreateAnonymousClient();
        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={Guid.NewGuid()}&direction=all");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_with_invalid_entityKind_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Bogus&entityId={Guid.NewGuid()}&direction=all");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_with_invalid_direction_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={Guid.NewGuid()}&direction=bogus");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_with_invalid_sortBy_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={Guid.NewGuid()}&sortBy=bogusField");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_with_limit_over_max_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={Guid.NewGuid()}&direction=all&limit=201");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_default_sort_is_createdAt_desc()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Default Sort Team");
        var src = await SeedServiceAsync(client, teamId, "list-ds-src");
        var t1 = await SeedServiceAsync(client, teamId, "list-ds-t1");
        var t2 = await SeedServiceAsync(client, teamId, "list-ds-t2");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, src, RelationshipType.DependsOn, EntityKind.Service, t1));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, src, RelationshipType.DependsOn, EntityKind.Service, t2));

        var resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={src}&direction=outgoing");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(2, page!.Items.Count);
        // newest first: second seeded edge's createdAt >= first's
        Assert.IsTrue(page.Items[0].CreatedAt >= page.Items[1].CreatedAt);
    }
}
