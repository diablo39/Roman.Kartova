using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;   // ProblemTypes
using Kartova.SharedKernel.Multitenancy;   // KartovaRoles
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Mvc;   // ProblemDetails

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Real-seam (gate-3) integration test for Task 2 (catalog-vm-linking): proves
/// <see cref="Kartova.Catalog.Infrastructure.CatalogEntityLookup"/> resolves
/// <see cref="EntityKind.Infrastructure"/> targets, so a <see cref="RelationshipType.DeployedOn"/>
/// edge from a Service to a VM can be created. Before this task, <c>Find(EntityKind.Infrastructure, …)</c>
/// always returned null, so the target could never be resolved and the POST 422'd
/// (invalid target entity) regardless of whether the VM existed.
/// </summary>
[TestClass]
public sealed class InfrastructureRelationshipTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";   // cf. SetComponentSystemTests
    private const string MemberEmail = "member@orga.kartova.local";   // cf. SetComponentSystemTests

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

    private static async Task<Guid> SeedVmAsync(HttpClient client, Guid teamId, string name)
    {
        var request = new RegisterVmRequest(
            DisplayName: name,
            Description: "integration",
            TeamId: teamId,
            Provider: "AWS",
            Attributes: new VmAttributesDto("running", "ubuntu-22.04", 4, 16, "host-1",
                new[] { "10.0.0.1" }, "eu-west-1"));
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/infrastructure/vms", request, KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedVm '{name}' failed: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static async Task<Guid> SeedSystemAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems", new { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedSystem '{name}' failed: {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson))!.Id;
    }

    private static Task<HttpResponseMessage> PutSystemAsync(HttpClient client, Guid vmId, Guid? systemId)
        => client.PutAsJsonAsync($"/api/v1/catalog/infrastructure/{vmId}/system", new { systemId }, KartovaApiFixtureBase.WireJson);

    /// <summary>PartOf edges whose source is the given infrastructure component, read back
    /// through the public list endpoint — mirrors SetComponentSystemTests.PartOfEdgesAsync.</summary>
    private static async Task<List<RelationshipResponse>> PartOfEdgesAsync(HttpClient client, Guid vmId)
    {
        var resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind={EntityKind.Infrastructure}&entityId={vmId}&direction=outgoing&limit=50");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        return [.. page!.Items.Where(r => r.Type == RelationshipType.PartOf)];
    }

    /// <summary>
    /// Task 4 (catalog-vm-linking): PUT /infrastructure/{id}/system enforces the same
    /// at-most-one PartOf invariant as the application/service routes — a second PUT naming a
    /// different System replaces, rather than adds to, the VM's membership.
    /// </summary>
    [TestMethod]
    public async Task Infrastructure_partOf_system_is_atmost_one()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Infra SetSystem Team AtMostOne");
        var vmId = await SeedVmAsync(client, teamId, "vm-setsystem-atmostone");
        var sysA = await SeedSystemAsync(client, teamId, "system-infra-atmostone-a");
        var sysB = await SeedSystemAsync(client, teamId, "system-infra-atmostone-b");

        var r1 = await PutSystemAsync(client, vmId, sysA);
        Assert.AreEqual(HttpStatusCode.OK, r1.StatusCode, $"first PUT failed: {await r1.Content.ReadAsStringAsync()}");
        var r2 = await PutSystemAsync(client, vmId, sysB);
        Assert.AreEqual(HttpStatusCode.OK, r2.StatusCode, $"second PUT failed: {await r2.Content.ReadAsStringAsync()}");

        // Only one PartOf edge remains, pointing at sysB.
        var edges = await PartOfEdgesAsync(client, vmId);
        Assert.AreEqual(1, edges.Count);
        Assert.AreEqual(sysB, edges[0].Target.Id);
    }

    /// <summary>
    /// Task 4 (catalog-vm-linking): a caller in a different tenant cannot see the VM at all —
    /// the component lookup misses under RLS, so the write 422s (InvalidSourceEntity) rather
    /// than leaking existence or succeeding cross-tenant.
    /// </summary>
    [TestMethod]
    public async Task Infrastructure_system_membership_is_tenant_isolated()
    {
        var clientA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamA = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Infra SetSystem Team TenantIso");
        var vmId = await SeedVmAsync(clientA, teamA, "vm-setsystem-tenantiso");
        var clientB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);

        var resp = await PutSystemAsync(clientB, vmId, Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode); // component not found in tenant B
        // Pin the specific branch, not just the status: SetComponentSystemAsync checks the
        // component (source) before the System (target), so a bogus systemId would ALSO 422
        // (InvalidTargetEntity) even if source-tenant isolation regressed. Asserting the Type
        // proves this 422 came from the source lookup missing the VM under RLS — mirrors
        // SetComponentSystemTests.PUT_a_cross_tenant_system_returns_422_not_a_leak.
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.InvalidSourceEntity, problem!.Type);
    }

    [TestMethod]
    public async Task POST_deployedOn_service_to_vm_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team DeployedOn 201");
        var serviceId = await SeedServiceAsync(client, teamId, "svc-deployedon-201");
        var vmId = await SeedVmAsync(client, teamId, "vm-deployedon-201");

        var resp = await PostRelAsync(client, EntityKind.Service, serviceId, RelationshipType.DeployedOn, EntityKind.Infrastructure, vmId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"CreateRelationship failed: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(RelationshipType.DeployedOn, body!.Type);
        Assert.AreEqual(serviceId, body.Source.Id);
        Assert.AreEqual("vm-deployedon-201", body.Target.DisplayName);
    }

    /// <summary>
    /// Task 3 (catalog-vm-linking): happy-path DeployedOn from an Application (not just a
    /// Service) to a VM, proving the VM-only guard in
    /// <see cref="Kartova.Catalog.Infrastructure.CatalogEndpointDelegates.CreateRelationshipAsync"/>
    /// does not block a legitimate VirtualMachine target. The negative (non-VM target) case is
    /// covered by a pure unit test on <see cref="DeployedOnTargetRules"/> — today VirtualMachine
    /// is the only <see cref="InfrastructureType"/>, so the reject branch is unreachable via the API.
    /// </summary>
    [TestMethod]
    public async Task POST_deployedOn_application_to_vm_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team DeployedOn App-VM");
        var appId = await SeedApplicationAsync(client, teamId, "app-deployedon-201");
        var vmId = await SeedVmAsync(client, teamId, "vm-deployedon-app-201");

        var resp = await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.DeployedOn, EntityKind.Infrastructure, vmId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"CreateRelationship failed: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(RelationshipType.DeployedOn, body!.Type);
        Assert.AreEqual(appId, body.Source.Id);
        Assert.AreEqual("vm-deployedon-app-201", body.Target.DisplayName);
    }

    /// <summary>
    /// Final-review fix (catalog-vm-linking): POST /relationships duplicate-PartOf path for an
    /// Infrastructure source. RelationshipTypeRules.IsAllowedPair(PartOf, Infrastructure, System)
    /// is true, so a second infra PartOf POST (VM already in a System, naming a different one)
    /// reaches CreateRelationshipAsync's at-most-one handling. Before this fix that handling was
    /// scoped to Application/Service sources only, so the pre-check skipped an infra source, the
    /// write hit the DB's ux_relationships_one_system unique index, and the 23505 catch's `when`
    /// filter ALSO excluded infra — leaving an uncaught DbUpdateException (HTTP 500) instead of
    /// the 409 ComponentAlreadyInSystem the App/Service paths return. This is specifically the
    /// POST /relationships duplicate path — PUT /infrastructure/{id}/system's own at-most-one
    /// replace-semantics are already covered by Infrastructure_partOf_system_is_atmost_one above.
    /// </summary>
    [TestMethod]
    public async Task POST_partOf_infra_duplicate_returns_409_not_500()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team Infra PartOf Dup");
        var vmId = await SeedVmAsync(client, teamId, "vm-partof-dup");
        var sysA = await SeedSystemAsync(client, teamId, "system-infra-partof-dup-a");
        var sysB = await SeedSystemAsync(client, teamId, "system-infra-partof-dup-b");

        var first = await PostRelAsync(client, EntityKind.Infrastructure, vmId, RelationshipType.PartOf, EntityKind.System, sysA);
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, $"first PartOf POST failed: {await first.Content.ReadAsStringAsync()}");

        var second = await PostRelAsync(client, EntityKind.Infrastructure, vmId, RelationshipType.PartOf, EntityKind.System, sysB);

        Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode,
            $"expected 409 ComponentAlreadyInSystem, got {second.StatusCode}: {await second.Content.ReadAsStringAsync()}");
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.ComponentAlreadyInSystem, problem!.Type);
    }

    /// <summary>
    /// Gate-7 fix (catalog-vm-linking): cross-tenant isolation for a DeployedOn TARGET. Tenant
    /// B's service cannot see tenant A's VM under RLS, so the target lookup misses and the POST
    /// 422s (InvalidTargetEntity) rather than leaking existence or succeeding cross-tenant.
    /// Mirrors Infrastructure_system_membership_is_tenant_isolated above, but exercises the
    /// TARGET side of POST /relationships (that test covers the SOURCE side of
    /// PUT /infrastructure/{id}/system).
    /// </summary>
    [TestMethod]
    public async Task POST_deployedOn_cross_tenant_target_returns_422_invalid_target()
    {
        var clientA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamA = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team DeployedOn CrossTenant A");
        var vmId = await SeedVmAsync(clientA, teamA, "vm-deployedon-crosstenant");

        var clientB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "Rel Team DeployedOn CrossTenant B");
        var serviceId = await SeedServiceAsync(clientB, teamB, "svc-deployedon-crosstenant");

        var resp = await PostRelAsync(clientB, EntityKind.Service, serviceId, RelationshipType.DeployedOn, EntityKind.Infrastructure, vmId);

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode,
            $"expected 422 InvalidTargetEntity, got {resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
        // Discriminative: source resolves fine (own tenant), only the target lookup should miss.
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.InvalidTargetEntity, problem!.Type);
    }

    /// <summary>
    /// Gate-7 fix (catalog-vm-linking): exact-duplicate DeployedOn Service→VM POST returns 409
    /// RelationshipAlreadyExists via the pre-check at CatalogEndpointDelegates' duplicate guard
    /// (ux_relationships_edge backstop covers the concurrent-race case).
    /// </summary>
    [TestMethod]
    public async Task POST_deployedOn_exact_duplicate_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team DeployedOn Dup");
        var serviceId = await SeedServiceAsync(client, teamId, "svc-deployedon-dup");
        var vmId = await SeedVmAsync(client, teamId, "vm-deployedon-dup");

        var first = await PostRelAsync(client, EntityKind.Service, serviceId, RelationshipType.DeployedOn, EntityKind.Infrastructure, vmId);
        Assert.AreEqual(HttpStatusCode.Created, first.StatusCode, $"first POST failed: {await first.Content.ReadAsStringAsync()}");

        var second = await PostRelAsync(client, EntityKind.Service, serviceId, RelationshipType.DeployedOn, EntityKind.Infrastructure, vmId);

        Assert.AreEqual(HttpStatusCode.Conflict, second.StatusCode,
            $"expected 409 RelationshipAlreadyExists, got {second.StatusCode}: {await second.Content.ReadAsStringAsync()}");
        var problem = await second.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.RelationshipAlreadyExists, problem!.Type);
    }

    /// <summary>
    /// Gate-8 fix (catalog-vm-linking): proves <see cref="Kartova.Catalog.Infrastructure.CatalogEntityLookup"/>'s
    /// infrastructure arm feeds the graph endpoint's traversal enrichment, not just the
    /// relationship-creation path already covered above. Seeds Service→VM DeployedOn (incoming
    /// to the VM) and VM→System PartOf (outgoing from the VM), then focuses the graph on the VM
    /// and asserts both the enriched infra node and both edges come back.
    /// </summary>
    [TestMethod]
    public async Task GET_graph_focused_on_infrastructure_returns_infra_node_and_both_edges()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Infra Team");
        var serviceId = await SeedServiceAsync(client, teamId, "graph-infra-svc");
        var vmId = await SeedVmAsync(client, teamId, "graph-infra-vm");
        var sysId = await SeedSystemAsync(client, teamId, "graph-infra-system");

        var deployResp = await PostRelAsync(client, EntityKind.Service, serviceId, RelationshipType.DeployedOn, EntityKind.Infrastructure, vmId);
        Assert.AreEqual(HttpStatusCode.Created, deployResp.StatusCode, $"seed DeployedOn failed: {await deployResp.Content.ReadAsStringAsync()}");
        var partOfResp = await PutSystemAsync(client, vmId, sysId);
        Assert.AreEqual(HttpStatusCode.OK, partOfResp.StatusCode, $"seed PartOf failed: {await partOfResp.Content.ReadAsStringAsync()}");

        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Infrastructure&entityId={vmId}&depth=1&direction=all");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        var vmNode = graph!.Nodes.SingleOrDefault(n => n.Id == vmId);
        Assert.IsNotNull(vmNode, "the VM's infrastructure node must be present");
        Assert.AreEqual(EntityKind.Infrastructure, vmNode!.Kind);
        Assert.AreEqual("graph-infra-vm", vmNode.DisplayName,
            "CatalogEntityLookup.Find's infrastructure arm must enrich the VM's DisplayName during traversal");
        Assert.IsTrue(
            graph.Edges.Any(e => e.Type == RelationshipType.DeployedOn && e.Source.Id == serviceId && e.Target.Id == vmId),
            "the incoming DeployedOn edge (Service -> VM) must be present");
        Assert.IsTrue(
            graph.Edges.Any(e => e.Type == RelationshipType.PartOf && e.Source.Id == vmId && e.Target.Id == sysId),
            "the outgoing PartOf edge (VM -> System) must be present");
    }

    /// <summary>
    /// Gate-8 fix (catalog-vm-linking): pins that the reused ADR-0108 either-endpoint authorization
    /// runs for the infrastructure wrapper of PUT /system too, not just the application/service
    /// routes covered by <see cref="SetComponentSystemTests.PUT_by_a_member_of_neither_team_returns_403"/>.
    /// A same-tenant caller who is neither OrgAdmin nor a member of the VM's or the System's team
    /// must be refused.
    /// </summary>
    [TestMethod]
    public async Task PUT_infrastructure_system_by_a_member_of_neither_team_returns_403()
    {
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var vmTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Infra Neither VM Team");
        var sysTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "SetSystem Infra Neither Sys Team");
        var vmId = await SeedVmAsync(admin, vmTeam, "vm-setsystem-403-neither");
        var sysId = await SeedSystemAsync(admin, sysTeam, "system-infra-403-neither");
        // The roles argument is REQUIRED: CreateAuthenticatedClientAsync defaults a null roles
        // array to [OrgAdmin], which short-circuits the authorization check — same idiom as
        // SetComponentSystemTests.PUT_by_a_member_of_neither_team_returns_403.
        var member = await Fx.CreateAuthenticatedClientAsync(MemberEmail, new[] { KartovaRoles.Member });

        var resp = await PutSystemAsync(member, vmId, sysId);

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
