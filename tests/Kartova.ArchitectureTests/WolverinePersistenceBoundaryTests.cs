using NetArchTest.Rules;

namespace Kartova.ArchitectureTests;

[TestClass]
public class WolverinePersistenceBoundaryTests
{
    [TestMethod]
    public void No_Production_Assembly_Depends_On_WolverinePostgresql()
    {
        foreach (var assembly in AssemblyRegistry.AllProduction())
        {
            var result = Types.InAssembly(assembly)
                .Should()
                .NotHaveDependencyOn("Wolverine.Postgresql")
                .GetResult();

            Assert.IsTrue(
                result.IsSuccessful,
                $"Wolverine PostgreSQL persistence is deferred per " +
                $"docs/superpowers/specs/2026-04-24-defer-wolverine-persistence-design.md. " +
                $"Assembly {assembly.GetName().Name} must not reference Wolverine.Postgresql. " +
                $"When an outbox-using slice lands, introduce the dependency in Kartova.Migrator " +
                $"(not listed in AllProduction()) and add API-side auto-create suppression. " +
                $"Violating types: {string.Join(", ", result.FailingTypeNames ?? [])}");
        }
    }
}
