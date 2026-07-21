using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Catalog.Application;
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

    private static async Task<Guid> SeedSystemAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems", new
        { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedSystem '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson);
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
    public async Task POST_unknown_source_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team 422S");
        var b = await SeedServiceAsync(client, teamId, "svc-known-422s");
        var resp = await PostRelAsync(client, EntityKind.Service, Guid.NewGuid(), RelationshipType.DependsOn, EntityKind.Service, b);
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

    public async Task POST_by_member_in_neither_team_returns_403()
    {
        // ADR-0108: Member who belongs to NEITHER endpoint's team is still rejected.
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var sourceTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Rel Neither Src 403");
        var targetTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Rel Neither Tgt 403");
        var a = await SeedServiceAsync(admin, sourceTeam, "svc-neither-1-403");
        var b = await SeedServiceAsync(admin, targetTeam, "svc-neither-2-403");

        var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
        var resp = await PostRelAsync(member, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b);

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_by_target_team_member_returns_201()
    {
        // ADR-0108: a member of the TARGET entity's team (but NOT the source team)
        // may declare the edge. Was 403 under source-only authority.
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var sourceTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Rel Either Src 201");
        var targetTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Rel Either Tgt 201");
        var src = await SeedServiceAsync(admin, sourceTeam, "svc-either-src-201");
        var tgt = await SeedServiceAsync(admin, targetTeam, "svc-either-tgt-201");

        var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
        var memberId = await Fx.GetSubClaimAsync("member@orga.kartova.local");
        await Fx.SeedTeamMembershipAsync(targetTeam, memberId, roleByte: 1 /* Member */);

        var resp = await PostRelAsync(member, EntityKind.Service, src, RelationshipType.DependsOn, EntityKind.Service, tgt);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
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
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(await Fx.GetSubClaimAsync(OrgAUser), body!.CreatedByUserId);
    }

    [TestMethod]
    public async Task POST_application_providesApiFor_api_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team ProvidesApi 201");
        var appId = await SeedApplicationAsync(client, teamId, "app-provider-201");
        var apiId = await SeedApiAsync(client, teamId, "orders-api-201");

        var resp = await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(RelationshipType.ProvidesApiFor, body!.Type);
        Assert.AreEqual("orders-api-201", body.Target.DisplayName);
    }

    [TestMethod]
    public async Task POST_two_services_can_provide_the_same_api()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team Shared Api");
        var apiId = await SeedApiAsync(client, teamId, "shared-contract");
        var connectorA = await SeedServiceAsync(client, teamId, "connector-a");
        var connectorB = await SeedServiceAsync(client, teamId, "connector-b");

        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, connectorA, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, connectorB, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId)).StatusCode);

        // Airtight oracle: the Api's graph must show both distinct providers, not just 2xN POSTs.
        var graph = await (await client.GetAsync($"/api/v1/catalog/graph?entityKind=Api&entityId={apiId}&depth=1&direction=all"))
            .Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
        var providerEdges = graph!.Edges.Where(e => e.Type == RelationshipType.ProvidesApiFor && e.Target.Id == apiId).ToList();
        Assert.AreEqual(2, providerEdges.Count, "both provider edges should point at the shared Api");
        Assert.IsTrue(providerEdges.Any(e => e.Source.Id == connectorA));
        Assert.IsTrue(providerEdges.Any(e => e.Source.Id == connectorB));
    }

    [TestMethod]
    public async Task POST_instanceOf_application_to_service_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team InstanceOf Bad Pair");
        var appId = await SeedApplicationAsync(client, teamId, "app-instanceof-badpair");
        var svcId = await SeedServiceAsync(client, teamId, "svc-instanceof-badpair");

        // InstanceOf is Service→Application only; Application→Service is a disallowed pair.
        var resp = await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.InstanceOf, EntityKind.Service, svcId);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_service_consumesApiFrom_api_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Consumer");
        var svcId = await SeedServiceAsync(client, teamId, "svc-consumer-201");
        var apiId = await SeedApiAsync(client, teamId, "consumed-api-201");

        var resp = await PostRelAsync(client, EntityKind.Service, svcId, RelationshipType.ConsumesApiFrom, EntityKind.Api, apiId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_service_instanceOf_application_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel InstanceOf 201");
        var svcId = await SeedServiceAsync(client, teamId, "svc-instance-201");
        var appId = await SeedApplicationAsync(client, teamId, "app-instance-201");

        var resp = await PostRelAsync(client, EntityKind.Service, svcId, RelationshipType.InstanceOf, EntityKind.Application, appId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(RelationshipType.InstanceOf, body!.Type);
    }

    [TestMethod]
    public async Task POST_providesApiFor_unknown_api_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team Unknown Api");
        var appId = await SeedApplicationAsync(client, teamId, "app-unknown-api-422");

        var resp = await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_disallowed_pair_providesApiFor_api_to_application_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team Bad Pair");
        var apiId = await SeedApiAsync(client, teamId, "api-badpair");
        var appId = await SeedApplicationAsync(client, teamId, "app-badpair");

        // Api→Application is a disallowed pair for ProvidesApiFor
        var resp = await PostRelAsync(client, EntityKind.Api, apiId, RelationshipType.ProvidesApiFor, EntityKind.Application, appId);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_dependsOn_with_system_endpoint_returns_400()
    {
        // RelationshipTypeRules excludes System from DependsOn — a System groups components
        // via PartOf only, so it must never appear on either side of a depends-on edge.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team DependsOn System");
        var svcId = await SeedServiceAsync(client, teamId, "svc-dependson-system");
        var sysId = await SeedSystemAsync(client, teamId, "system-dependson-badpair");

        var resp = await PostRelAsync(client, EntityKind.Service, svcId, RelationshipType.DependsOn, EntityKind.System, sysId);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_duplicate_providesApiFor_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team Dup Api");
        var appId = await SeedApplicationAsync(client, teamId, "app-dup-provider");
        var apiId = await SeedApiAsync(client, teamId, "api-dup-provider");

        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId)).StatusCode);
    }

    [TestMethod]
    public async Task POST_providesApiFor_by_member_of_api_team_returns_201()
    {
        // ADR-0108 either-team: a member of the API's owning team (but not provider app's) may declare the edge.
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var appTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Provider App Team");
        var apiTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Api Owner Team");
        var appId = await SeedApplicationAsync(admin, appTeam, "app-authz-provider");
        var apiId = await SeedApiAsync(admin, apiTeam, "api-authz-target");

        var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
        var memberId = await Fx.GetSubClaimAsync("member@orga.kartova.local");
        await Fx.SeedTeamMembershipAsync(apiTeam, memberId, roleByte: 1 /* Member */);

        var resp = await PostRelAsync(member, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_writes_relationship_created_audit_row()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Audit Create Team");
        var a = await SeedServiceAsync(client, teamId, "svc-audit-c1");
        var b = await SeedServiceAsync(client, teamId, "svc-audit-c2");

        var resp = await PostRelAsync(client, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b);
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var created = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.RelationshipCreated &&
            r.TargetId == created!.Id.ToString());
        Assert.AreEqual(CatalogAuditTargetTypes.Relationship, row.TargetType);
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual(EntityKind.Service.ToString(), data.RootElement.GetProperty("sourceKind").GetString());
        Assert.AreEqual(a.ToString(), data.RootElement.GetProperty("sourceId").GetString());
        Assert.AreEqual(RelationshipType.DependsOn.ToString(), data.RootElement.GetProperty("type").GetString());
    }
}
