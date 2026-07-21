using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public sealed class GetImpactAnalysisTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private static Task<HttpResponseMessage> PostRelAsync(
        HttpClient client, EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid)
        => client.PostAsJsonAsync(
            "/api/v1/catalog/relationships",
            new { sourceKind = sk, sourceId = sid, type = t, targetKind = tk, targetId = tid },
            KartovaApiFixtureBase.WireJson);

    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services",
            new { displayName = name, description = "x", teamId, endpoints = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}': {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson))!.Id;
    }

    private static async Task<Guid> SeedApplicationAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/applications",
            new { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApp '{name}': {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!.Id;
    }

    private static async Task<Guid> SeedApiAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", new
        { displayName = name, description = "x", style = ApiStyle.Rest, version = "v1", specUrl = (string?)null, teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApi '{name}': {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson))!.Id;
    }

    private static Task<HttpResponseMessage> GetImpactAsync(HttpClient client, string kind, Guid id)
        => client.GetAsync($"/api/v1/catalog/impact?entityKind={kind}&entityId={id}");

    [TestMethod]
    public async Task Multi_tier_blast_radius_includes_explicit_and_derived()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Impact " + Guid.NewGuid());

        // Explicit: A depends-on F (tier 1); B depends-on A (tier 2).
        var f = await SeedServiceAsync(client, teamId, "impact-focus");
        var a = await SeedServiceAsync(client, teamId, "impact-a");
        var b = await SeedServiceAsync(client, teamId, "impact-b");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, f)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, b, RelationshipType.DependsOn, EntityKind.Service, a)).StatusCode);

        // Derived: C consumes an API that F provides ⇒ C derives depends-on F (tier 1).
        var c = await SeedServiceAsync(client, teamId, "impact-c");
        var api = await SeedApiAsync(client, teamId, "impact-api");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, f, RelationshipType.ProvidesApiFor, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, c, RelationshipType.ConsumesApiFrom, EntityKind.Api, api)).StatusCode);

        var resp = await GetImpactAsync(client, "service", f);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(body);

        // Nodes: focus (depth 0) + A,C (depth 1) + B (depth 2). API node is NOT a dependent (not depends-on).
        int Depth(Guid id) => body!.Nodes.Single(n => n.Id == id).Depth;
        Assert.AreEqual(0, Depth(f));
        Assert.AreEqual(1, Depth(a));
        Assert.AreEqual(1, Depth(c));
        Assert.AreEqual(2, Depth(b));
        Assert.IsFalse(body!.Nodes.Any(n => n.Id == api), "API node is not a depends-on dependent");
        Assert.IsFalse(body.Truncated);
        // A derived edge (C→F) is present in DerivedEdges; explicit A→F, B→A in Edges.
        Assert.IsTrue(body.DerivedEdges.Any(e => e.Source.Id == c && e.Target.Id == f));
        Assert.IsTrue(body.Edges.Any(e => e.Source.Id == a && e.Target.Id == f));
    }

    [TestMethod]
    public async Task Application_focus_is_supported()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Impact App " + Guid.NewGuid());
        var app = await SeedApplicationAsync(client, teamId, "impact-app-focus");
        var svc = await SeedServiceAsync(client, teamId, "impact-app-dependent");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svc, RelationshipType.DependsOn, EntityKind.Application, app)).StatusCode);

        var resp = await GetImpactAsync(client, "application", app);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(1, body!.Nodes.Single(n => n.Id == svc).Depth);
    }

    [TestMethod]
    public async Task Api_focus_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Impact Api " + Guid.NewGuid());
        var api = await SeedApiAsync(client, teamId, "impact-api-focus");
        var resp = await GetImpactAsync(client, "api", api);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task System_focus_returns_400()
    {
        // A System has no impact analysis this slice (grouping-only entity) — same structural
        // 400 branch as entityKind=api, rather than falling through to a meaningless empty 200.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await GetImpactAsync(client, "system", Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Unknown_entity_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await GetImpactAsync(client, "service", Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task Empty_entityId_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await GetImpactAsync(client, "service", Guid.Empty);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Other_tenant_entity_is_not_visible_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Impact XT " + Guid.NewGuid());
        var f = await SeedServiceAsync(client, teamId, "impact-xt-focus");

        var otherClient = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await GetImpactAsync(otherClient, "service", f);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }
}
