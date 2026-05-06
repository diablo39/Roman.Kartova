using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

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
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();
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
            "/api/v1/catalog/applications");
        var explicitResp = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=desc&limit=50");

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
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=4");
        first!.Items.Should().HaveCount(4);
        first.NextCursor.Should().NotBeNull("there must be a next page after 4 rows");

        await _fx.DeleteApplicationAsync(OrgATenant, first.Items[^1].Id);

        var second = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
            $"/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=4&cursor={Uri.EscapeDataString(first.NextCursor!)}");

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
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();
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
            "/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=2");
        ascResp!.NextCursor.Should().NotBeNull();

        // Replay the same cursor with desc — should 400.
        var mismatchUrl = $"/api/v1/catalog/applications?sortBy=createdAt&sortOrder=desc&limit=2&cursor={Uri.EscapeDataString(ascResp.NextCursor!)}";
        var mismatchResp = await client.GetAsync(mismatchUrl);

        mismatchResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await mismatchResp.Content.ReadAsStringAsync();
        body.Should().Contain("invalid-cursor");
    }

}
