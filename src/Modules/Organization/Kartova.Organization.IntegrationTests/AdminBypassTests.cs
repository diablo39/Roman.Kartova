using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.Testing.Auth;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

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
    public async Task Non_platform_admin_cannot_post_admin_organizations()
    {
        var client = Fx.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", Fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" }));

        var resp = await client.PostAsJsonAsync("/api/v1/admin/organizations", new { name = "Denied" });
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
