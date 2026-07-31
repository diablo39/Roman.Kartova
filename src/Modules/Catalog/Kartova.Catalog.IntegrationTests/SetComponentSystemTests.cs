using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Application;   // CatalogAuditActions
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;   // CatalogDbContext, SetComponentSystemHandler
using Kartova.SharedKernel.AspNetCore;   // ProblemTypes, ICurrentUser
using Kartova.SharedKernel.Audit;   // IAuditWriter, AuditEntry
using Kartova.SharedKernel.Multitenancy;   // KartovaRoles, ITenantContext
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Mvc;   // ProblemDetails
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;   // ChangeTracker
using Microsoft.EntityFrameworkCore.Diagnostics;   // SaveChangesInterceptor
using Npgsql;

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
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team Idem");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-idem");
        var sysId = await SeedSystemAsync(client, teamId, "system-setsystem-idem");

        await PutSystemAsync(client, "applications", appId, sysId);
        // Watermark AFTER the first (real) write, before the repeat — "kept" is otherwise
        // indistinguishable from "deleted and re-inserted with the same target" from the
        // response/edge-count alone. Mirrors PUT_null_on_an_already_unassigned_component_is_a_no_op.
        var watermark = (await Fx.ReadAuditLogAsync(tenantId)).Select(r => r.Seq).DefaultIfEmpty(0L).Max();
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
        var after = (await Fx.ReadAuditLogAsync(tenantId)).Where(r => r.Seq > watermark).ToList();
        Assert.IsFalse(after.Any(r => r.DataJson is not null && r.DataJson.Contains(appId.ToString())),
            "re-requesting the current membership must write no audit row — a delete+re-insert would");
    }

    [TestMethod]
    public async Task PUT_unknown_system_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team 422 Sys");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-422sys");

        var resp = await PutSystemAsync(client, "applications", appId, Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        // Pin the specific URI, not just the status: swapping InvalidSourceEntity/InvalidTargetEntity
        // between the component-lookup and System-lookup branches would ship silently otherwise.
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.InvalidTargetEntity, problem!.Type);
    }

    [TestMethod]
    public async Task PUT_unknown_component_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "SetSystem Team 422 App");
        var sysId = await SeedSystemAsync(client, teamId, "system-setsystem-422app");

        var resp = await PutSystemAsync(client, "applications", Guid.NewGuid(), sysId);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.InvalidSourceEntity, problem!.Type);
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
    public async Task PUT_moving_a_component_INTO_a_foreign_system_by_a_steward_of_only_the_SOURCE_returns_403()
    {
        // Symmetric to the DESTINATION-only case above: a steward of only the SOURCE system
        // authorizes the move's delete half (they own the edge being removed), but not its insert
        // half into a System they have no claim on. Both touched edges must be authorized
        // independently — this pins that the insert-side check isn't skipped when the delete-side
        // one already passed.
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var appTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Move2 AppTeam");
        var fromTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Move2 FromTeam");
        var toTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Move2 ToTeam");
        var appId = await SeedApplicationAsync(admin, appTeam, "app-setsystem-moveauthz2");
        var from = await SeedSystemAsync(admin, fromTeam, "system-move-authz2-from");
        var to = await SeedSystemAsync(admin, toTeam, "system-move-authz2-to");
        await PutSystemAsync(admin, "applications", appId, from);
        var fromSteward = await Fx.CreateAuthenticatedClientAsync(MemberEmail, new[] { KartovaRoles.Member });
        var stewardId = await Fx.GetSubClaimAsync(MemberEmail);
        await Fx.SeedTeamMembershipAsync(fromTeam, stewardId, roleByte: 1 /* Member */);

        var resp = await PutSystemAsync(fromSteward, "applications", appId, to);

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task PUT_a_steward_of_the_current_system_may_clear_the_membership()
    {
        // The clearing counterpart of the positive assign test below: clearing (systemId: null)
        // only deletes the existing edge, so a steward of the System side — not the component's
        // own team — must be able to authorize it, same as they authorize an insert.
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var appTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Clear AppTeam");
        var sysTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Clear SysTeam");
        var appId = await SeedApplicationAsync(admin, appTeam, "app-setsystem-clearauthz");
        var sysId = await SeedSystemAsync(admin, sysTeam, "system-setsystem-clearauthz");
        await PutSystemAsync(admin, "applications", appId, sysId);
        var steward = await Fx.CreateAuthenticatedClientAsync(MemberEmail, new[] { KartovaRoles.Member });
        var stewardId = await Fx.GetSubClaimAsync(MemberEmail);
        await Fx.SeedTeamMembershipAsync(sysTeam, stewardId, roleByte: 1 /* Member */);

        var resp = await PutSystemAsync(steward, "applications", appId, null);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
            "a System steward may clear a membership pointing at their System — they are an endpoint of the deleted edge");
        Assert.AreEqual(0, (await PartOfEdgesAsync(admin, EntityKind.Application, appId)).Count);
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
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.InvalidTargetEntity, problem!.Type);
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
        var depResp = await client.PostAsJsonAsync("/api/v1/catalog/relationships", new
        {
            sourceKind = EntityKind.Application, sourceId = appId, type = RelationshipType.DependsOn,
            targetKind = EntityKind.Application, targetId = otherApp,
        }, KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(HttpStatusCode.Created, depResp.StatusCode, "seed DependsOn relationship must succeed");

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

        var ex = await Assert.ThrowsExactlyAsync<DbUpdateException>(
            () => Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, appId, sysB));

        // ThrowsExactlyAsync<DbUpdateException> alone is satisfied by ANY unique violation —
        // pin the specific constraint so this test can't pass for the wrong reason (e.g. a
        // collision on ux_relationships_edge instead).
        var pg = ex.InnerException as PostgresException;
        Assert.IsNotNull(pg, $"expected a PostgresException inner exception, got {ex.InnerException?.GetType().Name}");
        Assert.AreEqual("23505", pg!.SqlState);
        Assert.IsTrue(pg.Message.Contains("ux_relationships_one_system"),
            $"expected the violation to name ux_relationships_one_system, message was: {pg.Message}");
    }

    // --- Gate 8/9 findings 1 and 2 (deep-review B): the delete-race and insert-race that only
    // the DATABASE, not the application pre-check, can catch. Both are constructed
    // deterministically (no threads, no Task.WhenAll) via a SaveChangesInterceptor that performs
    // an out-of-band mutation through a second bypass connection during the handler's own
    // SaveChangesAsync — after its initial read already tracked the row(s), but before its own
    // write reaches Postgres. This calls SetComponentSystemHandler.Handle directly (not through
    // HTTP) with fakes for ITenantContext/ICurrentUser/IAuditWriter — the audit trail and HTTP
    // status mapping aren't what these races exercise; the handler's own exception handling is.

    [TestMethod]
    public async Task PUT_null_when_a_concurrent_delete_wins_the_race_reconciles_instead_of_412()
    {
        // SetComponentSystemHandler removes tracked entities read a few lines earlier. If a
        // concurrent DELETE /relationships/{id} (or a second clear) commits in between, the
        // DELETE affects 0 rows, EF throws DbUpdateConcurrencyException, and — uncaught — the
        // global ConcurrencyConflictExceptionHandler maps that to 412 Precondition Failed, a
        // status this route never declares. The requested end state (no membership) already
        // holds, so the fix reconciles to 200 instead of inventing a new status.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Race Delete Team");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-racedelete");
        var sysId = await SeedSystemAsync(client, teamId, "system-racedelete");
        await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, appId, sysId);

        var raced = false;
        var interceptor = new RaceOnSaveInterceptor(
            tracker => tracker.Entries<Relationship>().Any(e => e.State == EntityState.Deleted),
            async () =>
            {
                raced = true;
                await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "DELETE FROM relationships WHERE source_kind = $1 AND source_id = $2 AND type = 'PartOf'";
                cmd.Parameters.AddWithValue(EntityKind.Application.ToString());
                cmd.Parameters.AddWithValue(appId);
                await cmd.ExecuteNonQueryAsync();
            });
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(Fx.BypassConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new CatalogDbContext(options);
        var handler = new SetComponentSystemHandler(TimeProvider.System);

        var response = await handler.Handle(
            new SetComponentSystemCommand(new EntityRef(EntityKind.Application, appId), null),
            null, db, new FakeTenantContext(tenant), new FakeCurrentUser(), new NoOpAuditWriter(), default);

        Assert.IsTrue(raced, "the interceptor must actually have raced the delete for this test to be meaningful");
        Assert.IsNull(response.SystemId, "the requested end state — no membership — already held after the lost race");
        Assert.AreEqual(0, (await PartOfEdgesAsync(client, EntityKind.Application, appId)).Count);
    }

    [TestMethod]
    public async Task PUT_when_a_concurrent_writer_wins_for_the_SAME_system_reconciles_to_200()
    {
        // Two concurrent PUTs naming the SAME System: both see no current membership, both
        // insert, and — before this fix — the loser's 23505 unconditionally became
        // ComponentAlreadyInSystemException -> 409, even though the state it asked for now
        // holds. ADR-0096 idempotence requires treating this as success.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Race Insert Same Team");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-raceinsert-same");
        var sysId = await SeedSystemAsync(client, teamId, "system-raceinsert-same");

        var raced = false;
        var interceptor = new RaceOnSaveInterceptor(
            tracker => tracker.Entries<Relationship>().Any(e => e.State == EntityState.Added),
            async () =>
            {
                raced = true;
                // The winning concurrent writer names the SAME System this request will ask for.
                await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, appId, sysId);
            });
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(Fx.BypassConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new CatalogDbContext(options);
        var handler = new SetComponentSystemHandler(TimeProvider.System);

        var response = await handler.Handle(
            new SetComponentSystemCommand(new EntityRef(EntityKind.Application, appId), sysId),
            "system-raceinsert-same", db, new FakeTenantContext(tenant), new FakeCurrentUser(), new NoOpAuditWriter(), default);

        Assert.IsTrue(raced, "the interceptor must actually have raced the insert for this test to be meaningful");
        Assert.AreEqual(sysId, response.SystemId, "the other writer already achieved exactly the state this request asked for");
        Assert.ContainsSingle(await PartOfEdgesAsync(client, EntityKind.Application, appId),
            "the loser must not also create a second row for the same membership");
    }

    [TestMethod]
    public async Task PUT_when_a_concurrent_writer_wins_for_a_DIFFERENT_system_still_returns_409()
    {
        // Companion to the same-System case above: a genuinely different winner must still
        // surface the conflict, proving the fix only forgives the idempotent case.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Race Insert Diff Team");
        var appId = await SeedApplicationAsync(client, teamId, "app-setsystem-raceinsert-diff");
        var requested = await SeedSystemAsync(client, teamId, "system-raceinsert-diff-requested");
        var winner = await SeedSystemAsync(client, teamId, "system-raceinsert-diff-winner");

        var raced = false;
        var interceptor = new RaceOnSaveInterceptor(
            tracker => tracker.Entries<Relationship>().Any(e => e.State == EntityState.Added),
            async () =>
            {
                raced = true;
                await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, appId, winner);
            });
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(Fx.BypassConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new CatalogDbContext(options);
        var handler = new SetComponentSystemHandler(TimeProvider.System);

        var ex = await Assert.ThrowsExactlyAsync<ComponentAlreadyInSystemException>(() => handler.Handle(
            new SetComponentSystemCommand(new EntityRef(EntityKind.Application, appId), requested),
            "system-raceinsert-diff-requested", db, new FakeTenantContext(tenant), new FakeCurrentUser(), new NoOpAuditWriter(), default));

        Assert.IsTrue(raced, "the interceptor must actually have raced the insert for this test to be meaningful");
        Assert.AreEqual(new EntityRef(EntityKind.Application, appId), ex.Component);
    }

    /// <summary>Fires <paramref name="raceAction"/> exactly once, the first time <paramref
    /// name="shouldFire"/> matches the pending change set — a deterministic (no threads, no
    /// Task.WhenAll) reproduction of a concurrent writer landing between this handler's initial
    /// read and its own SaveChangesAsync reaching Postgres.</summary>
    private sealed class RaceOnSaveInterceptor(Func<ChangeTracker, bool> shouldFire, Func<Task> raceAction)
        : SaveChangesInterceptor
    {
        private bool _fired;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_fired && eventData.Context is { } ctx && shouldFire(ctx.ChangeTracker))
            {
                _fired = true;
                await raceAction();
            }
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FakeTenantContext(TenantId id) : ITenantContext
    {
        public TenantId Id { get; } = id;
        public bool IsTenantScoped => true;
        public IReadOnlyCollection<string> Roles => [];
        public IReadOnlyList<TeamMembershipInfo> TeamMemberships => [];
        public IReadOnlySet<Guid> TeamIds { get; } = new HashSet<Guid>();
        public void Populate(TenantId id, IReadOnlyCollection<string> roles) { }
        public void PopulateTeamMemberships(IReadOnlyList<TeamMembershipInfo> memberships) { }
        public void Clear() { }
    }

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId => Guid.NewGuid();
        public string DisplayName => "race-test-actor";
        public IReadOnlyList<TeamMembershipInfo> TeamMemberships => [];
        public IReadOnlySet<Guid> TeamIds { get; } = new HashSet<Guid>();
    }

    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task AppendAsync(AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
        public Task AppendSystemAsync(TenantId tenant, AuditEntry entry, CancellationToken ct) => Task.CompletedTask;
    }
}
