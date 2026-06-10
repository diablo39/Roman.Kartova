using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Wire-shape integration tests for slice 9 / E2 (reframed slice 10 / ADR-0103):
/// <c>GET /api/v1/catalog/applications</c> gains an optional <c>?createdByUserId=</c>
/// filter (spec §6.5). The filter validates against the tenant-scoped
/// <c>IUserDirectory</c> projection, so an unknown or cross-tenant id surfaces as
/// 422 <c>invalid-created-by</c>; a valid id narrows the result set to that user's
/// created applications only. Mirrors the slice-8 invalid-team envelope test pattern
/// in <see cref="AssignApplicationTeamTests"/>.
/// </summary>
[TestClass]
public sealed class ListApplicationsOwnerFilterTests : CatalogIntegrationTestBase
{
    [TestMethod]
    public async Task Filter_by_valid_createdByUserId_returns_only_that_creators_apps()
    {
        // Seed two real users in the tenant and create 2 apps by creatorA and 1 by
        // creatorB. The filter ?createdByUserId={A} must return exactly the 2 apps
        // created by A, with CreatedBy.Id == A on each. Pins both the predicate and
        // the post-filter CreatedBy enrichment.
        var unique = $"e2-ok-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");

        var creatorA = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: "Creator A", email: $"{unique}-a@orga.kartova.local");
        var creatorB = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: "Creator B", email: $"{unique}-b@orga.kartova.local");

        var appA1 = await Fx.SeedSingleApplicationAsync(
            tenantId, creatorA, teamId: null, namePrefix: $"{unique}-a1");
        var appA2 = await Fx.SeedSingleApplicationAsync(
            tenantId, creatorA, teamId: null, namePrefix: $"{unique}-a2");
        var appB1 = await Fx.SeedSingleApplicationAsync(
            tenantId, creatorB, teamId: null, namePrefix: $"{unique}-b1");

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync(
                $"/api/v1/catalog/applications?createdByUserId={creatorA}&limit=200");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page);

            // Filter narrows by CreatedByUserId at the SQL layer, so even with other
            // tests' rows lying around the predicate guarantees only creatorA's apps
            // come back for this id. Asserting on the seeded ids only would miss a
            // regression where the filter is silently dropped; assert that EVERY
            // returned row has the expected CreatedByUserId instead.
            Assert.IsTrue(page!.Items.All(i => i.CreatedByUserId == creatorA),
                "filter must return only rows whose CreatedByUserId matches the supplied id");
            // The two seeded rows must both be visible.
            Assert.IsNotNull(page.Items.SingleOrDefault(i => i.Id == appA1),
                "seeded appA1 must be visible in the filtered list");
            Assert.IsNotNull(page.Items.SingleOrDefault(i => i.Id == appA2),
                "seeded appA2 must be visible in the filtered list");
            // App created by B must not appear.
            Assert.IsNull(page.Items.SingleOrDefault(i => i.Id == appB1),
                "seeded appB1 (created by B) must be hidden by the filter");

            // CreatedBy enrichment still runs on the filtered page — every returned
            // row should carry CreatedBy with the matching directory entry.
            foreach (var item in page.Items.Where(i => i.Id == appA1 || i.Id == appA2))
            {
                Assert.IsNotNull(item.CreatedBy, "filtered rows must still be enriched with CreatedBy");
                Assert.AreEqual(creatorA, item.CreatedBy!.Id);
                Assert.AreEqual("Creator A", item.CreatedBy.DisplayName);
            }
        }
        finally
        {
            // Order: user-row deletes first so the more leak-prone cleanup
            // (Organization schema, no prefix-based sweep) runs even if the
            // catalog cleanup is the one that throws. Catalog rows can be
            // recovered by DeleteApplicationsByPrefixAsync. Mirrors the E1
            // convention established in commit e5aaf73 +
            // ApplicationOwnerEnrichmentTests.cs.
            await Fx.DeleteUserInOrganizationAsync(creatorA);
            await Fx.DeleteUserInOrganizationAsync(creatorB);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
        }
    }

    [TestMethod]
    public async Task Filter_by_unknown_createdByUserId_returns_422_invalid_created_by()
    {
        // Random guid that was never seeded — IUserDirectory.GetAsync returns null,
        // so the endpoint must short-circuit with 422 invalid-created-by before invoking
        // the handler. Symmetric with AssignApplicationTeam's invalid-team test.
        var unknownCreatorId = Guid.NewGuid();
        var client = Fx.CreateClientForOrgA();

        var resp = await client.GetAsync(
            $"/api/v1/catalog/applications?createdByUserId={unknownCreatorId}");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, ProblemTypes.InvalidCreatedBy);
    }

    [TestMethod]
    public async Task Filter_by_other_tenant_createdByUserId_returns_422_invalid_created_by()
    {
        // Cross-tenant safety: a user that exists in tenant B is invisible to
        // tenant A (RLS hides the row from IUserDirectory's lookup), so the
        // existence check sees null and surfaces the same 422 invalid-created-by
        // envelope as an unknown id. Mirrors the slice-8 cross-tenant test on
        // invalid-team — the principle is identical: cross-module existence
        // checks short-circuit through the tenant-scoped projection, never
        // through a direct DB read. No catalog rows seeded in tenant A: the
        // validation gate fires before any query against catalog_applications.
        var tenantB = Fx.TenantIdForEmail("admin@orgb.kartova.local");
        var unique = $"e2-xt-{Guid.NewGuid():N}";

        var creatorInB = await Fx.SeedUserInOrganizationAsync(
            tenantB, displayName: "Creator in B", email: $"{unique}@orgb.kartova.local");

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync(
                $"/api/v1/catalog/applications?createdByUserId={creatorInB}");

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
            var body = await resp.Content.ReadAsStringAsync();
            StringAssert.Contains(body, ProblemTypes.InvalidCreatedBy);
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(creatorInB);
        }
    }

    [TestMethod]
    public async Task Omitted_createdByUserId_returns_all_visible_rows()
    {
        // Regression guard for the null branch: omitting ?createdByUserId= must not
        // alter behaviour. Seed one app created by user A and one by user B;
        // omitting the query param returns both. (Confirms the optional parameter
        // truly is optional at the binding layer.)
        var unique = $"e2-omit-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");

        var creatorA = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: "Omit A", email: $"{unique}-a@orga.kartova.local");
        var creatorB = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: "Omit B", email: $"{unique}-b@orga.kartova.local");

        var appA = await Fx.SeedSingleApplicationAsync(
            tenantId, creatorA, teamId: null, namePrefix: $"{unique}-a");
        var appB = await Fx.SeedSingleApplicationAsync(
            tenantId, creatorB, teamId: null, namePrefix: $"{unique}-b");

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync("/api/v1/catalog/applications?limit=200");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page);
            Assert.IsNotNull(page!.Items.SingleOrDefault(i => i.Id == appA),
                "both seeded apps must be visible when createdByUserId is omitted");
            Assert.IsNotNull(page.Items.SingleOrDefault(i => i.Id == appB),
                "both seeded apps must be visible when createdByUserId is omitted");
        }
        finally
        {
            // Order: user-row deletes first (Organization schema, no prefix
            // sweep, more leak-prone) then catalog rows. Mirrors E1's e5aaf73
            // convention and ApplicationOwnerEnrichmentTests.cs.
            await Fx.DeleteUserInOrganizationAsync(creatorA);
            await Fx.DeleteUserInOrganizationAsync(creatorB);
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
        }
    }
}
