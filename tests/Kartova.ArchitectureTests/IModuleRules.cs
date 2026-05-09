using System.Text.RegularExpressions;
using Kartova.SharedKernel;
using Kartova.SharedKernel.AspNetCore;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

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

    private static IEnumerable<Type> AllModuleTypes() =>
        AssemblyRegistry.AllProduction()
            .SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && typeof(IModule).IsAssignableFrom(t));
}
