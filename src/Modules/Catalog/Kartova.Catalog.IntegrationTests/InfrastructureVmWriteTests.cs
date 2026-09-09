using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
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

    /// <summary>Shared POST-then-201-then-deserialize boilerplate repeated across most tests in
    /// this file (each POSTs a VM as setup, then acts on the returned <see cref="VmDetailResponse"/>).
    /// Not used by <see cref="Get_EmitsEtagMatchingVersion"/>, which needs the raw
    /// <c>HttpResponseMessage</c> to assert the response's <c>ETag</c> header.</summary>
    private static async Task<VmDetailResponse> RegisterVmAsync(HttpClient client, RegisterVmRequest request)
    {
        var post = await client.PostAsJsonAsync("/api/v1/catalog/infrastructure/vms", request, KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(HttpStatusCode.Created, post.StatusCode, $"RegisterVm failed: {await post.Content.ReadAsStringAsync()}");
        return (await post.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    [TestMethod]
    public async Task Post_ThenGet_ReturnsProvider()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team Provider");
        var unique = $"vm-provider-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(client, ValidVm(teamId, unique));
        Assert.AreEqual("AWS", created.Provider);

        var get = await client.GetAsync($"/api/v1/catalog/infrastructure/vms/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        var fetched = await get.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("AWS", fetched!.Provider);
    }

    /// <summary>
    /// Task 4: GET single-VM emits an RFC 7232 quoted ETag equal to the body's
    /// <c>version</c> field — the prerequisite for <c>If-Match</c> on the PUT/DELETE
    /// endpoints Tasks 5/6 add. Mirrors the register response too, since both
    /// construction sites encode <c>InfrastructureResource.Xmin</c>.
    /// </summary>
    [TestMethod]
    public async Task Get_EmitsEtagMatchingVersion()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team Etag");
        var unique = $"vm-etag-{Guid.NewGuid():N}";

        var post = await client.PostAsJsonAsync(
            "/api/v1/catalog/infrastructure/vms", ValidVm(teamId, unique), KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(HttpStatusCode.Created, post.StatusCode, $"RegisterVm failed: {await post.Content.ReadAsStringAsync()}");
        var created = await post.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(created!.Version);
        Assert.AreEqual($"\"{created.Version}\"", post.Headers.ETag!.ToString());

        var get = await client.GetAsync($"/api/v1/catalog/infrastructure/vms/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
        var fetched = await get.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(fetched!.Version);
        Assert.AreEqual($"\"{fetched.Version}\"", get.Headers.ETag!.ToString());
    }

    // ---- Task 5: PUT /infrastructure/vms/{id} with If-Match --------------------------

    private static EditVmRequest EditFrom(VmDetailResponse created, string? provider = null) => new(
        DisplayName: created.DisplayName,
        Description: created.Description,
        Provider: provider ?? created.Provider,
        Attributes: created.Attributes);

    private static HttpRequestMessage NewPut(Guid id, string? ifMatch, EditVmRequest request)
    {
        var msg = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/infrastructure/vms/{id}")
        {
            Content = JsonContent.Create(request, options: KartovaApiFixtureBase.WireJson),
        };
        if (ifMatch is not null)
        {
            msg.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        }
        return msg;
    }

    [TestMethod]
    public async Task Put_UpdatesVm_Returns200()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team Edit");
        var unique = $"vm-edit-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(client, ValidVm(teamId, unique));

        var put = NewPut(created.Id, created.Version, EditFrom(created, provider: "Azure"));
        var resp = await client.SendAsync(put);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"EditVm failed: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("Azure", body!.Provider);
        Assert.AreNotEqual(created.Version, body.Version);
        Assert.AreEqual($"\"{body.Version}\"", resp.Headers.ETag?.Tag);
    }

    [TestMethod]
    public async Task Put_StaleIfMatch_Returns412()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team Stale");
        var unique = $"vm-stale-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(client, ValidVm(teamId, unique));

        // First edit advances the version.
        var ok = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, provider: "Azure")));
        Assert.AreEqual(HttpStatusCode.OK, ok.StatusCode);

        // Reuse the now-stale original ETag.
        var stale = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, provider: "GCP")));
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [TestMethod]
    public async Task Put_MissingIfMatch_Returns428()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team NoIfMatch");
        var unique = $"vm-noifmatch-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(client, ValidVm(teamId, unique));

        var resp = await client.SendAsync(NewPut(created.Id, ifMatch: null, EditFrom(created, provider: "Azure")));

        Assert.AreEqual(HttpStatusCode.PreconditionRequired, resp.StatusCode);
    }

    [TestMethod]
    public async Task Put_BadAttributes_Returns400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team BadAttrs");
        var unique = $"vm-badattrs-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(client, ValidVm(teamId, unique));

        var badAttrs = created.Attributes with { Vcpu = 0 };
        var resp = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created) with { Attributes = badAttrs }));

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Put_WithoutRegisterPerm_Returns403()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team NoPerm");
        var unique = $"vm-noperm-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(client, ValidVm(teamId, unique));

        var viewer = await Fx.CreateAuthenticatedClientAsync(
            "viewer-edit@orga.kartova.local", new[] { KartovaRoles.Viewer });

        var resp = await viewer.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, provider: "Azure")));

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task Put_UnknownId_Returns404()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);

        var badVm = new VmDetailResponse(
            Guid.NewGuid(), Guid.NewGuid(), "n", "d", "AWS", Guid.NewGuid(), null, Guid.NewGuid(),
            DateTimeOffset.UtcNow, VersionEncoding.Encode(0u),
            new VmAttributesDto("running", "ubuntu-22.04", 4, 16, "host-1", new[] { "10.0.0.1" }, "eu-west-1"));

        var resp = await client.SendAsync(NewPut(Guid.NewGuid(), badVm.Version, EditFrom(badVm)));

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Put_CrossTenant_Returns404()
    {
        const string orgBUser = "admin@orgb.kartova.local";
        var orgAClient = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team CrossTenant");
        var unique = $"vm-crosstenant-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(orgAClient, ValidVm(teamId, unique));

        var orgBClient = await Fx.CreateAuthenticatedClientAsync(orgBUser);
        var resp = await orgBClient.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, provider: "Hijack")));

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---- Task 6: DELETE /infrastructure/vms/{id} with If-Match (OrgAdmin-only) --------

    private static HttpRequestMessage NewDelete(Guid id, string? ifMatch)
    {
        var msg = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/catalog/infrastructure/vms/{id}");
        if (ifMatch is not null)
        {
            msg.Headers.TryAddWithoutValidation("If-Match", $"\"{ifMatch}\"");
        }
        return msg;
    }

    [TestMethod]
    public async Task Delete_RemovesVm_Returns204()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team Delete");
        var unique = $"vm-delete-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(client, ValidVm(teamId, unique));

        var del = await client.SendAsync(NewDelete(created.Id, created.Version));
        Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode, $"DeleteVm failed: {await del.Content.ReadAsStringAsync()}");

        var get = await client.GetAsync($"/api/v1/catalog/infrastructure/vms/{created.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, get.StatusCode);
    }

    [TestMethod]
    public async Task Delete_NonOrgAdmin_Returns403()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team DeleteNoPerm");
        var unique = $"vm-delete-noperm-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(client, ValidVm(teamId, unique));

        // Member carries CatalogInfrastructureRegister but NOT CatalogInfrastructureDelete
        // (OrgAdmin-only) — proves the delete gate is a distinct, narrower permission.
        var member = await Fx.CreateAuthenticatedClientAsync(
            "member-delete@orga.kartova.local", new[] { KartovaRoles.Member });

        var resp = await member.SendAsync(NewDelete(created.Id, created.Version));

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task Delete_StaleIfMatch_Returns412()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team DeleteStale");
        var unique = $"vm-delete-stale-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(client, ValidVm(teamId, unique));

        // Advance the version first so the original ETag goes stale.
        var edit = await client.SendAsync(NewPut(created.Id, created.Version, EditFrom(created, provider: "Azure")));
        Assert.AreEqual(HttpStatusCode.OK, edit.StatusCode);

        var stale = await client.SendAsync(NewDelete(created.Id, created.Version));
        Assert.AreEqual(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [TestMethod]
    public async Task Delete_MissingIfMatch_Returns428()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team DeleteNoIfMatch");
        var unique = $"vm-delete-noifmatch-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(client, ValidVm(teamId, unique));

        var resp = await client.SendAsync(NewDelete(created.Id, ifMatch: null));

        Assert.AreEqual(HttpStatusCode.PreconditionRequired, resp.StatusCode);
    }

    [TestMethod]
    public async Task Delete_UnknownId_Returns404()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);

        var resp = await client.SendAsync(NewDelete(Guid.NewGuid(), VersionEncoding.Encode(0u)));

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task Delete_CrossTenant_Returns404()
    {
        const string orgBUser = "admin@orgb.kartova.local";
        var orgAClient = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team DeleteCrossTenant");
        var unique = $"vm-delete-crosstenant-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(orgAClient, ValidVm(teamId, unique));

        var orgBClient = await Fx.CreateAuthenticatedClientAsync(orgBUser);
        var resp = await orgBClient.SendAsync(NewDelete(created.Id, created.Version));

        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
