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

    [TestMethod]
    public async Task DisplayNameContains_filters_case_insensitively()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Filter");
        await Fx.SeedTeamAsync(Tenant.Value, "Capacity");  // contains "pa" at index 2
        await Fx.SeedTeamAsync(Tenant.Value, "Payments");  // contains "Pa" at index 0
        await Fx.SeedTeamAsync(Tenant.Value, "Data");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            var resp = await client.GetAsync("/api/v1/organizations/teams?displayNameContains=pa");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<TeamResponse>>(KartovaApiFixtureBase.WireJson);
            CollectionAssert.AreEquivalent(
                new[] { "Payments", "Capacity" },
                page!.Items.Select(t => t.DisplayName).ToArray());
        }
        finally { await Fx.DeleteTeamsForTenantAsync(Tenant.Value); }
    }

    [TestMethod]
    public async Task Blank_displayNameContains_returns_all_teams()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Blank");
        await Fx.SeedTeamAsync(Tenant.Value, "Alpha");
        await Fx.SeedTeamAsync(Tenant.Value, "Beta");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            var resp = await client.GetAsync("/api/v1/organizations/teams?displayNameContains=%20");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<TeamResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(2, page!.Items.Count);
        }
        finally { await Fx.DeleteTeamsForTenantAsync(Tenant.Value); }
    }

    [TestMethod]
    public async Task DisplayNameContains_escapes_like_wildcards()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Escape");
        await Fx.SeedTeamAsync(Tenant.Value, "Alpha");
        await Fx.SeedTeamAsync(Tenant.Value, "Beta");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            // '%' must be treated literally — no team contains it, so zero rows (not all).
            var resp = await client.GetAsync("/api/v1/organizations/teams?displayNameContains=%25");
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<TeamResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(0, page!.Items.Count);
        }
        finally { await Fx.DeleteTeamsForTenantAsync(Tenant.Value); }
    }

    [TestMethod]
    public async Task Changing_filter_mid_pagination_returns_400_cursor_filter_mismatch()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Mismatch");
        await Fx.SeedTeamAsync(Tenant.Value, "Apple");
        await Fx.SeedTeamAsync(Tenant.Value, "Apricot");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            var first = await client.GetFromJsonAsync<CursorPage<TeamResponse>>(
                "/api/v1/organizations/teams?displayNameContains=ap&limit=1", KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(first!.NextCursor);

            // Reuse the cursor under a DIFFERENT filter → CursorFilterMismatchException → 400.
            var resp = await client.GetAsync(
                $"/api/v1/organizations/teams?displayNameContains=zz&cursor={Uri.EscapeDataString(first.NextCursor!)}");
            Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        }
        finally { await Fx.DeleteTeamsForTenantAsync(Tenant.Value); }
    }

    [TestMethod]
    public async Task DisplayNameContains_does_not_leak_cross_tenant()
    {
        var other = new TenantId(Guid.Parse("aaaaaaaa-0002-0002-0002-000000000099"));
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-RLS");
        await Fx.SeedOrganizationAsync(other.Value, "OrgB-RLS");
        await Fx.SeedTeamAsync(Tenant.Value, "AlphaMine");
        await Fx.SeedTeamAsync(other.Value, "AlphaTheirs");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            var page = await client.GetFromJsonAsync<CursorPage<TeamResponse>>(
                "/api/v1/organizations/teams?displayNameContains=Alpha", KartovaApiFixtureBase.WireJson);
            CollectionAssert.AreEquivalent(new[] { "AlphaMine" }, page!.Items.Select(t => t.DisplayName).ToArray());
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
            await Fx.DeleteTeamsForTenantAsync(other.Value);
        }
    }

    [TestMethod]
    public async Task Default_sort_is_displayName_ascending()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Sort");
        await Fx.SeedTeamAsync(Tenant.Value, "Zeta");
        await Fx.SeedTeamAsync(Tenant.Value, "Alpha");
        await Fx.SeedTeamAsync(Tenant.Value, "Mu");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));

            var page = await client.GetFromJsonAsync<CursorPage<TeamResponse>>(
                "/api/v1/organizations/teams", KartovaApiFixtureBase.WireJson);
            CollectionAssert.AreEqual(
                new[] { "Alpha", "Mu", "Zeta" },
                page!.Items.Select(t => t.DisplayName).ToArray());
        }
        finally { await Fx.DeleteTeamsForTenantAsync(Tenant.Value); }
    }
}
