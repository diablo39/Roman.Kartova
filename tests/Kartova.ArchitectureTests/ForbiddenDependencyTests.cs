using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Kartova.ArchitectureTests;

public class ForbiddenDependencyTests
{
    [Fact]
    public void No_Module_References_MediatR()
    {
        foreach (var assembly in AssemblyRegistry.AllProduction())
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("MediatR")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"MediatR is not used per ADR-0080; assembly {assembly.GetName().Name} should route through Wolverine IMessageBus. " +
                $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [Fact]
    public void No_Module_References_MassTransit()
    {
        foreach (var assembly in AssemblyRegistry.AllProduction())
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("MassTransit")
                .GetResult();

            result.IsSuccessful.Should().BeTrue(
                $"MassTransit is not used per ADR-0003/ADR-0080; Kafka is Wolverine (outbound) + KafkaFlow (inbound). " +
                $"Violating types in {assembly.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }
}
