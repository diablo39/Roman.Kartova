using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Postgres;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Pins lifetime contracts that ADR-0090 + ADR-0093 rely on: tenant-scoped collaborators
/// must live in the HTTP request scope so they observe the same <c>ITenantScope</c> the
/// transport adapter began. Singleton or Transient drift would silently break tenant isolation
/// at scale (a singleton ICurrentUser would freeze on the first caller's identity).
/// </summary>
[TestClass]
public class DiLifetimeRules
{
    [TestMethod]
    public void ICurrentUser_is_registered_as_scoped()
    {
        var services = BuildContainer();

        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ICurrentUser));

        Assert.IsNotNull(descriptor, "AddKartovaJwtAuth wires ICurrentUser → HttpContextCurrentUser");
        Assert.AreEqual(
            ServiceLifetime.Scoped,
            descriptor!.Lifetime,
            "ICurrentUser reads per-request claims; singleton or transient would either freeze or " +
            "fail to share the JWT-derived tenant scope (ADR-0090/0093)");
    }

    [TestMethod]
    [DataRow(typeof(Kartova.Catalog.Infrastructure.CatalogDbContext))]
    [DataRow(typeof(Kartova.Organization.Infrastructure.OrganizationDbContext))]
    public void Tenant_scoped_DbContexts_are_registered_as_scoped(Type dbContextType)
    {
        var services = BuildContainer();

        var descriptor = services.SingleOrDefault(d => d.ServiceType == dbContextType);

        Assert.IsNotNull(descriptor, $"{dbContextType.Name} is registered by its module's RegisterServices");
        Assert.AreEqual(
            ServiceLifetime.Scoped,
            descriptor!.Lifetime,
            $"{dbContextType.Name} resolves its connection from ITenantScope; " +
            "singleton/transient would either share state across tenants or open extra connections " +
            "outside the request scope (ADR-0090)");
    }

    private static IServiceCollection BuildContainer()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [AuthenticationConfigKeys.Authority] = "https://keycloak.example.com/realms/kartova",
                [AuthenticationConfigKeys.Audience] = "kartova-api",
                [AuthenticationConfigKeys.RequireHttpsMetadata] = "false",
                ["ConnectionStrings:Main"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["ConnectionStrings:Bypass"] = "Host=localhost;Database=test;Username=test;Password=test",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNpgsqlDataSource(configuration.GetConnectionString("Main")!);
        services.AddTenantScope();
        services.AddKartovaJwtAuth(configuration);

        foreach (var module in DiscoverModules())
        {
            module.RegisterServices(services, configuration);
        }

        return services;
    }

    private static IEnumerable<IModule> DiscoverModules() =>
        AssemblyRegistry.AllProduction()
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(IModule).IsAssignableFrom(t))
            .Select(t => (IModule)Activator.CreateInstance(t)!);
}
