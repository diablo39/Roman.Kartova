using System.Diagnostics.CodeAnalysis;
using DotNet.Testcontainers.Builders;
using Testcontainers.Keycloak;

namespace Kartova.Testing.Auth;

/// <summary>
/// Shared Keycloak Testcontainer — boots a <c>quay.io/keycloak/keycloak:26.1</c>
/// container with <c>--import-realm</c> mounting both <c>kartova-realm.json</c>
/// (tenant realm) and <c>kartova-platform-realm.json</c> (ADR-0118 operator realm)
/// so the realm seeds (clients, roles, service accounts) are in place by the time
/// the wait strategy observes both realms' <c>/.well-known/openid-configuration</c>
/// discovery documents.
/// <para>
/// Originally lived in <c>Kartova.Api.IntegrationTests</c> (where the auth-smoke
/// tests were the only consumer). Slice 9 / H1-prereq promoted it to
/// <c>Kartova.Testing.Auth</c> so module integration tests can opt into a real KC
/// via <see cref="KartovaApiFixtureBase.UsesKeycloakContainer"/>.
/// </para>
/// <para>
/// Slice 9 / H1-prereq follow-up: the Postgres container previously bundled into
/// this fixture has been split out. <see cref="KartovaApiFixtureBase"/> owns its own
/// Postgres lifecycle, so leaving a second one inside this fixture spun up an
/// unused container per assembly run. Consumers that need a sibling Postgres
/// (e.g. <c>Kartova.Api.IntegrationTests</c>) compose one separately.
/// </para>
/// <para>
/// <b>Realm JSON discovery:</b> the file must be present in the consuming test
/// assembly's output directory at <c>AppContext.BaseDirectory</c>. MSBuild does
/// NOT propagate <c>&lt;None CopyToOutputDirectory&gt;</c> items across project
/// references, so every consumer csproj that opts into KC must include the
/// <c>kartova-realm.json</c> file as a copy-to-output item (see
/// <c>Kartova.Api.IntegrationTests.csproj</c> and the Organization integration
/// test csproj for the pattern).
/// </para>
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class KeycloakContainerFixture : IAsyncDisposable
{
    public KeycloakContainer Keycloak { get; } = new KeycloakBuilder()
        .WithImage("quay.io/keycloak/keycloak:26.1")
        // KeycloakBuilder.Init() already appends "start-dev"; WithCommand merges
        // rather than replacing, so we only add the extra flag here (otherwise
        // Cmd becomes ["start-dev", "start-dev", "--import-realm"] and Keycloak
        // 26.1 rejects the duplicate with "Unknown option: '--profile'").
        .WithCommand("--import-realm")
        // Target is a *directory* — the source filename is appended. Pointing
        // at "/opt/keycloak/data/import/kartova-realm.json" would create that
        // as a subdirectory and nest the file inside it, so Keycloak's
        // --import-realm scan of /opt/keycloak/data/import/*.json would miss it.
        .WithResourceMapping(
            Path.Combine(AppContext.BaseDirectory, "kartova-realm.json"),
            "/opt/keycloak/data/import")
        .WithResourceMapping(
            Path.Combine(AppContext.BaseDirectory, "kartova-platform-realm.json"),
            "/opt/keycloak/data/import")
        // Liveness check only — each discovery doc proves its realm imported, not that every
        // client/user inside it did too; downstream live tests (e.g. PlatformRealmLiveTokenTests) cover that.
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath($"/realms/{RealmSeedConstants.RealmName}/.well-known/openid-configuration"))
            .UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath($"/realms/{RealmSeedConstants.PlatformRealmName}/.well-known/openid-configuration")))
        .Build();

    public Task InitializeAsync() => Keycloak.StartAsync();

    public ValueTask DisposeAsync() => Keycloak.DisposeAsync();

    /// <summary>
    /// OIDC authority for the seeded realm — appended with <c>/protocol/...</c> by
    /// token endpoints. Matches the <c>Authentication:Authority</c> shape used by
    /// the Kartova API. <see cref="KeycloakContainer.GetBaseAddress"/> returns a
    /// URL with a trailing slash, so the concatenation produces
    /// <c>http://host:port/realms/kartova</c> (no double slash).
    /// </summary>
    public string KeycloakAuthority => $"{Keycloak.GetBaseAddress()}realms/{RealmSeedConstants.RealmName}";

    /// <summary>OIDC authority for the ADR-0118 operator realm.</summary>
    public string PlatformKeycloakAuthority => $"{Keycloak.GetBaseAddress()}realms/{RealmSeedConstants.PlatformRealmName}";

    /// <summary>
    /// Base URL of the Keycloak admin REST API, without a trailing slash so
    /// callers can safely append <c>/realms/{realm}/...</c>. Maps directly onto
    /// the <c>KartovaIdentity:Keycloak:BaseUrl</c> option binding.
    /// </summary>
    public string KeycloakBaseUrl => Keycloak.GetBaseAddress().TrimEnd('/');
}
