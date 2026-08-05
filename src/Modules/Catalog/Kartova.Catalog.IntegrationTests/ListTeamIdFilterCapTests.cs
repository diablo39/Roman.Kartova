using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// A2 extension (gate-9 follow-up): the pre-existing <c>?teamId=</c> multi-select filter on
/// both list endpoints predates the <c>systemId</c> cap (<see cref="ListBySystemFilterTests"/>)
/// and was reachable, uncapped, via the same page-2 cursor-break exploit the cap exists to
/// prevent. These tests are the <c>teamId</c> analog of <see cref="ListBySystemFilterTests"/>'s
/// cap/dedup/replay coverage. Applications and Services are covered independently (not just one
/// endpoint) per the same Gate 7/8 rationale documented there — the guard is duplicated per list
/// delegate, so a wiring slip at either call site must not hide behind the other's green test.
/// </summary>
[TestClass]
public sealed class ListTeamIdFilterCapTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    [TestMethod]
    public async Task Applications_more_than_the_cap_teamId_values_returns_400_too_many_filter_values()
    {
        var client = Fx.CreateClientForOrgA();
        var ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray();
        var query = string.Join("&", ids.Select(id => $"teamId={id}"));

        var resp = await client.GetAsync($"/api/v1/catalog/applications?{query}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.TooManyFilterValues, problem!.Type);
        var detail = problem.Detail ?? string.Empty;
        StringAssert.Contains(detail, "50", "the detail must name the cap");
        StringAssert.Contains(detail, "51", "the detail must name the supplied count");
    }

    [TestMethod]
    public async Task Services_more_than_the_cap_teamId_values_returns_400_too_many_filter_values()
    {
        var client = Fx.CreateClientForOrgA();
        var ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray();
        var query = string.Join("&", ids.Select(id => $"teamId={id}"));

        var resp = await client.GetAsync($"/api/v1/catalog/services?{query}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.TooManyFilterValues, problem!.Type);
        var detail = problem.Detail ?? string.Empty;
        StringAssert.Contains(detail, "50", "the detail must name the cap");
        StringAssert.Contains(detail, "51", "the detail must name the supplied count");
    }

    [TestMethod]
    public async Task Apis_more_than_the_cap_teamId_values_returns_400_too_many_filter_values()
    {
        // ListApisHandler encodes teamId into the cursor f-map exactly as the Applications and
        // Services handlers do, so this endpoint carries the same page-2 break and the same cap.
        var client = Fx.CreateClientForOrgA();
        var ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray();
        var query = string.Join("&", ids.Select(id => $"teamId={id}"));

        var resp = await client.GetAsync($"/api/v1/catalog/apis?{query}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.TooManyFilterValues, problem!.Type);
    }

    [TestMethod]
    public async Task Systems_more_than_the_cap_teamId_values_returns_400_too_many_filter_values()
    {
        var client = Fx.CreateClientForOrgA();
        var ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray();
        var query = string.Join("&", ids.Select(id => $"teamId={id}"));

        var resp = await client.GetAsync($"/api/v1/catalog/systems?{query}");

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.TooManyFilterValues, problem!.Type);
    }

    [TestMethod]
    public async Task Applications_duplicate_teamId_values_do_not_count_toward_the_cap()
    {
        // Pins the ordering of dedup-then-cap: 50 distinct ids, each supplied twice (100
        // params), must pass — because ToHashSet() runs before the > MaxFilterValues check.
        var client = Fx.CreateClientForOrgA();
        var ids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
        var doubled = ids.Concat(ids);
        var query = string.Join("&", doubled.Select(id => $"teamId={id}"));

        var resp = await client.GetAsync($"/api/v1/catalog/applications?{query}");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task Services_duplicate_teamId_values_do_not_count_toward_the_cap()
    {
        var client = Fx.CreateClientForOrgA();
        var ids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();
        var doubled = ids.Concat(ids);
        var query = string.Join("&", doubled.Select(id => $"teamId={id}"));

        var resp = await client.GetAsync($"/api/v1/catalog/services?{query}");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    }

    [TestMethod]
    public async Task Applications_at_the_cap_teamId_the_returned_cursor_is_replayable()
    {
        // Proves the cap was chosen large enough to be usable (a round trip works) and small
        // enough to keep the cursor inside the request line — mirrors
        // ListBySystemFilterTests.At_the_cap_the_returned_cursor_is_replayable for teamId.
        var unique = $"a2-teamid-cap-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var a1 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a1");
        var a2 = await Fx.SeedSingleApplicationAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-a2");

        try
        {
            var ids = new List<Guid> { teamId };
            ids.AddRange(Enumerable.Range(0, 49).Select(_ => Guid.NewGuid()));
            Assert.AreEqual(50, ids.Distinct().Count());
            var query = string.Join("&", ids.Select(id => $"teamId={id}"));

            var page1 = await client.GetFromJsonAsync<CursorPage<ApplicationResponse>>(
                $"/api/v1/catalog/applications?{query}&limit=1", KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page1!.NextCursor);

            var resp = await client.GetAsync(
                $"/api/v1/catalog/applications?{query}&limit=1&cursor={Uri.EscapeDataString(page1.NextCursor!)}");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

            var page2 = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var seenAcrossBothPages = page1.Items.Concat(page2!.Items).Select(i => i.Id).ToHashSet();
            Assert.IsTrue(seenAcrossBothPages.Contains(a1), "a1 must be reachable across the two pages");
            Assert.IsTrue(seenAcrossBothPages.Contains(a2), "a2 must be reachable across the two pages");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenant, unique);
        }
    }

    [TestMethod]
    public async Task Services_at_the_cap_teamId_the_returned_cursor_is_replayable()
    {
        // Services counterpart of the Applications replay test above. The cap guard is wired per
        // list delegate, so the round trip is proven on both endpoints rather than inferred from
        // one — same rationale as the cap/dedup pairs in this file.
        var unique = $"a2-teamid-cap-svc-{Guid.NewGuid():N}";
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var client = Fx.CreateClientForOrgA();
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenant, $"{unique}-team");
        var s1 = await Fx.SeedSingleServiceAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-s1");
        var s2 = await Fx.SeedSingleServiceAsync(tenant, Guid.NewGuid(), teamId, $"{unique}-s2");

        try
        {
            var ids = new List<Guid> { teamId };
            ids.AddRange(Enumerable.Range(0, 49).Select(_ => Guid.NewGuid()));
            Assert.AreEqual(50, ids.Distinct().Count());
            var query = string.Join("&", ids.Select(id => $"teamId={id}"));

            var page1 = await client.GetFromJsonAsync<CursorPage<ServiceResponse>>(
                $"/api/v1/catalog/services?{query}&limit=1", KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page1!.NextCursor);

            var resp = await client.GetAsync(
                $"/api/v1/catalog/services?{query}&limit=1&cursor={Uri.EscapeDataString(page1.NextCursor!)}");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);

            var page2 = await resp.Content.ReadFromJsonAsync<CursorPage<ServiceResponse>>(KartovaApiFixtureBase.WireJson);
            var seenAcrossBothPages = page1.Items.Concat(page2!.Items).Select(i => i.Id).ToHashSet();
            Assert.IsTrue(seenAcrossBothPages.Contains(s1), "s1 must be reachable across the two pages");
            Assert.IsTrue(seenAcrossBothPages.Contains(s2), "s2 must be reachable across the two pages");
        }
        finally
        {
            await Fx.DeleteServicesByPrefixAsync(tenant, unique);
        }
    }
}
