using System.Diagnostics.CodeAnalysis;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;

namespace Kartova.Api.IntegrationTests;

[ExcludeFromCodeCoverage]
public sealed class KeycloakContainerFixture : IAsyncDisposable
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:18-alpine")
        .WithDatabase("kartova")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

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
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/realms/kartova/.well-known/openid-configuration")))
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), Keycloak.StartAsync());
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(Postgres.DisposeAsync().AsTask(), Keycloak.DisposeAsync().AsTask());
    }

    public string KeycloakAuthority => $"{Keycloak.GetBaseAddress()}realms/kartova";
}
