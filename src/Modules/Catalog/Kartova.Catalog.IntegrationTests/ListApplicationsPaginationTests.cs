using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Pagination + sort + RLS integration tests for
/// GET /api/v1/catalog/applications (ADR-0095).
/// Uses the shared <see cref="KartovaApiFixture"/> + Testcontainers PostgreSQL
/// fixture, which owns its own Postgres container and EF migrations.
/// </summary>
[TestClass]
public sealed class ListApplicationsPaginationTests : CatalogIntegrationTestBase
{
    // Stable test tenant ids derived from the same email-domain algorithm the
    // JWT signer uses, so seeded rows are visible through the RLS filter.
    private static readonly TenantId OrgATenant = KartovaApiFixtureBase.TenantFor("admin@orga.kartova.local");
    private static readonly TenantId OrgBTenant = KartovaApiFixtureBase.TenantFor("admin@orgb.kartova.local");

    [TestMethod]
    public async Task Pages_through_75_apps_in_batches_of_50_yields_no_duplicates_no_skips()
    {
        await Fx.SeedApplicationsAsync(OrgATenant, count: 75, namePrefix: "pg-a-");
        await Fx.SeedApplicationsAsync(OrgBTenant, count: 75, namePrefix: "pg-b-");

        var client = Fx.CreateClientForOrgA();

        var allIds = new HashSet<Guid>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            // Wire-contract camelCase values (ADR-0095).
            var url = "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=50"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var resp = await client.GetAsync(url);
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page);
            foreach (var item in page!.Items)
            {
                Assert.IsTrue(allIds.Add(item.Id), "each id must appear exactly once");
            }
            cursor = page.NextCursor;
            pageCount++;
        } while (cursor is not null && pageCount < 10);

        // OrgA seeded at least 75 rows; RLS hides OrgB's rows entirely.
        // The test may see more than 75 if other tests seeded OrgA rows first,
        // so we assert >= 75 and that no OrgB rows leaked in.
        Assert.IsTrue(allIds.Count >= 75, "at least OrgA's 75 seeded rows must be visible");
    }

    [TestMethod]
    public async Task CamelCase_sortBy_value_binds_correctly()
    {
        // Wire-contract test (ADR-0095): the OpenAPI spec emits camelCase enum values,
        // so frontend clients and third-party consumers send ?sortBy=createdAt&sortOrder=desc.
        // The endpoint accepts strings and parses them case-insensitively, so camelCase,
        // PascalCase, and numeric values (for out-of-range detection) all work.
        var client = Fx.CreateClientForOrgA();
        var resp = await client.GetAsync("/api/v1/catalog/applications?sortBy=createdAt&sortOrder=desc");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task OutOfRange_numeric_sortBy_returns_400_invalid_sort_field()
    {
        // Numeric strings like "999" are parsed by Enum.TryParse into an undefined
        // enum value, which passes binding but then falls through to the _ case in
        // ApplicationSortSpecs.Resolve. That throws InvalidSortFieldException →
        // PagingExceptionHandler → RFC 7807 400 with our "invalid-sort-field" type.
        var client = Fx.CreateClientForOrgA();
        var resp = await client.GetAsync("/api/v1/catalog/applications?sortBy=999");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "invalid-sort-field");
        StringAssert.Contains(body, "createdAt");
    }

    [TestMethod]
    public async Task UnknownString_sortBy_returns_400_invalid_sort_field()
    {
        var client = Fx.CreateClientForOrgA();

        var resp = await client.GetAsync("/api/v1/catalog/applications?sortBy=garbage");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "invalid-sort-field");
    }

    [TestMethod]
    public async Task UnknownString_sortOrder_returns_400_invalid_sort_order()
    {
        var client = Fx.CreateClientForOrgA();

        var resp = await client.GetAsync("/api/v1/catalog/applications?sortOrder=upward");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "invalid-sort-order");
    }

    [TestMethod]
    [DataRow("999")]
    [DataRow("-1")]
    public async Task OutOfRange_numeric_sortOrder_returns_400_invalid_sort_order(string raw)
    {
        // Symmetric with OutOfRange_numeric_sortBy: Enum.TryParse accepts numeric strings
        // and binds them to undefined enum values that would otherwise silently fall through
        // to the desc branch. The Enum.IsDefined check rejects them as invalid-sort-order.
        var client = Fx.CreateClientForOrgA();

        var resp = await client.GetAsync($"/api/v1/catalog/applications?sortOrder={raw}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "invalid-sort-order");
    }

    [TestMethod]
    public async Task TamperedCursor_returns_400_invalid_cursor()
    {
        var client = Fx.CreateClientForOrgA();

        var resp = await client.GetAsync("/api/v1/catalog/applications?cursor=not-a-valid-cursor!!!");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        StringAssert.Contains(await resp.Content.ReadAsStringAsync(), "invalid-cursor");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(201)]
    [DataRow(-1)]
    public async Task LimitOutOfRange_returns_400(int limit)
    {
        var client = Fx.CreateClientForOrgA();

        var resp = await client.GetAsync($"/api/v1/catalog/applications?limit={limit}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "invalid-limit");
    }

    [TestMethod]
    [DataRow("abc")]
    [DataRow("1.5")]
    [DataRow("")]
    public async Task LimitNonInteger_returns_400_invalid_limit(string raw)
    {
        // Non-integer ?limit values must surface the same RFC 7807 invalid-limit
        // envelope as out-of-range numerics, not a framework-generated parse-error
        // 400. Symmetric with sortOrder=999 / sortBy=999 handling.
        var client = Fx.CreateClientForOrgA();

        var resp = await client.GetAsync($"/api/v1/catalog/applications?limit={raw}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "invalid-limit");
    }

    [TestMethod]
    public async Task SortBy_displayName_asc_orders_rows_lexicographically()
    {
        // Positive case for the new ADR-0095 displayName sortable column: seed three rows
        // with deterministic display names (suffixed by index, padded to D3 so lexicographic
        // == numeric), then assert ?sortBy=displayName&sortOrder=asc returns them in order.
        // A unique prefix isolates this test from other rows in the shared tenant database.
        var unique = $"dn-asc-{Guid.NewGuid():N}";
        var prefix = $"{unique}-";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        await Fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: prefix);

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync(
                $"/api/v1/catalog/applications?sortBy=displayName&sortOrder=asc&limit=200");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items
                .Where(i => i.DisplayName.StartsWith(unique, StringComparison.Ordinal))
                .Select(i => i.DisplayName)
                .ToList();

            CollectionAssert.AreEqual(
                new[] { $"{prefix}000", $"{prefix}001", $"{prefix}002" },
                ours,
                "rows must be returned in ascending displayName order");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, prefix);
        }
    }

    [TestMethod]
    public async Task DefaultParams_match_explicit_displayName_asc_50()
    {
        // ADR-0107 / Task-3: default sort flipped from createdAt/desc to displayName/asc.
        await Fx.SeedApplicationsAsync(OrgATenant, count: 5, namePrefix: "def-");
        var client = Fx.CreateClientForOrgA();

        var defaultResp = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications", KartovaApiFixtureBase.WireJson);
        var explicitResp = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=displayName&sortOrder=asc&limit=50",
            KartovaApiFixtureBase.WireJson);

        Assert.IsNotNull(defaultResp);
        Assert.IsNotNull(explicitResp);
        CollectionAssert.AreEqual(
            explicitResp!.Items.Select(i => i.Id).ToList(),
            defaultResp!.Items.Select(i => i.Id).ToList(),
            "default parameters must match displayName/asc/50 explicitly");
    }

    [TestMethod]
    public async Task Deletion_of_cursor_row_mid_pagination_does_not_skip_or_dup()
    {
        await Fx.SeedApplicationsAsync(OrgATenant, count: 10, namePrefix: "del-");
        var client = Fx.CreateClientForOrgA();

        var first = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=4",
            KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(4, first!.Items.Count);
        Assert.IsNotNull(first.NextCursor);

        await Fx.DeleteApplicationAsync(OrgATenant, first.Items[^1].Id);

        var second = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            $"/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=4&cursor={Uri.EscapeDataString(first.NextCursor!)}",
            KartovaApiFixtureBase.WireJson);

        Assert.IsNotNull(second);
        var firstIds = first.Items.Select(i => i.Id).ToHashSet();
        Assert.IsFalse(
            second!.Items.Select(i => i.Id).Any(firstIds.Contains),
            "no row from the first page should reappear after the cursor row was deleted");
    }

    [TestMethod]
    public async Task Cursor_issued_for_asc_replayed_with_desc_returns_400_invalid_cursor()
    {
        await Fx.SeedApplicationsAsync(OrgATenant, count: 5, namePrefix: "dir-");

        var client = Fx.CreateClientForOrgA();

        // Generate a cursor with ascending direction.
        var ascResp = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=2",
            KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(ascResp!.NextCursor);

        // Replay the same cursor with desc — should 400.
        var mismatchUrl = $"/api/v1/catalog/applications?sortBy=createdAt&sortOrder=desc&limit=2&cursor={Uri.EscapeDataString(ascResp.NextCursor!)}";
        var mismatchResp = await client.GetAsync(mismatchUrl);

        Assert.AreEqual(HttpStatusCode.BadRequest, mismatchResp.StatusCode);
        var body = await mismatchResp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "invalid-cursor");
    }

    [TestMethod]
    public async Task NonNumericLimit_returns_400_with_raw_value_in_envelope()
    {
        // Mutation-killing test for the `throw new InvalidLimitException(rawLimit, ...)` branch:
        // if that throw is removed, effectiveLimit defaults to 0 (the failed TryParse out-value),
        // and the handler later throws InvalidLimitException(0, MinLimit, MaxLimit). Both produce a
        // 400 with type=invalid-limit, so a generic body.Contains("invalid-limit") cannot
        // distinguish them. Pin the rawLimit echo back: only the original throw preserves
        // the literal "abc" in the envelope; the fall-through path would echo "0".
        var client = Fx.CreateClientForOrgA();

        var resp = await client.GetAsync("/api/v1/catalog/applications?limit=abc");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, "invalid-limit");
        StringAssert.Contains(body, "\"rawLimit\":\"abc\"");
    }

    // -----------------------------------------------------------------------
    // Default-view / lifecycle exclusion (ADR-0073).
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GET_applications_default_excludes_Decommissioned()
    {
        // Use a unique prefix; rows from earlier tests in the assembly survive in
        // the shared DB (the assembly-scoped Fx in IntegrationTestAssemblySetup keeps
        // one Postgres container alive across all test classes).
        var unique = $"f6-excl-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";

        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        await Fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await Fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);

        try
        {
            var client = Fx.CreateClientForOrgA();
            // Retrieve enough rows to see all our seeded items and use the unique prefix to filter.
            var response = await client.GetAsync("/api/v1/catalog/applications?limit=200");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var page = await response.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.Where(i => i.DisplayName.StartsWith(unique, StringComparison.Ordinal)).ToList();

            // All 3 active-prefix rows must be visible.
            Assert.AreEqual(3, ours.Where(i => i.DisplayName.StartsWith(activePrefix)).Count(), "active rows must not be filtered");
            // No decomm-prefix rows must appear (default view excludes Decommissioned).
            Assert.AreEqual(0, ours.Where(i => i.DisplayName.StartsWith(decommPrefix)).Count(), "Decommissioned rows must be excluded by default");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    // -----------------------------------------------------------------------
    // Slice — displayName filter (ADR-0107). Real seam: real Postgres/RLS + JWT.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GET_with_displayNameContains_returns_only_matching_applications()
    {
        var unique = $"flt-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var creator = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: "Filter Creator", email: $"{unique}@orga.kartova.local");

        var match1 = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-pay-1");
        var match2 = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-pay-2");
        var other  = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-ship");

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync($"/api/v1/catalog/applications?displayNameContains={unique}-PAY&limit=200");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ids = page!.Items.Select(i => i.Id).ToHashSet();

            Assert.IsTrue(ids.Contains(match1) && ids.Contains(match2), "both *-pay-* apps must match (case-insensitive)");
            Assert.IsFalse(ids.Contains(other), "non-matching *-ship app must be excluded");
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(creator);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
        }
    }

    [TestMethod]
    public async Task GET_default_sort_orders_matching_rows_by_displayName_ascending()
    {
        // Asserts the default sort (no sortBy) orders matching rows ascending. The GLOBAL
        // default-params == displayName/asc/50 contract is proven by DefaultParams_match_explicit_displayName_asc_50.
        var unique = $"dsort-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var creator = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: "Sort Creator", email: $"{unique}@orga.kartova.local");

        await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-zzz");
        await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-aaa");
        await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: $"{unique}-mmm");

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync($"/api/v1/catalog/applications?displayNameContains={unique}&limit=200");
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var seeded = page!.Items.Select(i => i.DisplayName).Where(n => n.StartsWith(unique)).ToList();
            var expected = seeded.OrderBy(n => n, StringComparer.Ordinal).ToList();
            CollectionAssert.AreEqual(expected, seeded, "default order must be ascending displayName");
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(creator);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
        }
    }

    // -----------------------------------------------------------------------
    // Lifecycle multi-select filter (ADR-0107) — real seam: real Postgres/RLS + JWT.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GET_with_lifecycle_decommissioned_reveals_decommissioned_rows()
    {
        var unique = $"lc-dec-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        await Fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await Fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);
        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync("/api/v1/catalog/applications?limit=200&lifecycle=decommissioned");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.Where(i => i.DisplayName.StartsWith(unique, StringComparison.Ordinal)).ToList();
            Assert.AreEqual(2, ours.Count(i => i.DisplayName.StartsWith(decommPrefix)), "decommissioned rows are revealed by ?lifecycle=decommissioned");
            Assert.AreEqual(0, ours.Count(i => i.DisplayName.StartsWith(activePrefix)), "active rows are excluded when only decommissioned is selected");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    [TestMethod]
    public async Task GET_with_all_lifecycles_returns_active_and_decommissioned()
    {
        var unique = $"lc-all-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        await Fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await Fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);
        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync("/api/v1/catalog/applications?limit=200&lifecycle=active&lifecycle=deprecated&lifecycle=decommissioned");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.Where(i => i.DisplayName.StartsWith(unique, StringComparison.Ordinal)).ToList();
            Assert.AreEqual(5, ours.Count, "selecting all lifecycles returns every seeded row");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    [TestMethod]
    public async Task GET_with_invalid_lifecycle_token_returns_400_invalid_lifecycle_filter()
    {
        var client = Fx.CreateClientForOrgA();
        var resp = await client.GetAsync("/api/v1/catalog/applications?lifecycle=garbage");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        StringAssert.Contains(await resp.Content.ReadAsStringAsync(), "invalid-lifecycle-filter");
    }

    // -----------------------------------------------------------------------
    // Team multi-select filter (ADR-0107).
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GET_with_teamId_filters_to_that_team()
    {
        var unique = $"tm-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var teamA = await Fx.SeedTeamInOrganizationAsync(tenantId, $"{unique}-A");
        var teamB = await Fx.SeedTeamInOrganizationAsync(tenantId, $"{unique}-B");
        var creator = await Fx.SeedUserInOrganizationAsync(tenantId, displayName: "Team Filter Creator", email: $"{unique}@orga.kartova.local");
        var inA = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: teamA, namePrefix: $"{unique}-a");
        var inB = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: teamB, namePrefix: $"{unique}-b");
        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync($"/api/v1/catalog/applications?limit=200&teamId={teamA}");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ids = page!.Items.Select(i => i.Id).ToHashSet();
            Assert.IsTrue(ids.Contains(inA), "app in team A is returned");
            Assert.IsFalse(ids.Contains(inB), "app in team B is excluded");
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(creator);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
        }
    }

    [TestMethod]
    public async Task GET_with_multiple_teamId_returns_union()
    {
        var unique = $"tm2-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var teamA = await Fx.SeedTeamInOrganizationAsync(tenantId, $"{unique}-A");
        var teamB = await Fx.SeedTeamInOrganizationAsync(tenantId, $"{unique}-B");
        var creator = await Fx.SeedUserInOrganizationAsync(tenantId, displayName: "Union Creator", email: $"{unique}@orga.kartova.local");
        var inA = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: teamA, namePrefix: $"{unique}-a");
        var inB = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: teamB, namePrefix: $"{unique}-b");
        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync($"/api/v1/catalog/applications?limit=200&teamId={teamA}&teamId={teamB}");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ids = page!.Items.Select(i => i.Id).ToHashSet();
            Assert.IsTrue(ids.Contains(inA) && ids.Contains(inB), "both teams' apps are returned");
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(creator);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
        }
    }

    // -----------------------------------------------------------------------
    // Cursor f-map mismatch — lifecycle filter key (ADR-0095 amendment).
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GET_lifecycle_cursor_then_changed_lifecycle_returns_400_cursor_filter_mismatch()
    {
        var unique = $"lc-mism-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        await Fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await Fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);
        try
        {
            var client = Fx.CreateClientForOrgA();
            // Page 1 selects all three lifecycles → f-map "Active,Decommissioned,Deprecated" (sorted).
            var page1 = await client.GetAsync("/api/v1/catalog/applications?limit=2&lifecycle=active&lifecycle=deprecated&lifecycle=decommissioned");
            Assert.AreEqual(HttpStatusCode.OK, page1.StatusCode);
            var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(p1!.NextCursor);

            // Page 2 narrows to just active → mismatch on the "lifecycle" filter key.
            var page2 = await client.GetAsync(
                $"/api/v1/catalog/applications?limit=2&lifecycle=active&cursor={Uri.EscapeDataString(p1.NextCursor!)}");
            Assert.AreEqual(HttpStatusCode.BadRequest, page2.StatusCode);
            var problem = await page2.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(ProblemTypes.CursorFilterMismatch, problem!.Type);
            Assert.AreEqual("lifecycle", problem.Extensions["filterName"]!.ToString());
            Assert.AreEqual("Active,Decommissioned,Deprecated", problem.Extensions["expectedValue"]!.ToString());
            Assert.AreEqual("Active", problem.Extensions["actualValue"]!.ToString());
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    // -----------------------------------------------------------------------
    // Combined lifecycle-default-view + team filter composition (ADR-0107).
    // Proves that "exclude Decommissioned by default" and "filter by team" compose.
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GET_teamId_filter_with_default_view_excludes_decommissioned_and_other_teams()
    {
        var unique = $"comp-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var teamA = await Fx.SeedTeamInOrganizationAsync(tenantId, $"{unique}-A");
        var teamB = await Fx.SeedTeamInOrganizationAsync(tenantId, $"{unique}-B");
        var creator = await Fx.SeedUserInOrganizationAsync(tenantId, displayName: "Compose Creator", email: $"{unique}@orga.kartova.local");

        // Seed: Decommissioned in TeamA, Active in TeamA, Active in TeamB.
        var decommInA = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: teamA, namePrefix: $"{unique}-decomm-a");
        await Fx.SetApplicationLifecycleAsync(decommInA, Lifecycle.Decommissioned);
        var activeInA = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: teamA, namePrefix: $"{unique}-active-a");
        var activeInB = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: teamB, namePrefix: $"{unique}-active-b");

        try
        {
            var client = Fx.CreateClientForOrgA();
            // No lifecycle param → default view (exclude Decommissioned). Team filter = TeamA only.
            var resp = await client.GetAsync($"/api/v1/catalog/applications?teamId={teamA}&limit=200");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ids = page!.Items.Select(i => i.Id).ToHashSet();

            Assert.IsTrue(ids.Contains(activeInA), "Active app in TeamA must be returned");
            Assert.IsFalse(ids.Contains(decommInA), "Decommissioned app in TeamA must be excluded by default-view");
            Assert.IsFalse(ids.Contains(activeInB), "Active app in TeamB must be excluded by team filter");
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(creator);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
        }
    }
}
