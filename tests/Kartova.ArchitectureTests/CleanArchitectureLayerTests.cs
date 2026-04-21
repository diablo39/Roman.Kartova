using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Kartova.ArchitectureTests;

public class CleanArchitectureLayerTests
{
    [Fact]
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

        result.IsSuccessful.Should().BeTrue(
            "Domain layer must not reference Infrastructure or external frameworks (ADR-0028). " +
            $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Application_Does_Not_Reference_Infrastructure()
    {
        var result = Types.InAssembly(AssemblyRegistry.Catalog.Application)
            .Should()
            .NotHaveDependencyOn("Kartova.Catalog.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Application layer may depend on Domain and Contracts, never Infrastructure (ADR-0028). " +
            $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
