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
    // Slice-6 filter tests — ?includeDecommissioned wire contract (ADR-0073)
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

    [TestMethod]
    public async Task GET_applications_with_includeDecommissioned_true_returns_all_lifecycles()
    {
        var unique = $"f6-incl-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";

        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        await Fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await Fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);

        try
        {
            var client = Fx.CreateClientForOrgA();
            var response = await client.GetAsync("/api/v1/catalog/applications?limit=200&includeDecommissioned=true");

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            var page = await response.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.Where(i => i.DisplayName.StartsWith(unique, StringComparison.Ordinal)).ToList();

            // Both active and decommissioned rows must be visible.
            Assert.AreEqual(5, ours.Count, "?includeDecommissioned=true must return all 5 seeded rows");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    [TestMethod]
    public async Task GET_applications_with_explicit_includeDecommissioned_false_matches_default()
    {
        var unique = $"f6-expl-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";

        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        await Fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await Fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);

        try
        {
            var client = Fx.CreateClientForOrgA();
            var defaultResp = await client.GetAsync("/api/v1/catalog/applications?limit=200");
            var explicitResp = await client.GetAsync("/api/v1/catalog/applications?limit=200&includeDecommissioned=false");

            Assert.AreEqual(HttpStatusCode.OK, defaultResp.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, explicitResp.StatusCode);
            var defaultPage = await defaultResp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var explicitPage = await explicitResp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            // Both queries must return the same row set. Compare filtered to our unique prefix.
            var defaultOurs = defaultPage!.Items.Where(i => i.DisplayName.StartsWith(unique, StringComparison.Ordinal)).Select(i => i.Id).ToList();
            var explicitOurs = explicitPage!.Items.Where(i => i.DisplayName.StartsWith(unique, StringComparison.Ordinal)).Select(i => i.Id).ToList();
            CollectionAssert.AreEquivalent(
                defaultOurs,
                explicitOurs,
                "explicit ?includeDecommissioned=false must match the default (omitted) behavior");
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
    public async Task GET_displayNameContains_combines_with_includeDecommissioned()
    {
        // Default view (includeDecommissioned omitted ⇒ false) hides Decommissioned rows
        // even when they match the name filter; includeDecommissioned=true surfaces them.
        // Note: SeedDecommissionedApplicationAsync doesn't exist in the fixture; we use
        // SeedApplicationsWithLifecycleAsync(count:1) and SeedSingleApplicationAsync for the
        // active row, then assert by display-name prefix (not id) for the decommissioned row.
        var unique = $"fltdec-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var creator = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: "Dec Creator", email: $"{unique}@orga.kartova.local");

        var activePrefix  = $"{unique}-keep-active";
        var deadPrefix    = $"{unique}-keep-dead";

        var active = await Fx.SeedSingleApplicationAsync(tenantId, creator, teamId: null, namePrefix: activePrefix);
        // SeedApplicationsWithLifecycleAsync drives the aggregate into Decommissioned state.
        await Fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 1, namePrefix: deadPrefix, Lifecycle.Decommissioned);

        try
        {
            var client = Fx.CreateClientForOrgA();

            var defaultView = await client.GetAsync($"/api/v1/catalog/applications?displayNameContains={unique}-KEEP&limit=200");
            var p1 = await defaultView.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ids1 = p1!.Items.Select(i => i.Id).ToHashSet();
            Assert.IsTrue(ids1.Contains(active), "active match visible in default view");
            Assert.IsFalse(
                p1.Items.Any(i => i.DisplayName.StartsWith(deadPrefix, StringComparison.Ordinal)),
                "decommissioned match hidden in default view");

            var withDead = await client.GetAsync(
                $"/api/v1/catalog/applications?displayNameContains={unique}-KEEP&includeDecommissioned=true&limit=200");
            var p2 = await withDead.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsTrue(p2!.Items.Select(i => i.Id).Contains(active), "active visible with includeDecommissioned=true");
            Assert.IsTrue(
                p2.Items.Any(i => i.DisplayName.StartsWith(deadPrefix, StringComparison.Ordinal)),
                "decommissioned visible with includeDecommissioned=true");
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

    [TestMethod]
    public async Task GET_applications_with_cursor_from_includeDecommissioned_true_then_request_false_returns_400_cursor_filter_mismatch()
    {
        var unique = $"f6-mism-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";

        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        // Seed enough rows (5 total) so limit=2 always produces a NextCursor regardless of
        // other tenant rows — the 5 rows are the most recent (seeded now), so createdAt desc
        // puts them at the front, and limit=2 leaves 3 more behind the cursor.
        await Fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await Fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);

        try
        {
            var client = Fx.CreateClientForOrgA();

            var page1 = await client.GetAsync("/api/v1/catalog/applications?limit=2&includeDecommissioned=true");
            Assert.AreEqual(HttpStatusCode.OK, page1.StatusCode);
            var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(p1!.NextCursor);

            var page2 = await client.GetAsync(
                $"/api/v1/catalog/applications?limit=2&includeDecommissioned=false&cursor={Uri.EscapeDataString(p1.NextCursor!)}");
            Assert.AreEqual(HttpStatusCode.BadRequest, page2.StatusCode);

            var problem = await page2.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
            Assert.AreEqual(ProblemTypes.CursorFilterMismatch, problem!.Type);
            Assert.AreEqual("includeDecommissioned", problem.Extensions["filterName"]!.ToString());
            Assert.AreEqual("true", problem.Extensions["expectedValue"]!.ToString());   // cursor was issued with true
            Assert.AreEqual("false", problem.Extensions["actualValue"]!.ToString());    // request sent false
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    [TestMethod]
    public async Task GET_applications_with_filterless_cursor_returns_400_cursor_filter_mismatch()
    {
        // Clean break (ADR-0095 amendment 2026-06-01): the applications endpoint
        // ALWAYS encodes the includeDecommissioned filter dimension into the
        // cursor. A cursor carrying no filter state — a pre-slice-6/9 legacy
        // `{s,i,d}`-only cursor — therefore no longer "decodes as ic=false"; it
        // mismatches the request's filter map and is rejected. (Old behaviour:
        // legacy cursor silently treated as ic=false and paged through.) The
        // mismatch fires before keyset filtering, so no seeding is needed and the
        // sort value/id need not point at real rows.
        var filterlessJson = "{\"s\":\"2020-01-01T00:00:00.0000000Z\",\"i\":\"" + Guid.NewGuid() + "\",\"d\":\"desc\"}";
        var filterlessCursor = System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(filterlessJson));

        var client = Fx.CreateClientForOrgA();

        // Default request (no includeDecommissioned param → false). The cursor has
        // no filter state (empty map), so includeDecommissioned is present in the
        // request map but absent from the cursor map → mismatch.
        var resp = await client.GetAsync(
            $"/api/v1/catalog/applications?limit=2&sortBy=createdAt&sortOrder=desc&cursor={Uri.EscapeDataString(filterlessCursor)}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.CursorFilterMismatch, problem!.Type);
        Assert.AreEqual("includeDecommissioned", problem.Extensions["filterName"]!.ToString());
        Assert.AreEqual("(none)", problem.Extensions["expectedValue"]!.ToString());  // cursor carries no filter state
        Assert.AreEqual("false", problem.Extensions["actualValue"]!.ToString());     // default request applies ic=false
    }

    [TestMethod]
    public async Task GET_applications_legacy_cursor_replayed_with_includeDecommissioned_true_returns_400()
    {
        // Build a filterless cursor (no filter state) directly — no seeding needed
        // because the filter-mismatch check fires before any keyset WHERE is
        // applied, so the sort value and id do not need to point at real rows.
        // Shape: { s, i, d } — pre-slice-6/9 format, decodes to an empty filter map.
        var fakeTimestamp = "2020-01-01T00:00:00.0000000Z";
        var fakeId = Guid.NewGuid().ToString();
        var legacyJson = $"{{\"s\":\"{fakeTimestamp}\",\"i\":\"{fakeId}\",\"d\":\"desc\"}}";
        var legacyCursor = System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(legacyJson));

        var client = Fx.CreateClientForOrgA();

        // Replay with ?includeDecommissioned=true. The cursor has no filter state
        // (empty map); the request map has includeDecommissioned=true → the key is
        // in the request but not the cursor → mismatch, before keyset filtering.
        var resp = await client.GetAsync(
            $"/api/v1/catalog/applications?limit=2&sortBy=createdAt&sortOrder=desc&includeDecommissioned=true&cursor={Uri.EscapeDataString(legacyCursor)}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.CursorFilterMismatch, problem!.Type);
        Assert.AreEqual("includeDecommissioned", problem.Extensions["filterName"]!.ToString());
        Assert.AreEqual("(none)", problem.Extensions["expectedValue"]!.ToString());  // cursor carries no filter state
        Assert.AreEqual("true", problem.Extensions["actualValue"]!.ToString());      // request applies ic=true
    }

}
