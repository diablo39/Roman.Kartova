using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Real-seam (gate-3) integration test for Task 2 (catalog-vm-linking): proves
/// <see cref="Kartova.Catalog.Infrastructure.CatalogEntityLookup"/> resolves
/// <see cref="EntityKind.Infrastructure"/> targets, so a <see cref="RelationshipType.DeployedOn"/>
/// edge from a Service to a VM can be created. Before this task, <c>Find(EntityKind.Infrastructure, …)</c>
/// always returned null, so the target could never be resolved and the POST 422'd
/// (invalid target entity) regardless of whether the VM existed.
/// </summary>
[TestClass]
public sealed class InfrastructureRelationshipTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static Task<HttpResponseMessage> PostRelAsync(
        HttpClient client, EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid)
        => client.PostAsJsonAsync(
            "/api/v1/catalog/relationships",
            new { sourceKind = sk, sourceId = sid, type = t, targetKind = tk, targetId = tid },
            KartovaApiFixtureBase.WireJson);

    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        { displayName = name, description = "x", teamId, endpoints = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static async Task<Guid> SeedVmAsync(HttpClient client, Guid teamId, string name)
    {
        var request = new RegisterVmRequest(
            DisplayName: name,
            Description: "integration",
            TeamId: teamId,
            Provider: "AWS",
            Attributes: new VmAttributesDto("running", "ubuntu-22.04", 4, 16, "host-1",
                new[] { "10.0.0.1" }, "eu-west-1"));
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/infrastructure/vms", request, KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedVm '{name}' failed: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    [TestMethod]
    public async Task POST_deployedOn_service_to_vm_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team DeployedOn 201");
        var serviceId = await SeedServiceAsync(client, teamId, "svc-deployedon-201");
        var vmId = await SeedVmAsync(client, teamId, "vm-deployedon-201");

        var resp = await PostRelAsync(client, EntityKind.Service, serviceId, RelationshipType.DeployedOn, EntityKind.Infrastructure, vmId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"CreateRelationship failed: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(RelationshipType.DeployedOn, body!.Type);
        Assert.AreEqual(serviceId, body.Source.Id);
        Assert.AreEqual("vm-deployedon-201", body.Target.DisplayName);
    }
}
