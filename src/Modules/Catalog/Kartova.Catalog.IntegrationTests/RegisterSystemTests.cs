using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>Real-seam integration tests for <c>POST/GET /systems</c> (E-03.F-03.S-01, Task 12).
/// Mirrors <see cref="RegisterApiTests"/> — real Postgres/RLS Testcontainer + real JWT.</summary>
[TestClass]
public class RegisterSystemTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static object Body(Guid teamId, string name = "payments-platform") => new
    {
        displayName = name,
        description = "Payments Platform grouping.",
        teamId,
    };

    [TestMethod]
    public async Task POST_with_valid_payload_returns_201_and_roundtrips_and_writes_audit_row()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "System Team");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems", Body(teamId));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode,
            $"Expected 201. Body: {await resp.Content.ReadAsStringAsync()}");
        Assert.IsNotNull(resp.Headers.Location);
        StringAssert.StartsWith(resp.Headers.Location!.ToString(), "/api/v1/catalog/systems/");
        var body = await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(body);
        Assert.AreEqual("payments-platform", body.DisplayName);
        Assert.AreEqual("Payments Platform grouping.", body.Description);
        Assert.AreEqual(teamId, body.TeamId);
        Assert.AreEqual(tenantId.Value, body.TenantId);
        Assert.IsTrue(body.CreatedAt > DateTimeOffset.MinValue);

        // Round-trips through GET-by-id: same entity, full field set.
        var get = await client.GetAsync($"/api/v1/catalog/systems/{body.Id}");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        var getBody = await get.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(body.Id, getBody!.Id);
        Assert.AreEqual(body.DisplayName, getBody.DisplayName);
        Assert.AreEqual(body.Description, getBody.Description);
        Assert.AreEqual(body.TeamId, getBody.TeamId);
        Assert.AreEqual(body.CreatedByUserId, getBody.CreatedByUserId);

        // Audit: a system.registered row exists with the expected data shape.
        var rows = await Fx.ReadAuditLogAsync(tenantId.Value);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.SystemRegistered &&
            r.TargetId == body.Id.ToString());
        Assert.AreEqual(CatalogAuditTargetTypes.System, row.TargetType);
        Assert.AreEqual(await Fx.GetSubClaimAsync(OrgAUser), row.ActorId);
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("payments-platform", data.RootElement.GetProperty("displayName").GetString());
        Assert.AreEqual(teamId.ToString(), data.RootElement.GetProperty("teamId").GetString());
    }

    [TestMethod]
    public async Task POST_allows_null_description()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "System Team Null Desc");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems",
            new { displayName = "no-desc-system", description = (string?)null, teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNull(body!.Description);
    }

    [TestMethod]
    public async Task POST_without_token_returns_401()
    {
        using var anon = Fx.CreateAnonymousClient();
        var resp = await anon.PostAsJsonAsync("/api/v1/catalog/systems", Body(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_empty_display_name_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "System Team 400");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems",
            new { displayName = "", description = "d", teamId });
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_unknown_team_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems", Body(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_cross_tenant_team_returns_422()
    {
        // A team that exists but belongs to a different tenant must resolve as "not found"
        // under the RLS-scoped team-existence checker — same 422 branch as unknown team.
        var otherTenant = Fx.TenantIdForEmail("admin@orgb.kartova.local");
        var crossTeam = await Fx.SeedTeamInOrganizationAsync(otherTenant, "Cross Tenant Team");

        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems", Body(crossTeam));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_by_member_not_in_target_team_returns_403()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "System Team Restricted");
        var memberClient = await Fx.CreateAuthenticatedClientAsync(
            "member@orga.kartova.local", new[] { KartovaRoles.Member });

        var resp = await memberClient.PostAsJsonAsync("/api/v1/catalog/systems", Body(teamId));
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_sets_CreatedByUserId_to_caller_sub()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "System Team Identity");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems", Body(teamId));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson);
        var expectedSub = await Fx.GetSubClaimAsync(OrgAUser);
        Assert.AreEqual(expectedSub, body!.CreatedByUserId);
    }
}
