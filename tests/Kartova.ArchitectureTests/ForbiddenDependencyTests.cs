using NetArchTest.Rules;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Kartova.ArchitectureTests;

[TestClass]
public class ForbiddenDependencyTests
{
    [TestMethod]
    public void No_Module_References_MediatR()
    {
        foreach (var assembly in AssemblyRegistry.AllProduction())
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("MediatR")
                .GetResult();

            Assert.IsTrue(
                result.IsSuccessful,
                $"MediatR is not used per ADR-0080; assembly {assembly.GetName().Name} should route through Wolverine IMessageBus. " +
                $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }

    [TestMethod]
    public void No_Module_References_MassTransit()
    {
        foreach (var assembly in AssemblyRegistry.AllProduction())
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("MassTransit")
                .GetResult();

            Assert.IsTrue(
                result.IsSuccessful,
                $"MassTransit is not used per ADR-0003/ADR-0080; Kafka is Wolverine (outbound) + KafkaFlow (inbound). " +
                $"Violating types in {assembly.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }
}
