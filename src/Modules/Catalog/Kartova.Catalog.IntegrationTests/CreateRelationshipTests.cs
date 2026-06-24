using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class CreateRelationshipTests : CatalogIntegrationTestBase
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
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        { displayName = name, description = "x", teamId, endpoints = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    [TestMethod]
    public async Task POST_dependsOn_between_two_services_returns_201_and_manual_origin()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team 201");
        var a = await SeedServiceAsync(client, teamId, "svc-a-201");
        var b = await SeedServiceAsync(client, teamId, "svc-b-201");

        var resp = await PostRelAsync(client, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(RelationshipOrigin.Manual, body!.Origin);
        Assert.AreEqual(a, body.Source.Id);
        Assert.AreEqual("svc-b-201", body.Target.DisplayName);
    }

    [TestMethod]
    public async Task POST_self_reference_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team Self");
        var a = await SeedServiceAsync(client, teamId, "svc-self-400");
        var resp = await PostRelAsync(client, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, a);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_non_creatable_type_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team Type");
        var a = await SeedServiceAsync(client, teamId, "svc-x-400");
        var b = await SeedServiceAsync(client, teamId, "svc-y-400");
        var resp = await PostRelAsync(client, EntityKind.Service, a, RelationshipType.PublishesTo, EntityKind.Service, b);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_unknown_target_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team 422T");
        var a = await SeedServiceAsync(client, teamId, "svc-known-422");
        var resp = await PostRelAsync(client, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_duplicate_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team Dup");
        var a = await SeedServiceAsync(client, teamId, "svc-d1-409");
        var b = await SeedServiceAsync(client, teamId, "svc-d2-409");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await PostRelAsync(client, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b)).StatusCode);
    }

    [TestMethod]
    public async Task POST_without_token_returns_401()
    {
        using var client = Fx.CreateAnonymousClient();
        var resp = await PostRelAsync(client, EntityKind.Service, Guid.NewGuid(), RelationshipType.DependsOn, EntityKind.Service, Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_by_member_not_in_source_team_returns_403()
    {
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Restricted 403");
        var a = await SeedServiceAsync(admin, teamId, "svc-r1-403");
        var b = await SeedServiceAsync(admin, teamId, "svc-r2-403");
        var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
        var resp = await PostRelAsync(member, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b);
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_cross_tenant_target_returns_422()
    {
        var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "B Team XT");
        var bSvc = await SeedServiceAsync(orgB, teamB, "b-svc-xt");

        var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamA = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "A Team XT");
        var aSvc = await SeedServiceAsync(orgA, teamA, "a-svc-xt");

        var resp = await PostRelAsync(orgA, EntityKind.Service, aSvc, RelationshipType.DependsOn, EntityKind.Service, bSvc);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_sets_CreatedByUserId_to_caller_sub()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team Sub");
        var a = await SeedServiceAsync(client, teamId, "svc-c1-sub");
        var b = await SeedServiceAsync(client, teamId, "svc-c2-sub");
        var resp = await PostRelAsync(client, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b);
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(await Fx.GetSubClaimAsync(OrgAUser), body!.CreatedByUserId);
    }
}
