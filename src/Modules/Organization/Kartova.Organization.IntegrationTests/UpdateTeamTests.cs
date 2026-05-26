using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>PUT /api/v1/organizations/teams/{id}</c> (slice 8, spec §10).
/// Exercises both authorization gates: the claim gate
/// (<c>team.metadata.edit</c>, granted to TeamAdmin + OrgAdmin only — Member is 403)
/// and the resource gate (<c>TeamAdminOfThis</c> — TeamAdmin of team-A is 403 on team-B).
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
    public async Task TeamAdmin_of_this_team_updates_returns_200()
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
            var token = Fx.Signer.IssueForTenant(
                Tenant,
                new[] { KartovaRoles.TeamAdmin },
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
    public async Task TeamAdmin_of_other_team_returns_403()
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
                new[] { KartovaRoles.TeamAdmin },
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
        // The claim-gate (team.metadata.edit) blocks Member at the route policy —
        // they don't even reach the resource handler. Either way, the wire result
        // must be 403.
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
