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

    private static async Task<Guid> SeedApiAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", new
        {
            displayName = name,
            description = "x",
            style = ApiStyle.Rest,
            version = "v1",
            specUrl = (string?)null,
            teamId,
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApi '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
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

    private static async Task<Guid> SeedApplicationAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/applications",
            new { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApplication '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static Task<HttpResponseMessage> PostRelAsync(
        HttpClient client, EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid)
        => client.PostAsJsonAsync(
            "/api/v1/catalog/relationships",
            new { sourceKind = sk, sourceId = sid, type = t, targetKind = tk, targetId = tid },
            KartovaApiFixtureBase.WireJson);

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

    [TestMethod]
    public async Task GET_graph_includes_cross_edges_between_kept_nodes()
    {
        // Arrange: F→A, F→B, B→A. Query outgoing depth=1 from F.
        // Node discovery: outgoing direction means only outgoing neighbours are added to the frontier.
        // Depth-1 outgoing from F: discovers A (via F→A) and B (via F→B). B→A is NOT traversed for discovery.
        // Edge inclusion: the final re-scan is undirected-among-kept-nodes — ALL edges between {F,A,B} appear.
        // Contract: 3 nodes, 3 edges (F→A, F→B, B→A). Pins: direction prunes discovery; edge inclusion is undirected.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph CrossEdge");
        var f = await SeedServiceAsync(client, teamId, "cross-focus");
        var a = await SeedServiceAsync(client, teamId, "cross-a");
        var b = await SeedServiceAsync(client, teamId, "cross-b");
        await DependsOnAsync(client, f, a);   // F → A
        await DependsOnAsync(client, f, b);   // F → B
        await DependsOnAsync(client, b, a);   // B → A  (cross-edge between two kept nodes)

        var resp = await client.GetAsync(
            $"/api/v1/catalog/graph?entityKind=Service&entityId={f}&depth=1&direction=outgoing");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        // All three nodes must be present (F at depth 0, A and B at depth 1).
        Assert.AreEqual(3, graph!.Nodes.Count, "focus + 2 outgoing neighbours");
        Assert.IsTrue(graph.Nodes.Any(n => n.Id == f), "focus node present");
        Assert.IsTrue(graph.Nodes.Any(n => n.Id == a), "node A present");
        Assert.IsTrue(graph.Nodes.Any(n => n.Id == b), "node B present");

        // The B→A cross-edge must be included (undirected-among-kept contract).
        Assert.AreEqual(3, graph.Edges.Count, "F→A + F→B + B→A cross-edge all included");
        Assert.IsTrue(
            graph.Edges.Any(e => e.Source.Id == b && e.Target.Id == a),
            "B→A cross-edge is present even though B→A was not traversed during discovery");
    }

    [TestMethod]
    public async Task GET_graph_focused_on_api_returns_provider_edge_and_enriched_api_node()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Api Team");
        var apiId = await SeedApiAsync(client, teamId, "graph-api");
        var providerSvc = await SeedServiceAsync(client, teamId, "graph-provider-svc");

        var rel = await client.PostAsJsonAsync("/api/v1/catalog/relationships", new
        {
            sourceKind = EntityKind.Service, sourceId = providerSvc,
            type = RelationshipType.ProvidesApiFor,
            targetKind = EntityKind.Api, targetId = apiId,
        });
        Assert.AreEqual(HttpStatusCode.Created, rel.StatusCode, $"seed provider edge: {rel.StatusCode}");

        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Api&entityId={apiId}&depth=1&direction=all");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        var apiNode = graph!.Nodes.Single(n => n.Id == apiId);
        Assert.AreEqual("graph-api", apiNode.DisplayName);
        Assert.AreEqual(teamId, apiNode.TeamId);
        Assert.IsTrue(graph.Nodes.Any(n => n.Id == providerSvc), "provider service node should be present at depth 1");
        Assert.AreEqual(1, graph.Edges.Count);
    }

    [TestMethod]
    public async Task derived_depends_on_appears_with_provenance_and_drives_discovery()
    {
        // Topology: svcS --consumes--> api <--provides-- app --instance-of-- svcT
        // svcS has no direct edge to svcT; the derived depends-on edge is what must drive discovery.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Derived Team");
        var svcS = await SeedServiceAsync(client, teamId, "derived-consumer");
        var svcT = await SeedServiceAsync(client, teamId, "derived-provider-instance");
        var app = await SeedApplicationAsync(client, teamId, "Provider App");
        var api = await SeedApiAsync(client, teamId, "Orders API");

        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svcT, RelationshipType.InstanceOf, EntityKind.Application, app)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, app, RelationshipType.ProvidesApiFor, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svcS, RelationshipType.ConsumesApiFrom, EntityKind.Api, api)).StatusCode);

        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={svcS}&depth=2&direction=all");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        Assert.IsTrue(graph!.Nodes.Any(n => n.Id == svcT), "derived edge must drive discovery of svcT");
        var derived = graph.DerivedEdges.Single(e => e.Source.Id == svcS && e.Target.Id == svcT);
        var path = derived.Paths.Single();
        Assert.AreEqual(api, path.ApiId);
        Assert.AreEqual("Orders API", path.ApiName);
        Assert.AreEqual(app, path.ViaApplicationId);
        Assert.AreEqual("Provider App", path.ViaApplicationDisplayName);
    }

    [TestMethod]
    public async Task explicit_depends_on_suppresses_the_derived_duplicate()
    {
        // Same consume/provide topology PLUS an explicit depends-on svcS->svcT.
        // Expect: persisted edge in Edges, NO derived edge for (svcS, svcT).
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Suppress Team");
        var svcT = await SeedServiceAsync(client, teamId, "Prov Svc 2");
        var svcS = await SeedServiceAsync(client, teamId, "Cons Svc 2");
        var api = await SeedApiAsync(client, teamId, "Billing API");

        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svcT, RelationshipType.ProvidesApiFor, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svcS, RelationshipType.ConsumesApiFrom, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svcS, RelationshipType.DependsOn, EntityKind.Service, svcT)).StatusCode);

        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={svcS}&depth=2&direction=all");
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        Assert.IsFalse(graph!.DerivedEdges.Any(e => e.Source.Id == svcS && e.Target.Id == svcT),
            "explicit depends-on should suppress the derived duplicate");
        Assert.IsTrue(graph.Edges.Any(e =>
            e.Source.Id == svcS && e.Target.Id == svcT && e.Type == RelationshipType.DependsOn));
    }

    [TestMethod]
    public async Task derived_edges_are_tenant_isolated()
    {
        // Both tenants seed a COMPLETE derived topology (svcS --consumes--> api <--provides-- app --instance-of-- svcT),
        // so each tenant genuinely has its own derived depends-on edge. This proves tenant A's precompute
        // is scoped to tenant A's data, not merely that an incomplete topology trivially yields zero edges.

        // Tenant B: appB / svcTB instance-of appB / apiB provided by appB / svcSB consumes apiB.
        var clientB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "Graph Iso Derived B");
        var svcSB = await SeedServiceAsync(clientB, teamB, "iso-consumer-b");
        var svcTB = await SeedServiceAsync(clientB, teamB, "iso-provider-instance-b");
        var appB = await SeedApplicationAsync(clientB, teamB, "Iso App B");
        var apiB = await SeedApiAsync(clientB, teamB, "Iso API B");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(clientB, EntityKind.Service, svcTB, RelationshipType.InstanceOf, EntityKind.Application, appB)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(clientB, EntityKind.Application, appB, RelationshipType.ProvidesApiFor, EntityKind.Api, apiB)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(clientB, EntityKind.Service, svcSB, RelationshipType.ConsumesApiFrom, EntityKind.Api, apiB)).StatusCode);

        // Tenant A: appA / svcTA instance-of appA / apiA provided by appA / svcSA consumes apiA.
        var clientA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamA = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Iso Derived A");
        var svcSA = await SeedServiceAsync(clientA, teamA, "iso-consumer-a");
        var svcTA = await SeedServiceAsync(clientA, teamA, "iso-provider-instance-a");
        var appA = await SeedApplicationAsync(clientA, teamA, "Iso App A");
        var apiA = await SeedApiAsync(clientA, teamA, "Iso API A");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(clientA, EntityKind.Service, svcTA, RelationshipType.InstanceOf, EntityKind.Application, appA)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(clientA, EntityKind.Application, appA, RelationshipType.ProvidesApiFor, EntityKind.Api, apiA)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(clientA, EntityKind.Service, svcSA, RelationshipType.ConsumesApiFrom, EntityKind.Api, apiA)).StatusCode);

        var resp = await clientA.GetAsync($"/api/v1/catalog/graph?entityKind=service&entityId={svcSA}&depth=2&direction=all");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        Assert.AreEqual(1, graph!.DerivedEdges.Count, "tenant A must see exactly its own derived edge");
        var derived = graph.DerivedEdges.Single();
        Assert.AreEqual(svcSA, derived.Source.Id);
        Assert.AreEqual(svcTA, derived.Target.Id);

        var leakedIds = new[] { svcSB, svcTB, appB, apiB };
        Assert.IsFalse(graph.Nodes.Any(n => leakedIds.Contains(n.Id)),
            "no tenant-B node should leak into tenant A's graph nodes");
        Assert.IsFalse(graph.DerivedEdges.Any(e => leakedIds.Contains(e.Source.Id) || leakedIds.Contains(e.Target.Id)),
            "no tenant-B id should appear in tenant A's derived edges");
    }
}
