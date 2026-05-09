using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.Testing.Auth;
using Npgsql;

namespace Kartova.Organization.IntegrationTests;

[TestClass]
public class TenantIsolationTests : OrganizationIntegrationTestBase
{
    [TestMethod]
    public async Task Each_tenant_only_sees_its_own_organization()
    {
        await Fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        await Fx.SeedOrganizationAsync(SeededOrgs.OrgB.Value, "Org B");

        var client = Fx.CreateClient();

        var tokenA = Fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        var respA = await client.GetAsync("/api/v1/organizations/me");
        Assert.AreEqual(HttpStatusCode.OK, respA.StatusCode);
        Assert.AreEqual("Org A", (await respA.Content.ReadFromJsonAsync<OrganizationDto>())!.Name);

        var tokenB = Fx.Signer.IssueForTenant(SeededOrgs.OrgB, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        var respB = await client.GetAsync("/api/v1/organizations/me");
        Assert.AreEqual(HttpStatusCode.OK, respB.StatusCode);
        Assert.AreEqual("Org B", (await respB.Content.ReadFromJsonAsync<OrganizationDto>())!.Name);
    }

    [TestMethod]
    public async Task Raw_sql_as_bypass_role_sees_both_rows()
    {
        await Fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        await Fx.SeedOrganizationAsync(SeededOrgs.OrgB.Value, "Org B");

        await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM organizations";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.IsTrue(count >= 2);
    }
}
