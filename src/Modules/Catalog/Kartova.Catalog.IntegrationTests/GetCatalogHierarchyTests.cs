using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public sealed class GetCatalogHierarchyTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

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

    private static async Task<Guid> SeedSystemAsync(HttpClient client, Guid stewardTeamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems",
            new { displayName = name, description = (string?)null, teamId = stewardTeamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedSystem '{name}': {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson))!.Id;
    }

    private static async Task AssignSystemAsync(HttpClient client, string componentKind, Guid componentId, Guid systemId)
    {
        var resp = await client.PutAsJsonAsync(
            $"/api/v1/catalog/{componentKind}/{componentId}/system", new { systemId });
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"Assign {componentKind} {componentId}: {resp.StatusCode}");
    }

    [TestMethod]
    public async Task Hierarchy_places_cross_team_member_under_steward_and_counts_roll_up()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var teamA = await Fx.SeedTeamInOrganizationAsync(tenant, "Hier-A-" + Guid.NewGuid());
        var teamB = await Fx.SeedTeamInOrganizationAsync(tenant, "Hier-B-" + Guid.NewGuid());

        // System stewarded by A; member service owned by B → must appear under A, not B.
        var sys = await SeedSystemAsync(client, teamA, "Sys-" + Guid.NewGuid());
        var memberOwnedByB = await SeedServiceAsync(client, teamB, "member-b");
        await AssignSystemAsync(client, "services", memberOwnedByB, sys);

        // Ungrouped app owned by B (no membership).
        var freeAppB = await SeedApplicationAsync(client, teamB, "free-b");

        var resp = await client.GetAsync("/api/v1/catalog/hierarchy");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var tree = (await resp.Content.ReadFromJsonAsync<CatalogHierarchyResponse>(KartovaApiFixtureBase.WireJson))!;

        var aNode = tree.Teams.Single(t => t.TeamId == teamA);
        var sysNode = aNode.Systems.Single(s => s.SystemId == sys);
        Assert.AreEqual(memberOwnedByB, sysNode.Members.Single().Id);   // cross-team member under steward A
        Assert.AreEqual("service", sysNode.Members.Single().Kind);

        var bNode = tree.Teams.Single(t => t.TeamId == teamB);
        Assert.AreEqual(0, bNode.Systems.Count);                        // B stewards nothing
        Assert.AreEqual(freeAppB, bNode.Ungrouped.Members.Single().Id); // its own app is ungrouped under B
        Assert.AreEqual(1, aNode.ComponentCount);
        Assert.AreEqual(1, bNode.ComponentCount);
        Assert.IsFalse(tree.Truncated);
    }

    [TestMethod]
    public async Task Unauthenticated_request_is_401()
    {
        var client = Fx.CreateClient();   // no bearer token
        var resp = await client.GetAsync("/api/v1/catalog/hierarchy");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task Rls_isolates_other_tenants_systems_and_components()
    {
        // Seed a system+member in Org B.
        var bClient = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var bTenant = Fx.TenantIdForEmail(OrgBUser);
        var bTeam = await Fx.SeedTeamInOrganizationAsync(bTenant, "Hier-Bten-" + Guid.NewGuid());
        var bSys = await SeedSystemAsync(bClient, bTeam, "b-only-sys");
        var bSvc = await SeedServiceAsync(bClient, bTeam, "b-only-svc");
        await AssignSystemAsync(bClient, "services", bSvc, bSys);

        // Org A's hierarchy must not contain any Org B id.
        var aClient = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tree = (await (await aClient.GetAsync("/api/v1/catalog/hierarchy"))
            .Content.ReadFromJsonAsync<CatalogHierarchyResponse>(KartovaApiFixtureBase.WireJson))!;

        Assert.IsFalse(tree.Teams.Any(t => t.TeamId == bTeam));
        Assert.IsFalse(tree.Teams.SelectMany(t => t.Systems).Any(s => s.SystemId == bSys));
        Assert.IsFalse(tree.Teams
            .SelectMany(t => t.Systems.SelectMany(s => s.Members).Concat(t.Ungrouped.Members))
            .Any(m => m.Id == bSvc));
    }

    [TestMethod]
    public async Task Small_catalog_is_not_truncated()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var team = await Fx.SeedTeamInOrganizationAsync(tenant, "Hier-NT-" + Guid.NewGuid());
        for (var i = 0; i < 3; i++) await SeedServiceAsync(client, team, $"nt-{i}-{Guid.NewGuid()}");

        var tree = (await (await client.GetAsync("/api/v1/catalog/hierarchy"))
            .Content.ReadFromJsonAsync<CatalogHierarchyResponse>(KartovaApiFixtureBase.WireJson))!;

        Assert.IsFalse(tree.Truncated);
    }
}
