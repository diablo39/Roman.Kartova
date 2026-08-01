using System.Linq;
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class ListRelationshipsTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = name, description = "x", teamId, endpoints = Array.Empty<object>(),
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static object Rel(EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid) =>
        new { sourceKind = sk, sourceId = sid, type = t, targetKind = tk, targetId = tid };

    private static Task<HttpResponseMessage> PostRelAsync(
        HttpClient client, EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid)
        => client.PostAsJsonAsync("/api/v1/catalog/relationships", Rel(sk, sid, t, tk, tid), KartovaApiFixtureBase.WireJson);

    private static async Task<Guid> SeedApplicationAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/applications",
            new { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApplication '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static async Task<Guid> SeedSystemAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/systems", new
        { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedSystem '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<SystemResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    [TestMethod]
    public async Task GET_enriches_CreatedBy_from_caller_when_user_row_exists()
    {
        // Seed the caller's own users-projection row (id == JWT sub) so IUserDirectory
        // can resolve the relationship's creator into CreatedBy (the "Added by" column).
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var sub = await Fx.GetSubClaimAsync(OrgAUser);
        var email = $"rel-creator-{Guid.NewGuid():N}@orga.kartova.local";
        await Fx.SeedUserWithIdInOrganizationAsync(tenantId, sub, "Rel Creator OrgA", email);
        try
        {
            var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
            var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Rel CreatedBy Team");
            var a = await SeedServiceAsync(client, teamId, "cb-svc-a");
            var b = await SeedServiceAsync(client, teamId, "cb-svc-b");
            await client.PostAsJsonAsync("/api/v1/catalog/relationships",
                Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));

            var page = await (await client.GetAsync(
                $"/api/v1/catalog/relationships?entityKind=Service&entityId={a}&direction=outgoing"))
                .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);

            Assert.AreEqual(1, page!.Items.Count);
            var rel = page.Items[0];
            Assert.AreEqual(sub, rel.CreatedByUserId);
            Assert.IsNotNull(rel.CreatedBy, "CreatedBy must be enriched when the creator's users row exists");
            Assert.AreEqual(sub, rel.CreatedBy!.Id);
            Assert.AreEqual("Rel Creator OrgA", rel.CreatedBy.DisplayName);
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(sub);
        }
    }

    [TestMethod]
    public async Task GET_incoming_returns_consumers_of_an_entity()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team");
        var a = await SeedServiceAsync(client, teamId, "list-svc-a");
        var b = await SeedServiceAsync(client, teamId, "list-svc-b");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));

        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={b}&direction=incoming");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(1, page!.Items.Count);
        Assert.AreEqual(a, page.Items[0].Source.Id);
    }

    [TestMethod]
    public async Task GET_outgoing_lists_only_edges_sourced_at_entity()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team Out");
        var a = await SeedServiceAsync(client, teamId, "list-svc-a2");
        var b = await SeedServiceAsync(client, teamId, "list-svc-b2");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));

        var outA = await (await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={a}&direction=outgoing"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        var outB = await (await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={b}&direction=outgoing"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);

        Assert.AreEqual(1, outA!.Items.Count);
        Assert.AreEqual(0, outB!.Items.Count);
    }

    [TestMethod]
    public async Task GET_all_direction_returns_both_incoming_and_outgoing()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team All");
        var a = await SeedServiceAsync(client, teamId, "list-svc-all-a");
        var b = await SeedServiceAsync(client, teamId, "list-svc-all-b");
        var c = await SeedServiceAsync(client, teamId, "list-svc-all-c");
        // a → b (b's outgoing, a's incoming target)
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));
        // b → c (b's outgoing)
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, b, RelationshipType.DependsOn, EntityKind.Service, c));

        var allB = await (await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={b}&direction=all"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);

        // b appears as target (a→b) and as source (b→c): 2 edges
        Assert.AreEqual(2, allB!.Items.Count);
    }

    [TestMethod]
    public async Task GET_is_tenant_isolated()
    {
        var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "B Team Iso");
        var b1 = await SeedServiceAsync(orgB, teamB, "b1-iso");
        var b2 = await SeedServiceAsync(orgB, teamB, "b2-iso");
        await orgB.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, b1, RelationshipType.DependsOn, EntityKind.Service, b2));

        var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var page = await (await orgA.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={b1}&direction=all"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(0, page!.Items.Count);
    }

    [TestMethod]
    public async Task GET_paginates_forward_and_sortBy_type_is_honoured()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Pag Team");
        var hub = await SeedServiceAsync(client, teamId, "list-rel-hub");
        var s1 = await SeedServiceAsync(client, teamId, "list-rel-s1");
        var s2 = await SeedServiceAsync(client, teamId, "list-rel-s2");
        var s3 = await SeedServiceAsync(client, teamId, "list-rel-s3");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, hub, RelationshipType.DependsOn, EntityKind.Service, s1));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, hub, RelationshipType.DependsOn, EntityKind.Service, s2));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, hub, RelationshipType.DependsOn, EntityKind.Service, s3));

        // Page 1: limit=2, sortBy=type
        var firstResp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={hub}&direction=outgoing&sortBy=type&sortOrder=asc&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, firstResp.StatusCode);
        var first = await firstResp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(2, first!.Items.Count);
        Assert.IsNotNull(first.NextCursor);

        // Page 2: follow cursor
        var nextResp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={hub}&direction=outgoing&sortBy=type&sortOrder=asc&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, nextResp.StatusCode);
        var next = await nextResp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(next!.Items.Count >= 1);
    }

    [TestMethod]
    public async Task GET_without_token_returns_401()
    {
        using var client = Fx.CreateAnonymousClient();
        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={Guid.NewGuid()}&direction=all");
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_with_invalid_entityKind_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Bogus&entityId={Guid.NewGuid()}&direction=all");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_with_invalid_direction_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={Guid.NewGuid()}&direction=bogus");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_with_invalid_sortBy_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={Guid.NewGuid()}&sortBy=bogusField");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_with_limit_over_max_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={Guid.NewGuid()}&direction=all&limit=201");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_default_sort_is_createdAt_desc()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Default Sort Team");
        var src = await SeedServiceAsync(client, teamId, "list-ds-src");
        var t1 = await SeedServiceAsync(client, teamId, "list-ds-t1");
        var t2 = await SeedServiceAsync(client, teamId, "list-ds-t2");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, src, RelationshipType.DependsOn, EntityKind.Service, t1));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, src, RelationshipType.DependsOn, EntityKind.Service, t2));

        var resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={src}&direction=outgoing");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(2, page!.Items.Count);
        // newest first: second seeded edge's createdAt >= first's
        Assert.IsTrue(page.Items[0].CreatedAt >= page.Items[1].CreatedAt);
    }

    private static async Task<Guid> SeedApiAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", new
        {
            displayName = name, description = "x", teamId, style = "Rest", version = "v1",
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApi '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    [TestMethod]
    public async Task GET_outgoing_with_excludeApiEdges_omits_provide_and_consume_edges()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Excl Team");
        var svc = await SeedServiceAsync(client, teamId, "excl-svc");
        var dep = await SeedServiceAsync(client, teamId, "excl-dep");
        var providedApi = await SeedApiAsync(client, teamId, "excl-api-provided");
        var consumedApi = await SeedApiAsync(client, teamId, "excl-api-consumed");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.DependsOn, EntityKind.Service, dep));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.ProvidesApiFor, EntityKind.Api, providedApi));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.ConsumesApiFrom, EntityKind.Api, consumedApi));

        var page = await (await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={svc}&direction=outgoing&excludeApiEdges=true"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);

        Assert.AreEqual(1, page!.Items.Count);
        Assert.AreEqual(RelationshipType.DependsOn, page.Items[0].Type);
    }

    [TestMethod]
    public async Task GET_outgoing_default_still_returns_api_edges()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Incl Team");
        var svc = await SeedServiceAsync(client, teamId, "incl-svc");
        var dep = await SeedServiceAsync(client, teamId, "incl-dep");
        var providedApi = await SeedApiAsync(client, teamId, "incl-api-provided");
        var consumedApi = await SeedApiAsync(client, teamId, "incl-api-consumed");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.DependsOn, EntityKind.Service, dep));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.ProvidesApiFor, EntityKind.Api, providedApi));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.ConsumesApiFrom, EntityKind.Api, consumedApi));

        var page = await (await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={svc}&direction=outgoing"))
            .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);

        Assert.AreEqual(3, page!.Items.Count);
    }

    [TestMethod]
    public async Task GET_outgoing_excludeApiEdges_paginates_without_short_count()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Excl Pag Team");
        var svc = await SeedServiceAsync(client, teamId, "excl-pag-svc");
        var dep1 = await SeedServiceAsync(client, teamId, "excl-pag-dep1");
        var dep2 = await SeedServiceAsync(client, teamId, "excl-pag-dep2");
        var dep3 = await SeedServiceAsync(client, teamId, "excl-pag-dep3");
        var api1 = await SeedApiAsync(client, teamId, "excl-pag-api1");
        var api2 = await SeedApiAsync(client, teamId, "excl-pag-api2");
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.DependsOn, EntityKind.Service, dep1));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.DependsOn, EntityKind.Service, dep2));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.DependsOn, EntityKind.Service, dep3));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.ProvidesApiFor, EntityKind.Api, api1));
        await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, svc, RelationshipType.ConsumesApiFrom, EntityKind.Api, api2));

        var firstResp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={svc}&direction=outgoing&excludeApiEdges=true&sortBy=type&sortOrder=asc&limit=2");
        Assert.AreEqual(HttpStatusCode.OK, firstResp.StatusCode);
        var first = await firstResp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(2, first!.Items.Count);
        Assert.IsTrue(first.Items.All(i => i.Type == RelationshipType.DependsOn));
        Assert.IsNotNull(first.NextCursor);

        var nextResp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={svc}&direction=outgoing&excludeApiEdges=true&sortBy=type&sortOrder=asc&limit=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.OK, nextResp.StatusCode);
        var next = await nextResp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(1, next!.Items.Count);
        Assert.AreEqual(RelationshipType.DependsOn, next.Items[0].Type);
        Assert.IsNull(next.NextCursor);
    }

    // -----------------------------------------------------------------------
    // `type` filter (task 4d / E-03 slice A1). Server-side filter so the frontend
    // never has to client-side filter a truncated page (which would render "Not
    // assigned" for entities with more than one page of outgoing edges).
    // -----------------------------------------------------------------------

    [TestMethod]
    public async Task GET_relationships_filtered_by_type_returns_only_that_type()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Type Filter Team");
        var appId = await SeedApplicationAsync(client, teamId, "app-type-filter");
        var depId = await SeedApplicationAsync(client, teamId, "app-type-filter-dep");
        var sysId = await SeedSystemAsync(client, teamId, "system-type-filter");
        await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.DependsOn, EntityKind.Application, depId);
        await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.PartOf, EntityKind.System, sysId);

        var resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind={EntityKind.Application}&entityId={appId}&direction=outgoing&type={RelationshipType.PartOf}&limit=20");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.ContainsSingle(page!.Items);
        Assert.AreEqual(RelationshipType.PartOf, page.Items[0].Type);
        Assert.AreEqual(sysId, page.Items[0].Target.Id);
    }

    [TestMethod]
    public async Task GET_type_filter_finds_row_beyond_first_unfiltered_page()
    {
        // This is the test that separates "filter before ToCursorPagedAsync" (correct)
        // from "filter after paging / filter page.Items in memory" (the bug this task
        // exists to prevent). Confirmed default sort for this endpoint is createdAt DESC
        // (newest first) — ListRelationshipsAsync: `SortOrder: parsedSortOrder ?? SortOrder.Desc`.
        // So the FIRST-created edge is the OLDEST and sorts LAST in the unfiltered order.
        // Seeding the PartOf edge first, then 12 DependsOn edges (all newer), puts PartOf
        // at unfiltered position 13 of 13 — strictly outside a limit=5 first page. A
        // post-hoc implementation would take the 5 newest (all DependsOn), filter to an
        // empty list, and still return a NextCursor implying more data exists — this test
        // fails under that implementation and only passes when the row set is narrowed
        // before pagination.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Type Trunc Team");
        var appId = await SeedApplicationAsync(client, teamId, "app-type-trunc");
        var sysId = await SeedSystemAsync(client, teamId, "system-type-trunc");

        // Created FIRST ⇒ oldest ⇒ sorts LAST under the default createdAt desc order.
        await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.PartOf, EntityKind.System, sysId);

        // 12 distinct DependsOn targets created AFTER ⇒ all newer ⇒ all 12 outrank PartOf
        // in the unfiltered createdAt-desc order, filling an entire limit=5 first page
        // (and then some) with non-matching rows.
        for (var i = 0; i < 12; i++)
        {
            var dep = await SeedApplicationAsync(client, teamId, $"app-type-trunc-dep-{i}");
            await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.DependsOn, EntityKind.Application, dep);
        }

        var resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind={EntityKind.Application}&entityId={appId}&direction=outgoing&type={RelationshipType.PartOf}&limit=5");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.ContainsSingle(page!.Items);
        Assert.AreEqual(RelationshipType.PartOf, page.Items[0].Type);
        Assert.AreEqual(sysId, page.Items[0].Target.Id);
        // The filtered row set has exactly 1 match — a NextCursor here would promise a
        // page that doesn't exist (the other half of the "hidden non-matching row
        // consumed a keyset slot" failure mode).
        Assert.IsNull(page.NextCursor, "filtered row set has only 1 match; no further page should be promised");
    }

    [TestMethod]
    public async Task GET_with_invalid_type_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind=Service&entityId={Guid.NewGuid()}&direction=all&type=bogusType");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_type_cursor_then_changed_type_returns_400_cursor_filter_mismatch()
    {
        // Mirrors ListApplicationsPaginationTests.GET_teamId_cursor_then_changed_teamId_returns_400_cursor_filter_mismatch
        // and ListServicesPaginationTests's teamId-mismatch test — no CursorFilterMismatch
        // precedent exists in ListRelationshipsTests itself, so this test is modelled on the
        // neighbouring list suites' pattern instead.
        // Two applications PartOf the same System (a component can only be PartOf ONE
        // System — ComponentAlreadyInSystemException — so the ≥2-rows-of-the-same-type
        // fixture has to come from the System's INCOMING side, i.e. its members, not a
        // single component's outgoing edges).
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Type Mismatch Team");
        var sysId = await SeedSystemAsync(client, teamId, "system-type-mismatch");
        var app1 = await SeedApplicationAsync(client, teamId, "app-type-mismatch-1");
        var app2 = await SeedApplicationAsync(client, teamId, "app-type-mismatch-2");
        await PostRelAsync(client, EntityKind.Application, app1, RelationshipType.PartOf, EntityKind.System, sysId);
        await PostRelAsync(client, EntityKind.Application, app2, RelationshipType.PartOf, EntityKind.System, sysId);

        // Page 1 filters to PartOf → f-map records type = PartOf.
        var page1Resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind={EntityKind.System}&entityId={sysId}&direction=incoming&type={RelationshipType.PartOf}&limit=1");
        Assert.AreEqual(HttpStatusCode.OK, page1Resp.StatusCode);
        var p1 = await page1Resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(p1!.NextCursor, "need a NextCursor to test the mismatch");

        // Page 2 switches to DependsOn → mismatch on the "type" filter key (400, not a
        // silent continuation across a different relationship type).
        var page2Resp = await client.GetAsync(
            $"/api/v1/catalog/relationships?entityKind={EntityKind.System}&entityId={sysId}&direction=incoming&type={RelationshipType.DependsOn}&limit=1&cursor={Uri.EscapeDataString(p1.NextCursor!)}");
        Assert.AreEqual(HttpStatusCode.BadRequest, page2Resp.StatusCode);
        var problem = await page2Resp.Content.ReadFromJsonAsync<ProblemDetails>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(ProblemTypes.CursorFilterMismatch, problem!.Type);
        Assert.AreEqual("type", problem.Extensions["filterName"]!.ToString());
        Assert.AreEqual(RelationshipType.PartOf.ToString(), problem.Extensions["expectedValue"]!.ToString());
        Assert.AreEqual(RelationshipType.DependsOn.ToString(), problem.Extensions["actualValue"]!.ToString());
    }
}
