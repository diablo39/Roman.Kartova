using System.Reflection;
using Kartova.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;

namespace Kartova.ArchitectureTests;

[TestClass]
public class TenantScopeRules
{
    private static readonly Assembly SharedKernel = typeof(Kartova.SharedKernel.Multitenancy.ITenantScope).Assembly;
    private static readonly Assembly SharedKernelAspNetCore = typeof(Kartova.SharedKernel.AspNetCore.TenantScopeBeginMiddleware).Assembly;
    private static readonly Assembly SharedKernelPostgres = typeof(Kartova.SharedKernel.Postgres.TenantScope).Assembly;
    private static readonly Assembly SharedKernelWolverine = typeof(Kartova.SharedKernel.Wolverine.TenantScopeWolverineMiddleware).Assembly;
    private static readonly Assembly OrganizationInfrastructure = typeof(Kartova.Organization.Infrastructure.OrganizationDbContext).Assembly;
    private static readonly Assembly OrganizationInfrastructureAdmin = typeof(Kartova.Organization.Infrastructure.Admin.AdminOrganizationDbContext).Assembly;

    [TestMethod]
    public void SharedKernel_has_no_framework_dependencies()
    {
        // KafkaFlow is added when the inbound consumer adapter lands in a later slice;
        // when added, append it here.
        var forbidden = new[]
        {
            "Npgsql",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "WolverineFx",
        };

        var result = Types.InAssembly(SharedKernel)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            "Kartova.SharedKernel must stay technology-agnostic; tech-specific code lives in SharedKernel.Postgres/AspNetCore/Wolverine (ADR-0090)");
    }

    [TestMethod]
    public void Admin_bypass_DbContext_is_isolated_to_admin_assembly()
    {
        var result = Types.InAssembly(OrganizationInfrastructure)
            .Should()
            .NotHaveDependencyOn("Kartova.Organization.Infrastructure.Admin")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            "Tenant-scoped Infrastructure must not depend on the BYPASSRLS Admin DbContext (ADR-0090)");
    }

    [TestMethod]
    public void AspNetCore_adapter_does_not_reference_Postgres_adapter()
    {
        // Spec §3.2: SharedKernel.AspNetCore and SharedKernel.Postgres are sibling
        // adapters consumed by the API composition root. Cross-reference would force
        // any future transport adapter (Wolverine, Kafka) to inherit a Postgres
        // dependency. ADR-0090 + slice-2-followup design 2026-04-28.
        var aspNetCoreRefs = SharedKernelAspNetCore
            .GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        Assert.IsFalse(
            aspNetCoreRefs.Contains("Kartova.SharedKernel.Postgres"),
            "Spec §3.2 forbids the cross-reference; transport adapters " +
            "exchange failures via Kartova.SharedKernel.Multitenancy.TenantScopeBeginException");
    }

    [TestMethod]
    public void Wolverine_middleware_project_exists()
    {
        // Sanity: ensure the Wolverine adapter skeleton is compiled and present.
        var mw = typeof(Kartova.SharedKernel.Wolverine.TenantScopeWolverineMiddleware);
        Assert.IsNotNull(mw.GetMethod("BeforeAsync", BindingFlags.Static | BindingFlags.Public));
    }

    [TestMethod]
    public void Every_tenant_owned_entity_has_RLS_policy_in_a_migration()
    {
        // Explicit allowlist of ITenantOwned aggregates this rule covers. Each entry must
        // have a migration in its Infrastructure assembly that contains ENABLE ROW LEVEL
        // SECURITY for its table. Add new tenant-owned aggregates here as they appear.
        var tenantOwnedTypes = new[] { typeof(Kartova.Organization.Domain.Organization) };

        foreach (var t in tenantOwnedTypes)
        {
            var tableName = t.Name.ToLowerInvariant() + "s"; // convention
            // Navigate from the test output assembly location to the source tree.
            // Test assembly is at: {repo}/tests/Kartova.ArchitectureTests/bin/Debug/net10.0/
            // Source infrastructure migrations are at: {repo}/src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/
            var testAssemblyLocation = Path.GetDirectoryName(OrganizationInfrastructure.Location)!;
            // Go up to repo root: ../../../../../..
            var repoRoot = Path.GetFullPath(Path.Combine(testAssemblyLocation, "..", "..", "..", "..", ".."));
            var migrationsDir = Path.Combine(repoRoot, "src", "Modules", "Organization", "Kartova.Organization.Infrastructure", "Migrations");

            var migrationSources = Directory.GetFiles(migrationsDir, "*InitialOrganization.cs", SearchOption.AllDirectories);
            Assert.IsTrue(
                migrationSources.Length > 0,
                $"expected a migration for {tableName}");

            var anyHasRls = migrationSources.Any(f =>
                File.ReadAllText(f).Contains("ENABLE ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(
                anyHasRls,
                $"migration for {tableName} must ENABLE ROW LEVEL SECURITY per ADR-0012/0090");
        }
    }

    [TestMethod]
    public void Module_DbContexts_register_via_AddModuleDbContext()
    {
        // Spec §6.1 / ADR-0090: every IModule's runtime DbContext registration must flow
        // through AddModuleDbContext so the scope's connection + interceptors are wired.
        // We verify this behaviorally: build a ServiceProvider WITHOUT INpgsqlTenantScope
        // and assert that resolving the module's DbContext fails because the AddModuleDbContext
        // factory requires it. Raw AddDbContext (which would silently bypass RLS) would not
        // require INpgsqlTenantScope and thus would succeed here — failing the test.

        var modules = new IModule[]
        {
            new Kartova.Catalog.Infrastructure.CatalogModule(),
            new Kartova.Organization.Infrastructure.OrganizationModule(),
        };

        // ConfigurationManager is empty — neither module reads configuration in its
        // tenant-scoped RegisterServices path; only RegisterForMigrator does (and that
        // path is not exercised here).
        var emptyConfig = new ConfigurationManager();

        foreach (var module in modules)
        {
            var services = new ServiceCollection();
            module.RegisterServices(services, emptyConfig);

            // Deliberately do NOT call AddTenantScope() — that's what makes the test
            // distinguish AddModuleDbContext (needs scope) from raw AddDbContext (doesn't).
            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var ex = Assert.ThrowsExactly<InvalidOperationException>(
                () => scope.ServiceProvider.GetRequiredService(module.DbContextType),
                $"module '{module.Name}' must register its DbContext via AddModuleDbContext (ADR-0090)");
            // FA's WithMessage("*INpgsqlTenantScope*") was a glob — translated to a substring check.
            StringAssert.Contains(ex.Message, "INpgsqlTenantScope");
        }
    }

    [TestMethod]
    public void AddModuleDbContext_helper_is_exposed()
    {
        var type = typeof(Kartova.SharedKernel.Postgres.AddModuleDbContextExtensions);
        Assert.IsNotNull(type.GetMethod("AddModuleDbContext", BindingFlags.Static | BindingFlags.Public));
        Assert.IsNotNull(type.GetMethod("AddTenantScope", BindingFlags.Static | BindingFlags.Public));
    }

    [TestMethod]
    public void TestJwtSigner_is_not_referenced_outside_test_projects()
    {
        // Kartova.Api must NOT reference Kartova.Testing.Auth.
        var apiRefs = typeof(Kartova.Api.Program).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();
        Assert.IsFalse(
            apiRefs.Contains("Kartova.Testing.Auth"),
            "Production API must not reference test-only JWT signer");
    }
}
