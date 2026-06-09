using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>PUT /api/v1/organizations/teams/{id}</c> (slice 8, spec §10;
/// ADR-0101). Authorization is the inline resource gate (<c>TeamAdminOfThis</c>) alone —
/// the claim gate was removed: a realm-Member who is Admin of this team succeeds, the
/// same user is 403 on another team, and a plain Member (non-Admin) is 403 here.
/// </summary>
[TestClass]
public sealed class UpdateTeamTests : OrganizationIntegrationTestBase
{
    // TeamRole.Admin == 2.
    private const byte AdminRole = 2;
    private const byte MemberRole = 1;

    private static readonly TenantId Tenant =
        new(Guid.Parse("aaaaaaaa-0004-0004-0004-000000000004"));

    [TestMethod]
    public async Task OrgAdmin_updates_any_team_returns_200()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Update");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Old name", "old");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}",
                new UpdateTeamRequest("New name", "new"));

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<TeamResponse>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual("New name", body!.DisplayName);
            Assert.AreEqual("new", body.Description);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Member_who_is_team_admin_updates_returns_200()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Update");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "TeamX", null);
        var userId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, userId, AdminRole);
        try
        {
            var client = Fx.CreateClient();
            // Mint a token whose sub claim is the user's Guid — the middleware reads
            // it to populate TeamMemberships, which the resource policy then queries.
            // The realm role is Member; team-admin authority is the per-team Admin
            // membership alone (ADR-0101 — no claim gate).
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.Member },
                subject: userId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}",
                new UpdateTeamRequest("Renamed", null));

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Member_admin_of_other_team_returns_403()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Update");
        var teamA = await Fx.SeedTeamAsync(Tenant.Value, "TeamA", null);
        var teamB = await Fx.SeedTeamAsync(Tenant.Value, "TeamB", null);
        var userId = Guid.NewGuid();
        // The user is Admin of teamB only — attempting to mutate teamA must 403.
        await Fx.SeedTeamMembershipAsync(teamB, userId, AdminRole);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.Member },
                subject: userId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamA}",
                new UpdateTeamRequest("Hijacked", null));

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Plain_Member_of_team_returns_403()
    {
        // The resource gate (TeamAdminOfThis) denies: this user's membership role
        // is Member, not Admin. (Previously the claim gate blocked Member before
        // the resource gate; now the resource gate is the sole check — same 403
        // result.)
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Update");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "TeamX", null);
        var userId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, userId, MemberRole);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.Member },
                subject: userId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}",
                new UpdateTeamRequest("Renamed", null));

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Cross_tenant_member_returns_404()
    {
        // Fills the cross-tenant cell in the auth matrix. A caller from tenant A
        // hitting tenant-B's team URL: LoadAndAuthorizeTeam's RLS-scoped read finds
        // no row under tenant A's tenant_id, so the gate returns 404 before any
        // resource-gate 403 — preventing existence-leak of tenant B's teams.
        //
        // Post-ADR-0101 there is no claim gate in front: LoadAndAuthorizeTeamAsync
        // reads the team first (404 if not visible under the tenant) for every
        // authenticated caller, so the 404-before-403 ordering holds uniformly.
        var tenantA = new TenantId(Guid.Parse("aaaaaaaa-0005-0005-0005-000000000001"));
        var tenantB = new TenantId(Guid.Parse("aaaaaaaa-0005-0005-0005-000000000002"));
        await Fx.SeedOrganizationAsync(tenantA.Value, "OrgA-XT");
        await Fx.SeedOrganizationAsync(tenantB.Value, "OrgB-XT");
        var teamInB = await Fx.SeedTeamAsync(tenantB.Value, "TeamInB", null);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(tenantA, new[] { KartovaRoles.Member });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamInB}",
                new UpdateTeamRequest("Hijacked", null));

            // 404 (RLS-hidden) before 403 — info-leak protection.
            Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(tenantA.Value);
            await Fx.DeleteTeamsForTenantAsync(tenantB.Value);
        }
    }

    [TestMethod]
    public async Task No_token_returns_401_on_team_mutation()
    {
        // ADR-0101: the team-mutation routes dropped their permission-claim policy and rely on
        // bare RequireAuthorization() for the authenticated baseline. Lock that anonymous → 401
        // (a regression to AllowAnonymous / a dropped RequireAuthorization would otherwise slip through).
        var client = Fx.CreateClient();   // no Authorization header
        var resp = await client.PutAsJsonAsync(
            $"/api/v1/organizations/teams/{Guid.NewGuid()}",
            new UpdateTeamRequest("X", null));
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task Empty_displayName_returns_400()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Update");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "TeamX", null);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}",
                new UpdateTeamRequest("", null));

            Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }
}
