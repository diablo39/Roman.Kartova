using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Integration tests for <c>PUT /api/v1/catalog/applications/{id}/team</c>
/// (slice 8, spec §10 / ADR-0098 §6.4). Exercises the happy assignment,
/// the 422 invalid-team branch (target team does not exist in the tenant),
/// and the 403 resource-auth branch (caller not in the app's current team).
/// </summary>
[TestClass]
public sealed class AssignApplicationTeamTests : CatalogIntegrationTestBase
{
    private const byte MemberRole = 1;

    private static readonly TenantId Tenant =
        new(Guid.Parse("cccccccc-0001-0001-0001-000000000001"));

    [TestMethod]
    public async Task OrgAdmin_assigns_app_to_team_returns_200_and_GET_reflects_teamId()
    {
        var teamId = await Fx.SeedTeamInOrganizationAsync(Tenant, "Platform");
        var appId = await Fx.SeedSingleApplicationAsync(Tenant, Guid.NewGuid(), teamId: null);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/catalog/applications/{appId}/team",
                new AssignTeamRequest(teamId));

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

            // GET-after-PUT roundtrip pins SaveChangesAsync side-effect — guards
            // against a regression where the handler returns 200 without persisting.
            var getResp = await client.GetAsync($"/api/v1/catalog/applications/{appId}");
            Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);
            var body = await getResp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(teamId, body!.TeamId);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(Tenant, "assign-app");
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Reassigning_Decommissioned_app_to_another_team_returns_409()
    {
        // ADR-0103: AssignTeam is reassign-only and blocked on Decommissioned
        // (terminal-write guard). Reassigning a Decommissioned app surfaces the
        // shared lifecycle-conflict 409 (mirrors the domain unit test
        // AssignTeam_when_Decommissioned_throws). Replaces the slice-8 unassign
        // carve-out, which ADR-0103 removed (no ownerless apps).
        var tenant = new TenantId(Guid.Parse("aaaaaaaa-0020-0020-0020-000000000001"));
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, "TeamForDecomm");
        var otherTeamId = await Fx.SeedTeamInOrganizationAsync(tenant, "OtherTeamForDecomm");
        var appId = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId,
            namePrefix: "decomm-reassign");
        await Fx.SetApplicationLifecycleAsync(appId, Lifecycle.Decommissioned);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/catalog/applications/{appId}/team",
                new AssignTeamRequest(otherTeamId));

            Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, "decomm-reassign");
            await Fx.DeleteTeamsForTenantAsync(tenant.Value);
        }
    }

    [TestMethod]
    public async Task Assigning_to_unknown_team_returns_422_invalid_team()
    {
        // App has no team yet (so the resource policy passes for OrgAdmin trivially),
        // and the target team uuid is one we never created — the cross-module existence
        // check must surface a 422 with type=invalid-team.
        var appId = await Fx.SeedSingleApplicationAsync(Tenant, Guid.NewGuid(), teamId: null);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var unknownTeamId = Guid.NewGuid();
            var resp = await client.PutAsJsonAsync(
                $"/api/v1/catalog/applications/{appId}/team",
                new AssignTeamRequest(unknownTeamId));

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
            var body = await resp.Content.ReadAsStringAsync();
            StringAssert.Contains(body, ProblemTypes.InvalidTeam);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(Tenant, "assign-app");
        }
    }

    [TestMethod]
    public async Task PUT_with_other_tenant_team_id_returns_422_invalid_team()
    {
        // Slice-boundary review fix #4: cross-tenant safety — assigning tenant-A's
        // app to a team that exists only in tenant-B must surface the same
        // 422 invalid-team envelope as an unknown team uuid. RLS hides team-B from
        // tenant-A's lookup, so the cross-module existence check
        // (IOrganizationTeamExistenceChecker) sees no row and returns false.
        var tenantA = new TenantId(Guid.Parse("aaaaaaaa-0019-0019-0019-000000000001"));
        var tenantB = new TenantId(Guid.Parse("aaaaaaaa-0019-0019-0019-000000000002"));

        var teamInB = await Fx.SeedTeamInOrganizationAsync(tenantB, "Team in OrgB");
        var appInA  = await Fx.SeedSingleApplicationAsync(tenantA, Guid.NewGuid(), teamId: null,
            namePrefix: "xtenant-app");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(tenantA, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/catalog/applications/{appInA}/team",
                new AssignTeamRequest(teamInB));

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
            var body = await resp.Content.ReadAsStringAsync();
            StringAssert.Contains(body, ProblemTypes.InvalidTeam);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantA, "xtenant-app");
            await Fx.DeleteTeamsForTenantAsync(tenantB.Value);
        }
    }

    [TestMethod]
    public async Task Member_not_in_apps_current_team_returns_403()
    {
        // App is assigned to teamA; caller is a Member of NO team. The resource
        // policy (ApplicationTeamScoped) requires the caller to be a member of
        // the app's current team — they aren't, so the gate rejects with 403.
        var teamA = await Fx.SeedTeamInOrganizationAsync(Tenant, "TeamA");
        var appId = await Fx.SeedSingleApplicationAsync(Tenant, Guid.NewGuid(), teamId: teamA);
        try
        {
            var callerId = Guid.NewGuid();
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.Member },
                subject: callerId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/catalog/applications/{appId}/team",
                new AssignTeamRequest(teamA));

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(Tenant, "assign-app");
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Member_in_team_A_reassigning_app_to_team_B_they_do_not_belong_to_returns_403()
    {
        // Slice-8 boundary-review fix SF-2: caller is a Member of teamA only.
        // App is in teamA, so the source-team gate (ApplicationTeamScoped) passes.
        // Target is teamB — caller isn't in teamB, so the target-team check must
        // 403 before the handler reassigns and orphans the caller from the app.
        var tenant = new TenantId(Guid.Parse("aaaaaaaa-0030-0030-0030-000000000001"));
        var teamA = await Fx.SeedTeamInOrganizationAsync(tenant, "Team A");
        var teamB = await Fx.SeedTeamInOrganizationAsync(tenant, "Team B");

        var memberId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamA, memberId, MemberRole);

        var appId = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId: teamA,
            namePrefix: "sf2-cross");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                tenant,
                new[] { KartovaRoles.Member },
                subject: memberId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/catalog/applications/{appId}/team",
                new AssignTeamRequest(teamB));

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, "sf2-cross");
            await Fx.DeleteTeamsForTenantAsync(tenant.Value);
        }
    }

    [TestMethod]
    public async Task Member_in_both_teams_reassigning_app_to_team_they_belong_to_succeeds()
    {
        // Control case for SF-2: caller is a Member of teamA1 AND teamA2.
        // App is in teamA1. Reassign to teamA2 — both source and target gates
        // pass and the handler runs. Verifies the SF-2 check doesn't over-block.
        var tenant = new TenantId(Guid.Parse("aaaaaaaa-0030-0030-0030-000000000002"));
        var teamA1 = await Fx.SeedTeamInOrganizationAsync(tenant, "Team A1");
        var teamA2 = await Fx.SeedTeamInOrganizationAsync(tenant, "Team A2");

        var memberId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamA1, memberId, MemberRole);
        await Fx.SeedTeamMembershipAsync(teamA2, memberId, MemberRole);

        var appId = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId: teamA1,
            namePrefix: "sf2-ok");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                tenant,
                new[] { KartovaRoles.Member },
                subject: memberId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/catalog/applications/{appId}/team",
                new AssignTeamRequest(teamA2));

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, "sf2-ok");
            await Fx.DeleteTeamsForTenantAsync(tenant.Value);
        }
    }

}
