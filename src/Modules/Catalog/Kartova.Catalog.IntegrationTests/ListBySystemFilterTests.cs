using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Task 5a (A2, real-seam) — the entire executable proof for Task 3's applications-list
/// <c>?systemId=</c> filter, f-map key, and System-column enrichment. Task 3 ships the
/// populated-<c>systemId</c> code path with no coverage of its own (the EF InMemory provider
/// cannot translate a query touching <see cref="Relationship"/>'s ComplexProperty members — see
/// <see cref="Infrastructure.ISystemMembershipEnricher"/>'s remarks — so only a real-Postgres
/// seam can exercise it). Services-side cases belong to Task 5b, run against a separate list
/// endpoint delegate that duplicates the same guard — not covered here.
/// <para>
/// OrgA's tenant is shared across the whole integration-test assembly, so every assertion here
/// is scoped by a unique <c>displayNameContains</c>/prefix guid per test and cleaned up in
/// <c>finally</c> (mirrors <see cref="ListApplicationsPaginationTests"/> / A1's
/// <see cref="SetComponentSystemTests"/>) — an unfiltered exact-count assertion over OrgA would
/// be flaky under parallel/repeated runs.
/// </para>
/// </summary>
[TestClass]
public sealed class ListBySystemFilterTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static Task<HttpResponseMessage> PutSystemAsync(HttpClient client, Guid appId, Guid? systemId)
        => client.PutAsJsonAsync($"/api/v1/catalog/applications/{appId}/system", new { systemId }, KartovaApiFixtureBase.WireJson);

    private static Task<HttpResponseMessage> PutServiceSystemAsync(HttpClient client, Guid serviceId, Guid? systemId)
        => client.PutAsJsonAsync($"/api/v1/catalog/services/{serviceId}/system", new { systemId }, KartovaApiFixtureBase.WireJson);

    private static async Task<CursorPage<ApplicationResponse>> GetPageAsync(HttpClient client, string url)
    {
        var resp = await client.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"GET {url} did not return 200");
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(page);
        return page!;
    }

    /// <summary>Services-side counterpart to <see cref="GetPageAsync"/> — Task 5b.</summary>
    private static async Task<CursorPage<ServiceResponse>> GetServicePageAsync(HttpClient client, string url)
    {
        var resp = await client.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode, $"GET {url} did not return 200");
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(page);
        return page!;
    }

    // -----------------------------------------------------------------------
    // 1-3: happy path + the column contract.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Applications_filtered_by_one_system_returns_only_its_members()
    {
        var unique = $"a2-one-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemId = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system");

        var inSystem1 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-in1");
        var inSystem2 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-in2");
        var unassigned = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-none");

        try
        {
            Assert.AreEqual(HttpStatusCode.OK, (await PutSystemAsync(client, inSystem1, systemId)).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await PutSystemAsync(client, inSystem2, systemId)).StatusCode);

            var page = await GetPageAsync(client, $"/api/v1/catalog/applications?systemId={systemId}&limit=200");
            var ids = page.Items.Select(i => i.Id).ToHashSet();

            Assert.IsTrue(ids.Contains(inSystem1), "app assigned to the System must be returned");
            Assert.IsTrue(ids.Contains(inSystem2), "the other app assigned to the same System must be returned");
            Assert.IsFalse(ids.Contains(unassigned), "an unassigned app must not be returned");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    [TestMethod]
    public async Task Applications_filtered_by_two_systems_returns_the_union()
    {
        var unique = $"a2-union-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemA = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-a");
        var systemB = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-b");
        var systemC = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-c");

        var inA = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a");
        var inB = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-b");
        var inC = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-c");
        var inNone = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-none");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, inA, systemA);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, inB, systemB);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, inC, systemC);

            var page = await GetPageAsync(
                client, $"/api/v1/catalog/applications?systemId={systemA}&systemId={systemB}&limit=200");
            var ids = page.Items.Select(i => i.Id).ToHashSet();

            // Inclusion alone survives a dropped predicate (e.g. an accidental `&&` -> `||`
            // against an unrelated condition) — assert exclusion of C and the unassigned app too.
            Assert.IsTrue(ids.Contains(inA), "member of System A must be included");
            Assert.IsTrue(ids.Contains(inB), "member of System B must be included");
            Assert.IsFalse(ids.Contains(inC), "member of System C (not selected) must be excluded");
            Assert.IsFalse(ids.Contains(inNone), "unassigned app must be excluded");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    [TestMethod]
    public async Task Application_rows_carry_SystemId_and_SystemDisplayName()
    {
        var unique = $"a2-column-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemId = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system");
        var assigned = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-assigned");
        var unassigned = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-unassigned");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, assigned, systemId);

            var page = await GetPageAsync(client, $"/api/v1/catalog/applications?displayNameContains={unique}&limit=200");
            var assignedRow = page.Items.Single(i => i.Id == assigned);
            var unassignedRow = page.Items.Single(i => i.Id == unassigned);

            Assert.AreEqual(systemId, assignedRow.SystemId);
            Assert.AreEqual($"{unique}-system", assignedRow.SystemDisplayName);
            Assert.IsNull(unassignedRow.SystemId, "an unassigned app must carry a null SystemId");
            Assert.IsNull(unassignedRow.SystemDisplayName, "an unassigned app must carry a null SystemDisplayName");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    // -----------------------------------------------------------------------
    // 4-5 (Task 5b): the Services-side mirrors of cases 1 and 3. ListServicesHandler
    // duplicates the filter/enrichment logic against a separate query source, so it needs
    // its own pin, not just the Applications one.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Services_filtered_by_system_returns_only_its_members()
    {
        var unique = $"a2-svc-one-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemId = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system");

        var inSystem1 = await Fx.SeedSingleServiceAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-in1");
        var inSystem2 = await Fx.SeedSingleServiceAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-in2");
        var unassigned = await Fx.SeedSingleServiceAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-none");

        try
        {
            Assert.AreEqual(HttpStatusCode.OK, (await PutServiceSystemAsync(client, inSystem1, systemId)).StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await PutServiceSystemAsync(client, inSystem2, systemId)).StatusCode);

            var page = await GetServicePageAsync(client, $"/api/v1/catalog/services?systemId={systemId}&limit=200");
            var ids = page.Items.Select(i => i.Id).ToHashSet();

            Assert.IsTrue(ids.Contains(inSystem1), "service assigned to the System must be returned");
            Assert.IsTrue(ids.Contains(inSystem2), "the other service assigned to the same System must be returned");
            Assert.IsFalse(ids.Contains(unassigned), "an unassigned service must not be returned");
        }
        finally
        {
            await Fx.DeleteServicesByPrefixAsync(tenant, unique);
        }
    }

    [TestMethod]
    public async Task Service_rows_carry_the_System_projection()
    {
        var unique = $"a2-svc-column-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemId = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system");
        var assigned = await Fx.SeedSingleServiceAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-assigned");
        var unassigned = await Fx.SeedSingleServiceAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-unassigned");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Service, assigned, systemId);

            var page = await GetServicePageAsync(client, $"/api/v1/catalog/services?displayNameContains={unique}&limit=200");
            var assignedRow = page.Items.Single(i => i.Id == assigned);
            var unassignedRow = page.Items.Single(i => i.Id == unassigned);

            Assert.AreEqual(systemId, assignedRow.SystemId);
            Assert.AreEqual($"{unique}-system", assignedRow.SystemDisplayName);
            Assert.IsNull(unassignedRow.SystemId, "an unassigned service must carry a null SystemId");
            Assert.IsNull(unassignedRow.SystemDisplayName, "an unassigned service must carry a null SystemDisplayName");
        }
        finally
        {
            await Fx.DeleteServicesByPrefixAsync(tenant, unique);
        }
    }

    // -----------------------------------------------------------------------
    // 6: unknown filter value degrades to empty, never a 500.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Filter_by_unknown_system_id_returns_empty_page_not_error()
    {
        // A genuinely-assigned component must exist in the tenant first: without one, a
        // `systemIds.Contains(...) -> true` mutant (i.e. "has *any* PartOf edge") would also
        // return nothing here and survive undetected.
        var unique = $"a2-unknown-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemC = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-c");
        var assignedApp = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-assigned");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, assignedApp, systemC);

            var unknownSystemId = Guid.NewGuid();
            var page = await GetPageAsync(client, $"/api/v1/catalog/applications?systemId={unknownSystemId}&limit=200");

            Assert.AreEqual(0, page.Items.Count);
            Assert.IsNull(page.NextCursor);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    // -----------------------------------------------------------------------
    // 7 (+ companion): cross-tenant enrichment must never surface a foreign System.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Enrichment_never_surfaces_a_System_from_another_tenant()
    {
        // Replaces the original case 7, which could not fail: filtering tenant A by a
        // tenant-B System id exercises no cross-tenant path at all (tenant A's candidate rows
        // can never carry tenant B's edges), so that assertion would hold even with RLS
        // disabled. Instead, construct the actual dangerous state: a PartOf row with
        // tenant_id = A, source = a tenant-A application, target_id = a tenant-B System.
        // Fx.InsertPartOfEdgeAsync only validates the EntityKind pairing (via
        // Relationship.CreateManual / RelationshipTypeRules.IsAllowedPair) — it does not check
        // that tenantId's source and systemId's owning tenant agree, so it can construct this
        // cross-tenant edge without a new raw-SQL helper.
        var unique = $"a2-xtenant-{Guid.NewGuid():N}";
        var tenantA = Fx.TenantIdForEmail(OrgAUser);
        var tenantB = Fx.TenantIdForEmail("admin@orgb.kartova.local");
        var clientA = Fx.CreateClientForOrgA();

        var teamB = await Fx.SeedTeamInOrganizationAsync(tenantB, $"{unique}-teamB");
        var systemInB = await Fx.SeedSystemAsync(tenantB, teamB, $"{unique}-system-b");

        var teamA = await Fx.SeedTeamInOrganizationAsync(tenantA, $"{unique}-teamA");
        var appInA = await Fx.SeedSingleApplicationAsync(tenantA, Guid.NewGuid(), teamA, $"{unique}-app");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenantA, EntityKind.Application, appInA, systemInB);

            var page = await GetPageAsync(clientA, $"/api/v1/catalog/applications?displayNameContains={unique}&limit=200");
            var row = page.Items.Single(i => i.Id == appInA);

            Assert.IsNull(row.SystemId, "a PartOf edge to another tenant's System must not surface an id");
            Assert.IsNull(row.SystemDisplayName, "the foreign System's display name must not leak");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantA, unique);
        }
    }

    [TestMethod]
    public async Task Filter_by_a_cross_tenant_system_id_is_indistinguishable_from_an_unknown_id()
    {
        // Companion to the case above: the property that makes leaving `systemId` unvalidated
        // safe is that "unknown" and "exists, but in another tenant" are indistinguishable —
        // assert the two responses are byte-identical (status, body, nextCursor), not just
        // separately "empty".
        var tenantB = Fx.TenantIdForEmail("admin@orgb.kartova.local");
        var clientA = Fx.CreateClientForOrgA();
        var teamB = await Fx.SeedTeamInOrganizationAsync(tenantB, $"a2-xfilt-{Guid.NewGuid():N}-team");
        var systemInB = await Fx.SeedSystemAsync(tenantB, teamB, $"a2-xfilt-{Guid.NewGuid():N}-system");
        var unknownSystemId = Guid.NewGuid();

        var respKnownForeign = await clientA.GetAsync($"/api/v1/catalog/applications?systemId={systemInB}&limit=200");
        var respUnknown = await clientA.GetAsync($"/api/v1/catalog/applications?systemId={unknownSystemId}&limit=200");

        Assert.AreEqual(respUnknown.StatusCode, respKnownForeign.StatusCode);
        var bodyForeign = await respKnownForeign.Content.ReadAsStringAsync();
        var bodyUnknown = await respUnknown.Content.ReadAsStringAsync();
        Assert.AreEqual(bodyUnknown, bodyForeign, "a cross-tenant System id must read identically to an unknown one");
    }

    // -----------------------------------------------------------------------
    // 8, 8b, 8c: cursor f-map mismatch — systemId filter key (ADR-0095).
    // Modeled on GET_teamId_cursor_then_changed_teamId_returns_400_cursor_filter_mismatch,
    // NOT on the direction-mismatch (invalid-cursor) test.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Changing_the_system_filter_mid_pagination_returns_400_cursor_filter_mismatch()
    {
        var unique = $"a2-mism-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemA = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-a");
        var systemB = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-b");

        // 3 members of System A so limit=2 yields a NextCursor.
        var a1 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a1");
        var a2 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a2");
        var a3 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a3");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a1, systemA);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a2, systemA);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a3, systemA);

            var page1 = await client.GetAsync($"/api/v1/catalog/applications?limit=2&systemId={systemA}");
            Assert.AreEqual(HttpStatusCode.OK, page1.StatusCode);
            var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(p1!.NextCursor);

            var page2 = await client.GetAsync(
                $"/api/v1/catalog/applications?limit=2&systemId={systemB}&cursor={Uri.EscapeDataString(p1.NextCursor!)}");
            Assert.AreEqual(HttpStatusCode.BadRequest, page2.StatusCode);
            var problem = await page2.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(ProblemTypes.CursorFilterMismatch, problem!.Type);
            Assert.AreEqual("systemId", problem.Extensions["filterName"]!.ToString());
            Assert.AreEqual(systemA.ToString("D"), problem.Extensions["expectedValue"]!.ToString());
            Assert.AreEqual(systemB.ToString("D"), problem.Extensions["actualValue"]!.ToString());
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    [TestMethod]
    public async Task Tampered_f_map_in_the_cursor_fails_closed()
    {
        // The cursor is unsigned base64url JSON; "the f-map is compared, never used as a filter
        // source" is load-bearing and currently unpinned. Tamper the decoded f.systemId value
        // for a different System's id, then replay with the ORIGINAL ?systemId= — the mismatch
        // must still be caught even though nothing about the request itself looks suspicious.
        var unique = $"a2-tamper-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemA = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-a");
        var systemB = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-b");
        var a1 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a1");
        var a2 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a2");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a1, systemA);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a2, systemA);

            var page1 = await client.GetAsync($"/api/v1/catalog/applications?limit=1&systemId={systemA}");
            Assert.AreEqual(HttpStatusCode.OK, page1.StatusCode);
            var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(p1!.NextCursor);

            var tamperedCursor = TamperFilterValue(p1.NextCursor!, "systemId", systemB.ToString("D"));

            var page2 = await client.GetAsync(
                $"/api/v1/catalog/applications?limit=1&systemId={systemA}&cursor={Uri.EscapeDataString(tamperedCursor)}");

            Assert.AreEqual(HttpStatusCode.BadRequest, page2.StatusCode);
            var problem = await page2.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(ProblemTypes.CursorFilterMismatch, problem!.Type);
            Assert.AreEqual("systemId", problem.Extensions["filterName"]!.ToString());
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    [TestMethod]
    public async Task Tampered_f_map_replayed_with_the_matching_systemId_is_not_a_forged_continuation()
    {
        // FINDING (see task-5a-report.md): the brief's literal case-8c expectation ("also 400")
        // does not hold — confirmed empirically, not just by static reading. When the cursor's
        // tampered f.systemId and the live request's own ?systemId= agree (both = B),
        // CursorFilterComparer.FindMismatch does pure ordinal string equality per key: there is
        // nothing left to disagree on, so no CursorFilterMismatchException is thrown and the
        // request succeeds (200). The f-map's job is DRIFT detection between two *different*
        // claimed filter states across a page boundary, not an independent integrity check on
        // the cursor as a whole (the cursor is explicitly unsigned base64url JSON — see case 8b).
        // This is NOT a security defect: the EXISTS predicate always filters on the LIVE
        // request's ?systemId=, never on the cursor's f-map value — asserted below by requiring
        // every returned row to actually belong to System B, proving the request (not the
        // cursor) is the authority for what's returned. The residual gap this leaves is
        // narrower than "also 400": the keyset (sort/id) boundary inherited from a different
        // filter's last row is never re-validated against the new filter, so a hand-crafted
        // cursor like this one gets an unspecified pagination continuity (may skip/duplicate
        // relative to a fresh System-B query) — a self-inflicted correctness quirk, not a
        // cross-tenant or authorization leak.
        var unique = $"a2-tamper2-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemA = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-a");
        var systemB = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-b");
        var a1 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a1");
        var a2 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a2");
        var b1 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-b1");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a1, systemA);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a2, systemA);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, b1, systemB);

            var page1 = await client.GetAsync($"/api/v1/catalog/applications?limit=1&systemId={systemA}");
            Assert.AreEqual(HttpStatusCode.OK, page1.StatusCode);
            var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(p1!.NextCursor);

            var tamperedCursor = TamperFilterValue(p1.NextCursor!, "systemId", systemB.ToString("D"));

            // Replay with ?systemId= matching the tampered value (B) — a self-consistent forgery.
            var resp = await client.GetAsync(
                $"/api/v1/catalog/applications?limit=1&systemId={systemB}&cursor={Uri.EscapeDataString(tamperedCursor)}");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
                "empirically confirmed: no f-map disagreement remains once the live request " +
                "matches the tampered value, so no CursorFilterMismatchException fires");
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page);
            foreach (var item in page!.Items)
            {
                Assert.AreEqual(systemB, item.SystemId,
                    "the live request's ?systemId=, not the cursor's f-map claim, must govern what's returned");
            }
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    // -----------------------------------------------------------------------
    // 8s (Task 5b): the Services mirror of case 8 — NOT optional and NOT covered by the
    // Applications case above. ListServicesHandler allocates its cursor f-map dictionary
    // lazily, guarded by `q.TeamId.Length > 0 || q.Health.Length > 0
    // || q.DisplayNameContains is not null || q.SystemId is { Length: > 0 }`
    // (ListServicesHandler.cs). A `?systemId=`-only request (no teamId/health/
    // displayNameContains) is the one shape that depends on the last disjunct alone: drop it
    // and the dictionary stays null, the cursor carries no f-map at all, and page 2 silently
    // applies the new filter against the old keyset instead of 400ing.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Services_changing_the_system_filter_mid_pagination_returns_400_cursor_filter_mismatch()
    {
        var unique = $"a2-svc-mism-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemA = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-a");
        var systemB = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-b");

        // 3 members of System A so limit=2 yields a NextCursor.
        var s1 = await Fx.SeedSingleServiceAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-s1");
        var s2 = await Fx.SeedSingleServiceAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-s2");
        var s3 = await Fx.SeedSingleServiceAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-s3");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Service, s1, systemA);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Service, s2, systemA);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Service, s3, systemA);

            // ?systemId=-only — no other filter dimension present. This is the exact shape
            // that would emit an f-map-less cursor if the allocation guard's systemId
            // disjunct were ever dropped.
            var page1 = await client.GetAsync($"/api/v1/catalog/services?limit=2&systemId={systemA}");
            Assert.AreEqual(HttpStatusCode.OK, page1.StatusCode);
            var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(p1!.NextCursor);

            var page2 = await client.GetAsync(
                $"/api/v1/catalog/services?limit=2&systemId={systemB}&cursor={Uri.EscapeDataString(p1.NextCursor!)}");
            Assert.AreEqual(HttpStatusCode.BadRequest, page2.StatusCode);
            var problem = await page2.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(ProblemTypes.CursorFilterMismatch, problem!.Type, "the problem type must be cursor-filter-mismatch");
            Assert.AreEqual("systemId", problem.Extensions["filterName"]!.ToString());
            Assert.AreEqual(systemA.ToString("D"), problem.Extensions["expectedValue"]!.ToString());
            Assert.AreEqual(systemB.ToString("D"), problem.Extensions["actualValue"]!.ToString());
        }
        finally
        {
            await Fx.DeleteServicesByPrefixAsync(tenant, unique);
        }
    }

    // -----------------------------------------------------------------------
    // 9-10d: pagination correctness, multi-System pages, dedup/order in the f-map.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Filtered_pagination_does_not_skip_or_duplicate_rows()
    {
        var unique = $"a2-page-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemId = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system");

        var expected = new HashSet<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var appId = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-m{i}");
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, appId, systemId);
            expected.Add(appId);
        }

        try
        {
            var seen = new HashSet<Guid>();
            string? cursor = null;
            var pageCount = 0;
            do
            {
                var url = $"/api/v1/catalog/applications?systemId={systemId}&limit=2"
                    + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
                var page = await GetPageAsync(client, url);
                foreach (var item in page.Items)
                {
                    Assert.IsTrue(seen.Add(item.Id), "each id must appear exactly once across pages");
                    // Enrichment runs once per page, so an id-only assertion would pass even if
                    // page 2 came back unenriched — assert the column on every row of every page.
                    Assert.AreEqual(systemId, item.SystemId);
                    Assert.AreEqual($"{unique}-system", item.SystemDisplayName);
                }
                cursor = page.NextCursor;
                pageCount++;
            } while (cursor is not null && pageCount < 10);

            CollectionAssert.AreEquivalent(expected.ToList(), seen.ToList());
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    [TestMethod]
    public async Task Two_components_in_different_systems_each_carry_their_own_on_one_page()
    {
        // Replaces the original case 10 (whose rationale was wrong: dropping
        // sourceIds.Contains(...) from the batched lookup is an over-fetch, not a behaviour
        // change, since the dictionary is still keyed by ComponentId). This is the only case
        // with two *different* Systems on one page, so it is what kills a genuine mis-keying
        // such as `systems.Values.First()`.
        var unique = $"a2-two-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var system1 = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-1");
        var system2 = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-2");
        var appA = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a");
        var appB = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-b");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, appA, system1);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, appB, system2);

            var page = await GetPageAsync(client, $"/api/v1/catalog/applications?displayNameContains={unique}&limit=200");
            var rowA = page.Items.Single(i => i.Id == appA);
            var rowB = page.Items.Single(i => i.Id == appB);

            Assert.AreEqual($"{unique}-system-1", rowA.SystemDisplayName);
            Assert.AreEqual($"{unique}-system-2", rowB.SystemDisplayName);
            Assert.AreNotEqual(rowA.SystemId, rowB.SystemId);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    [TestMethod]
    public async Task Filtered_and_unfiltered_pages_agree_on_the_System_column()
    {
        var unique = $"a2-agree-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemId = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system");
        var appId = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-app");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, appId, systemId);

            var filtered = await GetPageAsync(client, $"/api/v1/catalog/applications?systemId={systemId}&limit=200");
            var unfiltered = await GetPageAsync(client, $"/api/v1/catalog/applications?displayNameContains={unique}&limit=200");

            var filteredRow = filtered.Items.Single(i => i.Id == appId);
            var unfilteredRow = unfiltered.Items.Single(i => i.Id == appId);

            Assert.AreEqual(filteredRow.SystemId, unfilteredRow.SystemId);
            Assert.AreEqual(filteredRow.SystemDisplayName, unfilteredRow.SystemDisplayName);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    [TestMethod]
    public async Task Repeated_systemId_values_are_deduped_in_the_cursor()
    {
        var unique = $"a2-dedup-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemId = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system");
        var a1 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a1");
        var a2 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a2");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a1, systemId);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a2, systemId);

            var page1 = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
                $"/api/v1/catalog/applications?systemId={systemId}&systemId={systemId}&limit=1",
                KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page1!.NextCursor);

            var resp = await client.GetAsync(
                $"/api/v1/catalog/applications?systemId={systemId}&limit=1&cursor={Uri.EscapeDataString(page1.NextCursor!)}");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
                "a client that normalizes duplicate selections between pages must not get a spurious mismatch");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    [TestMethod]
    public async Task Selection_order_does_not_change_the_cursor()
    {
        var unique = $"a2-order-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemA = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-a");
        var systemB = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system-b");
        var a1 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a1");
        var b1 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-b1");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a1, systemA);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, b1, systemB);

            var pageAB = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
                $"/api/v1/catalog/applications?systemId={systemA}&systemId={systemB}&limit=1",
                KartovaApiFixtureBase.WireJson);
            var pageBA = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
                $"/api/v1/catalog/applications?systemId={systemB}&systemId={systemA}&limit=1",
                KartovaApiFixtureBase.WireJson);

            Assert.IsNotNull(pageAB!.NextCursor);
            Assert.IsNotNull(pageBA!.NextCursor);
            Assert.AreEqual(pageAB.NextCursor, pageBA.NextCursor,
                "the f-map key must be order-independent (OrderBy(..., StringComparer.Ordinal))");

            var replayWithAB = await client.GetAsync(
                $"/api/v1/catalog/applications?systemId={systemA}&systemId={systemB}&limit=1&cursor={Uri.EscapeDataString(pageBA.NextCursor!)}");
            var replayWithBA = await client.GetAsync(
                $"/api/v1/catalog/applications?systemId={systemB}&systemId={systemA}&limit=1&cursor={Uri.EscapeDataString(pageAB.NextCursor!)}");

            Assert.AreEqual(HttpStatusCode.OK, replayWithAB.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, replayWithBA.StatusCode);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    // -----------------------------------------------------------------------
    // 11 (Task 5b): a same-Guid collision between an Application and a Service must not let
    // one kind's PartOf edge leak into the other kind's System column/filter. The only case
    // pinning Source.Kind — calibration: absent a Guid collision this mutant is
    // near-equivalent (the system never produces a shared id across kinds on its own), so this
    // is a defensive test, not a shipped-defect risk.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task An_application_and_a_service_with_the_same_id_do_not_cross_contaminate()
    {
        // Application.Create / Service.Create always mint their own id and their id-taking
        // constructors are private, so Fx.SeedComponentWithIdAsync (raw SQL over BYPASSRLS,
        // mirrors InsertRawRelationshipAsync) is the only way to construct this precondition.
        var unique = $"a2-samekind-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemId = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system");
        var sharedId = Guid.NewGuid();

        await Fx.SeedComponentWithIdAsync(tenant, EntityKind.Application, sharedId, teamId, $"{unique}-app");
        await Fx.SeedComponentWithIdAsync(tenant, EntityKind.Service, sharedId, teamId, $"{unique}-svc");

        try
        {
            // Assign only the Service side.
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Service, sharedId, systemId);

            // Filtering the applications list by this System must not surface the Application
            // row, even though a row with the SAME id is a genuine PartOf member on the
            // Service side — this is what pins the EXISTS predicate's Source.Kind clause.
            var appPage = await GetPageAsync(client, $"/api/v1/catalog/applications?systemId={systemId}&limit=200");
            Assert.IsFalse(appPage.Items.Select(i => i.Id).Contains(sharedId),
                "the Application must not match the systemId filter even though a Service sharing its id is assigned");

            var svcPage = await GetServicePageAsync(client, $"/api/v1/catalog/services?systemId={systemId}&limit=200");
            Assert.IsTrue(svcPage.Items.Select(i => i.Id).Contains(sharedId),
                "the assigned Service must still be returned by its own filter");

            // Unfiltered column contract: same assertion via displayNameContains, so this also
            // pins the enrichment lookup (not just the EXISTS filter) against Source.Kind.
            var unfilteredApp = (await GetPageAsync(client, $"/api/v1/catalog/applications?displayNameContains={unique}-app&limit=200"))
                .Items.Single(i => i.Id == sharedId);
            Assert.IsNull(unfilteredApp.SystemId, "the Application row must carry no System despite the id collision");
            Assert.IsNull(unfilteredApp.SystemDisplayName);

            var unfilteredSvc = (await GetServicePageAsync(client, $"/api/v1/catalog/services?displayNameContains={unique}-svc&limit=200"))
                .Items.Single(i => i.Id == sharedId);
            Assert.AreEqual(systemId, unfilteredSvc.SystemId, "the Service row must carry its own assigned System");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
            await Fx.DeleteServicesByPrefixAsync(tenant, unique);
        }
    }

    // -----------------------------------------------------------------------
    // 12: a stranded PartOf edge whose target is not a System.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Filter_by_a_PartOf_edge_whose_target_is_not_a_System_returns_empty_page()
    {
        var unique = $"a2-stray-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var appId = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-app");

        // A PartOf edge whose target is NOT a System — Relationship.CreateManual rejects this
        // via RelationshipTypeRules.IsAllowedPair (Relationship.cs:33-34), and
        // Fx.InsertPartOfEdgeAsync hard-codes EntityKind.System as the target kind regardless of
        // what's passed. InsertRawRelationshipAsync deliberately bypasses the domain factory
        // BECAUSE the domain forbids the state under test, reproducing the stranded-edge drift
        // the PurgePartOfRelationships migration exists to clean up.
        var strayTargetId = Guid.NewGuid();
        await Fx.InsertRawRelationshipAsync(
            tenant, EntityKind.Application, appId, RelationshipType.PartOf, EntityKind.Api, strayTargetId);

        try
        {
            var filtered = await GetPageAsync(client, $"/api/v1/catalog/applications?systemId={strayTargetId}&limit=200");
            Assert.AreEqual(0, filtered.Items.Count,
                "a PartOf edge to a non-System target must not match the systemId filter");

            var unfilteredRow = (await GetPageAsync(client, $"/api/v1/catalog/applications?displayNameContains={unique}&limit=200"))
                .Items.Single(i => i.Id == appId);
            Assert.IsNull(unfilteredRow.SystemId, "the stray edge must not resolve to a System either");
            Assert.IsNull(unfilteredRow.SystemDisplayName);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    // -----------------------------------------------------------------------
    // 14 (Task 5b): a page where no component has a System renders every row unenriched,
    // never erroring. Must be scoped by a unique prefix, not asserted unfiltered — OrgA is
    // shared across the assembly and other tests in this class (and SetComponentSystemTests)
    // leave assigned components in it.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task A_page_where_no_component_has_a_System_returns_all_rows_unenriched()
    {
        var unique = $"a2-noenrich-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");

        var a1 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a1");
        var a2 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a2");
        var a3 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a3");

        try
        {
            var page = await GetPageAsync(client, $"/api/v1/catalog/applications?displayNameContains={unique}&limit=50");

            CollectionAssert.AreEquivalent(
                new[] { a1, a2, a3 }, page.Items.Select(i => i.Id).ToList(),
                "only the 3 seeded, unassigned apps under this unique prefix must be returned");
            foreach (var item in page.Items)
            {
                Assert.IsNull(item.SystemId, $"{item.Id} has no PartOf edge, so SystemId must be null");
                Assert.IsNull(item.SystemDisplayName, $"{item.Id} has no PartOf edge, so SystemDisplayName must be null");
            }
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    // -----------------------------------------------------------------------
    // 13, 15-17: input validation and the 50-value cap (ADR-0107 MaxFilterValues).
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task Malformed_systemId_returns_400()
    {
        // systemId binds as Guid[]; a non-Guid token must be rejected (a 400), not silently
        // dropped — a silent drop would yield an unfiltered view the user never asked for.
        var client = Fx.CreateClientForOrgA();
        var resp = await client.GetAsync("/api/v1/catalog/applications?systemId=not-a-guid");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task More_than_the_cap_systemId_values_returns_400_too_many_filter_values()
    {
        // The guard is duplicated per list delegate (applications vs. services) — this covers
        // only the applications list; the services list is Task 5b's responsibility.
        var client = Fx.CreateClientForOrgA();
        var ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray();
        var query = string.Join("&", ids.Select(id => $"systemId={id}"));

        var resp = await client.GetAsync($"/api/v1/catalog/applications?{query}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.TooManyFilterValues, problem!.Type);
        var detail = problem.Detail ?? string.Empty;
        StringAssert.Contains(detail, "50", "the detail must name the cap");
        StringAssert.Contains(detail, "51", "the detail must name the supplied count");
    }

    [TestMethod]
    public async Task At_the_cap_the_returned_cursor_is_replayable()
    {
        // Proves the cap was chosen large enough to be usable (a round trip works) and small
        // enough to keep the cursor inside the request line — not just that the rejection test
        // (above) fires past it. Asserts on the round trip, not on nextCursor.Length.
        var unique = $"a2-cap-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var systemId = await Fx.SeedSystemAsync(tenant, teamId, $"{unique}-system");
        var a1 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a1");
        var a2 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a2");

        try
        {
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a1, systemId);
            await Fx.InsertPartOfEdgeAsync(tenant, EntityKind.Application, a2, systemId);

            var ids = new List<Guid> { systemId };
            ids.AddRange(Enumerable.Range(0, 49).Select(_ => Guid.NewGuid()));
            Assert.AreEqual(50, ids.Distinct().Count());
            var query = string.Join("&", ids.Select(id => $"systemId={id}"));

            var page1 = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
                $"/api/v1/catalog/applications?{query}&limit=1", KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page1!.NextCursor);

            var resp = await client.GetAsync(
                $"/api/v1/catalog/applications?{query}&limit=1&cursor={Uri.EscapeDataString(page1.NextCursor!)}");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    [TestMethod]
    public async Task Duplicate_values_do_not_count_toward_the_cap()
    {
        // Pins the ordering of dedup-then-cap: 50 distinct ids, each supplied twice (100
        // params), must pass — because ToHashSet() runs before the > MaxFilterValues check.
        var client = Fx.CreateClientForOrgA();
        var ids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
        var doubled = ids.Concat(ids);
        var query = string.Join("&", doubled.Select(id => $"systemId={id}"));

        var resp = await client.GetAsync($"/api/v1/catalog/applications?{query}");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    /// <summary>
    /// Base64url-decodes a cursor issued by <c>CursorCodec</c>, overwrites its filter-map ("f")
    /// entry for <paramref name="filterKey"/>, and re-encodes — the f-map counterpart to
    /// <see cref="ListApplicationsPaginationTests"/>'s private <c>TamperSortValue</c>, mirroring
    /// the codec's own base64url + JSON wire format (ADR-0095) without depending on its private
    /// <c>CursorPayload</c> record.
    /// </summary>
    private static string TamperFilterValue(string cursor, string filterKey, string tamperedValue)
    {
        var bytes = System.Buffers.Text.Base64Url.DecodeFromChars(cursor.AsSpan());
        var node = System.Text.Json.Nodes.JsonNode.Parse(bytes)!.AsObject();
        var filters = node["f"]!.AsObject();
        filters[filterKey] = tamperedValue;
        var tamperedBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(node);
        return System.Buffers.Text.Base64Url.EncodeToString(tamperedBytes);
    }
}
