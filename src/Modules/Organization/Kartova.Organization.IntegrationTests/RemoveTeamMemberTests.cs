using System.Net;
using System.Net.Http.Headers;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>DELETE /api/v1/organizations/teams/{id}/members/{userId}</c>
/// (slice 8, spec §10).
/// </summary>
[TestClass]
public sealed class RemoveTeamMemberTests : OrganizationIntegrationTestBase
{
    private const byte MemberRole = 1;
    private const byte AdminRole = 2;

    private static readonly TenantId Tenant =
        new(Guid.Parse("aaaaaaaa-0007-0007-0007-000000000007"));

    [TestMethod]
    public async Task OrgAdmin_removes_member_returns_204()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Remove");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        var memberId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, memberId, MemberRole);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.DeleteAsync(
                $"/api/v1/organizations/teams/{teamId}/members/{memberId}");

            Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Removing_nonexistent_member_returns_404()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Remove-404");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.DeleteAsync(
                $"/api/v1/organizations/teams/{teamId}/members/{Guid.NewGuid()}");

            Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Member_who_is_team_admin_of_other_team_removing_member_returns_403()
    {
        // Caller is a realm-Member who is Admin of team B but NOT of team A. The
        // resource gate (TeamAdminOfThis) is now the sole authorization (ADR-0101 —
        // the claim gate is gone); it must short-circuit with 403 before the
        // membership row is touched. We seed a real member in team A so the path
        // exercised is the resource gate, not "member doesn't exist". Addresses
        // deep-review MT-2.
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Remove-OtherTeam-403");
        var teamA = await Fx.SeedTeamAsync(Tenant.Value, "Team A");
        var teamB = await Fx.SeedTeamAsync(Tenant.Value, "Team B");
        var memberInA = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamA, memberInA, MemberRole);
        var teamAdminUserId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamB, teamAdminUserId, AdminRole);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.Member },
                subject: teamAdminUserId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.DeleteAsync(
                $"/api/v1/organizations/teams/{teamA}/members/{memberInA}");

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Plain_member_not_admin_returns_403()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Remove-403");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        var memberId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, memberId, MemberRole);
        try
        {
            var client = Fx.CreateClient();
            // Caller is a plain Member of this team — the resource gate
            // (TeamAdminOfThis) denies because their membership role is Member,
            // not Admin (ADR-0101 — the resource gate is now the sole check).
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.Member },
                subject: memberId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.DeleteAsync(
                $"/api/v1/organizations/teams/{teamId}/members/{memberId}");

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Member_who_is_team_admin_of_this_team_removes_member_returns_204()
    {
        // The footgun fix (ADR-0101, spec §8): a realm-Member who holds an Admin
        // membership on THIS team can remove members. Pre-change this returned 403
        // (Member lacked the team.members.manage claim); the resource gate
        // (TeamAdminOfThis) is now the sole authorization and recognises the
        // per-team Admin membership.
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Remove-MemberAdmin");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        var actingUserId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, actingUserId, AdminRole);
        var memberId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, memberId, MemberRole);
        try
        {
            var client = Fx.CreateClient();
            // The subject claim must match the seeded Admin membership — the
            // middleware reads it to populate TeamMemberships, which the resource
            // policy then queries. The realm role is Member, not TeamAdmin.
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.Member },
                subject: actingUserId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.DeleteAsync(
                $"/api/v1/organizations/teams/{teamId}/members/{memberId}");

            Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }
}
