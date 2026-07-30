using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Application;   // CatalogAuditActions
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;   // KartovaRoles
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>Real-seam tests for the System membership setter (A1 spec §3.3): real Postgres +
/// RLS + real JWT validation via <see cref="CatalogIntegrationTestBase"/>. Covers assign,
/// move, clear, idempotence, the 422/403 negatives, and the defensive multi-membership
/// collapse.</summary>
[TestClass]
public sealed class SetComponentSystemTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";      // cf. CreateRelationshipTests
    private const string MemberEmail = "member@orga.kartova.local";  // cf. CreatePartOfRelationshipTests.cs:169

    private static Task<HttpResponseMessage> PutSystemAsync(HttpClient client, string segment, Guid id, Guid? systemId)
        => client.PutAsJsonAsync($"/api/v1/catalog/{segment}/{id}/system", new { systemId }, KartovaApiFixtureBase.WireJson);

    private static async Task<Guid> SeedApplicationAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/applications", new { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApplication '{name}' failed: {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!.Id;
    }

    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services",
            new { displayName = name, description = "x", teamId, endpoints = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}' failed: {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson))!.Id;
    }

    private static async Task<Guid> SeedSystemAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems", new { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedSystem '{name}' failed: {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson))!.Id;
    }

    /// <summary>PartOf edges whose source is the given component, read back through the public list endpoint.</summary>
    private static async Task<List<RelationshipResponse>> PartOfEdgesAsync(HttpClient client, EntityKind kind, Guid id)
    {
        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind={kind}&entityId={id}&direction=outgoing&limit=50");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        return [.. page!.Items.Where(r => r.Type == RelationshipType.PartOf)];
    }

    [TestMethod]
    public async Task PUT_application_system_assigns_membership()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team App");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-assign");
        var sysId = await SeedSystemAsync(client, teamId, "system-setsystem-assign");

        var resp = await PutSystemAsync(client, "applications", appId, sysId);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SystemMembershipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(sysId, body!.SystemId);
        Assert.AreEqual("system-setsystem-assign", body.SystemDisplayName);
        var edges = await PartOfEdgesAsync(client, EntityKind.Application, appId);
        Assert.AreEqual(1, edges.Count);
        Assert.AreEqual(sysId, edges[0].Target.Id);
    }

    [TestMethod]
    public async Task PUT_service_system_assigns_membership()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team Svc");
        var svcId = await SeedServiceAsync(client, teamId, "svc-setsystem-assign");
        var sysId = await SeedSystemAsync(client, teamId, "system-setsystem-svc");

        var resp = await PutSystemAsync(client, "services", svcId, sysId);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var edges = await PartOfEdgesAsync(client, EntityKind.Service, svcId);
        Assert.AreEqual(1, edges.Count);
        Assert.AreEqual(sysId, edges[0].Target.Id);
    }

    [TestMethod]
    public async Task PUT_system_replaces_the_previous_membership()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team Move");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-move");
        var first = await SeedSystemAsync(client, teamId, "system-move-from");
        var second = await SeedSystemAsync(client, teamId, "system-move-to");

        Assert.AreEqual(HttpStatusCode.OK, (await PutSystemAsync(client, "applications", appId, first)).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await PutSystemAsync(client, "applications", appId, second)).StatusCode);

        var edges = await PartOfEdgesAsync(client, EntityKind.Application, appId);
        Assert.AreEqual(1, edges.Count, "at-most-one: the old edge must be gone");
        Assert.AreEqual(second, edges[0].Target.Id);
    }

    [TestMethod]
    public async Task PUT_system_null_clears_the_membership()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team Clear");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-clear");
        var sysId = await SeedSystemAsync(client, teamId, "system-setsystem-clear");
        await PutSystemAsync(client, "applications", appId, sysId);

        var resp = await PutSystemAsync(client, "applications", appId, null);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SystemMembershipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNull(body!.SystemId);
        Assert.IsNull(body.SystemDisplayName);
        Assert.AreEqual(0, (await PartOfEdgesAsync(client, EntityKind.Application, appId)).Count);
    }

    [TestMethod]
    public async Task PUT_the_same_system_twice_is_idempotent()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team Idem");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-idem");
        var sysId = await SeedSystemAsync(client, teamId, "system-setsystem-idem");

        await PutSystemAsync(client, "applications", appId, sysId);
        var second = await PutSystemAsync(client, "applications", appId, sysId);

        Assert.AreEqual(HttpStatusCode.OK, second.StatusCode);
        // The no-op path must still return the CURRENT membership, name included — a
        // response of (null, null) or a null display name here is the bug this pins.
        var body = await second.Content.ReadFromJsonAsync<SystemMembershipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(sysId, body!.SystemId);
        Assert.AreEqual("system-setsystem-idem", body.SystemDisplayName);
        var edges = await PartOfEdgesAsync(client, EntityKind.Application, appId);
        Assert.AreEqual(1, edges.Count);
        Assert.AreEqual(sysId, edges[0].Target.Id, "the original edge is kept, not replaced");
    }

    [TestMethod]
    public async Task PUT_unknown_system_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team 422 Sys");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-422sys");

        var resp = await PutSystemAsync(client, "applications", appId, Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_unknown_component_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team 422 App");
        var sysId = await SeedSystemAsync(client, teamId, "system-setsystem-422app");

        var resp = await PutSystemAsync(client, "applications", Guid.NewGuid(), sysId);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_by_a_member_of_neither_team_returns_403()
    {
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var appTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Neither App Team");
        var sysTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Neither Sys Team");
        var appId = await SeedApplicationAsync(admin, appTeam, "app-setsystem-403");
        var sysId = await SeedSystemAsync(admin, sysTeam, "system-setsystem-403");
        // The roles argument is REQUIRED: CreateAuthenticatedClientAsync defaults a null
        // roles array to [OrgAdmin] (KartovaApiFixtureBase.cs:256-267), and OrgAdmin
        // short-circuits AuthorizeEitherTeamAsync (CatalogEndpointDelegates.cs:1199-1201),
        // so omitting it makes this test assert 403 against a guaranteed 200.
        // Same idiom as CreateRelationshipTests.cs:177 / CreatePartOfRelationshipTests.cs:169.
        var member = await Fx.CreateAuthenticatedClientAsync(MemberEmail, new[] { KartovaRoles.Member });

        var resp = await PutSystemAsync(member, "applications", appId, sysId);

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_moving_a_component_OUT_of_a_system_by_a_steward_of_only_the_DESTINATION_returns_403()
    {
        // ADR-0108 applies per mutated edge: removing (C, PartOf, X) requires C's team or
        // X's team. A steward of only the destination Y must not be able to strip C out of X.
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var appTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Move AppTeam");
        var fromTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Move FromTeam");
        var toTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Move ToTeam");
        var appId = await SeedApplicationAsync(admin, appTeam, "app-setsystem-moveauthz");
        var from = await SeedSystemAsync(admin, fromTeam, "system-move-authz-from");
        var to = await SeedSystemAsync(admin, toTeam, "system-move-authz-to");
        await PutSystemAsync(admin, "applications", appId, from);
        // Real membership-seeding idiom (CreateRelationshipTests.cs:177-179): authenticated
        // client + GetSubClaimAsync for the JWT subject + SeedTeamMembershipAsync.
        var toSteward = await Fx.CreateAuthenticatedClientAsync(MemberEmail, new[] { KartovaRoles.Member });
        var stewardId = await Fx.GetSubClaimAsync(MemberEmail);
        await Fx.SeedTeamMembershipAsync(toTeam, stewardId, roleByte: 1 /* Member */);

        var resp = await PutSystemAsync(toSteward, "applications", appId, to);

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_by_a_steward_of_only_the_TARGET_system_assigns_an_unassigned_component()
    {
        // The POSITIVE half of ADR-0108's either-endpoint rule, and the only test that walks the
        // per-edge authorization block to a 200. Without it every mutation that makes that block
        // stricter survives — swapping the lookup's EntityKind, changing the `?? Guid.Empty`
        // fallback, or dropping its PartOf filter all still produce the 403 the other tests want.
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var appTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Positive AppTeam");
        var sysTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Positive SysTeam");
        var appId = await SeedApplicationAsync(admin, appTeam, "app-setsystem-positive");
        var sysId = await SeedSystemAsync(admin, sysTeam, "system-setsystem-positive");
        var steward = await Fx.CreateAuthenticatedClientAsync(MemberEmail, new[] { KartovaRoles.Member });
        var stewardId = await Fx.GetSubClaimAsync(MemberEmail);
        await Fx.SeedTeamMembershipAsync(sysTeam, stewardId, roleByte: 1 /* Member */);

        var resp = await PutSystemAsync(steward, "applications", appId, sysId);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
            "a System steward may pull a component in — they are an endpoint of the inserted edge");
        var edges = await PartOfEdgesAsync(admin, EntityKind.Application, appId);
        Assert.ContainsSingle(edges.Where(e => e.Target.Id == sysId));
    }

    [TestMethod]
    public async Task PUT_a_cross_tenant_system_returns_422_not_a_leak()
    {
        // Guid.NewGuid() cannot distinguish "absent" from "RLS hid it" — TESTING-STRATEGY.md §5
        // requires a genuinely cross-tenant entity for an RLS-touching slice.
        var clientA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamA = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Tenant A");
        var appId = await SeedApplicationAsync(clientA, teamA, "app-setsystem-xtenant");

        var clientB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "SetSystem Tenant B");
        var sysInB = await SeedSystemAsync(clientB, teamB, "system-setsystem-xtenant");

        var resp = await PutSystemAsync(clientA, "applications", appId, sysInB);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_without_authentication_returns_401()
    {
        var anonymous = Fx.CreateAnonymousClient();   // KartovaApiFixtureBase.cs:280

        var resp = await PutSystemAsync(anonymous, "applications", Guid.NewGuid(), Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_writes_the_relationship_audit_entries()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team Audit");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-audit");
        var from = await SeedSystemAsync(client, teamId, "system-audit-from");
        var to = await SeedSystemAsync(client, teamId, "system-audit-to");

        // Watermark BEFORE acting. The OrgA tenant's audit_log is shared by every test in this
        // assembly — CreateRelationshipTests and DeleteRelationshipTests write these exact two
        // actions — so an unscoped `rows.Any(r => r.Action == "relationship.created")` passes
        // even when this handler audits nothing. Scope by sequence AND by this component's id.
        // ReadAuditLogAsync takes a raw Guid (KartovaApiFixture.cs:386) and returns
        // AuditRowRecord(Seq, Action, …, DataJson, …) (KartovaApiFixture.cs:413-416).
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var watermark = (await Fx.ReadAuditLogAsync(tenantId)).Select(r => r.Seq).DefaultIfEmpty(0L).Max();

        await PutSystemAsync(client, "applications", appId, from);
        await PutSystemAsync(client, "applications", appId, to);

        var mine = (await Fx.ReadAuditLogAsync(tenantId))
            .Where(r => r.Seq > watermark && r.DataJson is not null && r.DataJson.Contains(appId.ToString()))
            .ToList();
        Assert.ContainsSingle(mine.Where(r => r.Action == CatalogAuditActions.RelationshipRemoved),
            "the move must audit exactly one removal for this component");
        Assert.AreEqual(2, mine.Count(r => r.Action == CatalogAuditActions.RelationshipCreated),
            "the assign and the move's insert are both creations");
    }

    [TestMethod]
    public async Task PUT_null_on_an_already_unassigned_component_is_a_no_op()
    {
        // Relocated from the handler unit tests (EF in-memory cannot translate the
        // ComplexProperty predicate, so that tier was deleted — Task 2 ruling).
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team NoopClear");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-noopclear");
        var watermark = (await Fx.ReadAuditLogAsync(tenantId)).Select(r => r.Seq).DefaultIfEmpty(0L).Max();

        var resp = await PutSystemAsync(client, "applications", appId, null);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SystemMembershipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNull(body!.SystemId);
        Assert.AreEqual(0, (await PartOfEdgesAsync(client, EntityKind.Application, appId)).Count);
        var after = (await Fx.ReadAuditLogAsync(tenantId)).Where(r => r.Seq > watermark).ToList();
        Assert.IsFalse(after.Any(r => r.DataJson is not null && r.DataJson.Contains(appId.ToString())),
            "clearing an unassigned component must write no audit row");
    }

    [TestMethod]
    public async Task PUT_leaves_another_components_membership_alone()
    {
        // Relocated from the handler unit tests. Kills "drop the Source.Id filter", which would
        // wipe every component's membership in the tenant on any single PUT.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team Sibling");
        var mine = await SeedApplicationAsync(client, teamId, "app-setsystem-sibling-mine");
        var theirs = await SeedApplicationAsync(client, teamId, "app-setsystem-sibling-theirs");
        var sysA = await SeedSystemAsync(client, teamId, "system-sibling-a");
        var sysB = await SeedSystemAsync(client, teamId, "system-sibling-b");
        await PutSystemAsync(client, "applications", theirs, sysA);

        await PutSystemAsync(client, "applications", mine, sysB);

        var theirEdges = await PartOfEdgesAsync(client, EntityKind.Application, theirs);
        Assert.ContainsSingle(theirEdges.Where(e => e.Target.Id == sysA),
            "the other component's membership must survive untouched");
        var myEdges = await PartOfEdgesAsync(client, EntityKind.Application, mine);
        Assert.ContainsSingle(myEdges.Where(e => e.Target.Id == sysB));
    }

    [TestMethod]
    public async Task PUT_persists_a_manual_edge_attributed_to_the_caller()
    {
        // Relocated from the handler unit tests: the edge's SHAPE (origin + provenance), not just
        // its existence. `origin` and `createdByUserId` come back on RelationshipResponse.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team Shape");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-shape");
        var sysId = await SeedSystemAsync(client, teamId, "system-setsystem-shape");
        var callerId = await Fx.GetSubClaimAsync(OrgAUser);

        await PutSystemAsync(client, "applications", appId, sysId);

        var edge = (await PartOfEdgesAsync(client, EntityKind.Application, appId)).Single();
        Assert.AreEqual(RelationshipOrigin.Manual, edge.Origin);
        Assert.AreEqual(callerId, edge.CreatedByUserId);
        Assert.AreEqual(EntityKind.Application, edge.Source.Kind);
        Assert.AreEqual(EntityKind.System, edge.Target.Kind);
    }

    [TestMethod]
    public async Task PUT_leaves_an_unrelated_dependsOn_edge_alone()
    {
        // The handler's query filters on Type == PartOf; dropping that filter would silently
        // delete a component's dependencies on every membership write. Nothing else pins it.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team TypeFilter");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-typefilter");
        var otherApp = await SeedApplicationAsync(client, teamId, "app-setsystem-typefilter-dep");
        var sysId = await SeedSystemAsync(client, teamId, "system-setsystem-typefilter");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships", new
        {
            sourceKind = EntityKind.Application, sourceId = appId, type = RelationshipType.DependsOn,
            targetKind = EntityKind.Application, targetId = otherApp,
        }, KartovaApiFixtureBase.WireJson);

        await PutSystemAsync(client, "applications", appId, sysId);

        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind={EntityKind.Application}&entityId={appId}&direction=outgoing&limit=50");
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.ContainsSingle(page!.Items.Where(r => r.Type == RelationshipType.DependsOn),
            "the dependsOn edge must survive a membership write");
        Assert.ContainsSingle(page.Items.Where(r => r.Type == RelationshipType.PartOf));
    }

    [TestMethod]
    public async Task Two_partOf_edges_for_one_component_are_refused_by_the_database()
    {
        // Proves the invariant is enforced by the DB, not only by the endpoints' pre-checks:
        // InsertPartOfEdgeAsync bypasses every application-level guard.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Team Index");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-index");
        var sysA = await SeedSystemAsync(client, teamId, "system-index-a");
        var sysB = await SeedSystemAsync(client, teamId, "system-index-b");
        await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, appId, sysA);

        await Assert.ThrowsExactlyAsync<DbUpdateException>(
            () => Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, appId, sysB));
    }
}
