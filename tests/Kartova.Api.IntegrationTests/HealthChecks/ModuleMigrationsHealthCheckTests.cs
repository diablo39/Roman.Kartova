using Kartova.Api.HealthChecks;
using Kartova.Audit.Infrastructure;
using Kartova.Catalog.Infrastructure;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Kartova.Api.IntegrationTests.HealthChecks;

[TestClass]
public class ModuleMigrationsHealthCheckTests
{
    private PostgreSqlContainer? _pg;

    [TestInitialize]
    public async Task InitializeAsync()
    {
        _pg = new PostgreSqlBuilder()
            .WithImage("postgres:18-alpine")
            .WithDatabase("kartova")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await _pg.StartAsync();

        try
        {
            await PostgresTestBootstrap.SeedRolesAndSchemaAsync(_pg.GetConnectionString());
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DuplicateObject)
        {
            // Roles/schema already seeded on this container by a prior call in this test run.
        }
    }

    [TestCleanup]
    public async Task CleanupAsync()
    {
        if (_pg is not null)
        {
            await _pg.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Healthy_when_every_module_is_fully_migrated()
    {
        await MigrateAllModulesAsync();
        var report = await RunCheckAsync(FakeModules());

        Assert.AreEqual(HealthStatus.Healthy, report.Entries["migrations"].Status);
    }

    [TestMethod]
    public async Task Unhealthy_and_names_the_module_when_one_module_has_pending_migrations()
    {
        await MigrateAsync<CatalogDbContext>(o => new CatalogDbContext(o));
        await MigrateAsync<OrganizationDbContext>(o => new OrganizationDbContext(o));
        // Audit intentionally NOT migrated.
        var report = await RunCheckAsync(FakeModules());

        Assert.AreEqual(HealthStatus.Unhealthy, report.Entries["migrations"].Status);
        StringAssert.Contains(report.Entries["migrations"].Description, "Audit");
    }

    [TestMethod]
    public async Task Unhealthy_not_throwing_when_a_module_DbContextType_has_no_factory_entry()
    {
        await MigrateAllModulesAsync();
        var bogusModule = Substitute.For<IModule>();
        bogusModule.Name.Returns("Bogus");
        bogusModule.DbContextType.Returns(typeof(object)); // never registered below

        var report = await RunCheckAsync([.. FakeModules(), bogusModule]);

        Assert.AreEqual(HealthStatus.Unhealthy, report.Entries["migrations"].Status);
        Assert.IsNotNull(report.Entries["migrations"].Exception);
    }

    private async Task<HealthReport> RunCheckAsync(IModule[] modules)
    {
        var appConnString = PostgresTestBootstrap.ConnectionStringFor(_pg!.GetConnectionString(), PostgresTestBootstrap.AppRole);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{KartovaConnectionStrings.Main}"] = appConnString,
            })
            .Build();

        // Mirrors Program.cs: build the migrations-check provider from each real
        // module's RegisterForMigrator override (the same tenant-scope-free
        // registration Kartova.Migrator uses), independent of the `modules` param
        // below, which may include NSubstitute fakes (e.g. a bogus 4th module).
        var migrationsCheckServices = new ServiceCollection();
        foreach (var realModule in RealModules())
        {
            realModule.RegisterForMigrator(migrationsCheckServices, configuration);
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(modules);
        services.AddSingleton(migrationsCheckServices.BuildServiceProvider());
        services.AddHealthChecks().AddCheck<ModuleMigrationsHealthCheck>("migrations", tags: ["startup"]);

        await using var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetRequiredService<HealthCheckService>();
        return await healthCheckService.CheckHealthAsync();
    }

    private static IModule[] RealModules() =>
        [new CatalogModule(), new OrganizationModule(), new AuditModule()];

    private Task MigrateAllModulesAsync() => Task.WhenAll(
        MigrateAsync<CatalogDbContext>(o => new CatalogDbContext(o)),
        MigrateAsync<OrganizationDbContext>(o => new OrganizationDbContext(o)),
        MigrateAsync<AuditDbContext>(o => new AuditDbContext(o)));

    private Task MigrateAsync<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
        => PostgresTestBootstrap.RunMigrationsAsync(
            PostgresTestBootstrap.ConnectionStringFor(_pg!.GetConnectionString(), PostgresTestBootstrap.MigratorRole),
            factory);

    private static IModule[] FakeModules()
    {
        var catalog = Substitute.For<IModule>();
        catalog.Name.Returns("Catalog");
        catalog.DbContextType.Returns(typeof(CatalogDbContext));

        var organization = Substitute.For<IModule>();
        organization.Name.Returns("Organization");
        organization.DbContextType.Returns(typeof(OrganizationDbContext));

        var audit = Substitute.For<IModule>();
        audit.Name.Returns("Audit");
        audit.DbContextType.Returns(typeof(AuditDbContext));

        return [catalog, organization, audit];
    }
}
