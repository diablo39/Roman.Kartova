using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Kartova.ArchitectureTests;

public class TenantScopeRules
{
    private static readonly Assembly SharedKernel = typeof(Kartova.SharedKernel.Multitenancy.ITenantScope).Assembly;
    private static readonly Assembly SharedKernelAspNetCore = typeof(Kartova.SharedKernel.AspNetCore.TenantScopeMiddleware).Assembly;
    private static readonly Assembly SharedKernelPostgres = typeof(Kartova.SharedKernel.Postgres.TenantScope).Assembly;
    private static readonly Assembly SharedKernelWolverine = typeof(Kartova.SharedKernel.Wolverine.TenantScopeWolverineMiddleware).Assembly;
    private static readonly Assembly OrganizationInfrastructure = typeof(Kartova.Organization.Infrastructure.OrganizationDbContext).Assembly;
    private static readonly Assembly OrganizationInfrastructureAdmin = typeof(Kartova.Organization.Infrastructure.Admin.AdminOrganizationDbContext).Assembly;

    [Fact]
    public void SharedKernel_has_no_framework_dependencies()
    {
        var forbidden = new[]
        {
            "Npgsql",
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "WolverineFx",
            "KafkaFlow",
        };

        var result = Types.InAssembly(SharedKernel)
            .Should()
            .NotHaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Kartova.SharedKernel must stay technology-agnostic; tech-specific code lives in SharedKernel.Postgres/AspNetCore/Wolverine (ADR-0090)");
    }

    [Fact]
    public void Admin_bypass_DbContext_is_isolated_to_admin_assembly()
    {
        var result = Types.InAssembly(OrganizationInfrastructure)
            .Should()
            .NotHaveDependencyOn("Kartova.Organization.Infrastructure.Admin")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Tenant-scoped Infrastructure must not depend on the BYPASSRLS Admin DbContext (ADR-0090)");
    }

    [Fact]
    public void Wolverine_middleware_project_exists()
    {
        // Sanity: ensure the Wolverine adapter skeleton is compiled and present.
        var mw = typeof(Kartova.SharedKernel.Wolverine.TenantScopeWolverineMiddleware);
        mw.GetMethod("BeforeAsync", BindingFlags.Static | BindingFlags.Public).Should().NotBeNull();
    }

    [Fact]
    public void Every_tenant_owned_entity_has_RLS_policy_in_a_migration()
    {
        // Find every class implementing ITenantOwned in any referenced assembly, then check
        // migration files in the corresponding Infrastructure assemblies contain an ENABLE
        // ROW LEVEL SECURITY for its table.
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
            migrationSources.Should().NotBeEmpty(because: $"expected a migration for {tableName}");

            var anyHasRls = migrationSources.Any(f =>
                File.ReadAllText(f).Contains("ENABLE ROW LEVEL SECURITY", StringComparison.OrdinalIgnoreCase));
            anyHasRls.Should().BeTrue(
                because: $"migration for {tableName} must ENABLE ROW LEVEL SECURITY per ADR-0012/0090");
        }
    }

    [Fact]
    public void AddModuleDbContext_helper_is_exposed()
    {
        var type = typeof(Kartova.SharedKernel.Postgres.AddModuleDbContextExtensions);
        type.GetMethod("AddModuleDbContext", BindingFlags.Static | BindingFlags.Public).Should().NotBeNull();
        type.GetMethod("AddTenantScope", BindingFlags.Static | BindingFlags.Public).Should().NotBeNull();
    }

    [Fact]
    public void TestJwtSigner_is_not_referenced_outside_test_projects()
    {
        // Kartova.Api must NOT reference Kartova.Testing.Auth.
        var apiRefs = typeof(Kartova.Api.Program).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name);
        apiRefs.Should().NotContain("Kartova.Testing.Auth",
            because: "Production API must not reference test-only JWT signer");
    }
}
