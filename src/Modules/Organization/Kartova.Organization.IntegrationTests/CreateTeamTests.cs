using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Integration tests for <c>POST /api/v1/organizations/teams</c> (slice 8, spec §10).
/// Each test scopes itself to a dedicated tenant id so the shared fixture's
/// <see cref="DoNotParallelizeAttribute"/> serialisation still leaves a clean
/// per-class footprint; cleanup runs through <see cref="KartovaApiFixture.DeleteTeamsForTenantAsync"/>.
/// </summary>
[TestClass]
public sealed class CreateTeamTests : OrganizationIntegrationTestBase
{
    private static readonly TenantId Tenant =
        new(Guid.Parse("aaaaaaaa-0001-0001-0001-000000000001"));

    [TestMethod]
    public async Task OrgAdmin_creates_team_returns_201_with_location_and_body()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Create");
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.PostAsJsonAsync(
                "/api/v1/organizations/teams",
                new CreateTeamRequest("Platform", "Owns infra"));

            Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
            Assert.IsNotNull(resp.Headers.Location);
            Assert.IsTrue(
                resp.Headers.Location!.OriginalString.StartsWith("/api/v1/organizations/teams/"),
                $"Location header must point at the new team. Actual: {resp.Headers.Location.OriginalString}");

            var body = await resp.Content.ReadFromJsonAsync<TeamResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.AreEqual("Platform", body!.DisplayName);
            Assert.AreEqual("Owns infra", body.Description);
            Assert.AreNotEqual(Guid.Empty, body.Id);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Member_without_team_create_permission_returns_403()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Create");

        var client = Fx.CreateClient();
        // Member's permission set excludes TeamCreate (only OrgAdmin grants it).
        var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync(
            "/api/v1/organizations/teams",
            new CreateTeamRequest("Platform", null));

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task Anonymous_returns_401()
    {
        var client = Fx.CreateClient();

        var resp = await client.PostAsJsonAsync(
            "/api/v1/organizations/teams",
            new CreateTeamRequest("Platform", null));

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public async Task Empty_displayName_returns_400(string blank)
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Create");

        var client = Fx.CreateClient();
        var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.PostAsJsonAsync(
            "/api/v1/organizations/teams",
            new CreateTeamRequest(blank, null));

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
