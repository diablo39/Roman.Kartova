using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using DomainApplication = Kartova.Catalog.Domain.Application;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Catalog-specific fixture. All cross-module plumbing (Postgres container,
/// role bootstrap, JWT signer wiring, env-var wiring of the Kartova.Api host,
/// JWT minting helpers) lives in <see cref="KartovaApiFixtureBase"/>; this
/// type only declares which DbContext to migrate.
/// </summary>
[ExcludeFromCodeCoverage]
public class KartovaApiFixture : KartovaApiFixtureBase
{
    protected override Task RunModuleMigrationsAsync(string migratorConnectionString) =>
        PostgresTestBootstrap.RunMigrationsAsync<CatalogDbContext>(
            migratorConnectionString,
            opts => new CatalogDbContext(opts));

    /// <summary>
    /// Creates an HTTP client with a bearer token scoped to OrgA
    /// ("admin@orga.kartova.local"). Synchronous overload for tests that
    /// cannot await during field initialisation.
    /// </summary>
    public HttpClient CreateClientForOrgA() => CreateClientForEmail("admin@orga.kartova.local");

    /// <summary>
    /// Creates an HTTP client with a bearer token scoped to OrgB
    /// ("admin@orgb.kartova.local").
    /// </summary>
    public HttpClient CreateClientForOrgB() => CreateClientForEmail("admin@orgb.kartova.local");

    private HttpClient CreateClientForEmail(string email)
    {
        // Reuse the deterministic sub + tenant derivation from the base class
        // by issuing a token directly via the TestJwtSigner — mirrors what
        // CreateAuthenticatedClientAsync does but without the async wrapper.
        var tenant = TenantFor(email);
        var token = Signer.IssueForTenant(tenant, ["OrgAdmin"], subject: SubFor(email).ToString());
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Returns the deterministic <see cref="TenantId"/> for <paramref name="email"/>'s
    /// domain — the same value RLS uses. Exposed so test classes can seed rows for
    /// the correct tenant without re-implementing the derivation algorithm.
    /// </summary>
    public TenantId TenantIdForEmail(string email) => TenantFor(email);

    /// <summary>
    /// Seeds <paramref name="count"/> applications for the given tenant, with
    /// spread-apart <c>createdAt</c> timestamps so sort-by-createdAt tests are
    /// deterministic. Uses the bypass-RLS connection so rows can be inserted
    /// without an active tenant scope.
    /// </summary>
    public async Task SeedApplicationsAsync(TenantId tenantId, int count, string namePrefix)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;

        await using var db = new CatalogDbContext(opts);
        var origin = DateTimeOffset.UtcNow.AddMinutes(-count);
        for (var i = 0; i < count; i++)
        {
            db.Applications.Add(DomainApplication.Create(
                name: $"{namePrefix}{i:D3}",
                displayName: $"{namePrefix.ToUpperInvariant()}{i:D3}",
                description: "seeded for pagination tests",
                ownerUserId: Guid.NewGuid(),
                tenantId: tenantId,
                createdAt: origin.AddMinutes(i)));
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes a single application row, bypassing RLS so the delete is not
    /// blocked by the missing tenant scope.
    /// </summary>
    public async Task DeleteApplicationAsync(TenantId tenantId, Guid applicationId)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;

        await using var db = new CatalogDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM catalog_applications WHERE id = {0} AND tenant_id = {1}",
            applicationId, tenantId.Value);
    }

    /// <summary>
    /// Seeds <paramref name="count"/> applications in the given lifecycle state
    /// for the given tenant, with spread-apart <c>createdAt</c> timestamps.
    /// Slice 6 — used by ListApplicationsPaginationTests to populate Decommissioned
    /// rows that ADR-0073's default-view filter must hide.
    /// </summary>
    public async Task SeedApplicationsWithLifecycleAsync(
        TenantId tenantId,
        int count,
        string namePrefix,
        Lifecycle lifecycle)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;

        await using var db = new CatalogDbContext(opts);
        var origin = DateTimeOffset.UtcNow.AddMinutes(-count);
        for (var i = 0; i < count; i++)
        {
            var app = DomainApplication.Create(
                name: $"{namePrefix}{i:D3}",
                displayName: $"{namePrefix.ToUpperInvariant()}{i:D3}",
                description: "seeded for filter tests",
                ownerUserId: Guid.NewGuid(),
                tenantId: tenantId,
                createdAt: origin.AddMinutes(i));

            // Drive the aggregate into the desired terminal state via its own methods,
            // not by reflection on the private setter — keeps the test honest about
            // what the production state machine actually does.
            if (lifecycle == Lifecycle.Deprecated || lifecycle == Lifecycle.Decommissioned)
            {
                var clock = new FakeTimeProvider();
                clock.SetUtcNow(origin.AddMinutes(i).AddHours(1));
                app.Deprecate(sunsetDate: clock.GetUtcNow().AddMinutes(1), clock);

                if (lifecycle == Lifecycle.Decommissioned)
                {
                    // Advance the same clock past the sunset date so Decommission's
                    // "now >= SunsetDate" invariant holds. Using one provider makes the
                    // temporal relationship explicit; otherwise a future tweak to the first
                    // clock could silently invalidate the decommission step.
                    clock.SetUtcNow(origin.AddMinutes(i).AddHours(2));
                    app.Decommission(clock);
                }
            }

            db.Applications.Add(app);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Deletes application rows for a tenant whose <c>Name</c> starts with
    /// <paramref name="namePrefix"/>. Use in test teardown when the test seeded
    /// rows with a unique (e.g., Guid-suffixed) prefix — preserves rows seeded
    /// by other tests in the same class fixture. Slice 6.
    /// </summary>
    public async Task DeleteApplicationsByPrefixAsync(TenantId tenantId, string namePrefix)
    {
        var opts = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql(BypassConnectionString)
            .Options;

        await using var db = new CatalogDbContext(opts);
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM catalog_applications WHERE tenant_id = {0} AND name LIKE {1} || '%'",
            tenantId.Value, namePrefix);
    }

}
