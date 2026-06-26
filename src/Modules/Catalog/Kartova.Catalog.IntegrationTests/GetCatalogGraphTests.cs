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

    private static Task DependsOnAsync(HttpClient client, Guid src, Guid tgt) =>
        client.PostAsJsonAsync("/api/v1/catalog/relationships", new
        {
            sourceKind = EntityKind.Service, sourceId = src,
            type = RelationshipType.DependsOn,
            targetKind = EntityKind.Service, targetId = tgt,
        });

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
}
