using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Real-seam (gate-3) integration test for Task 3 (Infrastructure/VM slice 2a — fields +
/// mutation): proves <c>provider</c> round-trips from <c>POST /infrastructure/vms</c> through
/// <c>GET /infrastructure/vms/{id}</c> via <see cref="KartovaApiFixtureBase"/> (real
/// Postgres/RLS + real JWT).
/// </summary>
[TestClass]
public sealed class InfrastructureVmWriteTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static RegisterVmRequest ValidVm(Guid teamId, string displayName, string? provider = "AWS") => new(
        DisplayName: displayName,
        Description: "integration",
        TeamId: teamId,
        Provider: provider,
        Attributes: new VmAttributesDto("running", "ubuntu-22.04", 4, 16, "host-1",
            new[] { "10.0.0.1" }, "eu-west-1"));

    [TestMethod]
    public async Task Post_ThenGet_ReturnsProvider()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team Provider");
        var unique = $"vm-provider-{Guid.NewGuid():N}";

        var post = await client.PostAsJsonAsync(
            "/api/v1/catalog/infrastructure/vms", ValidVm(teamId, unique), KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(HttpStatusCode.Created, post.StatusCode, $"RegisterVm failed: {await post.Content.ReadAsStringAsync()}");
        var created = await post.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("AWS", created!.Provider);

        var get = await client.GetAsync($"/api/v1/catalog/infrastructure/vms/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        var fetched = await get.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("AWS", fetched!.Provider);
    }
}
