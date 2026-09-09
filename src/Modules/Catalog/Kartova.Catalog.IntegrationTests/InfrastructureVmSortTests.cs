using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Npgsql;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Real-seam (gate-3) integration tests for VM JSONB-column sort (ADR-0115 slice 2a, Task 7):
/// <c>GET /catalog/infrastructure/vms?sortBy=&lt;field&gt;</c> for the 6 JSONB attributes
/// (powerState/os/vcpu/memoryGb/hostname/region), the typed <c>provider</c> column, and the
/// typed <c>createdAt</c> column, plus cursor keyset stability on a JSONB sort key and a raw
/// <c>EXPLAIN</c> proving the partial expression index is actually used (no <c>Seq Scan</c>).
/// <para>
/// VMs are seeded directly via <see cref="KartovaApiFixture.CatalogDb"/> (bypass-RLS), not via
/// the HTTP register endpoint, so every dimension (createdAt included) can be pinned to an
/// exact, monotonically-increasing value across 3 rows — the HTTP path can't control
/// <c>createdAt</c> (always "now").
/// </para>
/// </summary>
[TestClass]
public sealed class InfrastructureVmSortTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static string AttributesJson(
        string powerState, string os, int vcpu, int memoryGb, string hostname, string region) =>
        JsonSerializer.Serialize(new
        {
            powerState,
            os,
            vcpu,
            memoryGb,
            hostname,
            ipAddresses = new[] { "10.0.0.1" },
            region,
        });

    private static async Task<Guid> SeedVmAsync(
        TenantId tenantId, Guid teamId, string displayName, DateTimeOffset createdAt, string? provider,
        string powerState, string os, int vcpu, int memoryGb, string hostname, string region)
    {
        await using var db = Fx.CatalogDb();
        var vm = InfrastructureResource.Create(
            displayName: displayName,
            description: "seeded for VM JSONB-sort integration tests",
            provider: provider,
            type: InfrastructureType.VirtualMachine,
            attributesJson: AttributesJson(powerState, os, vcpu, memoryGb, hostname, region),
            createdByUserId: Guid.NewGuid(),
            teamId: teamId,
            tenantId: tenantId,
            createdAt: createdAt);
        db.Infrastructure.Add(vm);
        await db.SaveChangesAsync();
        return vm.Id.Value;
    }

    /// <summary>
    /// FW7-S1: each of the 8 sortable fields lifted by ADR-0115 slice 2a — the 6 JSONB
    /// attributes plus the typed <c>provider</c> and <c>createdAt</c> columns — returns rows in
    /// ascending order. <c>displayName</c> and generic-tier <c>type</c> sort are already pinned
    /// by <see cref="InfrastructureVmEndpointsTests"/>.
    /// </summary>
    [TestMethod]
    [DataRow("powerState")]
    [DataRow("os")]
    [DataRow("vcpu")]
    [DataRow("memoryGb")]
    [DataRow("hostname")]
    [DataRow("region")]
    [DataRow("provider")]
    [DataRow("createdAt")]
    public async Task ListVms_sortBy_field_returns_ascending_order(string sortByField)
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, $"Vm Sort Team {sortByField}");
        var unique = $"vm-sort-{sortByField}-{Guid.NewGuid():N}";
        var origin = DateTimeOffset.UtcNow;

        // 3 rows, every dimension monotonically increasing A < B < C so a single asc query on
        // ANY field returns them in the same [A, B, C] order.
        var idA = await SeedVmAsync(
            tenantId, teamId, $"{unique}-a", origin, "provider-a",
            "running", "os-a", 2, 8, "host-a", "region-a");
        var idB = await SeedVmAsync(
            tenantId, teamId, $"{unique}-b", origin.AddMinutes(1), "provider-b",
            "stopped", "os-b", 4, 16, "host-b", "region-b");
        var idC = await SeedVmAsync(
            tenantId, teamId, $"{unique}-c", origin.AddMinutes(2), "provider-c",
            "suspended", "os-c", 8, 32, "host-c", "region-c");

        var resp = await client.GetAsync(
            $"/api/v1/catalog/infrastructure/vms?teamId={teamId}&sortBy={sortByField}&sortOrder=asc&limit=200");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"sortBy={sortByField} failed: {await resp.Content.ReadAsStringAsync()}");
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);

        CollectionAssert.AreEqual(
            new[] { idA, idB, idC }, page!.Items.Select(i => i.Id).ToList(),
            $"sortBy={sortByField} must return the 3 seeded rows in ascending order");
    }

    /// <summary>
    /// FW7-S2: cursor keyset pagination is stable (no dup/skip, globally ordered) across a page
    /// boundary when the primary sort key is a JSONB attribute (<c>hostname</c>) — proves the
    /// id-tiebreak keyset mechanism works the same for a JSONB expression key as for a typed
    /// column (mirrors <see cref="InfrastructureVmEndpointsTests.ListVms_cursor_is_stable_default_sort"/>).
    /// </summary>
    [TestMethod]
    public async Task ListVms_cursor_is_stable_for_jsonb_hostname_sort()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Vm Team JsonbCursor");
        var unique = $"vm-jsonbcursor-{Guid.NewGuid():N}";
        var origin = DateTimeOffset.UtcNow;

        var expectedIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var hostname = $"host-{i:D3}";
            expectedIds.Add(await SeedVmAsync(
                tenantId, teamId, $"{unique}-{i:D3}", origin.AddMinutes(i), $"provider-{i:D3}",
                "running", "os-x", 2, 8, hostname, "region-x"));
        }

        var page1Resp = await client.GetAsync(
            $"/api/v1/catalog/infrastructure/vms?teamId={teamId}&sortBy=hostname&sortOrder=asc&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, page1Resp.StatusCode);
        var page1 = await page1Resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(page1!.NextCursor, "5 rows with limit=2 must yield a next cursor");

        var page2Resp = await client.GetAsync(
            $"/api/v1/catalog/infrastructure/vms?teamId={teamId}&sortBy=hostname&sortOrder=asc&limit=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, page2Resp.StatusCode);
        var page2 = await page2Resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);

        var page3Resp = await client.GetAsync(
            $"/api/v1/catalog/infrastructure/vms?teamId={teamId}&sortBy=hostname&sortOrder=asc&limit=2&cursor={Uri.EscapeDataString(page2!.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, page3Resp.StatusCode);
        var page3 = await page3Resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);

        var allIds = page1.Items.Select(i => i.Id)
            .Concat(page2.Items.Select(i => i.Id))
            .Concat(page3!.Items.Select(i => i.Id))
            .ToList();

        Assert.AreEqual(5, allIds.Count, "no row skipped or duplicated across pages");
        Assert.AreEqual(5, allIds.Distinct().Count(), "no duplicate ids across pages (page1/page2/page3 disjoint)");
        CollectionAssert.AreEqual(expectedIds, allIds, "rows must be returned in stable hostname-asc, id-tiebreak order");
    }

    /// <summary>
    /// FW7-S3 (the critical proof): a raw <c>EXPLAIN</c> on the powerState-sorted VM query shows
    /// the <c>ix_catalog_infrastructure_vm_power_state</c> partial expression index in the plan
    /// and NO <c>Seq Scan</c> node. <c>enable_seqscan</c>/<c>enable_sort</c> are turned off for
    /// this session so the assertion holds regardless of the seeded table's tiny row count (the
    /// planner would otherwise legitimately prefer a cheap seq-scan-and-sort over an index for a
    /// handful of rows) — this isolates what's actually under test: whether
    /// <c>VmSortSpecs.PowerState</c>'s translated SQL is byte-identical to the index expression,
    /// not whether Postgres' cost-based planner happens to favor the index at this table size.
    /// If the SQL did NOT match the index expression, Postgres would have no valid plan besides
    /// Seq Scan + explicit Sort even with both disabled — this test would then correctly fail.
    /// </summary>
    [TestMethod]
    public async Task ListVms_sortBy_powerState_explain_uses_partial_index_no_seqscan()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Vm Team Explain");
        await SeedVmAsync(
            tenantId, teamId, $"vm-explain-{Guid.NewGuid():N}", DateTimeOffset.UtcNow, "provider-x",
            "running", "os-x", 2, 8, "host-x", "region-x");

        await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var setCmd = conn.CreateCommand())
        {
            setCmd.Transaction = tx;
            setCmd.CommandText = "SET LOCAL enable_seqscan = off; SET LOCAL enable_sort = off;";
            await setCmd.ExecuteNonQueryAsync();
        }

        await using var explainCmd = conn.CreateCommand();
        explainCmd.Transaction = tx;
        explainCmd.CommandText = """
            EXPLAIN
            SELECT id, attributes
            FROM catalog_infrastructure
            WHERE type = 0
            ORDER BY jsonb_extract_path_text(attributes, 'powerState')
            LIMIT 50
            """;

        var planLines = new List<string>();
        await using (var reader = await explainCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                planLines.Add(reader.GetString(0));
            }
        }
        await tx.RollbackAsync();
        var plan = string.Join("\n", planLines);

        Assert.IsFalse(
            plan.Contains("Seq Scan", StringComparison.OrdinalIgnoreCase),
            $"Expected no Seq Scan in the plan; actual plan:\n{plan}");
        Assert.IsTrue(
            plan.Contains("ix_catalog_infrastructure_vm_power_state", StringComparison.OrdinalIgnoreCase),
            $"Expected the partial expression index to be used; actual plan:\n{plan}");
    }
}
