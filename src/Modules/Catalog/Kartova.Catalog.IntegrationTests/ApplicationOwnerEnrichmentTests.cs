using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Wire-shape integration tests for slice 9 / E1 (renamed slice 10 / ADR-0103): the
/// catalog list + detail endpoints must enrich <see cref="ApplicationResponse.CreatedBy"/>
/// via <c>IUserDirectory</c> (ADR-0098). Both branches are pinned:
/// <list type="bullet">
///   <item>CreatedBy populated when a matching <c>users</c> row exists in the same tenant.</item>
///   <item>CreatedBy null when there is no matching user (offboarded or never imported).</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ApplicationOwnerEnrichmentTests : CatalogIntegrationTestBase
{
    [TestMethod]
    public async Task GET_applications_returns_CreatedBy_populated_when_user_row_exists()
    {
        var unique = $"e1-list-creator-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var creatorUserId = await Fx.SeedUserInOrganizationAsync(
            tenantId,
            displayName: "Creator Of List App",
            email: $"{unique}@orga.kartova.local");
        var appId = await Fx.SeedSingleApplicationAsync(
            tenantId, creatorUserId, teamId: null, namePrefix: unique);

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync("/api/v1/catalog/applications?limit=200");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.SingleOrDefault(i => i.Id == appId);
            Assert.IsNotNull(ours, "seeded application must be visible in the list");
            Assert.IsNotNull(ours!.CreatedBy, "CreatedBy must be populated when a matching users row exists");
            Assert.AreEqual(creatorUserId, ours.CreatedBy!.Id);
            Assert.AreEqual("Creator Of List App", ours.CreatedBy.DisplayName);
            Assert.AreEqual($"{unique}@orga.kartova.local", ours.CreatedBy.Email);
        }
        finally
        {
            // Order: user-row delete first so the more leak-prone cleanup (Organization
            // schema, no prefix-based sweep) runs even if the catalog cleanup is the one
            // that throws. Catalog rows can be recovered by DeleteApplicationsByPrefixAsync.
            await Fx.DeleteUserInOrganizationAsync(creatorUserId);
            await Fx.DeleteApplicationAsync(tenantId, appId);
        }
    }

    [TestMethod]
    public async Task GET_applications_returns_CreatedBy_null_when_user_row_is_missing()
    {
        // Seed an application whose CreatedByUserId has no corresponding users row in the
        // Organization schema (simulates "creator offboarded after application registered").
        var unique = $"e1-list-nocreator-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var orphanCreatorId = Guid.NewGuid();
        var appId = await Fx.SeedSingleApplicationAsync(
            tenantId, orphanCreatorId, teamId: null, namePrefix: unique);

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync("/api/v1/catalog/applications?limit=200");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>(KartovaApiFixtureBase.WireJson);
            var ours = page!.Items.SingleOrDefault(i => i.Id == appId);
            Assert.IsNotNull(ours, "seeded application must be visible in the list even without a matching users row");
            Assert.IsNull(ours!.CreatedBy, "CreatedBy must be null when no matching users row exists for CreatedByUserId");
            // The CreatedByUserId column itself is still present — only the display projection is missing.
            Assert.AreEqual(orphanCreatorId, ours.CreatedByUserId);
        }
        finally
        {
            await Fx.DeleteApplicationAsync(tenantId, appId);
        }
    }

    [TestMethod]
    public async Task GET_application_by_id_returns_CreatedBy_populated_when_user_row_exists()
    {
        var unique = $"e1-detail-creator-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var creatorUserId = await Fx.SeedUserInOrganizationAsync(
            tenantId,
            displayName: "Creator Of Detail App",
            email: $"{unique}@orga.kartova.local");
        var appId = await Fx.SeedSingleApplicationAsync(
            tenantId, creatorUserId, teamId: null, namePrefix: unique);

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync($"/api/v1/catalog/applications/{appId}");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.IsNotNull(body!.CreatedBy, "CreatedBy must be populated on the detail endpoint when a matching users row exists");
            Assert.AreEqual(creatorUserId, body.CreatedBy!.Id);
            Assert.AreEqual("Creator Of Detail App", body.CreatedBy.DisplayName);
            Assert.AreEqual($"{unique}@orga.kartova.local", body.CreatedBy.Email);
        }
        finally
        {
            // Order: user-row delete first so the more leak-prone cleanup (Organization
            // schema, no prefix-based sweep) runs even if the catalog cleanup is the one
            // that throws. Catalog rows can be recovered by DeleteApplicationsByPrefixAsync.
            await Fx.DeleteUserInOrganizationAsync(creatorUserId);
            await Fx.DeleteApplicationAsync(tenantId, appId);
        }
    }

    [TestMethod]
    public async Task GET_application_by_id_returns_CreatedBy_null_when_user_row_is_missing()
    {
        var unique = $"e1-detail-nocreator-{Guid.NewGuid():N}";
        var tenantId = Fx.TenantIdForEmail("admin@orga.kartova.local");
        var orphanCreatorId = Guid.NewGuid();
        var appId = await Fx.SeedSingleApplicationAsync(
            tenantId, orphanCreatorId, teamId: null, namePrefix: unique);

        try
        {
            var client = Fx.CreateClientForOrgA();
            var resp = await client.GetAsync($"/api/v1/catalog/applications/{appId}");

            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(body);
            Assert.IsNull(body!.CreatedBy, "CreatedBy must be null on the detail endpoint when no matching users row exists");
            Assert.AreEqual(orphanCreatorId, body.CreatedByUserId);
        }
        finally
        {
            await Fx.DeleteApplicationAsync(tenantId, appId);
        }
    }
}
