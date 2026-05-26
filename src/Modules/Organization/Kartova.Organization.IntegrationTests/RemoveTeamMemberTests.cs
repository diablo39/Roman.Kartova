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
    public async Task Plain_member_not_admin_returns_403()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Remove-403");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        var memberId = Guid.NewGuid();
        await Fx.SeedTeamMembershipAsync(teamId, memberId, MemberRole);
        try
        {
            var client = Fx.CreateClient();
            // Caller is a plain Member — claim gate (team.members.manage) blocks them.
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
}
