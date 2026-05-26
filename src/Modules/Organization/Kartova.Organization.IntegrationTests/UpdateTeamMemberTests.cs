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
