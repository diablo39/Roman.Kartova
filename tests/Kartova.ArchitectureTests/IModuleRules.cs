using System.Reflection;
using System.Text.RegularExpressions;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;

namespace Kartova.ArchitectureTests;

/// <summary>
/// Reflection-based pin for the <see cref="IModule"/> URL convention (ADR-0092):
/// every module declares a non-empty, lowercase kebab-case <c>Slug</c>, and every
/// module that participates in DI also exposes endpoints via <see cref="IModuleEndpoints"/>.
/// </summary>
[TestClass]
public class IModuleRules
{
    private static readonly Regex KebabCase = new("^[a-z][a-z0-9-]*$", RegexOptions.Compiled);

    [TestMethod]
    public void Every_IModule_implementation_declares_non_empty_Slug()
    {
        foreach (var t in AllModuleTypes())
        {
            var module = (IModule)Activator.CreateInstance(t)!;
            Assert.IsFalse(
                string.IsNullOrWhiteSpace(module.Slug),
                $"{t.FullName} must declare a non-empty Slug per ADR-0092");
        }
    }

    [TestMethod]
    public void Every_IModule_Slug_is_lowercase_kebab_case()
    {
        foreach (var t in AllModuleTypes())
        {
            var module = (IModule)Activator.CreateInstance(t)!;
            Assert.IsTrue(
                KebabCase.IsMatch(module.Slug),
                $"{t.FullName}.Slug='{module.Slug}' must match ^[a-z][a-z0-9-]*$ per ADR-0092");
        }
    }

    [TestMethod]
    public void Every_IModule_implementation_also_implements_IModuleEndpoints()
    {
        foreach (var t in AllModuleTypes())
        {
            Assert.IsTrue(
                typeof(IModuleEndpoints).IsAssignableFrom(t),
                $"{t.FullName} implements IModule and must also implement IModuleEndpoints " +
                "so the API composition root can map its routes (ADR-0092)");
        }
    }

    [TestMethod]
    public void Every_IModule_implementation_overrides_RegisterForMigrator()
    {
        // IModule.RegisterForMigrator's default implementation delegates to
        // RegisterServices, which registers module DbContexts via AddModuleDbContext
        // (ADR-0090) — that requires an active per-request ITenantScope. Both
        // Kartova.Migrator and ModuleMigrationsHealthCheck (gate-7 review, 2026-09-23)
        // resolve every module through RegisterForMigrator OUTSIDE any tenant scope,
        // so a module that relies on the default here breaks both at runtime instead
        // of failing this build-time check.
        foreach (var t in AllModuleTypes())
        {
            var declared = t.GetMethod(
                nameof(IModule.RegisterForMigrator),
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            Assert.IsNotNull(declared,
                $"{t.FullName} must override IModule.RegisterForMigrator with a plain " +
                "(non-tenant-scoped) DbContext registration — see this test's comment for why.");
        }
    }

    private static IEnumerable<Type> AllModuleTypes() =>
        AssemblyRegistry.AllProduction()
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(IModule).IsAssignableFrom(t));
}
