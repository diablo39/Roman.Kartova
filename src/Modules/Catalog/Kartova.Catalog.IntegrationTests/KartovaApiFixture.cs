using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Kartova.Api;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// WebApplicationFactory-based fixture for Catalog HTTP integration tests.
/// Mirrors Organization's KartovaApiFixture: starts a Postgres container, seeds
/// roles + grants, runs Catalog migrations under the migrator role, and exposes
/// a TestJwtSigner so tests can mint authenticated clients.
/// </summary>
[ExcludeFromCodeCoverage]
public class KartovaApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("kartova")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public TestJwtSigner Signer { get; } = new();

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await PostgresTestBootstrap.SeedRolesAndSchemaAsync(_pg.GetConnectionString());
        await PostgresTestBootstrap.RunMigrationsAsync<CatalogDbContext>(
            MigratorConnectionString,
            opts => new CatalogDbContext(opts));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _pg.DisposeAsync();
        await base.DisposeAsync();
    }

    public string MainConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.AppRole);

    public string BypassConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.BypassRole);

    public string MigratorConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.MigratorRole);

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // Env vars must be set BEFORE Program.Main reads configuration; double-underscore maps to ':'.
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Main}", MainConnectionString);
        Environment.SetEnvironmentVariable($"ConnectionStrings__{KartovaConnectionStrings.Bypass}", BypassConnectionString);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Authority), TestJwtSigner.Issuer);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.Audience), TestJwtSigner.Audience);
        Environment.SetEnvironmentVariable(EnvKey(AuthenticationConfigKeys.RequireHttpsMetadata), "false");
        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.UseTestJwtSigner(Signer);
        });
    }

    /// <summary>
    /// Creates an HTTP client whose Authorization header carries a JWT for
    /// <paramref name="email"/>'s deterministic <c>sub</c> claim (Guid form) and
    /// the deterministic test tenant id derived from the email's domain. Roles
    /// default to <c>OrgAdmin</c> so the request passes any role guards.
    /// </summary>
    public Task<HttpClient> CreateAuthenticatedClientAsync(string email, string[]? roles = null)
    {
        var sub = SubFor(email);
        var tenant = TenantFor(email);
        var token = Signer.IssueForTenant(tenant, roles ?? new[] { "OrgAdmin" }, subject: sub.ToString());

        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Task.FromResult(client);
    }

    /// <summary>
    /// Anonymous client (no Authorization header). Used to verify 401 paths.
    /// </summary>
    public HttpClient CreateAnonymousClient() => CreateClient();

    /// <summary>
    /// Returns the deterministic Guid that <see cref="CreateAuthenticatedClientAsync"/>
    /// uses as the JWT <c>sub</c> claim for <paramref name="email"/>.
    /// </summary>
    public Task<Guid> GetSubClaimAsync(string email) => Task.FromResult(SubFor(email));

    /// <summary>
    /// Returns the deterministic tenant id that
    /// <see cref="CreateAuthenticatedClientAsync"/> embeds in the JWT for
    /// <paramref name="email"/>.
    /// </summary>
    public Task<Guid> GetTenantIdClaimAsync(string email) => Task.FromResult(TenantFor(email).Value);

    private static Guid SubFor(string email) => DeterministicGuid("sub:" + email.ToLowerInvariant());

    private static TenantId TenantFor(string email)
    {
        // Same domain → same tenant. Two users at "@orga.kartova.local" share OrgA.
        var at = email.IndexOf('@');
        var domain = at >= 0 ? email[(at + 1)..].ToLowerInvariant() : email.ToLowerInvariant();
        return new TenantId(DeterministicGuid("tenant:" + domain));
    }

    private static Guid DeterministicGuid(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        var bytes = new byte[16];
        Array.Copy(hash, bytes, 16);
        // Force version 4 / variant 1 bits so the value is a well-formed UUID.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static string EnvKey(string configKey) => configKey.Replace(":", "__");
}
