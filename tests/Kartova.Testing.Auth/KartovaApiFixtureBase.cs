using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Kartova.Api;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kartova.Testing.Auth;

/// <summary>
/// Shared <see cref="WebApplicationFactory{TEntryPoint}"/> base for module-level
/// HTTP integration tests. Owns the cross-cutting plumbing every module repeats:
/// a <c>postgres:16-alpine</c> Testcontainer, the role-and-grants seed, the
/// <see cref="TestJwtSigner"/> swap into the API's JWT-bearer pipeline, and JWT
/// minting helpers (deterministic <c>sub</c> and tenant-id derivation from
/// email). Module-specific fixtures derive from this and only have to declare
/// which DbContext to migrate.
/// </summary>
[ExcludeFromCodeCoverage]
public abstract class KartovaApiFixtureBase : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("kartova")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public TestJwtSigner Signer { get; } = new();

    public string MainConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.AppRole);

    public string BypassConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.BypassRole);

    public string MigratorConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.MigratorRole);

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        await PostgresTestBootstrap.SeedRolesAndSchemaAsync(_pg.GetConnectionString());
        await RunModuleMigrationsAsync(MigratorConnectionString);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _pg.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>
    /// Applies the deriving module's EF migrations against
    /// <paramref name="migratorConnectionString"/>. Implementations typically call
    /// <see cref="PostgresTestBootstrap.RunMigrationsAsync{TDbContext}"/> with their
    /// module's <c>DbContext</c>.
    /// </summary>
    protected abstract Task RunModuleMigrationsAsync(string migratorConnectionString);

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
