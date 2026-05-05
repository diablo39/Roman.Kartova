using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
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
        var tenant = TenantIdForEmail(email);
        var token = Signer.IssueForTenant(tenant, ["OrgAdmin"], subject: SubForEmail(email).ToString());
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

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

    // Replicated from KartovaApiFixtureBase private methods so the synchronous
    // CreateClientForEmail helper can use them without going async.
    private static Guid SubForEmail(string email)
        => DeterministicGuid("sub:" + email.ToLowerInvariant());

    private static TenantId TenantIdForEmail(string email)
    {
        var at = email.IndexOf('@');
        var domain = at >= 0 ? email[(at + 1)..].ToLowerInvariant() : email.ToLowerInvariant();
        return new TenantId(DeterministicGuid("tenant:" + domain));
    }

    private static Guid DeterministicGuid(string seed)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(seed));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}
