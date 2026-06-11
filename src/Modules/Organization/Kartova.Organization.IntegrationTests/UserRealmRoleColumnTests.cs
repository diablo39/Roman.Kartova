using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Verifies that the <c>realm_role</c> column added to the <c>users</c> table
/// (ADR-0102 / slice-10 Task 1) round-trips correctly: the seeder inserts the
/// value, the EF model maps it, and the row can be fetched via HTTP.
/// </summary>
[TestClass]
public sealed class UserRealmRoleColumnTests : OrganizationIntegrationTestBase
{
    [TestMethod]
    public async Task Seeded_user_persists_realm_role()
    {
        var (adminEmail, tenantId) = await NewTenantAsync("realm-role-col");
        var unique = Guid.NewGuid().ToString("N")[..8];

        var userId = await Fx.SeedUserInOrganizationAsync(
            new TenantId(tenantId),
            "Ada Admin",
            $"ada-{unique}@orga.test",
            KartovaRoles.OrgAdmin);

        try
        {
            // 1. Verify via bypass DB read: realm_role column round-trips through EF.
            await using var db = new OrganizationDbContext(BypassOptions());
            var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId);
            Assert.IsNotNull(user, "Seeded user row must be present in the users table.");
            Assert.AreEqual(KartovaRoles.OrgAdmin, user!.RealmRole,
                "realm_role must persist the value supplied to SeedUserInOrganizationAsync.");

            // 2. Verify via HTTP: GET /api/v1/organizations/users/{id} returns 200.
            var client = await Fx.CreateAuthenticatedClientAsync(
                adminEmail, new[] { KartovaRoles.OrgAdmin });
            var resp = await client.GetAsync($"/api/v1/organizations/users/{userId}");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, resp.StatusCode,
                $"GET /users/{{id}} must return 200. Body: {await resp.Content.ReadAsStringAsync()}");
        }
        finally
        {
#pragma warning disable CA1031 // best-effort test teardown — log and continue
            try { await Fx.DeleteUserInOrganizationAsync(userId); }
            catch (Exception ex) { await Console.Error.WriteLineAsync($"[cleanup] users delete failed: {ex.Message}"); }
            try { await Fx.DeleteOrganizationsForTenantAsync(tenantId); }
            catch (Exception ex) { await Console.Error.WriteLineAsync($"[cleanup] orgs delete failed: {ex.Message}"); }
#pragma warning restore CA1031
        }
    }
}
