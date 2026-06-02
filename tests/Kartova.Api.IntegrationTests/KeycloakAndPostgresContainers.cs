using System.Diagnostics.CodeAnalysis;
using Kartova.Testing.Auth;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;

namespace Kartova.Api.IntegrationTests;

/// <summary>
/// Assembly-scoped aggregate that owns a <see cref="KeycloakContainerFixture"/>
/// and a sibling <see cref="PostgreSqlContainer"/>. The two containers are
/// independent (no inter-dependency) and start in parallel so the wall-clock
/// stays close to <c>max(KC startup, Postgres startup)</c> rather than the sum.
/// <para>
/// Slice 9 / H1-prereq follow-up: <see cref="KeycloakContainerFixture"/> used to
/// bundle Postgres alongside Keycloak. Promoting the fixture to
/// <c>Kartova.Testing.Auth</c> and wiring it into <see cref="KartovaApiFixtureBase"/>
/// (which owns its own Postgres) meant module integration tests spun up two
/// Postgres containers per assembly run. The fix splits the Postgres ownership
/// out of <see cref="KeycloakContainerFixture"/>; the Api.IntegrationTests
/// consumers — the only ones that drove a bundled Postgres directly — get a
/// thin aggregate here so the existing <c>Containers.Postgres</c> /
/// <c>Containers.Keycloak</c> call shape stays intact.
/// </para>
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class KeycloakAndPostgresContainers : IAsyncDisposable
{
    private readonly KeycloakContainerFixture _keycloakFixture = new();

    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .WithDatabase("kartova")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    /// <summary>
    /// Underlying <see cref="KeycloakContainer"/>. Exposed at the Testcontainers
    /// level (not wrapped by the fixture) so consumers that already called
    /// <c>Containers.Keycloak.GetBaseAddress()</c> in the pre-split shape keep
    /// working without churn.
    /// </summary>
    public KeycloakContainer Keycloak => _keycloakFixture.Keycloak;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), _keycloakFixture.InitializeAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(Postgres.DisposeAsync().AsTask(), _keycloakFixture.DisposeAsync().AsTask());
    }

    /// <summary>
    /// OIDC authority for the seeded realm. Forwards to
    /// <see cref="KeycloakContainerFixture.KeycloakAuthority"/> so existing
    /// callers can keep using <c>Containers.KeycloakAuthority</c>.
    /// </summary>
    public string KeycloakAuthority => _keycloakFixture.KeycloakAuthority;

    /// <summary>
    /// Admin REST base URL. Forwards to
    /// <see cref="KeycloakContainerFixture.KeycloakBaseUrl"/>.
    /// </summary>
    public string KeycloakBaseUrl => _keycloakFixture.KeycloakBaseUrl;
}
