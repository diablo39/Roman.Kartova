using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class AuditWiringTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    // --- Happy: register writes a correct, chained audit row ---
    [TestMethod]
    public async Task Register_WritesApplicationRegisteredAuditRow()
    {
        var (tenantId, teamId, client) = await ArrangeAsync("Audit Reg Team", nameClaim: "Ada Catalog");

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Audit Reg App", "Desc.", teamId));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode,
            $"Expected 201. Body: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.ApplicationRegistered &&
            r.TargetId == body!.Id.ToString());
        Assert.AreEqual(await Fx.GetSubClaimAsync(OrgAUser), row.ActorId);
        Assert.AreEqual("Ada Catalog", row.ActorDisplay);
        Assert.AreEqual("User", row.ActorType);
        Assert.AreEqual(CatalogAuditTargetTypes.Application, row.TargetType);
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Audit Reg App", data.RootElement.GetProperty("displayName").GetString());
        Assert.AreEqual(teamId.ToString(), data.RootElement.GetProperty("teamId").GetString());

        // Catalog rows interleave into the same per-tenant hash chain as the
        // Organization rows — verify the whole tenant chain stays contiguous and
        // linked (seq 1..n, each prev_hash == predecessor row_hash). Design §7.
        AssertChainLinked(rows);
    }

    private static void AssertChainLinked(IReadOnlyList<KartovaApiFixture.AuditRowRecord> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            Assert.AreEqual(i + 1, rows[i].Seq, "seq must be contiguous from 1");
            var expectedPrev = i == 0 ? new byte[32] : rows[i - 1].RowHash;
            CollectionAssert.AreEqual(expectedPrev, rows[i].PrevHash,
                "prev_hash must link to predecessor row_hash");
        }
    }

    private async Task<ApplicationResponse> RegisterAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, "Desc.", teamId));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode,
            $"Expected 201. Body: {await resp.Content.ReadAsStringAsync()}");
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    /// <summary>
    /// Shared arrange step: seeds an owning team for the OrgA tenant and returns
    /// (tenantId, teamId, authenticated OrgAdmin client). Pass <paramref name="nameClaim"/>
    /// when the test asserts <c>actor_display</c>.
    /// </summary>
    private async Task<(Guid TenantId, Guid TeamId, HttpClient Client)> ArrangeAsync(
        string teamName, string? nameClaim = null)
    {
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, teamName);
        var client = await Fx.CreateAuthenticatedClientAsync(
            OrgAUser, new[] { KartovaRoles.OrgAdmin }, nameClaim: nameClaim);
        return (tenant.Value, teamId, client);
    }

    // --- Happy: deprecate writes a lifecycle_changed row with from/to ---
    [TestMethod]
    public async Task Deprecate_WritesLifecycleChangedAuditRow()
    {
        var (tenantId, teamId, client) = await ArrangeAsync("Audit Dep Team");
        var app = await RegisterAsync(client, teamId, "Audit Dep App");

        var sunset = DateTimeOffset.UtcNow.AddDays(30);
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{app.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
            $"Expected 200. Body: {await resp.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.ApplicationLifecycleChanged &&
            r.TargetId == app.Id.ToString());
        Assert.AreEqual(await Fx.GetSubClaimAsync(OrgAUser), row.ActorId);
        Assert.AreEqual("User", row.ActorType);
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Active", data.RootElement.GetProperty("from").GetString());
        Assert.AreEqual("Deprecated", data.RootElement.GetProperty("to").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(data.RootElement.GetProperty("sunsetDate").GetString()));
    }

    // --- Negative: a rejected transition writes no row ---
    [TestMethod]
    public async Task Decommission_BeforeSunset_WritesNoAuditRow()
    {
        var (tenantId, teamId, client) = await ArrangeAsync("Audit NoRow Team");
        var app = await RegisterAsync(client, teamId, "Audit NoRow App");

        // Deprecate with a far-future sunset, then attempt to decommission before it -> 409.
        var depResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{app.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.AreEqual(HttpStatusCode.OK, depResp.StatusCode);

        var decResp = await client.PostAsync(
            $"/api/v1/catalog/applications/{app.Id}/decommission", content: null);
        Assert.AreEqual(HttpStatusCode.Conflict, decResp.StatusCode,
            $"Expected 409. Body: {await decResp.Content.ReadAsStringAsync()}");

        // Exactly one lifecycle_changed row (the deprecate), none for the rejected decommission.
        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var lifecycleRows = rows.Where(r =>
            r.Action == CatalogAuditActions.ApplicationLifecycleChanged &&
            r.TargetId == app.Id.ToString()).ToList();
        Assert.AreEqual(1, lifecycleRows.Count, "rejected decommission must not write an audit row");
        using var data = JsonDocument.Parse(lifecycleRows[0].DataJson!);
        Assert.AreEqual("Deprecated", data.RootElement.GetProperty("to").GetString());
    }

    // --- Happy: edit writes an application.edited row with the new state ---
    [TestMethod]
    public async Task Edit_WritesApplicationEditedAuditRow()
    {
        var (tenantId, teamId, client) = await ArrangeAsync("Audit Edit Team");
        var app = await RegisterAsync(client, teamId, "Audit Edit App");

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/applications/{app.Id}")
        {
            Content = JsonContent.Create(new EditApplicationRequest("Edited Name", "Edited desc.")),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{app.Version}\"");
        var resp = await client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
            $"Expected 200. Body: {await resp.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.ApplicationEdited &&
            r.TargetId == app.Id.ToString());
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Edited Name", data.RootElement.GetProperty("displayName").GetString());
        Assert.AreEqual("Edited desc.", data.RootElement.GetProperty("description").GetString());
    }

    // --- Happy: decommission writes a lifecycle_changed row from=Deprecated, to=Decommissioned ---
    [TestMethod]
    public async Task Decommission_WritesLifecycleChangedAuditRow()
    {
        var (tenantId, teamId, client) = await ArrangeAsync("Audit Dec Team");
        var app = await RegisterAsync(client, teamId, "Audit Dec App");

        // Deprecate with a near-future sunset, wait for it to expire, then decommission.
        // Uses the same Task.Delay(2000) pattern as DecommissionApplicationTests.
        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var depResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{app.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.AreEqual(HttpStatusCode.OK, depResp.StatusCode,
            $"Expected 200. Body: {await depResp.Content.ReadAsStringAsync()}");

        await Task.Delay(2000);

        var decResp = await client.PostAsync(
            $"/api/v1/catalog/applications/{app.Id}/decommission",
            content: null);
        Assert.AreEqual(HttpStatusCode.OK, decResp.StatusCode,
            $"Expected 200. Body: {await decResp.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        // Two lifecycle rows exist (Deprecate + Decommission) — pick the one transitioning to Decommissioned.
        var lifecycleRows = rows.Where(r =>
            r.Action == CatalogAuditActions.ApplicationLifecycleChanged &&
            r.TargetId == app.Id.ToString()).ToList();
        var row = lifecycleRows.Single(r =>
        {
            using var d = JsonDocument.Parse(r.DataJson!);
            return d.RootElement.GetProperty("to").GetString() == "Decommissioned";
        });
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Deprecated", data.RootElement.GetProperty("from").GetString());
        Assert.AreEqual("Decommissioned", data.RootElement.GetProperty("to").GetString());
    }

    // --- Happy: reactivate writes a lifecycle_changed row from=Deprecated, to=Active, sunsetDate absent/null ---
    [TestMethod]
    public async Task Reactivate_WritesLifecycleChangedAuditRow()
    {
        var (tenantId, teamId, client) = await ArrangeAsync("Audit Reac Team");
        var app = await RegisterAsync(client, teamId, "Audit Reac App");

        var depResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{app.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.AreEqual(HttpStatusCode.OK, depResp.StatusCode,
            $"Expected 200. Body: {await depResp.Content.ReadAsStringAsync()}");

        var reacResp = await client.PostAsync(
            $"/api/v1/catalog/applications/{app.Id}/reactivate",
            content: null);
        Assert.AreEqual(HttpStatusCode.OK, reacResp.StatusCode,
            $"Expected 200. Body: {await reacResp.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        // Two lifecycle rows (Deprecate + Reactivate) — pick the one transitioning to Active.
        var lifecycleRows = rows.Where(r =>
            r.Action == CatalogAuditActions.ApplicationLifecycleChanged &&
            r.TargetId == app.Id.ToString()).ToList();
        var row = lifecycleRows.Single(r =>
        {
            using var d = JsonDocument.Parse(r.DataJson!);
            return d.RootElement.GetProperty("to").GetString() == "Active";
        });
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Deprecated", data.RootElement.GetProperty("from").GetString());
        Assert.AreEqual("Active", data.RootElement.GetProperty("to").GetString());
        // Reactivate clears sunsetDate — the key should be absent or null.
        Assert.IsFalse(
            data.RootElement.TryGetProperty("sunsetDate", out var sunsetProp) &&
            sunsetProp.ValueKind != JsonValueKind.Null,
            "sunsetDate must be absent or null after Reactivate");
    }

    // --- Happy: un-decommission writes a lifecycle_changed row from=Decommissioned, to=Deprecated ---
    [TestMethod]
    public async Task UnDecommission_WritesLifecycleChangedAuditRow()
    {
        var (tenantId, teamId, client) = await ArrangeAsync("Audit UnDec Team");
        var app = await RegisterAsync(client, teamId, "Audit UnDec App");

        // Drive Active → Deprecated → Decommissioned (uses same Task.Delay(2000) pattern
        // as UnDecommissionApplicationTests.POST_un_decommission_from_Decommissioned_returns_200).
        var sunset = DateTimeOffset.UtcNow.AddSeconds(1);
        var depResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{app.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.AreEqual(HttpStatusCode.OK, depResp.StatusCode,
            $"Expected 200. Body: {await depResp.Content.ReadAsStringAsync()}");

        await Task.Delay(2000);

        var decResp = await client.PostAsync(
            $"/api/v1/catalog/applications/{app.Id}/decommission",
            content: null);
        Assert.AreEqual(HttpStatusCode.OK, decResp.StatusCode,
            $"Expected 200. Body: {await decResp.Content.ReadAsStringAsync()}");

        // Un-decommission back to Deprecated with a new future sunset.
        var newSunset = DateTimeOffset.UtcNow.AddDays(30);
        var unDecResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{app.Id}/un-decommission",
            new UnDecommissionApplicationRequest(newSunset));
        Assert.AreEqual(HttpStatusCode.OK, unDecResp.StatusCode,
            $"Expected 200. Body: {await unDecResp.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        // Three lifecycle rows (Deprecate + Decommission + UnDecommission) — pick the one to=Deprecated
        // that came from Decommissioned.
        var lifecycleRows = rows.Where(r =>
            r.Action == CatalogAuditActions.ApplicationLifecycleChanged &&
            r.TargetId == app.Id.ToString()).ToList();
        var row = lifecycleRows.Single(r =>
        {
            using var d = JsonDocument.Parse(r.DataJson!);
            return d.RootElement.GetProperty("from").GetString() == "Decommissioned";
        });
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Decommissioned", data.RootElement.GetProperty("from").GetString());
        Assert.AreEqual("Deprecated", data.RootElement.GetProperty("to").GetString());
    }

    // --- Happy: team assignment writes from/to team ids ---
    [TestMethod]
    public async Task AssignTeam_WritesTeamAssignedAuditRow()
    {
        var (tenantId, fromTeam, client) = await ArrangeAsync("Audit From Team");
        var toTeam = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Audit To Team");
        var app = await RegisterAsync(client, fromTeam, "Audit Assign App");

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/catalog/applications/{app.Id}/team",
            new AssignTeamRequest(toTeam));
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
            $"Expected 200. Body: {await resp.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.ApplicationTeamAssigned &&
            r.TargetId == app.Id.ToString());
        Assert.AreEqual(await Fx.GetSubClaimAsync(OrgAUser), row.ActorId);
        Assert.AreEqual("User", row.ActorType);
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual(fromTeam.ToString(), data.RootElement.GetProperty("fromTeamId").GetString());
        Assert.AreEqual(toTeam.ToString(), data.RootElement.GetProperty("toTeamId").GetString());
    }

    // --- Negative: team assignment with unknown target team writes no audit row ---
    [TestMethod]
    public async Task AssignTeam_InvalidTeam_WritesNoAuditRow()
    {
        var (tenantId, teamId, client) = await ArrangeAsync("Audit AssignNeg Team");
        var app = await RegisterAsync(client, teamId, "Audit AssignNeg App");

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/catalog/applications/{app.Id}/team",
            new AssignTeamRequest(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode,
            $"Expected 422. Body: {await resp.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        Assert.AreEqual(0, rows.Count(r =>
            r.Action == CatalogAuditActions.ApplicationTeamAssigned &&
            r.TargetId == app.Id.ToString()));
    }
}
