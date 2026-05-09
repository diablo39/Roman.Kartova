using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

[TestClass]
public class OrganizationEndpointHappyPathTests : OrganizationIntegrationTestBase
{
    [TestMethod]
    public async Task Get_me_returns_current_tenant_row()
    {
        await Fx.SeedOrganizationAsync(SeededOrgs.OrgA.Value, "Org A");
        await Fx.SeedOrganizationAsync(SeededOrgs.OrgB.Value, "Org B");

        var client = Fx.CreateClient();
        var token = Fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/organizations/me");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var dto = await response.Content.ReadFromJsonAsync<OrganizationDto>();
        Assert.IsNotNull(dto);
        Assert.AreEqual(SeededOrgs.OrgA.Value, dto!.Id);
        Assert.AreEqual("Org A", dto.Name);
    }
}
