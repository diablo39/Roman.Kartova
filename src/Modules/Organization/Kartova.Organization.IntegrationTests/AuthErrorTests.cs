using System.Net;
using System.Net.Http.Headers;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

[TestClass]
public class AuthErrorTests : OrganizationIntegrationTestBase
{
    [TestMethod]
    public async Task No_token_returns_401()
    {
        var client = Fx.CreateClient();
        var resp = await client.GetAsync("/api/v1/organizations/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task Expired_token_returns_401()
    {
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Fx.Signer.IssueExpired(SeededOrgs.OrgA));
        var resp = await client.GetAsync("/api/v1/organizations/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task Platform_admin_without_tenant_hits_missing_tenant_on_tenant_scoped_route()
    {
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Fx.Signer.IssueForPlatformAdmin());
        var resp = await client.GetAsync("/api/v1/organizations/me");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "missing-tenant-claim");
    }

    [TestMethod]
    public async Task Non_org_admin_gets_403_on_admin_only_endpoint()
    {
        await Fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "Member" }));
        var resp = await client.GetAsync("/api/v1/organizations/me/admin-only");
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
