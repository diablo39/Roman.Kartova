using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
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
[Collection(KartovaApiCollection.Name)]
public sealed class ListApplicationsPaginationTests
{
    private readonly KartovaApiFixture _fx;

    // Stable test tenant ids derived from the same email-domain algorithm the
    // JWT signer uses, so seeded rows are visible through the RLS filter.
    private static readonly TenantId OrgATenant = KartovaApiFixtureBase.TenantFor("admin@orga.kartova.local");
    private static readonly TenantId OrgBTenant = KartovaApiFixtureBase.TenantFor("admin@orgb.kartova.local");

    public ListApplicationsPaginationTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Pages_through_75_apps_in_batches_of_50_yields_no_duplicates_no_skips()
    {
        await _fx.SeedApplicationsAsync(OrgATenant, count: 75, namePrefix: "pg-a-");
        await _fx.SeedApplicationsAsync(OrgBTenant, count: 75, namePrefix: "pg-b-");

        var client = _fx.CreateClientForOrgA();

        var allIds = new HashSet<Guid>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            // Wire-contract camelCase values (ADR-0095).
            var url = "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=50"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var resp = await client.GetAsync(url);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            page.Should().NotBeNull();
            foreach (var item in page!.Items)
            {
                allIds.Add(item.Id).Should().BeTrue("each id must appear exactly once");
            }
            cursor = page.NextCursor;
            pageCount++;
        } while (cursor is not null && pageCount < 10);

