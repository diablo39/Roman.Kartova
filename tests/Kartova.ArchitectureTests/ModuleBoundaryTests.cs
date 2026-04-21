using System.Linq;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Kartova.ArchitectureTests;

public class ModuleBoundaryTests
{
    [Fact]
    public void Catalog_Does_Not_Reference_Other_Modules_Internals()
    {
        // In Slice 1 only Catalog exists; this test is vacuously true but scaffolds
        // the rule. Slice 2 adds Organization — extend the forbidden list then.
        var forbiddenNamespaces = new string[]
        {
            // placeholder — populated when other modules land
        };

        if (!forbiddenNamespaces.Any())
        {
            // Nothing to enforce yet; register the rule as passing.
            return;
        }

        var catalogAssemblies = new[]
        {
            AssemblyRegistry.Catalog.Domain,
            AssemblyRegistry.Catalog.Application,
            AssemblyRegistry.Catalog.Infrastructure,
            AssemblyRegistry.Catalog.Contracts,
        };

        foreach (var assembly in catalogAssemblies)
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOnAny(forbiddenNamespaces)
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"Catalog assembly {assembly.GetName().Name} must not reference other modules' internals (ADR-0082). " +
                $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [Fact]
    public void SharedKernel_Does_Not_Reference_Any_Module()
    {
        var result = Types.InAssembly(AssemblyRegistry.SharedKernel)
            .Should()
            .NotHaveDependencyOnAny(
                "Kartova.Catalog.Domain",
                "Kartova.Catalog.Application",
                "Kartova.Catalog.Infrastructure",
                "Kartova.Catalog.Contracts")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "SharedKernel must be stable and not depend on any module (ADR-0082). " +
            $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
