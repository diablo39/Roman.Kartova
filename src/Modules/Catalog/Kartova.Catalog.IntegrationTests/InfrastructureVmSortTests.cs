using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Real-seam (gate-3) integration tests for VM JSONB-column sort (ADR-0115 slice 2a, Task 7):
/// <c>GET /catalog/infrastructure/vms?sortBy=&lt;field&gt;</c> for the 6 JSONB attributes
/// (powerState/os/vcpu/memoryGb/hostname/region), the typed <c>provider</c> column, and the
/// typed <c>createdAt</c> column; cursor keyset stability on a JSONB sort key and across a
/// nullable-column (<c>provider</c>) sort key; and a proof — grounded in the ACTUAL
/// <see cref="DbCommand"/> EF emits for <see cref="ListVmsHandler"/>, not a hand-duplicated SQL
/// string — that the powerState-sorted query's key literal is not parameterized and that its
/// partial expression index is used (no <c>Seq Scan</c>) (gate-8 review, fix round 1).
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

    /// <summary>
    /// Captures the exact <see cref="DbCommand.CommandText"/> + bound parameter values EF emits
    /// for a query (gate-8 review, fix round 1) — <c>ToQueryString()</c> is documented as a
    /// debug-only string that interpolates parameter values back in for readability, so it does
    /// NOT prove whether a value was actually sent as a literal or as a bound parameter on the
    /// wire. This interceptor observes the real <see cref="DbCommand"/> right before Npgsql
    /// executes it against Postgres.
    /// </summary>
    private sealed class CommandTextCapturingInterceptor : DbCommandInterceptor
    {
        public string? LastCommandText { get; private set; }
        public List<NpgsqlParameter> LastParameters { get; } = [];

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Capture(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Capture(command);
            return ValueTask.FromResult(result);
        }

        private void Capture(DbCommand command)
        {
            LastCommandText = command.CommandText;
            LastParameters.Clear();
            foreach (NpgsqlParameter p in command.Parameters)
            {
                LastParameters.Add((NpgsqlParameter)((ICloneable)p).Clone());
            }
        }
    }

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
    /// gate-7 T4: mirrors <see cref="ListVms_cursor_is_stable_for_jsonb_hostname_sort"/> but for
    /// an INT-cast JSONB attribute (<c>vcpu</c>) — proves the id-tiebreak keyset mechanism is
    /// stable (no dup/skip, globally ordered) across a page boundary for the numeric-cast sort
    /// index too, not just the text-typed one hostname exercises.
    /// </summary>
    [TestMethod]
    public async Task ListVms_cursor_is_stable_for_jsonb_vcpu_sort()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Vm Team VcpuCursor");
        var unique = $"vm-vcpucursor-{Guid.NewGuid():N}";
        var origin = DateTimeOffset.UtcNow;

        var expectedIds = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var vcpu = i + 1;
            expectedIds.Add(await SeedVmAsync(
                tenantId, teamId, $"{unique}-{i:D3}", origin.AddMinutes(i), $"provider-{i:D3}",
                "running", "os-x", vcpu, 8, $"host-{i:D3}", "region-x"));
        }

        var page1Resp = await client.GetAsync(
            $"/api/v1/catalog/infrastructure/vms?teamId={teamId}&sortBy=vcpu&sortOrder=asc&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, page1Resp.StatusCode);
        var page1 = await page1Resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(page1!.NextCursor, "5 rows with limit=2 must yield a next cursor");

        var page2Resp = await client.GetAsync(
            $"/api/v1/catalog/infrastructure/vms?teamId={teamId}&sortBy=vcpu&sortOrder=asc&limit=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, page2Resp.StatusCode);
        var page2 = await page2Resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);

        var page3Resp = await client.GetAsync(
            $"/api/v1/catalog/infrastructure/vms?teamId={teamId}&sortBy=vcpu&sortOrder=asc&limit=2&cursor={Uri.EscapeDataString(page2!.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, page3Resp.StatusCode);
        var page3 = await page3Resp.Content.ReadFromJsonAsync<CursorPage<VmListItemResponse>>(KartovaApiFixtureBase.WireJson);

        var allIds = page1.Items.Select(i => i.Id)
            .Concat(page2.Items.Select(i => i.Id))
            .Concat(page3!.Items.Select(i => i.Id))
            .ToList();

        Assert.AreEqual(5, allIds.Count, "no row skipped or duplicated across pages");
        Assert.AreEqual(5, allIds.Distinct().Count(), "no duplicate ids across pages (page1/page2/page3 disjoint)");
        CollectionAssert.AreEqual(expectedIds, allIds, "rows must be returned in stable vcpu-asc, id-tiebreak order");
    }

    /// <summary>
    /// Shared plumbing for the "prove it against the REAL emitted command" tests (gate-8 review
    /// fix round 1; extended for the int-cast indexes in the slice 2a final-review fix wave):
    /// runs <see cref="ListVmsHandler"/> for the given sort field through
    /// <see cref="CommandTextCapturingInterceptor"/> (not <c>ToQueryString()</c> — see that
    /// type's remarks, which is documented-debug-only and doesn't prove literal-vs-parameter),
    /// then <c>EXPLAIN</c>s the EXACT captured <see cref="DbCommand.CommandText"/> + bound
    /// parameter values (never a hand-duplicated SQL string) with
    /// <c>enable_seqscan</c>/<c>enable_sort</c> off for the EXPLAIN session — so the plan
    /// assertion holds regardless of the seeded table's tiny row count (the planner would
    /// otherwise legitimately prefer a cheap seq-scan-and-sort for a handful of rows). If the
    /// captured SQL did NOT match the target partial index's expression, Postgres would have no
    /// valid plan besides Seq Scan + explicit Sort even with both disabled, so a mismatch still
    /// fails the caller's assertions.
    /// </summary>
    private static async Task<(string Sql, IReadOnlyList<NpgsqlParameter> Parameters, string Plan)> CaptureCommandAndExplainAsync(
        Guid teamId, VmSortField sortField)
    {
        var interceptor = new CommandTextCapturingInterceptor();
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(Fx.BypassConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using (var db = new CatalogDbContext(options))
        {
            var handler = new ListVmsHandler();
            var query = new ListVmsQuery(
                sortField, SortOrder.Asc, Cursor: null, Limit: 50, TeamId: [teamId]);
            await handler.Handle(query, db, CancellationToken.None);
        }

        Assert.IsNotNull(interceptor.LastCommandText, "ListVmsHandler must have executed a reader command");
        var sql = interceptor.LastCommandText!;
        var parameters = interceptor.LastParameters.ToList();

        // EXPLAIN the EXACT statement EF emitted (same CommandText, same bound parameter
        // values) — not a hand-duplicated string.
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
        explainCmd.CommandText = "EXPLAIN " + sql;
        foreach (var p in parameters)
        {
            explainCmd.Parameters.Add((NpgsqlParameter)((ICloneable)p).Clone());
        }

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

        return (sql, parameters, plan);
    }

    /// <summary>
    /// FW7-S3 / FIX #1 (gate-8 review fix round 1; slice 2a final-review fix wave, Important
    /// finding): captures the ACTUAL <see cref="DbCommand"/> <see cref="ListVmsHandler"/> emits
    /// for a JSONB-attribute-sorted query and asserts two things against it: (1) the field's key
    /// literal is embedded directly in the emitted SQL text, not lifted into a bound parameter —
    /// proving <see cref="JsonbFunctions.JsonbExtractPathText"/>'s <c>[NotParameterized]</c>
    /// attribute on <c>key</c> actually holds; (2) a raw <c>EXPLAIN</c> of that exact statement
    /// shows the field's partial expression index used and no <c>Seq Scan</c>. Covers both the
    /// TEXT-cast index shape (<c>powerState</c>) and the INT-cast shape (<c>vcpu</c>/
    /// <c>memoryGb</c>), which is a materially different expression
    /// (<c>(jsonb_extract_path_text(...))::int</c>) — all three were previously verified only by
    /// ordering assertions, which a seq-scan-and-sort plan satisfies just as well as an index
    /// scan; this proof needs a real EXPLAIN per shape.
    /// </summary>
    [TestMethod]
    [DataRow(VmSortField.PowerState, "powerState", "ix_catalog_infrastructure_vm_power_state")]
    [DataRow(VmSortField.Vcpu, "vcpu", "ix_catalog_infrastructure_vm_vcpu")]
    [DataRow(VmSortField.MemoryGb, "memoryGb", "ix_catalog_infrastructure_vm_memory_gb")]
    public async Task ListVms_sortBy_jsonbField_emits_literal_key_and_uses_partial_index(
        VmSortField sortField, string key, string expectedIndexName)
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, $"Vm Team IntCastCapture {key}");
        await SeedVmAsync(
            tenantId, teamId, $"vm-intcast-{key}-{Guid.NewGuid():N}", DateTimeOffset.UtcNow, "provider-x",
            "running", "os-x", 2, 8, "host-x", "region-x");

        var (sql, parameters, plan) = await CaptureCommandAndExplainAsync(teamId, sortField);

        // Prove #1: the key literal is embedded directly in the SQL EF actually emits — not
        // lifted into a bound parameter.
        Assert.IsTrue(
            sql.Contains("jsonb_extract_path_text", StringComparison.Ordinal),
            $"expected jsonb_extract_path_text in the emitted SQL:\n{sql}");
        Assert.IsTrue(
            sql.Contains($"'{key}'", StringComparison.Ordinal),
            $"expected the '{key}' key literal inline (not parameterized) in the emitted SQL:\n{sql}");
        Assert.IsFalse(
            parameters.Any(p => key.Equals(p.Value)),
            $"the '{key}' key must not be sent as a bound parameter value");

        // Prove #2: EXPLAIN of the ACTUAL EF-emitted statement uses the field's partial index,
        // no seq scan — proving the expression (text- or int-cast) matches byte-for-byte.
        Assert.IsFalse(
            plan.Contains("Seq Scan", StringComparison.OrdinalIgnoreCase),
            $"Expected no Seq Scan for the ACTUAL EF-emitted statement; plan:\n{plan}\nSQL was:\n{sql}");
        Assert.IsTrue(
            plan.Contains(expectedIndexName, StringComparison.OrdinalIgnoreCase),
            $"Expected {expectedIndexName} for the ACTUAL EF-emitted statement; plan:\n{plan}\nSQL was:\n{sql}");
    }

    /// <summary>
    /// FW7-S4 (gate-8 review finding, fix round 1): <c>provider</c> is a nullable column. Before
    /// the <c>COALESCE(provider, '')</c> fix, sorting by provider produced a NULL cursor
    /// sort-value whenever a page-boundary row had a null <c>Provider</c> — the shared keyset
    /// predicate <c>sortKey &gt; @p OR (sortKey = @p AND id &gt; @p)</c> then evaluates to SQL
    /// UNKNOWN for EVERY row once <c>@p</c> is NULL (not just the null-provider one), so the next
    /// page came back completely empty even though more rows existed — pagination silently
    /// truncated at the first null-provider boundary.
    /// <para>
    /// Seeds 3 null-provider rows + 2 non-null-provider rows with <c>limit=2</c>, which
    /// guarantees a null-provider row is the page-1 boundary (empty string sorts before any
    /// non-empty provider, so the first 2 rows returned are always drawn from the 3 tied
    /// null-provider rows). Asserts every row is still returned across pages with no
    /// duplicates/truncation, and that the coalesced provider values are non-decreasing overall.
    /// The exact tiebreak order among rows sharing the same coalesced key is deliberately NOT
    /// asserted — .NET's default <see cref="Guid"/> ordering does not match Postgres' <c>uuid</c>
    /// btree ordering, so pinning a specific tiebreak sequence here would test the wrong thing.
    /// </para>
    /// </summary>
    [TestMethod]
    public Task ListVms_cursor_is_stable_across_null_provider_boundary() =>
        AssertNullProviderBoundaryStableAsync<VmListItemResponse>(
            "/api/v1/catalog/infrastructure/vms", "Vm Team NullProviderCursor", "vm-nullprovider",
            i => i.Id, i => i.Provider);

    /// <summary>
    /// FIX #6 (slice 2a final-review fix wave, Minor finding): the null-provider keyset-paging
    /// boundary was previously proven only for the VM-specific tier
    /// (<see cref="ListVms_cursor_is_stable_across_null_provider_boundary"/> /
    /// <c>VmSortSpecs.Provider</c>). The GENERIC <c>GET /catalog/infrastructure</c> list
    /// (<c>InfrastructureSortSpecs.Provider</c>) applies the identical <c>COALESCE(provider, "")</c>
    /// fix but had no analogous coverage. Mirrors that test exactly, against
    /// <c>ListInfrastructureHandler</c>/<c>InfrastructureListItemResponse</c> instead — VMs alone
    /// are a sufficient InfrastructureType since only one type exists in the domain today.
    /// </summary>
    [TestMethod]
    public Task ListInfrastructure_cursor_is_stable_across_null_provider_boundary() =>
        AssertNullProviderBoundaryStableAsync<InfrastructureListItemResponse>(
            "/api/v1/catalog/infrastructure", "Vm Team GenericNullProviderCursor", "vm-genericnullprovider",
            i => i.Id, i => i.Provider);

    /// <summary>
    /// Shared body for the null-provider keyset-paging boundary proof (dedup of the two tests
    /// above — line-identical except list path, DTO type, team name, and seed-id prefix). Seeds
    /// 3 null-provider rows + 2 non-null-provider rows with <c>limit=2</c>, which guarantees a
    /// null-provider row is the page-1 boundary (empty string sorts before any non-empty
    /// provider, so the first 2 rows returned are always drawn from the 3 tied null-provider
    /// rows). Asserts every row is still returned across pages with no duplicates/truncation, and
    /// that the coalesced provider values are non-decreasing overall. The exact tiebreak order
    /// among rows sharing the same coalesced key is deliberately NOT asserted — .NET's default
    /// <see cref="Guid"/> ordering does not match Postgres' <c>uuid</c> btree ordering, so pinning
    /// a specific tiebreak sequence here would test the wrong thing.
    /// </summary>
    private static async Task AssertNullProviderBoundaryStableAsync<T>(
        string listPath, string teamName, string uniquePrefix, Func<T, Guid> idOf, Func<T, string?> providerOf)
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, teamName);
        var unique = $"{uniquePrefix}-{Guid.NewGuid():N}";
        var origin = DateTimeOffset.UtcNow;

        var expectedIds = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            expectedIds.Add(await SeedVmAsync(
                tenantId, teamId, $"{unique}-null-{i}", origin.AddMinutes(i), provider: null,
                "running", "os-x", 2, 8, $"host-null-{i}", "region-x"));
        }
        expectedIds.Add(await SeedVmAsync(
            tenantId, teamId, $"{unique}-p-a", origin.AddMinutes(10), "zzz-provider-a",
            "running", "os-x", 2, 8, "host-p-a", "region-x"));
        expectedIds.Add(await SeedVmAsync(
            tenantId, teamId, $"{unique}-p-b", origin.AddMinutes(11), "zzz-provider-b",
            "running", "os-x", 2, 8, "host-p-b", "region-x"));

        var allItems = new List<T>();
        string? cursor = null;
        for (var page = 0; page < 10; page++)
        {
            var url = $"{listPath}?teamId={teamId}&sortBy=provider&sortOrder=asc&limit=2"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var resp = await client.GetAsync(url);
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"page {page} failed: {await resp.Content.ReadAsStringAsync()}");
            var thisPage = await resp.Content.ReadFromJsonAsync<CursorPage<T>>(KartovaApiFixtureBase.WireJson);
            allItems.AddRange(thisPage!.Items);
            cursor = thisPage.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        Assert.AreEqual(
            5, allItems.Count,
            "all 5 rows must be returned — pagination must not truncate at the null-provider boundary");
        Assert.AreEqual(5, allItems.Select(idOf).Distinct().Count(), "no duplicate rows across pages");
        CollectionAssert.AreEquivalent(
            expectedIds, allItems.Select(idOf).ToList(), "every seeded row must appear exactly once");

        var coalescedProviders = allItems.Select(i => providerOf(i) ?? "").ToList();
        for (var i = 1; i < coalescedProviders.Count; i++)
        {
            Assert.IsTrue(
                string.CompareOrdinal(coalescedProviders[i - 1], coalescedProviders[i]) <= 0,
                $"expected non-decreasing provider order; got: {string.Join(", ", coalescedProviders)}");
        }
    }
}
