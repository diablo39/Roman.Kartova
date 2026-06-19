using System.Linq;
using NetArchTest.Rules;

namespace Kartova.ArchitectureTests;

[TestClass]
public class ModuleBoundaryTests
{
    [TestMethod]
    public void Catalog_Does_Not_Reference_Other_Modules_Internals()
    {
        // Slice-9 §11.1: Catalog must not depend on Organization internals. Cross-module
        // communication happens via Wolverine IMessageBus or the IUserDirectory /
        // IOrganizationTeamExistenceChecker ports (which live in SharedKernel.Multitenancy).
        // Referencing Kartova.Organization.Contracts IS allowed — Contracts is the public
        // wire surface and is intentionally shared across modules.
        var forbiddenNamespaces = new[]
        {
            "Kartova.Organization.Domain",
            "Kartova.Organization.Application",
            "Kartova.Organization.Infrastructure",
            // Catalog communicates with the Audit module exclusively via IAuditWriter
            // (SharedKernel port) — direct coupling to Audit.Infrastructure is forbidden (ADR-0082).
            "Kartova.Audit.Infrastructure",
        };

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

            Assert.IsTrue(
                result.IsSuccessful,
                $"Catalog assembly {assembly.GetName().Name} must not reference other modules' internals (ADR-0082). " +
                $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    /// <summary>
    /// Slice-9 §11.1 named sentinel: Catalog must not reference Organization.Domain.
    /// Owner enrichment is via <c>IUserDirectory</c> only (the cross-cutting port in
    /// <c>Kartova.SharedKernel.Multitenancy</c>), never by referencing User/Invitation
    /// aggregates directly. Kept as a distinct method so a regression failure points
    /// at the slice-9 contract by name.
    /// </summary>
    [TestMethod]
    public void Catalog_does_not_reference_Organization_Domain()
    {
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
                .NotHaveDependencyOn("Kartova.Organization.Domain")
                .GetResult();

            Assert.IsTrue(
                result.IsSuccessful,
                $"Catalog assembly {assembly.GetName().Name} must enrich owners via IUserDirectory, " +
                "never by referencing Kartova.Organization.Domain directly (slice-9 §11.1). " +
                $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [TestMethod]
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

        Assert.IsTrue(
            result.IsSuccessful,
            "SharedKernel must be stable and not depend on any module (ADR-0082). " +
            $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    /// <summary>
    /// Slice-9 §11.1: <c>Kartova.SharedKernel.Identity</c> is the cross-cutting people-port
    /// shared library. It must remain reusable from non-ASP.NET hosts (CLI tooling, workers,
    /// migration jobs), so any <c>Microsoft.AspNetCore.*</c> dependency is forbidden — that
    /// would force a transitive Kestrel/HTTP pipeline reference on every consumer.
    /// </summary>
    [TestMethod]
    public void Kartova_SharedKernel_Identity_does_not_reference_AspNetCore()
    {
        var result = Types.InAssembly(AssemblyRegistry.SharedKernelIdentity)
            .Should()
            .NotHaveDependencyOn("Microsoft.AspNetCore")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            "Kartova.SharedKernel.Identity must not depend on Microsoft.AspNetCore.* — it is a " +
            "host-agnostic library used by both API and non-HTTP processes (slice-9 §11.1). " +
            $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
