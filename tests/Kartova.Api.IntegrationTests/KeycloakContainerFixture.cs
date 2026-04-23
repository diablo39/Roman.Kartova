using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Testcontainers.Keycloak;
using Testcontainers.PostgreSql;
using Xunit;

namespace Kartova.Api.IntegrationTests;

public sealed class KeycloakContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("kartova")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public KeycloakContainer Keycloak { get; } = new KeycloakBuilder()
        .WithImage("quay.io/keycloak/keycloak:26.1")
        .WithCommand("start-dev", "--import-realm")
        .WithResourceMapping(
            Path.Combine(AppContext.BaseDirectory, "kartova-realm.json"),
            "/opt/keycloak/data/import/kartova-realm.json")
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPort(8080).ForPath("/realms/kartova/.well-known/openid-configuration")))
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), Keycloak.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(Postgres.DisposeAsync().AsTask(), Keycloak.DisposeAsync().AsTask());
    }

    public string KeycloakAuthority => $"{Keycloak.GetBaseAddress()}realms/kartova";
}
