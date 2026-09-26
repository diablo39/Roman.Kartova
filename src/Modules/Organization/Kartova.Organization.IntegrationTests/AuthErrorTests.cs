using System.Net;
using System.Net.Http.Headers;
using Kartova.SharedKernel.Multitenancy;
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
    public async Task Platform_realm_token_is_unauthenticated_on_tenant_scoped_route()
    {
        // ADR-0118: tenant routes authenticate only with the default (tenant-realm) scheme, so an
        // operator token is rejected at authentication (401) — it never reaches the permission
        // check that previously produced 403.
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Fx.Signer.IssueForPlatformAdmin());

        var resp = await client.GetAsync("/api/v1/organizations/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        StringAssert.Contains(resp.Headers.WwwAuthenticate.ToString(), "invalid_token");
    }

    [TestMethod]
    public async Task Non_org_admin_gets_403_on_admin_only_endpoint()
    {
        await Fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { KartovaRoles.Member }));
        var resp = await client.GetAsync("/api/v1/organizations/me/admin-only");
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
