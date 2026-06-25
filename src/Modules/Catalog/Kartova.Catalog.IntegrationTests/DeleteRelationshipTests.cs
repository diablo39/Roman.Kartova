using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class DeleteRelationshipTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

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

    private static object Rel(EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid)
        => new { sourceKind = sk, sourceId = sid, type = t, targetKind = tk, targetId = tid };

    [TestMethod]
    public async Task DELETE_removes_relationship_returns_204()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Del Rel Team 204");
        var a = await SeedServiceAsync(client, teamId, "svc-da-204");
        var b = await SeedServiceAsync(client, teamId, "svc-db-204");
        var created = await (await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b)))
            .Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);

        var del = await client.DeleteAsync($"/api/v1/catalog/relationships/{created!.Id}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);

        var page = await (await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={a}&direction=outgoing"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(0, page!.Items.Count);
    }

    [TestMethod]
    public async Task DELETE_writes_relationship_removed_audit_row()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Del Rel Audit Team");
        var a = await SeedServiceAsync(client, teamId, "svc-da-audit");
        var b = await SeedServiceAsync(client, teamId, "svc-db-audit");
        var created = await (await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b)))
            .Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);

        var del = await client.DeleteAsync($"/api/v1/catalog/relationships/{created!.Id}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.RelationshipRemoved &&
            r.TargetId == created.Id.ToString());
        Assert.AreEqual(CatalogAuditTargetTypes.Relationship, row.TargetType);
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual(EntityKind.Service.ToString(), data.RootElement.GetProperty("sourceKind").GetString());
        Assert.AreEqual(a.ToString(), data.RootElement.GetProperty("sourceId").GetString());
        Assert.AreEqual(RelationshipType.DependsOn.ToString(), data.RootElement.GetProperty("type").GetString());
    }

    [TestMethod]
    public async Task DELETE_nonexistent_returns_404()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.DeleteAsync($"/api/v1/catalog/relationships/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task DELETE_other_tenant_relationship_returns_404()
    {
        // Create relationship in OrgB; attempt to delete from OrgA — RLS scopes to tenant, so 404.
        const string OrgBUser = "admin@orgb.kartova.local";
        var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "Del Rel XT Team B");
        var b1 = await SeedServiceAsync(orgB, teamB, "svc-xt-b1");
        var b2 = await SeedServiceAsync(orgB, teamB, "svc-xt-b2");
        var created = await (await orgB.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, b1, RelationshipType.DependsOn, EntityKind.Service, b2)))
            .Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);

        var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await orgA.DeleteAsync($"/api/v1/catalog/relationships/{created!.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task DELETE_by_member_in_neither_team_returns_403()
    {
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var sourceTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Del Neither Src 403");
        var targetTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Del Neither Tgt 403");
        var a = await SeedServiceAsync(admin, sourceTeam, "svc-dn-1-403");
        var b = await SeedServiceAsync(admin, targetTeam, "svc-dn-2-403");
        var created = await (await admin.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b)))
            .Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);

        var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
        var resp = await member.DeleteAsync($"/api/v1/catalog/relationships/{created!.Id}");
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task DELETE_by_target_team_member_returns_204()
    {
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var sourceTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Del Either Src 204");
        var targetTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Del Either Tgt 204");
        var src = await SeedServiceAsync(admin, sourceTeam, "svc-del-either-src");
        var tgt = await SeedServiceAsync(admin, targetTeam, "svc-del-either-tgt");
        var created = await (await admin.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, src, RelationshipType.DependsOn, EntityKind.Service, tgt)))
            .Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);

        var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
        var memberId = await Fx.GetSubClaimAsync("member@orga.kartova.local");
        await Fx.SeedTeamMembershipAsync(targetTeam, memberId, roleByte: 1 /* Member */);

        var del = await member.DeleteAsync($"/api/v1/catalog/relationships/{created!.Id}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);
    }

    [TestMethod]
    public async Task DELETE_by_target_team_member_after_source_deleted_returns_204()
    {
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var sourceTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Del Fallback Src");
        var targetTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Del Fallback Tgt");
        const string appPrefix = "app-del-fallback";
        var srcApp = await SeedApplicationAsync(admin, sourceTeam, appPrefix);
        var tgtSvc = await SeedServiceAsync(admin, targetTeam, "svc-del-fallback");
        var created = await (await admin.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Application, srcApp, RelationshipType.DependsOn, EntityKind.Service, tgtSvc)))
            .Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);

        // Hard-delete the SOURCE application — its team can no longer be resolved.
        await Fx.DeleteApplicationsByPrefixAsync(tenant, appPrefix);

        var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
        var memberId = await Fx.GetSubClaimAsync("member@orga.kartova.local");
        await Fx.SeedTeamMembershipAsync(targetTeam, memberId, roleByte: 1 /* Member */);

        var del = await member.DeleteAsync($"/api/v1/catalog/relationships/{created!.Id}");
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);
    }
}