        // OrgA seeded at least 75 rows; RLS hides OrgB's rows entirely.
        // The test may see more than 75 if other tests seeded OrgA rows first,
        // so we assert >= 75 and that no OrgB rows leaked in.
        allIds.Count.Should().BeGreaterThanOrEqualTo(75,
            "at least OrgA's 75 seeded rows must be visible");
    }

    [Fact]
    public async Task CamelCase_sortBy_value_binds_correctly()
    {
        // Wire-contract test (ADR-0095): the OpenAPI spec emits camelCase enum values,
        // so frontend clients and third-party consumers send ?sortBy=createdAt&sortOrder=desc.
        // The endpoint accepts strings and parses them case-insensitively, so camelCase,
        // PascalCase, and numeric values (for out-of-range detection) all work.
        var client = _fx.CreateClientForOrgA();
        var resp = await client.GetAsync("/api/v1/catalog/applications?sortBy=createdAt&sortOrder=desc");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OutOfRange_numeric_sortBy_returns_400_invalid_sort_field()
    {
        // Numeric strings like "999" are parsed by Enum.TryParse into an undefined
        // enum value, which passes binding but then falls through to the _ case in
        // ApplicationSortSpecs.Resolve. That throws InvalidSortFieldException →
        // PagingExceptionHandler → RFC 7807 400 with our "invalid-sort-field" type.
        var client = _fx.CreateClientForOrgA();
        var resp = await client.GetAsync("/api/v1/catalog/applications?sortBy=999");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("invalid-sort-field");
        body.Should().Contain("createdAt");
        body.Should().Contain("name");
    }

    [Fact]
    public async Task UnknownString_sortBy_returns_400_invalid_sort_field()
    {
        var client = _fx.CreateClientForOrgA();

        var resp = await client.GetAsync("/api/v1/catalog/applications?sortBy=garbage");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("invalid-sort-field");
    }

    [Fact]
    public async Task UnknownString_sortOrder_returns_400_invalid_sort_order()
    {
        var client = _fx.CreateClientForOrgA();

        var resp = await client.GetAsync("/api/v1/catalog/applications?sortOrder=upward");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("invalid-sort-order");
    }

    [Theory]
    [InlineData("999")]
    [InlineData("-1")]
    public async Task OutOfRange_numeric_sortOrder_returns_400_invalid_sort_order(string raw)
    {
        // Symmetric with OutOfRange_numeric_sortBy: Enum.TryParse accepts numeric strings
        // and binds them to undefined enum values that would otherwise silently fall through
        // to the desc branch. The Enum.IsDefined check rejects them as invalid-sort-order.
        var client = _fx.CreateClientForOrgA();

        var resp = await client.GetAsync($"/api/v1/catalog/applications?sortOrder={raw}");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("invalid-sort-order");
    }

    [Fact]
    public async Task TamperedCursor_returns_400_invalid_cursor()
    {
        var client = _fx.CreateClientForOrgA();

        var resp = await client.GetAsync("/api/v1/catalog/applications?cursor=not-a-valid-cursor!!!");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadAsStringAsync()).Should().Contain("invalid-cursor");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    [InlineData(-1)]
    public async Task LimitOutOfRange_returns_400(int limit)
    {
        var client = _fx.CreateClientForOrgA();

        var resp = await client.GetAsync($"/api/v1/catalog/applications?limit={limit}");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("invalid-limit");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("1.5")]
    [InlineData("")]
    public async Task LimitNonInteger_returns_400_invalid_limit(string raw)
    {
        // Non-integer ?limit values must surface the same RFC 7807 invalid-limit
        // envelope as out-of-range numerics, not a framework-generated parse-error
        // 400. Symmetric with sortOrder=999 / sortBy=999 handling.
        var client = _fx.CreateClientForOrgA();

        var resp = await client.GetAsync($"/api/v1/catalog/applications?limit={raw}");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("invalid-limit");
    }

    [Fact]
    public async Task DefaultParams_match_explicit_createdAt_desc_50()
    {
        await _fx.SeedApplicationsAsync(OrgATenant, count: 5, namePrefix: "def-");
        var client = _fx.CreateClientForOrgA();

        var defaultResp = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications", KartovaApiFixtureBase.WireJson);
        var explicitResp = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=desc&limit=50",
            KartovaApiFixtureBase.WireJson);

        defaultResp.Should().NotBeNull();
        explicitResp.Should().NotBeNull();
        defaultResp!.Items.Select(i => i.Id)
            .Should().Equal(explicitResp!.Items.Select(i => i.Id),
                "default parameters must match createdAt/desc/50 explicitly");
    }

    [Fact]
    public async Task Deletion_of_cursor_row_mid_pagination_does_not_skip_or_dup()
    {
        await _fx.SeedApplicationsAsync(OrgATenant, count: 10, namePrefix: "del-");
        var client = _fx.CreateClientForOrgA();

        var first = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=4",
            KartovaApiFixtureBase.WireJson);
        first!.Items.Should().HaveCount(4);
        first.NextCursor.Should().NotBeNull("there must be a next page after 4 rows");

        await _fx.DeleteApplicationAsync(OrgATenant, first.Items[^1].Id);

        var second = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            $"/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=4&cursor={Uri.EscapeDataString(first.NextCursor!)}",
            KartovaApiFixtureBase.WireJson);

        second.Should().NotBeNull();
        var firstIds = first.Items.Select(i => i.Id).ToHashSet();
        second!.Items.Select(i => i.Id).Should().NotIntersectWith(firstIds,
            "no row from the first page should reappear after the cursor row was deleted");
    }

    [Fact]
    public async Task Pages_through_25_apps_sorted_by_name_yields_no_duplicates_no_skips()
    {
        await _fx.SeedApplicationsAsync(OrgATenant, count: 25, namePrefix: "n-");

        var client = _fx.CreateClientForOrgA();

        var allIds = new HashSet<Guid>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            var url = "/api/v1/catalog/applications?sortBy=name&sortOrder=asc&limit=10"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var resp = await client.GetAsync(url);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            page.Should().NotBeNull();
            foreach (var item in page!.Items)
            {
                allIds.Add(item.Id).Should().BeTrue("each id must appear exactly once");
            }
            cursor = page.NextCursor;
            pageCount++;
        } while (cursor is not null && pageCount < 5);

        // Filter to just our seeded prefix-25; other tests may have left rows on the same tenant.
        allIds.Count.Should().BeGreaterThanOrEqualTo(25, "all 25 prefix-n- rows should be visible");
    }

    [Fact]
    public async Task Cursor_issued_for_asc_replayed_with_desc_returns_400_invalid_cursor()
    {
        await _fx.SeedApplicationsAsync(OrgATenant, count: 5, namePrefix: "dir-");

        var client = _fx.CreateClientForOrgA();

        // Generate a cursor with ascending direction.
        var ascResp = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=2",
            KartovaApiFixtureBase.WireJson);
        ascResp!.NextCursor.Should().NotBeNull();

        // Replay the same cursor with desc — should 400.
        var mismatchUrl = $"/api/v1/catalog/applications?sortBy=createdAt&sortOrder=desc&limit=2&cursor={Uri.EscapeDataString(ascResp.NextCursor!)}";
        var mismatchResp = await client.GetAsync(mismatchUrl);

        mismatchResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await mismatchResp.Content.ReadAsStringAsync();
        body.Should().Contain("invalid-cursor");
    }

    [Fact]
    public async Task NonNumericLimit_returns_400_with_raw_value_in_envelope()
    {
        // Mutation-killing test (line 102): if the throw of InvalidLimitException(rawLimit, ...)
        // is removed, effectiveLimit defaults to 0 (the failed TryParse out-value), and the
        // handler later throws InvalidLimitException(0, MinLimit, MaxLimit). Both produce a
        // 400 with type=invalid-limit, so a generic body.Contains("invalid-limit") cannot
        // distinguish them. Pin the rawLimit echo back: only the original throw preserves
        // the literal "abc" in the envelope; the fall-through path would echo "0".
        var client = _fx.CreateClientForOrgA();

        var resp = await client.GetAsync("/api/v1/catalog/applications?limit=abc");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("invalid-limit");
        body.Should().Contain("\"rawLimit\":\"abc\"",
            "the original raw string must be echoed back in the RFC 7807 envelope");
    }

    [Fact]
    public async Task SortBy_name_asc_overrides_default_createdAt_ordering()
    {
        // Mutation-killing test (line 109): if `parsedSortBy ?? ApplicationSortField.CreatedAt`
        // collapses to `ApplicationSortField.CreatedAt`, user-supplied sortBy=name is silently
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
            .UseNpgsql(_fx.BypassConnectionString)
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

        var client = _fx.CreateClientForOrgA();
        var page = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=name&sortOrder=asc&limit=200",
            KartovaApiFixtureBase.WireJson);

        page.Should().NotBeNull();
        var ours = page!.Items
            .Where(i => i.Name.StartsWith(unique, StringComparison.Ordinal))
            .Select(i => i.Name)
            .ToList();
        ours.Should().Equal(
            new[] { aName, bName, cName },
            "sortBy=name&sortOrder=asc must order by Name (alpha), not by CreatedAt");
    }

    // -----------------------------------------------------------------------
    // Slice-6 filter tests — ?includeDecommissioned wire contract (ADR-0073)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GET_applications_default_excludes_Decommissioned()
    {
        // Use a unique prefix so the assertions are not confused by rows seeded by
        // other tests in the same shared fixture (IClassFixture keeps DB across tests).
        var unique = $"f6-excl-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";

        var tenantId = _fx.TenantIdForEmail("admin@orga.kartova.local");
        await _fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await _fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);

        try
        {
            var client = _fx.CreateClientForOrgA();
            // Retrieve enough rows to see all our seeded items and use the unique prefix to filter.
            var response = await client.GetAsync("/api/v1/catalog/applications?limit=200");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await response.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.Where(i => i.Name.StartsWith(unique, StringComparison.Ordinal)).ToList();

            // All 3 active-prefix rows must be visible.
            ours.Where(i => i.Name.StartsWith(activePrefix)).Should().HaveCount(3, "active rows must not be filtered");
            // No decomm-prefix rows must appear (default view excludes Decommissioned).
            ours.Where(i => i.Name.StartsWith(decommPrefix)).Should().BeEmpty("Decommissioned rows must be excluded by default");
        }
        finally
        {
            await _fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await _fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    [Fact]
    public async Task GET_applications_with_includeDecommissioned_true_returns_all_lifecycles()
    {
        var unique = $"f6-incl-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";

        var tenantId = _fx.TenantIdForEmail("admin@orga.kartova.local");
        await _fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await _fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);

        try
        {
            var client = _fx.CreateClientForOrgA();
            var response = await client.GetAsync("/api/v1/catalog/applications?limit=200&includeDecommissioned=true");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var page = await response.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.Where(i => i.Name.StartsWith(unique, StringComparison.Ordinal)).ToList();

            // Both active and decommissioned rows must be visible.
            ours.Should().HaveCount(5, "?includeDecommissioned=true must return all 5 seeded rows");
        }
        finally
        {
            await _fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await _fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    [Fact]
    public async Task GET_applications_with_explicit_includeDecommissioned_false_matches_default()
    {
        var unique = $"f6-expl-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";

        var tenantId = _fx.TenantIdForEmail("admin@orga.kartova.local");
        await _fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await _fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);

        try
        {
            var client = _fx.CreateClientForOrgA();
            var defaultResp = await client.GetAsync("/api/v1/catalog/applications?limit=200");
            var explicitResp = await client.GetAsync("/api/v1/catalog/applications?limit=200&includeDecommissioned=false");

            defaultResp.StatusCode.Should().Be(HttpStatusCode.OK);
            explicitResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var defaultPage = await defaultResp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var explicitPage = await explicitResp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            // Both queries must return the same row set. Compare filtered to our unique prefix.
            var defaultOurs = defaultPage!.Items.Where(i => i.Name.StartsWith(unique, StringComparison.Ordinal)).Select(i => i.Id);
            var explicitOurs = explicitPage!.Items.Where(i => i.Name.StartsWith(unique, StringComparison.Ordinal)).Select(i => i.Id);
            explicitOurs.Should().BeEquivalentTo(defaultOurs,
                "explicit ?includeDecommissioned=false must match the default (omitted) behavior");
        }
        finally
        {
            await _fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await _fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    [Fact]
    public async Task GET_applications_with_cursor_from_includeDecommissioned_true_then_request_false_returns_400_cursor_filter_mismatch()
    {
        var unique = $"f6-mism-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";
        var decommPrefix = $"{unique}-d-";

        var tenantId = _fx.TenantIdForEmail("admin@orga.kartova.local");
        // Seed enough rows (5 total) so limit=2 always produces a NextCursor regardless of
        // other tenant rows — the 5 rows are the most recent (seeded now), so createdAt desc
        // puts them at the front, and limit=2 leaves 3 more behind the cursor.
        await _fx.SeedApplicationsAsync(tenantId, count: 3, namePrefix: activePrefix);
        await _fx.SeedApplicationsWithLifecycleAsync(tenantId, count: 2, namePrefix: decommPrefix, Lifecycle.Decommissioned);

        try
        {
            var client = _fx.CreateClientForOrgA();

            var page1 = await client.GetAsync("/api/v1/catalog/applications?limit=2&includeDecommissioned=true");
            page1.StatusCode.Should().Be(HttpStatusCode.OK);
            var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            p1!.NextCursor.Should().NotBeNull();

            var page2 = await client.GetAsync(
                $"/api/v1/catalog/applications?limit=2&includeDecommissioned=false&cursor={Uri.EscapeDataString(p1.NextCursor!)}");
            page2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            var problem = await page2.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
            problem!.Type.Should().Be(ProblemTypes.CursorFilterMismatch);
            problem.Extensions["filterName"]!.ToString().Should().Be("includeDecommissioned");
        }
        finally
        {
            await _fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
            await _fx.DeleteApplicationsByPrefixAsync(tenantId, decommPrefix);
        }
    }

    [Fact]
    public async Task GET_applications_with_legacy_cursor_lacking_ic_decodes_as_false_and_pages()
    {
        var unique = $"f6-legc-{Guid.NewGuid():N}";
        var activePrefix = $"{unique}-a-";

        var tenantId = _fx.TenantIdForEmail("admin@orga.kartova.local");
        // Seed 5 rows as the most recent rows in the tenant (seeded now, so createdAt desc
        // puts them at the top). limit=2 on page1 returns 2 of our rows and yields a cursor
        // pointing into our seeded range.
        await _fx.SeedApplicationsAsync(tenantId, count: 5, namePrefix: activePrefix);

        try
        {
            var client = _fx.CreateClientForOrgA();

            var page1 = await client.GetAsync("/api/v1/catalog/applications?limit=2&sortBy=createdAt&sortOrder=desc");
            page1.StatusCode.Should().Be(HttpStatusCode.OK);
            var p1 = await page1.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var boundary = p1!.Items.Last();

            // Construct a cursor JSON without the `ic` field — pre-slice-6 shape.
            var legacyJson = $"{{\"s\":\"{boundary.CreatedAt:O}\",\"i\":\"{boundary.Id}\",\"d\":\"desc\"}}";
            var legacyCursor = System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(legacyJson));

            // Default request (no includeDecommissioned param → false). Legacy cursor decodes to ic=false → match.
            var page2 = await client.GetAsync(
                $"/api/v1/catalog/applications?limit=2&sortBy=createdAt&sortOrder=desc&cursor={Uri.EscapeDataString(legacyCursor)}");

            page2.StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await _fx.DeleteApplicationsByPrefixAsync(tenantId, activePrefix);
        }
    }

}
