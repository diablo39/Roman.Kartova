using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

namespace Kartova.Testing.Auth;

/// <summary>
/// Shared <see cref="WebApplicationFactory{TEntryPoint}"/> base for module-level
/// HTTP integration tests. Owns the cross-cutting plumbing every module repeats:
/// a <c>postgres:18-alpine</c> Testcontainer, the role-and-grants seed, the
/// <see cref="TestJwtSigner"/> swap into the API's JWT-bearer pipeline, and JWT
/// minting helpers (deterministic <c>sub</c> and tenant-id derivation from
/// email). Module-specific fixtures derive from this and only have to declare
/// which DbContext to migrate.
/// </summary>
[ExcludeFromCodeCoverage]
public abstract class KartovaApiFixtureBase
    : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .WithDatabase("kartova")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private KeycloakContainerFixture? _keycloak;

    public TestJwtSigner Signer { get; } = new();

    /// <summary>
    /// Opt-in hook for derived fixtures that need a real Keycloak container.
    /// When <see langword="true"/>, <see cref="InitializeAsync"/> additionally
    /// spins up a <see cref="KeycloakContainerFixture"/> and <see cref="CreateHost"/>
    /// wires the four <c>KartovaIdentity__Keycloak__*</c> env vars from the live
    /// container instead of the placeholder fallback. Defaults to <see langword="false"/>
    /// so module fixtures that do not exercise the Keycloak admin client (Catalog)
    /// keep their fast startup path.
    /// <para>
    /// <b>Precondition:</b> when this is <see langword="true"/>,
    /// <see cref="InitializeAsync"/> MUST run before <see cref="CreateClient"/> /
    /// <see cref="CreateAuthenticatedClientAsync"/> — the live KC URL is only
    /// available after the container starts. <see cref="CreateHost"/> throws
    /// <see cref="InvalidOperationException"/> if the precondition is violated.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Consumer pattern: override on the derived module fixture, e.g.
    /// <code>
    /// protected override bool UsesKeycloakContainer =&gt; true;
    /// </code>
    /// Test code that needs the live Keycloak endpoint (token issuance, admin REST)
    /// can read it via <see cref="Keycloak"/> once the fixture has initialized.
    /// </remarks>
    protected virtual bool UsesKeycloakContainer => false;

    /// <summary>
    /// The shared Keycloak fixture when <see cref="UsesKeycloakContainer"/> is
    /// <see langword="true"/>; otherwise <see langword="null"/>. Exposed so opt-in
    /// derived fixtures (and tests built on them) can read the live authority,
    /// admin client secret, etc.
    /// </summary>
    public KeycloakContainerFixture? Keycloak => _keycloak;

    /// <summary>
    /// Mirror of the API's <c>ConfigureHttpJsonOptions</c> setup in <c>Program.cs</c>:
    /// camelCase property names (default for ASP.NET Core minimal APIs) plus a
    /// <see cref="JsonStringEnumConverter"/> that emits enum values as camelCase
    /// strings (per ADR-0095). Tests deserialize HTTP responses with this so the
    /// wire shape matches end-to-end — without it, any enum field on a response
    /// (e.g. <c>ApplicationResponse.Lifecycle</c>) fails to deserialize because
    /// <see cref="JsonSerializerOptions.Default"/> expects integer enums.
    /// </summary>
    public static JsonSerializerOptions WireJson { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string MainConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.AppRole);

    public string BypassConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.BypassRole);

    public string MigratorConnectionString =>
        PostgresTestBootstrap.ConnectionStringFor(_pg.GetConnectionString(), PostgresTestBootstrap.MigratorRole);

    /// <summary>
    /// Spins up the Postgres container and applies module migrations. Call once per
    /// assembly run from an <c>[AssemblyInitialize]</c> handler (see remarks).
    /// </summary>
    /// <remarks>
    /// Consumer pattern: one fixture per assembly run, shared across every test class.
    /// Requires <c>[assembly: DoNotParallelize]</c> in <c>Properties/AssemblyInfo.cs</c>
    /// because the fixture mutates process-global env vars.
    /// <code>
    /// [TestClass]
    /// public sealed class IntegrationTestAssemblySetup
    /// {
    ///     public static KartovaApiFixture Fx { get; private set; } = null!;
    ///
    ///     [AssemblyInitialize]
    ///     public static async Task InitAsync(TestContext _)
    ///     {
    ///         Fx = new KartovaApiFixture();
    ///         await Fx.InitializeAsync();
    ///     }
    ///
    ///     [AssemblyCleanup]
    ///     public static async Task CleanupAsync()
    ///     {
    ///         if (Fx is not null) await ((IAsyncDisposable)Fx).DisposeAsync();
    ///     }
    /// }
    /// </code>
    /// Per-class <c>[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]</c>
    /// is the wrong pattern here — it creates one fixture per derived class, which
    /// for a heavyweight Postgres+API fixture is a 6× wall-clock regression vs the
    /// xUnit baseline. See <c>docs/superpowers/verification/2026-05-09-feat-mstest-migration/phase-9-review.md</c>
    /// for the bug Phase 9 hit and corrected.
    /// </remarks>
    public async Task InitializeAsync()
    {
        var pgTask = _pg.StartAsync();
        Task? kcTask = null;
        if (UsesKeycloakContainer)
        {
            // Start KC in parallel with Postgres — both take ~5-10s and there is
            // no inter-dependency. Saves wall-clock when the opt-in path is active.
            _keycloak = new KeycloakContainerFixture();
            kcTask = _keycloak.InitializeAsync();
        }

        await pgTask;
        if (kcTask is not null) await kcTask;

        await PostgresTestBootstrap.SeedRolesAndSchemaAsync(_pg.GetConnectionString());
        await RunModuleMigrationsAsync(MigratorConnectionString);
    }

    ValueTask IAsyncDisposable.DisposeAsync() => DisposeAsyncCore();

    /// <summary>
    /// Override hook for module-specific teardown — called via the
    /// <see cref="IAsyncDisposable"/>.DisposeAsync hook (used by <c>await using</c>
    /// or MSTest <c>[ClassCleanup]</c>). Derived classes that own additional disposable
    /// resources should override and chain via <c>await base.DisposeAsyncCore();</c>.
    /// </summary>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        await _pg.DisposeAsync();
        if (_keycloak is not null)
        {
            await _keycloak.DisposeAsync();
        }
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

        if (UsesKeycloakContainer)
        {
            // Read the virtual rather than the backing field so a misordered
            // lifecycle (CreateClient before InitializeAsync) surfaces as an
            // explicit InvalidOperationException instead of silently falling
            // through to the placeholder branch and producing a confusing
            // "KC admin client can't authenticate" runtime error.
            if (_keycloak is null)
            {
                throw new InvalidOperationException(
                    $"{nameof(InitializeAsync)} must run before {nameof(CreateClient)} when {nameof(UsesKeycloakContainer)} is true.");
            }

            // Opt-in path: point the AddKeycloakAdminClient binding at the live
            // Testcontainer. Realm seed matches deploy/keycloak/kartova-realm.json.
            // The CreateInvitation / RevokeInvitation / ExpireInvitation handlers
            // in the Organization module exercise this for real.
            Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__BaseUrl", _keycloak.KeycloakBaseUrl);
            Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__Realm", RealmSeedConstants.RealmName);
            Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__AdminClientId", RealmSeedConstants.AdminClientId);
            Environment.SetEnvironmentVariable("KartovaIdentity__Keycloak__AdminClientSecret", RealmSeedConstants.AdminClientSecret);
        }
        else
        {
            // Slice 9 / Phase D: Kartova.SharedKernel.Identity.AddKeycloakAdminClient runs
            // .ValidateOnStart() and rejects the appsettings.json placeholder verbatim.
            // Fixtures that do not opt into a real KC container (Catalog and similar)
            // do not exercise the KC admin client, but the options validation runs at
            // host startup unconditionally — supply a test-only secret so the host
            // boots. Production overrides this via secret store.
            Environment.SetEnvironmentVariable(
                "KartovaIdentity__Keycloak__AdminClientSecret",
                "test-only-secret-not-used-by-any-real-call");
        }
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
    /// <para>
    /// Slice 9 / H1 batch 4 added two optional overrides used by session-bootstrap
    /// tests that need to impersonate a specific KC user id (e.g. the
    /// <c>keycloak_user_id</c> stored on a Pending invitation) and emit an
    /// <c>email</c> claim so <c>SessionStartHandler</c> can run its upsert +
    /// invitation-acceptance side effects:
    /// </para>
    /// <list type="bullet">
    ///   <item><paramref name="subjectOverride"/> — replaces the <c>sub</c> claim
    ///   value (the deterministic <see cref="SubFor"/> Guid is used when null).</item>
    ///   <item><paramref name="emailClaim"/> — when non-null, adds an <c>email</c>
    ///   claim to the JWT. Defaults to null so existing tests continue to mint
    ///   tokens without an email claim (their assertions don't depend on the
    ///   session-bootstrap projection upsert).</item>
    /// </list>
    /// </summary>
    public Task<HttpClient> CreateAuthenticatedClientAsync(
        string email,
        string[]? roles = null,
        Guid? subjectOverride = null,
        string? emailClaim = null,
        string? nameClaim = null)
    {
        var sub = subjectOverride ?? SubFor(email);
        var tenant = TenantFor(email);
        var token = Signer.IssueForTenant(
            tenant,
            roles ?? new[] { KartovaRoles.OrgAdmin },
            subject: sub.ToString(),
            email: emailClaim,
            name: nameClaim);

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

    protected static Guid SubFor(string email) => DeterministicGuid("sub:" + email.ToLowerInvariant());

    public static TenantId TenantFor(string email)
    {
        // Same domain → same tenant. Two users at "@orga.kartova.local" share OrgA.
        var at = email.IndexOf('@');
        var domain = at >= 0 ? email[(at + 1)..].ToLowerInvariant() : email.ToLowerInvariant();
        return new TenantId(DeterministicGuid("tenant:" + domain));
    }

    public static Guid DeterministicGuid(string seed)
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
