using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>GET /api/v1/organizations/teams</c> (slice 8, spec §10).
/// Read endpoint — any role carrying <c>team.read</c> (Viewer, Member, TeamAdmin, OrgAdmin)
/// must see the paginated envelope.
/// </summary>
[TestClass]
public sealed class ListTeamsTests : OrganizationIntegrationTestBase
{
    private static readonly TenantId Tenant =
        new(Guid.Parse("aaaaaaaa-0002-0002-0002-000000000002"));

    [TestMethod]
    [DataRow(KartovaRoles.Viewer)]
    [DataRow(KartovaRoles.Member)]
    [DataRow(KartovaRoles.OrgAdmin)]
    public async Task Role_with_team_read_returns_200_paginated(string role)
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-List");
        await Fx.SeedTeamAsync(Tenant.Value, "Team 1");
        await Fx.SeedTeamAsync(Tenant.Value, "Team 2");
        await Fx.SeedTeamAsync(Tenant.Value, "Team 3");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { role });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.GetAsync("/api/v1/organizations/teams");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

            var page = await resp.Content.ReadFromJsonAsync<CursorPage<TeamResponse>>(
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page);
            Assert.AreEqual(3, page!.Items.Count);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Anonymous_returns_401()
    {
        var client = Fx.CreateClient();

        var resp = await client.GetAsync("/api/v1/organizations/teams");

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
