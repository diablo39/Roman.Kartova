using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

[TestClass]
public class AdminBypassTests : OrganizationIntegrationTestBase
{
    [TestMethod]
    public async Task Platform_admin_can_create_organization_without_tenant_scope()
    {
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Fx.Signer.IssueForPlatformAdmin());

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = "Newly created" });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.AreEqual("Newly created", dto!.Name);
        Assert.AreEqual(dto.TenantId, dto.Id);
    }

    [TestMethod]
    public async Task Tenant_realm_OrgAdmin_token_is_rejected_by_scheme_on_admin_routes()
    {
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { KartovaRoles.OrgAdmin }));

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = "Denied" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        StringAssert.Contains(resp.Headers.WwwAuthenticate.ToString(), "invalid_token",
            "401 must come from token rejection by the PlatformAdmin scheme, not from a missing token.");
    }

    [TestMethod]
    public async Task Tenant_realm_token_claiming_platform_admin_is_rejected_on_admin_routes()
    {
        // ADR-0118 key regression: the role claim alone must never open the operator surface.
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { KartovaRoles.PlatformAdmin }));

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = "Denied" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
        StringAssert.Contains(resp.Headers.WwwAuthenticate.ToString(), "invalid_token");
    }

    [TestMethod]
    public async Task Platform_realm_token_without_platform_admin_role_gets_403()
    {
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Fx.Signer.IssueForPlatformRealm(Array.Empty<string>()));

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = "Denied" });

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
