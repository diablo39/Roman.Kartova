using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>Real-seam integration tests for the reintroduced <c>PartOf</c> relationship edge
/// (<c>{Application, Service} → System</c>, E-03.F-03.S-01 Task 13). Mirrors
/// <see cref="CreateRelationshipTests"/>'s seed helpers and negative-case shapes, plus
/// option-A visibility assertions (the edge must surface via
/// <c>GET /relationships</c> and <c>GET /catalog/graph</c> without special-casing).</summary>
[TestClass]
public sealed class CreatePartOfRelationshipTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static Task<HttpResponseMessage> PostRelAsync(
        HttpClient client, EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid)
        => client.PostAsJsonAsync(
            "/api/v1/catalog/relationships",
            new { sourceKind = sk, sourceId = sid, type = t, targetKind = tk, targetId = tid },
            KartovaApiFixtureBase.WireJson);

    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        { displayName = name, description = "x", teamId, endpoints = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static async Task<Guid> SeedApplicationAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/applications",
            new { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApplication '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static async Task<Guid> SeedApiAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", new
        {
            displayName = name, description = "x", style = ApiStyle.Rest, version = "v1",
            specUrl = (string?)null, teamId,
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApi '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static async Task<Guid> SeedSystemAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems", new
        { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedSystem '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    [TestMethod]
    public async Task POST_application_partOf_system_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "PartOf Team App 201");
        var appId = await SeedApplicationAsync(client, teamId, "app-partof-201");
        var sysId = await SeedSystemAsync(client, teamId, "system-partof-201");

        var resp = await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.PartOf, EntityKind.System, sysId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(RelationshipType.PartOf, body!.Type);
        Assert.AreEqual(appId, body.Source.Id);
        Assert.AreEqual(sysId, body.Target.Id);
        Assert.AreEqual("system-partof-201", body.Target.DisplayName);
    }

    [TestMethod]
    public async Task POST_service_partOf_system_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "PartOf Team Svc 201");
        var svcId = await SeedServiceAsync(client, teamId, "svc-partof-201");
        var sysId = await SeedSystemAsync(client, teamId, "system-svc-partof-201");

        var resp = await PostRelAsync(client, EntityKind.Service, svcId, RelationshipType.PartOf, EntityKind.System, sysId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(RelationshipType.PartOf, body!.Type);
    }

    // NOTE on status codes: the plan draft named these two disallowed-pair cases 422, but the
    // established convention in this codebase (RelationshipTypeRules.IsAllowedPair returning
    // false for entities that DO resolve) is 400 via DomainValidationExceptionHandler — see
    // POST_disallowed_pair_providesApiFor_api_to_application_returns_400 and
    // POST_instanceOf_application_to_service_returns_400 in CreateRelationshipTests. 422 is
    // reserved for entities that fail to resolve at all (see
    // POST_partOf_unknown_target_system_returns_422 below). Mirrored here for consistency.
    [TestMethod]
    public async Task POST_api_partOf_system_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "PartOf Team Api Bad Pair");
        var apiId = await SeedApiAsync(client, teamId, "api-partof-badpair");
        var sysId = await SeedSystemAsync(client, teamId, "system-partof-badpair-api");

        var resp = await PostRelAsync(client, EntityKind.Api, apiId, RelationshipType.PartOf, EntityKind.System, sysId);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_system_partOf_system_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "PartOf Team Nested Systems");
        var sys1 = await SeedSystemAsync(client, teamId, "system-nested-1");
        var sys2 = await SeedSystemAsync(client, teamId, "system-nested-2");

        var resp = await PostRelAsync(client, EntityKind.System, sys1, RelationshipType.PartOf, EntityKind.System, sys2);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_partOf_unknown_target_system_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "PartOf Team Unknown Sys");
        var appId = await SeedApplicationAsync(client, teamId, "app-partof-unknown-sys");

        var resp = await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.PartOf, EntityKind.System, Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_partOf_unknown_source_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "PartOf Team Unknown Source");
        var sysId = await SeedSystemAsync(client, teamId, "system-partof-unknown-source");

        var resp = await PostRelAsync(client, EntityKind.Application, Guid.NewGuid(), RelationshipType.PartOf, EntityKind.System, sysId);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_partOf_by_member_in_neither_team_returns_403()
    {
        // ADR-0108: a Member who belongs to NEITHER the component's team NOR the System's
        // steward team is rejected, mirroring CreateRelationshipTests.POST_by_member_in_neither_team_returns_403.
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var appTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "PartOf Neither App Team");
        var sysTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "PartOf Neither Sys Team");
        var appId = await SeedApplicationAsync(admin, appTeam, "app-partof-neither");
        var sysId = await SeedSystemAsync(admin, sysTeam, "system-partof-neither");

        var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
        var resp = await PostRelAsync(member, EntityKind.Application, appId, RelationshipType.PartOf, EntityKind.System, sysId);

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_duplicate_partOf_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "PartOf Team Dup");
        var appId = await SeedApplicationAsync(client, teamId, "app-partof-dup");
        var sysId = await SeedSystemAsync(client, teamId, "system-partof-dup");

        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.PartOf, EntityKind.System, sysId)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.PartOf, EntityKind.System, sysId)).StatusCode);
    }

    // --- Option-A visibility: the PartOf edge must surface, unfiltered, on the generic
    // relationship-list and graph read surfaces (no PartOf-specific carve-out anywhere). ---

    [TestMethod]
    public async Task PartOf_edge_appears_in_relationships_list_for_the_application()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "PartOf Visibility List Team");
        var appId = await SeedApplicationAsync(client, teamId, "app-partof-vis-list");
        var sysId = await SeedSystemAsync(client, teamId, "system-partof-vis-list");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.PartOf, EntityKind.System, sysId)).StatusCode);

        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Application&entityId={appId}&direction=outgoing");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);

        var partOfEdge = page!.Items.SingleOrDefault(i => i.Type == RelationshipType.PartOf);
        Assert.IsNotNull(partOfEdge, "the PartOf edge must appear, unfiltered, on the generic relationships list");
        Assert.AreEqual(sysId, partOfEdge!.Target.Id);
    }

    [TestMethod]
    public async Task PartOf_edge_appears_in_catalog_graph_without_500()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "PartOf Visibility Graph Team");
        var appId = await SeedApplicationAsync(client, teamId, "app-partof-vis-graph");
        var sysId = await SeedSystemAsync(client, teamId, "system-partof-vis-graph");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.PartOf, EntityKind.System, sysId)).StatusCode);

        var resp = await client.GetAsync(
            $"/api/v1/catalog/graph?entityKind=Application&entityId={appId}&depth=1&direction=all");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, "a PartOf edge must not 500 the graph surface");
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        Assert.IsTrue(graph!.Nodes.Any(n => n.Id == sysId && n.Kind == EntityKind.System),
            "the System node must be discovered and enriched via the graph traversal");
        var partOfEdge = graph.Edges.SingleOrDefault(e => e.Type == RelationshipType.PartOf);
        Assert.IsNotNull(partOfEdge, "the PartOf edge must appear, unfiltered, in the graph's edge set");
        Assert.AreEqual(appId, partOfEdge!.Source.Id);
        Assert.AreEqual(sysId, partOfEdge.Target.Id);
    }
}
