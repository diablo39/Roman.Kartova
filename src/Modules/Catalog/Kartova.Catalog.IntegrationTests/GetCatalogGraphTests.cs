using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class GetCatalogGraphTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = name, description = "x", teamId, endpoints = Array.Empty<object>(),
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}': {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static async Task DependsOnAsync(HttpClient client, Guid src, Guid tgt)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/relationships", new
        {
            sourceKind = EntityKind.Service, sourceId = src,
            type = RelationshipType.DependsOn,
            targetKind = EntityKind.Service, targetId = tgt,
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"DependsOn {src}->{tgt}: {resp.StatusCode}");
    }

    [TestMethod]
    public async Task GET_graph_returns_two_hop_neighbourhood_with_depths()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Team");
        var f = await SeedServiceAsync(client, teamId, "graph-focus");
        var a = await SeedServiceAsync(client, teamId, "graph-a");
        var b = await SeedServiceAsync(client, teamId, "graph-b");
        await DependsOnAsync(client, f, a);   // F -> A
        await DependsOnAsync(client, a, b);   // A -> B

        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={f}&depth=2&direction=all");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        Assert.AreEqual(3, graph!.Nodes.Count);
        Assert.AreEqual(0, graph.Nodes.Single(n => n.Id == f).Depth);
        Assert.AreEqual(1, graph.Nodes.Single(n => n.Id == a).Depth);
        Assert.AreEqual(2, graph.Nodes.Single(n => n.Id == b).Depth);
        Assert.AreEqual(teamId, graph.Nodes.Single(n => n.Id == a).TeamId);
        Assert.AreEqual("graph-a", graph.Nodes.Single(n => n.Id == a).DisplayName);
        Assert.AreEqual(2, graph.Edges.Count);
        Assert.IsFalse(graph.Truncated);
    }

    [TestMethod]
    public async Task GET_graph_depth_1_excludes_second_hop()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph D1");
        var f = await SeedServiceAsync(client, teamId, "d1-focus");
        var a = await SeedServiceAsync(client, teamId, "d1-a");
        var b = await SeedServiceAsync(client, teamId, "d1-b");
        await DependsOnAsync(client, f, a);
        await DependsOnAsync(client, a, b);

        var graph = await (await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={f}&depth=1&direction=all"))
            .Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(graph!.Nodes.Any(n => n.Id == a), "the depth-1 neighbour should be present");
        Assert.IsFalse(graph!.Nodes.Any(n => n.Id == b));
    }

    [TestMethod]
    public async Task GET_graph_outgoing_excludes_incoming_neighbour()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Dir");
        var f = await SeedServiceAsync(client, teamId, "dir-focus");
        var a = await SeedServiceAsync(client, teamId, "dir-out");
        var c = await SeedServiceAsync(client, teamId, "dir-in");
        await DependsOnAsync(client, f, a);   // outgoing from F
        await DependsOnAsync(client, c, f);   // incoming to F

        var graph = await (await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={f}&depth=1&direction=outgoing"))
            .Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(graph!.Nodes.Any(n => n.Id == a));
        Assert.IsFalse(graph.Nodes.Any(n => n.Id == c));
    }

    [TestMethod]
    public async Task GET_graph_handles_a_cycle()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Cycle");
        var f = await SeedServiceAsync(client, teamId, "cyc-f");
        var a = await SeedServiceAsync(client, teamId, "cyc-a");
        await DependsOnAsync(client, f, a);
        await DependsOnAsync(client, a, f);

        var graph = await (await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={f}&depth=3&direction=all"))
            .Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(2, graph!.Nodes.Count);
        Assert.AreEqual(2, graph.Edges.Count);
    }

    [TestMethod]
    public async Task GET_graph_is_tenant_isolated()
    {
        var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "B Graph Iso");
        var b1 = await SeedServiceAsync(orgB, teamB, "biso-1");
        var b2 = await SeedServiceAsync(orgB, teamB, "biso-2");
        await DependsOnAsync(orgB, b1, b2);

        var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var graph = await (await orgA.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={b1}&depth=2&direction=all"))
            .Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
        // b1 is invisible to org A → only the (empty) focus node, no neighbours/edges.
        Assert.AreEqual(0, graph!.Edges.Count);
        Assert.IsFalse(graph.Nodes.Any(n => n.DisplayName.Contains("biso")),
            "no org-B display name should leak to an org-A caller");
    }

    [TestMethod]
    public async Task GET_graph_without_token_returns_401()
    {
        using var client = Fx.CreateAnonymousClient();
        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={Guid.NewGuid()}&depth=2");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_graph_with_invalid_entityKind_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Bogus&entityId={Guid.NewGuid()}&depth=2");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_graph_with_depth_out_of_range_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={Guid.NewGuid()}&depth=9");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
