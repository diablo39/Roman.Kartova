using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>PUT /api/v1/organizations/teams/{id}/members/{userId}</c>
/// (slice 8, spec §10). Equivalent surface to <see cref="AddTeamMemberTests"/>
/// but changes role on an existing membership.
/// </summary>
[TestClass]
public sealed class UpdateTeamMemberTests : OrganizationIntegrationTestBase
{
    private const byte MemberRole = 1;
    private const byte AdminRole = 2;

    private static readonly TenantId Tenant =
        new(Guid.Parse("aaaaaaaa-0008-0008-0008-000000000008"));

    [TestMethod]
    public async Task OrgAdmin_changes_role_returns_204()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Change");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        var memberId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, memberId, MemberRole);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}/members/{memberId}",
                new UpdateTeamMemberRequest("Admin"));

            Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Updating_nonexistent_member_returns_404()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Change-404");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}/members/{Guid.NewGuid()}",
                new UpdateTeamMemberRequest("Admin"));

            Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Plain_member_not_admin_returns_403()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Change-403");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        var memberId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, memberId, MemberRole);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.Member },
                subject: memberId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}/members/{memberId}",
                new UpdateTeamMemberRequest("Admin"));

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Member_who_is_team_admin_of_other_team_updating_role_returns_403()
    {
        // Caller is a realm-Member who is Admin of team B but NOT of team A. The
        // resource gate (TeamAdminOfThis) is now the sole authorization (ADR-0101 —
        // the claim gate is gone); it must short-circuit with 403 before the role
        // change is attempted. We seed a real member in team A so the exercised
        // path is the resource gate, not "member doesn't exist". Addresses
        // deep-review MT-2.
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Change-OtherTeam-403");
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

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamA}/members/{memberInA}",
                new UpdateTeamMemberRequest("Admin"));

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Member_who_is_team_admin_of_this_team_updates_member_role_returns_204()
    {
        // The footgun fix (ADR-0101, spec §8): a realm-Member who holds an Admin
        // membership on THIS team can change a member's role. Pre-change this
        // returned 403 (Member lacked the team.members.manage claim); the resource
        // gate (TeamAdminOfThis) is now the sole authorization and recognises the
        // per-team Admin membership.
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Change-MemberAdmin");
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

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}/members/{memberId}",
                new UpdateTeamMemberRequest("Admin"));

            Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Invalid_role_string_returns_400()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Change-400");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        var memberId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, memberId, MemberRole);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PutAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}/members/{memberId}",
                new UpdateTeamMemberRequest("Banana"));

            Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }
}
