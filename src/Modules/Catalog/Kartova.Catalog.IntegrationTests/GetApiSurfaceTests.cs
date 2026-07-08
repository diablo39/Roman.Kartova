using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public sealed class GetApiSurfaceTests : CatalogIntegrationTestBase
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

    private sealed record ServiceSurfaceContext(
        HttpClient Client, Guid ServiceId, Guid AppId, string AppDisplayName,
        Guid ApiSvcId, Guid ApiApp1Id, Guid ApiApp2Id, Guid ApiConsId);

    private static async Task<ServiceSurfaceContext> SeedServiceSurfaceScenarioAsync()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Surface Team " + Guid.NewGuid());

        const string appName = "app-surface-1";
        var appId = await SeedApplicationAsync(client, teamId, appName);
        var svcId = await SeedServiceAsync(client, teamId, "svc-surface-1");

        var apiApp1 = await SeedApiAsync(client, teamId, "api-app1-surface");
        var apiApp2 = await SeedApiAsync(client, teamId, "api-app2-surface");
        var apiSvc = await SeedApiAsync(client, teamId, "api-svc-direct-surface");
        var apiCons = await SeedApiAsync(client, teamId, "api-cons-surface");

        // App provides ApiApp1 and ApiApp2.
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiApp1)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiApp2)).StatusCode);

        // Service is instance-of App (derived exposes ApiApp1/ApiApp2).
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svcId, RelationshipType.InstanceOf, EntityKind.Application, appId)).StatusCode);

        // Service directly provides ApiSvc.
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svcId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiSvc)).StatusCode);

        // Service consumes ApiCons.
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svcId, RelationshipType.ConsumesApiFrom, EntityKind.Api, apiCons)).StatusCode);

        return new ServiceSurfaceContext(client, svcId, appId, appName, apiSvc, apiApp1, apiApp2, apiCons);
    }

    [TestMethod]
    public async Task Service_surface_includes_direct_derived_and_consumes()
    {
        var ctx = await SeedServiceSurfaceScenarioAsync();

        var resp = await ctx.Client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=service&entityId={ctx.ServiceId}");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiSurfaceResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(body);

        // Provides = 1 direct (ApiSvc) + 2 derived (ApiApp1, ApiApp2)
        Assert.AreEqual(3, body!.Provides.Count);
        var direct = body.Provides.Single(i => i.ApiId == ctx.ApiSvcId);
        Assert.AreEqual(ApiSurfaceOrigin.Direct, direct.Origin);
        var derived = body.Provides.Single(i => i.ApiId == ctx.ApiApp1Id);
        Assert.AreEqual(ApiSurfaceOrigin.Derived, derived.Origin);
        Assert.AreEqual(ctx.AppId, derived.ViaApplicationId);
        Assert.AreEqual(ctx.AppDisplayName, derived.ViaApplicationDisplayName);

        // metadata joined
        Assert.AreEqual("v1", direct.Version);
        Assert.IsFalse(direct.HasSpec);

        // Consumes = 1 direct
        Assert.AreEqual(1, body.Consumes.Count);
        Assert.AreEqual(ctx.ApiConsId, body.Consumes.Single().ApiId);
        Assert.AreEqual(ApiSurfaceOrigin.Direct, body.Consumes.Single().Origin);
    }

    [TestMethod]
    public async Task Direct_provides_wins_over_derived_for_same_api()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Surface Wins Team " + Guid.NewGuid());

        var appId = await SeedApplicationAsync(client, teamId, "app-directwins");
        var svcId = await SeedServiceAsync(client, teamId, "svc-directwins");
        var apiX = await SeedApiAsync(client, teamId, "api-x-directwins");

        // App provides ApiX; Service is instance-of App (derived ApiX)...
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiX)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svcId, RelationshipType.InstanceOf, EntityKind.Application, appId)).StatusCode);
        // ...AND Service also directly provides ApiX.
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svcId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiX)).StatusCode);

        var body = await (await client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=service&entityId={svcId}"))
            .Content.ReadFromJsonAsync<ApiSurfaceResponse>(KartovaApiFixtureBase.WireJson);

        var rows = body!.Provides.Where(i => i.ApiId == apiX).ToList();
        Assert.AreEqual(1, rows.Count);                       // deduped
        Assert.AreEqual(ApiSurfaceOrigin.Direct, rows[0].Origin);
    }

    [TestMethod]
    public async Task Application_surface_has_no_derived_rows()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Surface App Team " + Guid.NewGuid());

        var appId = await SeedApplicationAsync(client, teamId, "app-surface-only");
        var apiProvided = await SeedApiAsync(client, teamId, "api-app-provided");
        var apiConsumed = await SeedApiAsync(client, teamId, "api-app-consumed");

        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiProvided)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ConsumesApiFrom, EntityKind.Api, apiConsumed)).StatusCode);

        var body = await (await client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=application&entityId={appId}"))
            .Content.ReadFromJsonAsync<ApiSurfaceResponse>(KartovaApiFixtureBase.WireJson);

        Assert.IsTrue(body!.Provides.All(i => i.Origin == ApiSurfaceOrigin.Direct));
        Assert.IsFalse(body.Provides.Any(i => i.ViaApplicationId is not null));
    }

    [TestMethod]
    public async Task Other_tenant_edges_and_apis_do_not_appear()
    {
        // Seed the scenario in tenant A; issue the request as a user in tenant B against A's service id.
        var ctx = await SeedServiceSurfaceScenarioAsync();
        var otherClient = await Fx.CreateAuthenticatedClientAsync(OrgBUser);

        var resp = await otherClient.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=service&entityId={ctx.ServiceId}");

        // The service isn't visible under B's RLS -> focus-entity lookup misses -> 422.
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task Unknown_entity_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=service&entityId={Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task EntityKind_api_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=api&entityId={Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Missing_entityId_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=service&entityId={Guid.Empty}");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
