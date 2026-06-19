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
}
