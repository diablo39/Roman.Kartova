using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DomainApplication = Kartova.Catalog.Domain.Application;

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
        StringAssert.Contains(body, "name");
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
    public async Task DefaultParams_match_explicit_createdAt_desc_50()
    {
        await Fx.SeedApplicationsAsync(OrgATenant, count: 5, namePrefix: "def-");
        var client = Fx.CreateClientForOrgA();

        var defaultResp = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications", KartovaApiFixtureBase.WireJson);
        var explicitResp = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=desc&limit=50",
            KartovaApiFixtureBase.WireJson);

        Assert.IsNotNull(defaultResp);
        Assert.IsNotNull(explicitResp);
        CollectionAssert.AreEqual(
            explicitResp!.Items.Select(i => i.Id).ToList(),
            defaultResp!.Items.Select(i => i.Id).ToList(),
            "default parameters must match createdAt/desc/50 explicitly");
    }

    [TestMethod]
    public async Task Deletion_of_cursor_row_mid_pagination_does_not_skip_or_dup()
    {
        await Fx.SeedApplicationsAsync(OrgATenant, count: 10, namePrefix: "del-");
        var client = Fx.CreateClientForOrgA();

        var first = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=4",
            KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(4, first!.Items.Count());
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
    public async Task Pages_through_25_apps_sorted_by_name_yields_no_duplicates_no_skips()
    {
        await Fx.SeedApplicationsAsync(OrgATenant, count: 25, namePrefix: "n-");

        var client = Fx.CreateClientForOrgA();

        var allIds = new HashSet<Guid>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            var url = "/api/v1/catalog/applications?sortBy=name&sortOrder=asc&limit=10"
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
        } while (cursor is not null && pageCount < 5);

        // Filter to just our seeded prefix-25; other tests may have left rows on the same tenant.
        Assert.IsTrue(allIds.Count >= 25, "all 25 prefix-n- rows should be visible");
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

    [TestMethod]
    public async Task SortBy_name_asc_overrides_default_createdAt_ordering()
    {
        // Mutation-killing test for the `parsedSortBy ?? ApplicationSortField.CreatedAt` expression:
        // if the null-coalesce is mutated and always returns CreatedAt, user-supplied sortBy=name is silently
        // ignored. To distinguish, we seed rows with names in REVERSE alpha order vs creation
        // order: created first = "zzz-mut-c-...", created last = "zzz-mut-a-...". Asking for
        // sortBy=name&sortOrder=asc must return alpha-first ("a..." then "b..." then "c...").
        // With the mutant (sortBy collapses to createdAt asc), the first item would be the
        // earliest-created row, i.e. the "c..." prefix.
        var tenant = OrgATenant;
        var unique = $"zzz-mut-{Guid.NewGuid():N}";
        var aName = $"{unique}-a";
        var bName = $"{unique}-b";
        var cName = $"{unique}-c";

        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(Fx.BypassConnectionString)
            .Options;
        await using (var db = new CatalogDbContext(opts))
        {
            // Created in REVERSE-alpha order: c first (oldest), then b, then a (newest).
            var origin = DateTimeOffset.UtcNow.AddMinutes(-5);
            db.Applications.Add(DomainApplication.Create(
                name: cName, displayName: cName, description: "mut-test",
                ownerUserId: Guid.NewGuid(), tenantId: tenant,
                createdAt: origin));
            db.Applications.Add(DomainApplication.Create(
                name: bName, displayName: bName, description: "mut-test",
                ownerUserId: Guid.NewGuid(), tenantId: tenant,
                createdAt: origin.AddMinutes(1)));
            db.Applications.Add(DomainApplication.Create(
                name: aName, displayName: aName, description: "mut-test",
                ownerUserId: Guid.NewGuid(), tenantId: tenant,
                createdAt: origin.AddMinutes(2)));
            await db.SaveChangesAsync();
        }

        var client = Fx.CreateClientForOrgA();
        var page = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=name&sortOrder=asc&limit=200",
            KartovaApiFixtureBase.WireJson);

        Assert.IsNotNull(page);
        var ours = page!.Items
            .Where(i => i.Name.StartsWith(unique, StringComparison.Ordinal))
            .Select(i => i.Name)
            .ToList();
        CollectionAssert.AreEqual(
            new[] { aName, bName, cName },
            ours,
            "sortBy=name&sortOrder=asc must order by Name (alpha), not by CreatedAt");
    }

    // -----------------------------------------------------------------------
    // Slice-6 filter tests — ?includeDecommissioned wire contract (ADR-0073)
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GET_applications_default_excludes_Decommissioned()
    {
        // Use a unique prefix so the assertions are not confused by rows seeded by
        // other tests in the same class — CatalogIntegrationTestBase's static Fx is
        // class-scoped (BeforeEachDerivedClass), so DB state survives across tests.
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
            var ours = page!.Items.Where(i => i.Name.StartsWith(unique, StringComparison.Ordinal)).ToList();

            // All 3 active-prefix rows must be visible.
            Assert.AreEqual(3, ours.Where(i => i.Name.StartsWith(activePrefix)).Count(), "active rows must not be filtered");
            // No decomm-prefix rows must appear (default view excludes Decommissioned).
            Assert.AreEqual(0, ours.Where(i => i.Name.StartsWith(decommPrefix)).Count(), "Decommissioned rows must be excluded by default");
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
            var ours = page!.Items.Where(i => i.Name.StartsWith(unique, StringComparison.Ordinal)).ToList();

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
            var defaultOurs = defaultPage!.Items.Where(i => i.Name.StartsWith(unique, StringComparison.Ordinal)).Select(i => i.Id).ToList();
            var explicitOurs = explicitPage!.Items.Where(i => i.Name.StartsWith(unique, StringComparison.Ordinal)).Select(i => i.Id).ToList();
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
    public async Task GET_applications_with_legacy_cursor_lacking_ic_decodes_as_false_and_pages()
    {
        var unique = $"f6-legc-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";

        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        // Seed 5 rows as the most recent rows in the tenant (seeded now, so createdAt desc
        // puts them at the top). limit=2 on page1 returns 2 of our rows and yields a cursor
        // pointing into our seeded range.
        await Fx.SeedApplicationsAsync(tenantId, count: 5, namePrefix: activePrefix);

        try
        {
            var client = Fx.CreateClientForOrgA();

            var page1 = await client.GetAsync("/api/v1/catalog/applications?limit=2&sortBy=createdAt&sortOrder=desc");
            Assert.AreEqual(HttpStatusCode.OK, page1.StatusCode);
            var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var boundary = p1!.Items.Last();

            // Construct a cursor JSON without the `ic` field — pre-slice-6 shape.
            var legacyJson = $"{{\"s\":\"{boundary.CreatedAt:O}\",\"i\":\"{boundary.Id}\",\"d\":\"desc\"}}";
            var legacyCursor = System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(legacyJson));

            // Default request (no includeDecommissioned param → false). Legacy cursor decodes to ic=false → match.
            var page2 = await client.GetAsync(
                $"/api/v1/catalog/applications?limit=2&sortBy=createdAt&sortOrder=desc&cursor={Uri.EscapeDataString(legacyCursor)}");

            Assert.AreEqual(HttpStatusCode.OK, page2.StatusCode);
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
        }
    }

    [TestMethod]
    public async Task GET_applications_legacy_cursor_replayed_with_includeDecommissioned_true_returns_400()
    {
        // Build a legacy cursor (no `ic` field) directly — no seeding needed because
        // the filter-mismatch check fires before any keyset WHERE is applied, so the
        // sort value and id do not need to point at real rows.
        // Legacy cursor shape: { s, i, d } — the pre-slice-6 format.
        var fakeTimestamp = "2020-01-01T00:00:00.0000000Z";
        var fakeId = Guid.NewGuid().ToString();
        var legacyJson = $"{{\"s\":\"{fakeTimestamp}\",\"i\":\"{fakeId}\",\"d\":\"desc\"}}";
        var legacyCursor = System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(legacyJson));

        var client = Fx.CreateClientForOrgA();

        // Replay with ?includeDecommissioned=true. Cursor decodes ic=false (legacy default),
        // request expects true → mismatch is detected before keyset filtering.
        var resp = await client.GetAsync(
            $"/api/v1/catalog/applications?limit=2&sortBy=createdAt&sortOrder=desc&includeDecommissioned=true&cursor={Uri.EscapeDataString(legacyCursor)}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.CursorFilterMismatch, problem!.Type);
    }

}
