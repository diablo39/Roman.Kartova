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
    public async Task Platform_admin_without_tenant_gets_403_on_tenant_scoped_route()
    {
        // Pipeline order: UseAuthentication → UseAuthorization → TenantScopeBeginMiddleware.
        // The tenant-scoped route requires the OrgProfileRead permission. PlatformAdmin
        // resolves to an empty permission set via KartovaRolePermissions.ForRole (the role
        // is deliberately absent from KartovaRolePermissions.Map — slice-9 Phase D
        // reconciliation #4), so UseAuthorization rejects with 403 BEFORE the
        // TenantScopeBeginMiddleware's 401 missing-tenant-claim path can fire.
        //
        // Slice-2 originally asserted 401 + "missing-tenant-claim" body. That was
        // pre-Phase D — once C/D wired role-based permission gates on every tenant-scoped
        // route, the permission check now wins the race. The 403 is the correct
        // current behavior: PlatformAdmin without a tenant context is genuinely missing
        // a permission for THIS tenant-scoped route.
        //
        // If the diagnostic 401 missing-tenant-claim contract becomes important again
        // (e.g., a platform-admin SDK that wants to discriminate "select a tenant first"
        // from generic permission failures), the fix is to insert a
        // TenantClaimRequiredMiddleware between UseAuthentication and UseAuthorization
        // that short-circuits with 401 for endpoints carrying RequireTenantScopeMarker.
        // Tracked in slice-9 Phase H7.
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Fx.Signer.IssueForPlatformAdmin());
        var resp = await client.GetAsync("/api/v1/organizations/me");
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
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
