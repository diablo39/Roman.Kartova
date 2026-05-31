using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>POST /api/v1/organizations/teams/{id}/members</c>
/// (slice 8, spec §10 / critic-revision §7 — 201 + body, not 204).
/// </summary>
[TestClass]
public sealed class AddTeamMemberTests : OrganizationIntegrationTestBase
{
    private const byte AdminRole = 2;
    private const byte MemberRole = 1;

    private static readonly TenantId Tenant =
        new(Guid.Parse("aaaaaaaa-0006-0006-0006-000000000006"));

    [TestMethod]
    public async Task OrgAdmin_adds_member_returns_201_with_body()
    {
        // Slice 9 / E3 (ADR-0098): the 201 body now includes DisplayName + Email
        // pulled from IUserDirectory. Seed a matching users row BEFORE the POST
        // so the enrichment path is exercised (not just the fallback).
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Add");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        var unique = Guid.NewGuid().ToString("N")[..8];
        var newUserId = await Fx.SeedUserInOrganizationAsync(
            Tenant,
            displayName: "Adam Admin",
            email: $"adam-{unique}@example.com");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}/members",
                new AddTeamMemberRequest(newUserId, "Admin"));

            Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<TeamMemberResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.AreEqual(newUserId, body!.UserId);
            Assert.AreEqual("Admin", body.Role);
            Assert.AreEqual("Adam Admin", body.DisplayName);
            Assert.AreEqual($"adam-{unique}@example.com", body.Email);
        }
        finally
        {
            // Order: user-row delete first so the more leak-prone cleanup
            // (direct-id delete, no prefix sweep) runs even if the teams
            // cleanup throws — mirrors E1 e5aaf73 / E2 4715c87 convention.
            await Fx.DeleteUserInOrganizationAsync(newUserId);
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Add_member_with_no_users_row_returns_empty_display_info()
    {
        // Slice 9 / E3: when no users projection row matches the supplied UserId
        // (race against post-auth sync, or a manually-supplied id), DisplayName
        // + Email must surface as the "" fallback rather than throwing. The 201
        // shape is otherwise unchanged.
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Add-NoUser");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var orphanUserId = Guid.NewGuid();
            var resp = await client.PostAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}/members",
                new AddTeamMemberRequest(orphanUserId, "Member"));

            Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<TeamMemberResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.AreEqual(orphanUserId, body!.UserId);
            Assert.AreEqual("Member", body.Role);
            Assert.AreEqual("", body.DisplayName);
            Assert.AreEqual("", body.Email);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Duplicate_member_returns_409()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Add-Dup");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        var existingUser = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, existingUser, MemberRole);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}/members",
                new AddTeamMemberRequest(existingUser, "Member"));

            Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Plain_member_not_admin_of_this_team_returns_403()
    {
        // The user is only a regular Member of the team — they should NOT be
        // allowed to manage members. The team.members.manage permission belongs
        // to TeamAdmin + OrgAdmin only, so the claim gate blocks this Member.
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Add-403");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
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

            var resp = await client.PostAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}/members",
                new AddTeamMemberRequest(Guid.NewGuid(), "Member"));

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task TeamAdmin_of_other_team_returns_403()
    {
        // Caller is TeamAdmin of team B (passes claim gate — TeamAdmin has
        // team.members.manage) but is NOT an admin of team A. The resource gate
        // (TeamAdminOfThis) must short-circuit with 403 before any membership
        // mutation. Proves the resource gate is wired, not just the claim gate.
        // Addresses deep-review MT-2.
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Add-OtherTeam-403");
        var teamA = await Fx.SeedTeamAsync(Tenant.Value, "Team A");
        var teamB = await Fx.SeedTeamAsync(Tenant.Value, "Team B");
        var teamAdminUserId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamB, teamAdminUserId, AdminRole);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.TeamAdmin },
                subject: teamAdminUserId.ToString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync(
                $"/api/v1/organizations/teams/{teamA}/members",
                new AddTeamMemberRequest(Guid.NewGuid(), "Member"));

            Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Invalid_role_string_returns_400()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Add-400");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync(
                $"/api/v1/organizations/teams/{teamId}/members",
                new AddTeamMemberRequest(Guid.NewGuid(), "Banana"));

            Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }
}
