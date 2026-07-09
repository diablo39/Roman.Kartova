using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public sealed class GetDerivedDependenciesTests : CatalogIntegrationTestBase
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
            displayName = name, description = "x", style = ApiStyle.Rest, version = "v1",
            specUrl = (string?)null, teamId,
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApi '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static Task<DerivedDependenciesResponse?> GetAsync(HttpClient client, Guid entityId)
        => client.GetAsync($"/api/v1/catalog/derived-dependencies?entityId={entityId}")
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<DerivedDependenciesResponse>(KartovaApiFixtureBase.WireJson))
            .Unwrap();

    // Topology: consumer S --consumes--> Api1 ; provider T --instance-of--> App --provides--> Api1.
    // => derived S depends-on T, provenance {Api1 via App}.
    private sealed record ViaAppContext(HttpClient Client, Guid S, Guid T, Guid App, string AppName, Guid Api);

    private static async Task<ViaAppContext> SeedViaAppScenarioAsync()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Derived Team " + Guid.NewGuid());
        const string appName = "derived-provider-app";
        var app = await SeedApplicationAsync(client, teamId, appName);
        var t = await SeedServiceAsync(client, teamId, "derived-provider-svc");
        var s = await SeedServiceAsync(client, teamId, "derived-consumer-svc");
        var api = await SeedApiAsync(client, teamId, "derived-orders-api");

        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, t, RelationshipType.InstanceOf, EntityKind.Application, app)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, app, RelationshipType.ProvidesApiFor, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, s, RelationshipType.ConsumesApiFrom, EntityKind.Api, api)).StatusCode);

        return new ViaAppContext(client, s, t, app, appName, api);
    }

    [TestMethod]
    public async Task Dependencies_include_provider_with_via_app_provenance()
    {
        var ctx = await SeedViaAppScenarioAsync();

        var body = await GetAsync(ctx.Client, ctx.S);

        Assert.IsNotNull(body);
        Assert.AreEqual(0, body!.Dependents.Count);
        var dep = body.Dependencies.Single();
        Assert.AreEqual(ctx.T, dep.ServiceId);
        Assert.AreEqual("derived-provider-svc", dep.DisplayName);
        var path = dep.Paths.Single();
        Assert.AreEqual(ctx.Api, path.ApiId);
        Assert.AreEqual("derived-orders-api", path.ApiName);
        Assert.AreEqual(ctx.App, path.ViaApplicationId);
        Assert.AreEqual(ctx.AppName, path.ViaApplicationDisplayName);
    }

    [TestMethod]
    public async Task Dependents_are_the_reverse_direction()
    {
        var ctx = await SeedViaAppScenarioAsync();

        // Focus the PROVIDER T: S derives a depends-on ON T → S appears in Dependents.
        var body = await GetAsync(ctx.Client, ctx.T);

        Assert.IsNotNull(body);
        Assert.AreEqual(0, body!.Dependencies.Count);
        var dependent = body.Dependents.Single();
        Assert.AreEqual(ctx.S, dependent.ServiceId);
        Assert.AreEqual("derived-consumer-svc", dependent.DisplayName);
        Assert.AreEqual(ctx.Api, dependent.Paths.Single().ApiId);
    }

    [TestMethod]
    public async Task Direct_provide_yields_null_via_app()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Derived Direct " + Guid.NewGuid());
        var t = await SeedServiceAsync(client, teamId, "direct-provider");
        var s = await SeedServiceAsync(client, teamId, "direct-consumer");
        var api = await SeedApiAsync(client, teamId, "direct-api");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, t, RelationshipType.ProvidesApiFor, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, s, RelationshipType.ConsumesApiFrom, EntityKind.Api, api)).StatusCode);

        var body = await GetAsync(client, s);

        var path = body!.Dependencies.Single().Paths.Single();
        Assert.AreEqual(api, path.ApiId);
        Assert.IsNull(path.ViaApplicationId);
        Assert.IsNull(path.ViaApplicationDisplayName);
    }

    [TestMethod]
    public async Task Explicit_depends_on_suppresses_the_derived_pair()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Derived Explicit " + Guid.NewGuid());
        var t = await SeedServiceAsync(client, teamId, "explicit-provider");
        var s = await SeedServiceAsync(client, teamId, "explicit-consumer");
        var api = await SeedApiAsync(client, teamId, "explicit-api");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, t, RelationshipType.ProvidesApiFor, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, s, RelationshipType.ConsumesApiFrom, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, s, RelationshipType.DependsOn, EntityKind.Service, t)).StatusCode);

        var body = await GetAsync(client, s);

        Assert.AreEqual(0, body!.Dependencies.Count, "explicit depends-on must suppress the derived pair");
    }

    [TestMethod]
    public async Task Unknown_entity_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/derived-dependencies?entityId={Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task Missing_entityId_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/derived-dependencies?entityId={Guid.Empty}");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Other_tenant_service_is_not_visible_422()
    {
        // Seed the scenario in tenant A; request A's consumer id as a user in tenant B.
        var ctx = await SeedViaAppScenarioAsync();
        var otherClient = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await otherClient.GetAsync($"/api/v1/catalog/derived-dependencies?entityId={ctx.S}");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task Wrong_kind_id_application_returns_422()
    {
        // The endpoint is service-only: a real-but-non-service id resolves absent via
        // lookup.Find(EntityKind.Service, ...) and hits the same 422 branch as an unknown id.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Derived Wrong Kind " + Guid.NewGuid());
        var appId = await SeedApplicationAsync(client, teamId, "derived-wrong-kind-app");

        var resp = await client.GetAsync($"/api/v1/catalog/derived-dependencies?entityId={appId}");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task Multiple_neighbours_both_directions_carry_team_id()
    {
        // Focus F: two derived dependencies (F consumes Api1 from T1, Api2 from T2) and one derived
        // dependent (C consumes Api3 provided by F). T1/T2/C share a known team so we can assert the
        // team join resolved correctly and wasn't swapped between the two directions.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var focusTeamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Derived Multi Focus " + Guid.NewGuid());
        var neighbourTeamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Derived Multi Neighbours " + Guid.NewGuid());

        var f = await SeedServiceAsync(client, focusTeamId, "multi-focus");
        var t1 = await SeedServiceAsync(client, neighbourTeamId, "multi-provider-1");
        var t2 = await SeedServiceAsync(client, neighbourTeamId, "multi-provider-2");
        var c = await SeedServiceAsync(client, neighbourTeamId, "multi-consumer");
        var api1 = await SeedApiAsync(client, neighbourTeamId, "multi-api-1");
        var api2 = await SeedApiAsync(client, neighbourTeamId, "multi-api-2");
        var api3 = await SeedApiAsync(client, focusTeamId, "multi-api-3");

        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, t1, RelationshipType.ProvidesApiFor, EntityKind.Api, api1)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, f, RelationshipType.ConsumesApiFrom, EntityKind.Api, api1)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, t2, RelationshipType.ProvidesApiFor, EntityKind.Api, api2)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, f, RelationshipType.ConsumesApiFrom, EntityKind.Api, api2)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, f, RelationshipType.ProvidesApiFor, EntityKind.Api, api3)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, c, RelationshipType.ConsumesApiFrom, EntityKind.Api, api3)).StatusCode);

        var body = await GetAsync(client, f);

        Assert.IsNotNull(body);
        Assert.AreEqual(2, body!.Dependencies.Count);
        var depIds = body.Dependencies.Select(d => d.ServiceId).ToList();
        Assert.IsTrue(depIds.Contains(t1));
        Assert.IsTrue(depIds.Contains(t2));
        Assert.IsFalse(depIds.Contains(f), "focus must not depend on itself");
        Assert.IsTrue(body.Dependents.Count >= 1);
        Assert.IsTrue(body.Dependents.Any(d => d.ServiceId == c));
        Assert.IsTrue(
            body.Dependencies.Concat(body.Dependents).Any(d => d.TeamId == neighbourTeamId),
            "team join must resolve to the seeded neighbour team on at least one item");
    }
}
