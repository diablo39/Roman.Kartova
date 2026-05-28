using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Wire-shape integration tests for slice 9 / E2: <c>GET /api/v1/catalog/applications</c>
/// gains an optional <c>?ownerUserId=</c> filter (spec §6.5). The filter validates
/// against the tenant-scoped <c>IUserDirectory</c> projection, so an unknown or
/// cross-tenant id surfaces as 422 <c>invalid-owner</c>; a valid id narrows the
/// result set to that owner's applications only. Mirrors the slice-8 invalid-team
/// envelope test pattern in <see cref="AssignApplicationTeamTests"/>.
/// </summary>
[TestClass]
public sealed class ListApplicationsOwnerFilterTests : CatalogIntegrationTestBase
{
    [TestMethod]
    public async Task Filter_by_valid_ownerUserId_returns_only_that_owners_apps()
    {
        // Seed two real users in the tenant and assign 2 apps to ownerA and 1 to
        // ownerB. The filter ?ownerUserId={A} must return exactly the 2 apps owned
        // by A, with Owner.Id == A on each. Pins both the predicate and the
        // post-filter Owner enrichment.
        var unique = $"e2-ok-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");

        var ownerA = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: "Owner A", email: $"{unique}-a@orga.kartova.local");
        var ownerB = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: "Owner B", email: $"{unique}-b@orga.kartova.local");

        var appA1 = await Fx.SeedSingleApplicationAsync(
            tenantId, ownerA, teamId: null, namePrefix: $"{unique}-a1");
        var appA2 = await Fx.SeedSingleApplicationAsync(
            tenantId, ownerA, teamId: null, namePrefix: $"{unique}-a2");
        var appB1 = await Fx.SeedSingleApplicationAsync(
            tenantId, ownerB, teamId: null, namePrefix: $"{unique}-b1");

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync(
                $"/api/v1/catalog/applications?ownerUserId={ownerA}&limit=200");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page);

            // Filter narrows by OwnerUserId at the SQL layer, so even with other
            // tests' rows lying around the predicate guarantees only ownerA's apps
            // come back for this owner id. Asserting on the seeded ids only would
            // miss a regression where the filter is silently dropped; assert that
            // EVERY returned row has the expected OwnerUserId instead.
            Assert.IsTrue(page!.Items.All(i => i.OwnerUserId == ownerA),
                "filter must return only rows whose OwnerUserId matches the supplied id");
            // The two seeded rows must both be visible.
            Assert.IsNotNull(page.Items.SingleOrDefault(i => i.Id == appA1),
                "seeded appA1 must be visible in the filtered list");
            Assert.IsNotNull(page.Items.SingleOrDefault(i => i.Id == appA2),
                "seeded appA2 must be visible in the filtered list");
            // App owned by B must not appear.
            Assert.IsNull(page.Items.SingleOrDefault(i => i.Id == appB1),
                "seeded appB1 (owned by B) must be hidden by the filter");

            // Owner enrichment still runs on the filtered page — every returned
            // row should carry Owner with the matching directory entry.
            foreach (var item in page.Items.Where(i => i.Id == appA1 || i.Id == appA2))
            {
                Assert.IsNotNull(item.Owner, "filtered rows must still be enriched with Owner");
                Assert.AreEqual(ownerA, item.Owner!.Id);
                Assert.AreEqual("Owner A", item.Owner.DisplayName);
            }
        }
        finally
        {
            // Clean rows the test owns. Order: catalog rows first (cheap prefix
            // sweep), then user rows (no prefix sweep, more leak-prone if left).
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
            await Fx.DeleteUserInOrganizationAsync(ownerA);
            await Fx.DeleteUserInOrganizationAsync(ownerB);
        }
    }

    [TestMethod]
    public async Task Filter_by_unknown_ownerUserId_returns_422_invalid_owner()
    {
        // Random guid that was never seeded — IUserDirectory.GetAsync returns null,
        // so the endpoint must short-circuit with 422 invalid-owner before invoking
        // the handler. Symmetric with AssignApplicationTeam's invalid-team test.
        var unknownOwnerId = Guid.NewGuid();
        var client = Fx.CreateClientForOrgA();

        var resp = await client.GetAsync(
            $"/api/v1/catalog/applications?ownerUserId={unknownOwnerId}");

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
        Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        var body = await resp.Content.ReadAsStringAsync();
        StringAssert.Contains(body, ProblemTypes.InvalidOwner);
    }

    [TestMethod]
    public async Task Filter_by_other_tenant_ownerUserId_returns_422_invalid_owner()
    {
        // Cross-tenant safety: a user that exists in tenant B is invisible to
        // tenant A (RLS hides the row from IUserDirectory's lookup), so the
        // existence check sees null and surfaces the same 422 invalid-owner
        // envelope as an unknown id. Mirrors the slice-8 cross-tenant test on
        // invalid-team — the principle is identical: cross-module existence
        // checks short-circuit through the tenant-scoped projection, never
        // through a direct DB read. No catalog rows seeded in tenant A: the
        // validation gate fires before any query against catalog_applications.
        var tenantB = Fx.TenantIdForEmail("admin@orgb.kartova.local");
        var unique = $"e2-xt-{Guid.NewGuid():N}";

        var ownerInB = await Fx.SeedUserInOrganizationAsync(
            tenantB, displayName: "Owner in B", email: $"{unique}@orgb.kartova.local");

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync(
                $"/api/v1/catalog/applications?ownerUserId={ownerInB}");

            Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            Assert.AreEqual("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
            var body = await resp.Content.ReadAsStringAsync();
            StringAssert.Contains(body, ProblemTypes.InvalidOwner);
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(ownerInB);
        }
    }

    [TestMethod]
    public async Task Omitted_ownerUserId_returns_all_visible_rows()
    {
        // Regression guard for the null branch: omitting ?ownerUserId= must not
        // alter behaviour. Seed one app owned by user A and one owned by user B;
        // omitting the query param returns both. (Confirms the optional parameter
        // truly is optional at the binding layer.)
        var unique = $"e2-omit-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");

        var ownerA = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: "Omit A", email: $"{unique}-a@orga.kartova.local");
        var ownerB = await Fx.SeedUserInOrganizationAsync(
            tenantId, displayName: "Omit B", email: $"{unique}-b@orga.kartova.local");

        var appA = await Fx.SeedSingleApplicationAsync(
            tenantId, ownerA, teamId: null, namePrefix: $"{unique}-a");
        var appB = await Fx.SeedSingleApplicationAsync(
            tenantId, ownerB, teamId: null, namePrefix: $"{unique}-b");

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync("/api/v1/catalog/applications?limit=200");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page);
            Assert.IsNotNull(page!.Items.SingleOrDefault(i => i.Id == appA),
                "both seeded apps must be visible when ownerUserId is omitted");
            Assert.IsNotNull(page.Items.SingleOrDefault(i => i.Id == appB),
                "both seeded apps must be visible when ownerUserId is omitted");
        }
        finally
        {
            await Fx.DeleteApplicationsByPrefixAsync(tenantId, unique);
            await Fx.DeleteUserInOrganizationAsync(ownerA);
            await Fx.DeleteUserInOrganizationAsync(ownerB);
        }
    }
}
