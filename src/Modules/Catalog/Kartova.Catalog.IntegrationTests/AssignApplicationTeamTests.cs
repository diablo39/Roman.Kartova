using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
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
    public async Task OrgAdmin_assigns_app_to_team_returns_200()
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
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(Tenant, "assign-app");
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
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
}
