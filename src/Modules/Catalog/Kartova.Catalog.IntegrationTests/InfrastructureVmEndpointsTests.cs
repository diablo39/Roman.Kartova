using System.Net;
using System.Net.Http.Json;
using System.Text;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Real-seam (gate-3) integration tests for the Infrastructure/VM slice-1 endpoints
/// (ADR-0111 amendment): <c>GET /infrastructure</c>, <c>GET /infrastructure/vms</c>,
/// <c>GET /infrastructure/vms/{id}</c>, <c>POST /infrastructure/vms</c>. Exercises the real
/// Postgres/RLS + JWT seam via <see cref="KartovaApiFixtureBase"/> — proves the
/// <c>AddInfrastructure</c> migration applies, RLS isolates tenants on the new table, and
/// <c>EF.Functions.JsonContains</c> actually translates against real Postgres (Task 12).
/// </summary>
[TestClass]
public sealed class InfrastructureVmEndpointsTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private static object VmBody(
        Guid teamId,
        string displayName,
        string powerState = "running",
        string os = "ubuntu-22.04",
        int vcpu = 4,
        int memoryGb = 16,
        string hostname = "host-1",
        string[]? ipAddresses = null,
        string region = "eu-west-1") => new
    {
        displayName,
        description = "seeded for infrastructure/vm integration tests",
        teamId,
        attributes = new
        {
            powerState,
            os,
            vcpu,
            memoryGb,
            hostname,
            ipAddresses = ipAddresses ?? new[] { "10.0.0.1", "10.0.0.2" },
            region,
        },
    };

    private static async Task<VmDetailResponse> RegisterVmAsync(HttpClient client, object body)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/infrastructure/vms", body);
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"RegisterVm failed: {await resp.Content.ReadAsStringAsync()}");
        return (await resp.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    private static void AssertAttributesEqual(VmAttributesDto expected, VmAttributesDto actual)
    {
        Assert.AreEqual(expected.PowerState, actual.PowerState);
        Assert.AreEqual(expected.Os, actual.Os);
        Assert.AreEqual(expected.Vcpu, actual.Vcpu);
        Assert.AreEqual(expected.MemoryGb, actual.MemoryGb);
        Assert.AreEqual(expected.Hostname, actual.Hostname);
        Assert.AreEqual(expected.Region, actual.Region);
        // VmAttributesDto has no value-equality override — compare IpAddresses element-wise.
        CollectionAssert.AreEqual(expected.IpAddresses.ToList(), actual.IpAddresses.ToList());
    }

    [TestMethod]
    public async Task RegisterVm_returns_201_and_roundtrips()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team Roundtrip");
        var unique = $"vm-roundtrip-{Guid.NewGuid():N}";

        var created = await RegisterVmAsync(client, VmBody(
            teamId, unique, ipAddresses: new[] { "10.1.2.3", "192.168.0.10" }));

        Assert.AreEqual(unique, created.DisplayName);
        Assert.AreEqual(teamId, created.TeamId);
        AssertAttributesEqual(
            new VmAttributesDto("running", "ubuntu-22.04", 4, 16, "host-1", new[] { "10.1.2.3", "192.168.0.10" }, "eu-west-1"),
            created.Attributes);

        var getResp = await client.GetAsync($"/api/v1/catalog/infrastructure/vms/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);
        var fetched = await getResp.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson);

        Assert.AreEqual(created.Id, fetched!.Id);
        Assert.AreEqual(created.DisplayName, fetched.DisplayName);
        AssertAttributesEqual(created.Attributes, fetched.Attributes);
    }

    [TestMethod]
    public async Task RegisterVm_invalid_ip_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team BadIp");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/infrastructure/vms",
            VmBody(teamId, "vm-badip", ipAddresses: new[] { "999.999.999.999" }));

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task RegisterVm_without_permission_returns_403()
    {
        // Viewer role carries CatalogRead only — not CatalogInfrastructureRegister — so the
        // RequireAuthorization policy on POST /infrastructure/vms rejects before the delegate runs.
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team NoPerm");
        var viewer = await Fx.CreateAuthenticatedClientAsync(
            "viewer@orga.kartova.local", new[] { KartovaRoles.Viewer });

        var resp = await viewer.PostAsJsonAsync("/api/v1/catalog/infrastructure/vms", VmBody(teamId, "vm-noperm"));

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task ListVms_rehydrates_attributes()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team Rehydrate");
        var unique = $"vm-rehydrate-{Guid.NewGuid():N}";

        var vm1 = await RegisterVmAsync(client, VmBody(teamId, $"{unique}-1", hostname: "host-alpha", region: "eu-north-1"));
        var vm2 = await RegisterVmAsync(client, VmBody(teamId, $"{unique}-2", hostname: "host-beta", region: "eu-south-1"));

        var resp = await client.GetAsync($"/api/v1/catalog/infrastructure/vms?teamId={teamId}&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);

        var found1 = page!.Items.Single(i => i.Id == vm1.Id);
        var found2 = page.Items.Single(i => i.Id == vm2.Id);
        AssertAttributesEqual(vm1.Attributes, found1.Attributes);
        AssertAttributesEqual(vm2.Attributes, found2.Attributes);
    }

    [TestMethod]
    public async Task ListVms_filter_powerState()
    {
        // The highest-value assertion in this file: EF.Functions.JsonContains must translate
        // to a real Postgres @> containment predicate for this to return only the matching row.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team PowerState");
        var unique = $"vm-pwr-{Guid.NewGuid():N}";

        var running = await RegisterVmAsync(client, VmBody(teamId, $"{unique}-running", powerState: "running"));
        var stopped = await RegisterVmAsync(client, VmBody(teamId, $"{unique}-stopped", powerState: "stopped"));

        var resp = await client.GetAsync($"/api/v1/catalog/infrastructure/vms?teamId={teamId}&powerState=running&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);
        var ids = page!.Items.Select(i => i.Id).ToHashSet();

        Assert.IsTrue(ids.Contains(running.Id), "the running VM must be returned");
        Assert.IsFalse(ids.Contains(stopped.Id), "the stopped VM must be excluded");
    }

    [TestMethod]
    public async Task ListVms_filter_ipAddress_contains()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team IpFilter");
        var unique = $"vm-ip-{Guid.NewGuid():N}";
        var targetIp = "10.99.1.1";

        var matching = await RegisterVmAsync(client, VmBody(teamId, $"{unique}-match", ipAddresses: new[] { targetIp, "10.0.0.5" }));
        var other = await RegisterVmAsync(client, VmBody(teamId, $"{unique}-other", ipAddresses: new[] { "10.0.0.99" }));

        var resp = await client.GetAsync($"/api/v1/catalog/infrastructure/vms?teamId={teamId}&ipAddress={targetIp}&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);
        var ids = page!.Items.Select(i => i.Id).ToHashSet();

        Assert.IsTrue(ids.Contains(matching.Id), "the VM carrying the target IP must be returned");
        Assert.IsFalse(ids.Contains(other.Id), "a VM without the target IP must be excluded");
    }

    [TestMethod]
    public async Task ListInfrastructure_returns_vm_row_shared_cols()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team Generic List");
        var unique = $"vm-generic-{Guid.NewGuid():N}";

        var vm = await RegisterVmAsync(client, VmBody(teamId, unique));

        var resp = await client.GetAsync($"/api/v1/catalog/infrastructure?teamId={teamId}&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<InfrastructureListItemResponse>>(KartovaApiFixtureBase.WireJson);

        var row = page!.Items.Single(i => i.Id == vm.Id);
        Assert.AreEqual("virtualMachine", row.Type);
        Assert.AreEqual(unique, row.DisplayName);
        Assert.AreEqual(teamId, row.TeamId);
        // Generic list is shared-columns-only — the raw response body must not carry the
        // opaque VM attributes payload (proves ListInfrastructureHandler never deserializes it).
        Assert.IsFalse(
            body.Contains("\"attributes\"", StringComparison.OrdinalIgnoreCase),
            "generic Infrastructure list response must not include an attributes block");
    }

    [TestMethod]
    public async Task Infrastructure_is_tenant_isolated()
    {
        var clientA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var clientB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var teamA = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team Isolation A");
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "Vm Team Isolation B");
        var unique = $"vm-isolate-{Guid.NewGuid():N}";

        var vmB = await RegisterVmAsync(clientB, VmBody(teamB, $"{unique}-b"));

        // Tenant A cannot see tenant B's VM via the detail endpoint (RLS hides the row → 404).
        var getResp = await clientA.GetAsync($"/api/v1/catalog/infrastructure/vms/{vmB.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, getResp.StatusCode);

        // Nor via the VM list or the generic Infrastructure list.
        var vmA = await RegisterVmAsync(clientA, VmBody(teamA, $"{unique}-a"));
        var vmListResp = await clientA.GetAsync($"/api/v1/catalog/infrastructure/vms?limit=200");
        var vmListPage = await vmListResp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(vmListPage!.Items.Any(i => i.Id == vmA.Id), "tenant A must see its own VM");
        Assert.IsFalse(vmListPage.Items.Any(i => i.Id == vmB.Id), "tenant A must not see tenant B's VM");

        var genericListResp = await clientA.GetAsync($"/api/v1/catalog/infrastructure?limit=200");
        var genericListPage = await genericListResp.Content.ReadFromJsonAsync<CursorPage<InfrastructureListItemResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsFalse(genericListPage!.Items.Any(i => i.Id == vmB.Id), "tenant A's generic Infrastructure list must not leak tenant B's VM");
    }

    [TestMethod]
    public async Task ListVms_cursor_is_stable_default_sort()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team Cursor");
        var unique = $"vm-cursor-{Guid.NewGuid():N}";

        // 5 rows with deterministic, lexicographically-ordered display names (D3-padded index).
        var registered = new List<VmDetailResponse>();
        for (var i = 0; i < 5; i++)
        {
            registered.Add(await RegisterVmAsync(client, VmBody(teamId, $"{unique}-{i:D3}")));
        }

        // Default params: sortBy=displayName, sortOrder=asc (ADR-0107 default).
        var page1Resp = await client.GetAsync($"/api/v1/catalog/infrastructure/vms?teamId={teamId}&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, page1Resp.StatusCode);
        var page1 = await page1Resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(page1!.NextCursor, "5 rows with limit=2 must yield a next cursor");

        var page2Resp = await client.GetAsync(
            $"/api/v1/catalog/infrastructure/vms?teamId={teamId}&limit=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, page2Resp.StatusCode);
        var page2 = await page2Resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);

        var page3Resp = await client.GetAsync(
            $"/api/v1/catalog/infrastructure/vms?teamId={teamId}&limit=2&cursor={Uri.EscapeDataString(page2!.NextCursor!)}");
        var page3 = await page3Resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);

        var allIds = page1.Items.Select(i => i.Id)
            .Concat(page2.Items.Select(i => i.Id))
            .Concat(page3!.Items.Select(i => i.Id))
            .ToList();

        Assert.AreEqual(5, allIds.Count, "no row skipped or duplicated across pages");
        Assert.AreEqual(5, allIds.Distinct().Count(), "no duplicate ids across pages");

        var allNames = page1.Items.Select(i => i.DisplayName)
            .Concat(page2.Items.Select(i => i.DisplayName))
            .Concat(page3.Items.Select(i => i.DisplayName))
            .ToList();
        var expectedOrder = registered.Select(r => r.DisplayName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        CollectionAssert.AreEqual(expectedOrder, allNames, "rows must be returned in stable displayName-asc, id-tiebreak order");
    }

    /// <summary>
    /// SF1: the SPA's registerVm schema keeps <c>vcpu</c>/<c>memoryGb</c> as validated digit
    /// STRINGS on the wire (openapi-typescript's int32-as-string generation — see
    /// web/src/features/catalog/schemas/registerVm.ts), not JSON numbers, even though
    /// <see cref="VmAttributesDto"/> types them as C# <c>int</c>. This pins the ASP.NET
    /// <c>System.Text.Json</c> default of accepting a quoted numeric string for an <c>int</c>
    /// property (<c>JsonNumberHandling.AllowReadingFromString</c> is on by default for minimal-API
    /// body binding) — a future switch to <c>JsonNumberHandling.Strict</c> would silently break
    /// every VM registration from the real SPA, and this test would catch it as a 400 instead.
    /// </summary>
    [TestMethod]
    public async Task RegisterVm_accepts_vcpu_and_memoryGb_as_json_strings()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team StringWire");
        var unique = $"vm-stringwire-{Guid.NewGuid():N}";

        var rawJson = $$"""
            {
              "displayName": "{{unique}}",
              "description": "seeded for infrastructure/vm integration tests (string-wire path)",
              "teamId": "{{teamId}}",
              "attributes": {
                "powerState": "running",
                "os": "ubuntu-22.04",
                "vcpu": "4",
                "memoryGb": "16",
                "hostname": "host-1",
                "ipAddresses": ["10.0.0.1", "10.0.0.2"],
                "region": "eu-west-1"
              }
            }
            """;

        var resp = await client.PostAsync(
            "/api/v1/catalog/infrastructure/vms",
            new StringContent(rawJson, Encoding.UTF8, "application/json"));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"RegisterVm (string wire) failed: {await resp.Content.ReadAsStringAsync()}");
        var created = await resp.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(4, created!.Attributes.Vcpu);
        Assert.AreEqual(16, created.Attributes.MemoryGb);

        var getResp = await client.GetAsync($"/api/v1/catalog/infrastructure/vms/{created.Id}");
        Assert.AreEqual(HttpStatusCode.OK, getResp.StatusCode);
        var fetched = await getResp.Content.ReadFromJsonAsync<VmDetailResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(4, fetched!.Attributes.Vcpu);
        Assert.AreEqual(16, fetched.Attributes.MemoryGb);
    }

    /// <summary>MT1: an unknown/non-existent teamId surfaces the same 422 invalid-team envelope
    /// as RegisterApplicationAsync/RegisterServiceAsync (ADR-0103).</summary>
    [TestMethod]
    public async Task RegisterVm_unknown_teamId_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/infrastructure/vms", VmBody(Guid.NewGuid(), "vm-unknown-team"));

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    /// <summary>MT2a: the generic Infrastructure list with an explicit <c>type=virtualMachine</c>
    /// filter returns the VM row, and a generic list with no <c>type</c> filter at all also
    /// returns it (sanity — matches <see cref="ListInfrastructure_returns_vm_row_shared_cols"/>'s
    /// no-filter case, but pins the filtered path too).</summary>
    [TestMethod]
    public async Task ListInfrastructure_type_filter_virtualMachine_returns_vm_row()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Vm Team TypeFilter");
        var unique = $"vm-typefilter-{Guid.NewGuid():N}";

        var vm = await RegisterVmAsync(client, VmBody(teamId, unique));

        var filteredResp = await client.GetAsync($"/api/v1/catalog/infrastructure?teamId={teamId}&type=virtualMachine&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, filteredResp.StatusCode);
        var filteredPage = await filteredResp.Content.ReadFromJsonAsync<CursorPage<InfrastructureListItemResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(filteredPage!.Items.Any(i => i.Id == vm.Id), "type=virtualMachine must return the VM row");

        var unfilteredResp = await client.GetAsync($"/api/v1/catalog/infrastructure?teamId={teamId}&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, unfilteredResp.StatusCode);
        var unfilteredPage = await unfilteredResp.Content.ReadFromJsonAsync<CursorPage<InfrastructureListItemResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(unfilteredPage!.Items.Any(i => i.Id == vm.Id), "no type filter must also return the VM row");
    }

    /// <summary>MT2b: an unrecognized <c>type</c> filter value returns 400 invalid-type-filter
    /// (mirrors the lifecycle/health/style token-parse rejects in CatalogEndpointDelegates).</summary>
    [TestMethod]
    public async Task ListInfrastructure_invalid_type_filter_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);

        var resp = await client.GetAsync("/api/v1/catalog/infrastructure?type=notARealType");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
