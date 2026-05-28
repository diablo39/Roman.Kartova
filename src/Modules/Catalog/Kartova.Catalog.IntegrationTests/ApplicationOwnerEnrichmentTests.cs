using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Wire-shape integration tests for slice 9 / E1: the catalog list + detail
/// endpoints must enrich <see cref="ApplicationResponse.Owner"/> via
/// <c>IUserDirectory</c> (ADR-0098). Both branches are pinned:
/// <list type="bullet">
///   <item>Owner populated when a matching <c>users</c> row exists in the same tenant.</item>
///   <item>Owner null when there is no matching user (deleted or never imported).</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ApplicationOwnerEnrichmentTests : CatalogIntegrationTestBase
{
    [TestMethod]
    public async Task GET_applications_returns_Owner_populated_when_user_row_exists()
    {
        var unique = $"e1-list-owner-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var ownerUserId = await Fx.SeedUserInOrganizationAsync(
            tenantId,
            displayName: "Owner Of List App",
            email: $"{unique}@orga.kartova.local");
        var appId = await Fx.SeedSingleApplicationAsync(
            tenantId, ownerUserId, teamId: null, namePrefix: unique);

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync("/api/v1/catalog/applications?limit=200");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.SingleOrDefault(i => i.Id == appId);
            Assert.IsNotNull(ours, "seeded application must be visible in the list");
            Assert.IsNotNull(ours!.Owner, "Owner must be populated when a matching users row exists");
            Assert.AreEqual(ownerUserId, ours.Owner!.Id);
            Assert.AreEqual("Owner Of List App", ours.Owner.DisplayName);
            Assert.AreEqual($"{unique}@orga.kartova.local", ours.Owner.Email);
        }
        finally
        {
            await Fx.DeleteApplicationAsync(tenantId, appId);
            await Fx.DeleteUserInOrganizationAsync(ownerUserId);
        }
    }

    [TestMethod]
    public async Task GET_applications_returns_Owner_null_when_user_row_is_missing()
    {
        // Seed an application whose OwnerUserId has no corresponding users row in the
        // Organization schema (simulates "user deleted after application registered").
        var unique = $"e1-list-noowner-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var orphanOwnerId = Guid.NewGuid();
        var appId = await Fx.SeedSingleApplicationAsync(
            tenantId, orphanOwnerId, teamId: null, namePrefix: unique);

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync("/api/v1/catalog/applications?limit=200");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.SingleOrDefault(i => i.Id == appId);
            Assert.IsNotNull(ours, "seeded application must be visible in the list even without a matching users row");
            Assert.IsNull(ours!.Owner, "Owner must be null when no matching users row exists for OwnerUserId");
            // The OwnerUserId column itself is still present — only the display projection is missing.
            Assert.AreEqual(orphanOwnerId, ours.OwnerUserId);
        }
        finally
        {
            await Fx.DeleteApplicationAsync(tenantId, appId);
        }
    }

    [TestMethod]
    public async Task GET_application_by_id_returns_Owner_populated_when_user_row_exists()
    {
        var unique = $"e1-detail-owner-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var ownerUserId = await Fx.SeedUserInOrganizationAsync(
            tenantId,
            displayName: "Owner Of Detail App",
            email: $"{unique}@orga.kartova.local");
        var appId = await Fx.SeedSingleApplicationAsync(
            tenantId, ownerUserId, teamId: null, namePrefix: unique);

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync($"/api/v1/catalog/applications/{appId}");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.IsNotNull(body!.Owner, "Owner must be populated on the detail endpoint when a matching users row exists");
            Assert.AreEqual(ownerUserId, body.Owner!.Id);
            Assert.AreEqual("Owner Of Detail App", body.Owner.DisplayName);
            Assert.AreEqual($"{unique}@orga.kartova.local", body.Owner.Email);
        }
        finally
        {
            await Fx.DeleteApplicationAsync(tenantId, appId);
            await Fx.DeleteUserInOrganizationAsync(ownerUserId);
        }
    }

    [TestMethod]
    public async Task GET_application_by_id_returns_Owner_null_when_user_row_is_missing()
    {
        var unique = $"e1-detail-noowner-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var orphanOwnerId = Guid.NewGuid();
        var appId = await Fx.SeedSingleApplicationAsync(
            tenantId, orphanOwnerId, teamId: null, namePrefix: unique);

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync($"/api/v1/catalog/applications/{appId}");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.IsNull(body!.Owner, "Owner must be null on the detail endpoint when no matching users row exists");
            Assert.AreEqual(orphanOwnerId, body.OwnerUserId);
        }
        finally
        {
            await Fx.DeleteApplicationAsync(tenantId, appId);
        }
    }
}
