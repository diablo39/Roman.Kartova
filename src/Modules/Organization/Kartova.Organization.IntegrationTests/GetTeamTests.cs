using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>GET /api/v1/organizations/teams/{id}</c> (slice 8, spec §10).
/// Covers happy-path detail shape, cross-tenant 404 (RLS hides the row), and the
/// bootstrap path (spec Decision #13): a fresh tenant lists no teams and can
/// create + immediately fetch one.
/// </summary>
[TestClass]
public sealed class GetTeamTests : OrganizationIntegrationTestBase
{
    private static readonly TenantId Tenant =
        new(Guid.Parse("aaaaaaaa-0003-0003-0003-000000000003"));

    private static readonly TenantId OtherTenant =
        new(Guid.Parse("bbbbbbbb-0003-0003-0003-000000000003"));

    [TestMethod]
    public async Task Returns_team_detail_with_members_and_applicationIds()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Get");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform", "desc");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.GetAsync($"/api/v1/organizations/teams/{teamId}");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

            var body = await resp.Content.ReadFromJsonAsync<TeamDetailResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.AreEqual(teamId, body!.Id);
            Assert.AreEqual("Platform", body.DisplayName);
            Assert.AreEqual("desc", body.Description);
            Assert.IsNotNull(body.Members);
            Assert.AreEqual(0, body.Members.Count);
            Assert.IsNotNull(body.ApplicationIds);
            Assert.AreEqual(0, body.ApplicationIds.Count);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Cross_tenant_returns_404()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Get-Cross");
        await Fx.SeedOrganizationAsync(OtherTenant.Value, "OrgB-Get-Cross");
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Secret");
        try
        {
            var client = Fx.CreateClient();
            // Token scoped to OtherTenant; the team lives in Tenant. RLS filter
            // turns the lookup into a miss — must surface as 404, NOT 403.
            var token = Fx.Signer.IssueForTenant(OtherTenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.GetAsync($"/api/v1/organizations/teams/{teamId}");

            Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
            await Fx.DeleteTeamsForTenantAsync(OtherTenant.Value);
        }
    }

    [TestMethod]
    public async Task Bootstrap_create_then_get_returns_the_new_team()
    {
        // Spec Decision #13 — a fresh org has no teams; OrgAdmin creates the first one
        // and the same client can GET it back. Verifies the create → get round-trip.
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Bootstrap");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var createResp = await client.PostAsJsonAsync(
                "/api/v1/organizations/teams",
                new CreateTeamRequest("First Team", null));
            Assert.AreEqual(HttpStatusCode.Created, createResp.StatusCode);
            var created = await createResp.Content.ReadFromJsonAsync<TeamResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(created);

            var getResp = await client.GetAsync($"/api/v1/organizations/teams/{created!.Id}");
            Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);
            var detail = await getResp.Content.ReadFromJsonAsync<TeamDetailResponse>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(created.Id, detail!.Id);
            Assert.AreEqual("First Team", detail.DisplayName);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }
}
