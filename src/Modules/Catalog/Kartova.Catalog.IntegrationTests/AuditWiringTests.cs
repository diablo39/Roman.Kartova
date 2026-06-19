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
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Audit Reg Team");
        var client = await Fx.CreateAuthenticatedClientAsync(
            OrgAUser, new[] { KartovaRoles.OrgAdmin }, nameClaim: "Ada Catalog");

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
        Assert.AreEqual(CatalogAuditTargetTypes.Application, row.TargetType);
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Audit Reg App", data.RootElement.GetProperty("displayName").GetString());
        Assert.AreEqual(teamId.ToString(), data.RootElement.GetProperty("teamId").GetString());
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

    // --- Happy: deprecate writes a lifecycle_changed row with from/to ---
    [TestMethod]
    public async Task Deprecate_WritesLifecycleChangedAuditRow()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Audit Dep Team");
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser, new[] { KartovaRoles.OrgAdmin });
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
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Active", data.RootElement.GetProperty("from").GetString());
        Assert.AreEqual("Deprecated", data.RootElement.GetProperty("to").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(data.RootElement.GetProperty("sunsetDate").GetString()));
    }

    // --- Negative: a rejected transition writes no row ---
    [TestMethod]
    public async Task Decommission_BeforeSunset_WritesNoAuditRow()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Audit NoRow Team");
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser, new[] { KartovaRoles.OrgAdmin });
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
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Audit Edit Team");
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser, new[] { KartovaRoles.OrgAdmin });
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
}
