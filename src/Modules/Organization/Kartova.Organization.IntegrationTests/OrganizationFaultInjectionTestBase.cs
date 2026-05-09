namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Convenience base class — exposes the assembly-scoped <see cref="KartovaApiFaultInjectionFixture"/>
/// (owned by <see cref="IntegrationTestAssemblySetup"/>) as a protected static so derived
/// test classes can write <c>Fx.X</c>. The fixture itself is created exactly once per
/// assembly run via <c>[AssemblyInitialize]</c>; this base class adds no lifecycle.
/// </summary>
[TestClass]
public abstract class OrganizationFaultInjectionTestBase
{
    protected static KartovaApiFaultInjectionFixture Fx => IntegrationTestAssemblySetup.FaultFx;
}
