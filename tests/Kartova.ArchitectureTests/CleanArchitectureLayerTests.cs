using NetArchTest.Rules;

namespace Kartova.ArchitectureTests;

[TestClass]
public class CleanArchitectureLayerTests
{
    [TestMethod]
    public void Domain_Does_Not_Reference_Infrastructure_Or_External_Libraries()
    {
        var result = Types.InAssembly(AssemblyRegistry.Catalog.Domain)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Microsoft.AspNetCore",
                "Wolverine",
                "Kartova.Catalog.Infrastructure")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            "Domain layer must not reference Infrastructure or external frameworks (ADR-0028). " +
            $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [TestMethod]
    public void Application_Does_Not_Reference_Infrastructure()
    {
        var result = Types.InAssembly(AssemblyRegistry.Catalog.Application)
            .Should()
            .NotHaveDependencyOn("Kartova.Catalog.Infrastructure")
            .GetResult();

        Assert.IsTrue(
            result.IsSuccessful,
            "Application layer may depend on Domain and Contracts, never Infrastructure (ADR-0028). " +
            $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
