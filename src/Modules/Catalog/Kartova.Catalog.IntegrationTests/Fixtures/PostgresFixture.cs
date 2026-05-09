using Testcontainers.PostgreSql;

namespace Kartova.Catalog.IntegrationTests.Fixtures;

public sealed class PostgresFixture : IAsyncDisposable
{
    private PostgreSqlContainer? _container;

    public string ConnectionString => _container?.GetConnectionString()
        ?? throw new InvalidOperationException("Container not started");

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder()
            .WithImage("postgres:18-alpine")
            .WithDatabase("kartova")
            .WithUsername("migrator")
            .WithPassword("dev")
            .Build();

        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
