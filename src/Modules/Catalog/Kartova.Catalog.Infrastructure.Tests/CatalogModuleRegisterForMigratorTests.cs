using FluentAssertions;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kartova.Catalog.Infrastructure.Tests;

/// <summary>
/// Pins the contract of <see cref="CatalogModule.RegisterForMigrator(IServiceCollection, IConfiguration)"/>
/// — the migrator-only DbContext registration path used by the Kartova.Migrator
/// container (Helm pre-upgrade Job / Docker init per ADR-0085). Slice-3 §13.10
/// followup: the equivalent Organization registration shows surviving NoCoverage
/// mutants in the slice-3 mutation report; this test closes the same gap on Catalog.
/// </summary>
public sealed class CatalogModuleRegisterForMigratorTests
{
    [Fact]
    public void RegisterForMigrator_resolves_CatalogDbContext_with_main_connection_string()
    {
        // Migrator runs against the Main connection (the migrator role is granted
        // BYPASSRLS in PG; KartovaConnectionStrings.RequireMain is what production
        // uses — see CatalogModule.cs:105 + OrganizationModule.cs:73).
        const string mainCs = "Host=localhost;Database=kartova_test_main;Username=test;Password=test";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{KartovaConnectionStrings.Main}"] = mainCs,
            })
            .Build();

        var services = new ServiceCollection();
        new CatalogModule().RegisterForMigrator(services, config);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        db.Should().NotBeNull();
        db.Database.GetConnectionString().Should().Be(mainCs);
    }

    [Fact]
    public void RegisterForMigrator_does_not_require_active_TenantScope_to_resolve_DbContext()
    {
        // The tenant-scoped path (AddModuleDbContext, used by RegisterServices) demands
        // an ITenantScope at resolution time and throws "TenantScope is not active"
        // otherwise. The migrator path uses plain AddDbContext; this test pins the
        // distinction so a future regression that wires the migrator through the
        // tenant-scoped path fails loudly here.
        const string mainCs = "Host=localhost;Database=kartova_test_main;Username=test;Password=test";
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{KartovaConnectionStrings.Main}"] = mainCs,
            })
            .Build();

        var services = new ServiceCollection();
        new CatalogModule().RegisterForMigrator(services, config);

        // No ITenantScope or TenantScopeBeginMiddleware in this graph — resolving
        // CatalogDbContext must succeed.
        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<CatalogDbContext>();

        act.Should().NotThrow();
    }

    [Fact]
    public void RegisterForMigrator_throws_canonical_InvalidOperationException_when_main_connection_string_is_missing()
    {
        // Pins the exact message shape KartovaConnectionStrings.Require produces.
        // CI bootstrap log scrapers depend on this format.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var services = new ServiceCollection();

        var act = () => new CatalogModule().RegisterForMigrator(services, config);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Connection string 'Kartova' is required. Set it via ConnectionStrings__Kartova env var.");
    }
}
