using Kartova.Organization.Infrastructure;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Convenience base class — exposes the assembly-scoped <see cref="KartovaApiFixture"/>
/// (owned by <see cref="IntegrationTestAssemblySetup"/>) as a protected static so derived
/// test classes can write <c>Fx.X</c> instead of the fully qualified
/// <c>IntegrationTestAssemblySetup.Fx.X</c>. The fixture itself is created exactly once
/// per assembly run via <c>[AssemblyInitialize]</c>; this base class adds no lifecycle.
/// Also hosts the two cross-suite test helpers (<see cref="BypassOptions"/> +
/// <see cref="NewTenantAsync"/>) that every H1 integration suite consumes — promoted here
/// in slice-9 H1 batch 2 cleanup so individual suites don't keep duplicating them.
/// </summary>
[TestClass]
public abstract class OrganizationIntegrationTestBase
{
    protected static KartovaApiFixture Fx => IntegrationTestAssemblySetup.Fx;

    /// <summary>
    /// Builds an <see cref="OrganizationDbContext"/> options object pointed at the
    /// BYPASSRLS connection. Test verifications run OUTSIDE a request (no
    /// <c>SET LOCAL app.current_tenant_id</c>) so RLS would otherwise hide every row.
    /// </summary>
    protected static DbContextOptions<OrganizationDbContext> BypassOptions() =>
        new DbContextOptionsBuilder<OrganizationDbContext>()
            .UseNpgsql(Fx.BypassConnectionString)
            .Options;

    /// <summary>
    /// Fresh per-test context: a unique 8-char suffix on the admin email derives a
    /// deterministic per-test tenant id via <see cref="KartovaApiFixtureBase.TenantFor"/>,
    /// isolating parallel tests. A matching <c>organizations</c> row is seeded via
    /// BYPASSRLS so the request-side scope finds it on the first hit. Returns the
    /// generated admin email + derived tenant id so callers can issue authenticated
    /// requests and clean up in <c>finally</c>.
    /// </summary>
    protected static async Task<(string adminEmail, Guid tenantId)> NewTenantAsync(string scenarioSlug)
    {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var adminEmail = $"admin@{scenarioSlug}-{unique}.kartova.local";
        var tenantId = KartovaApiFixtureBase.TenantFor(adminEmail).Value;
        await Fx.SeedOrganizationAsync(tenantId, $"Org-{scenarioSlug}");
        return (adminEmail, tenantId);
    }
}
