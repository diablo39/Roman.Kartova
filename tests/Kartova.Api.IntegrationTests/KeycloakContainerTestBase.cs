namespace Kartova.Api.IntegrationTests;

/// <summary>
/// Convenience base class — exposes the assembly-scoped <see cref="KeycloakContainerFixture"/>
/// (owned by <see cref="IntegrationTestAssemblySetup"/>) as a protected static so derived
/// test classes can write <c>Containers.X</c> instead of the fully qualified
/// <c>IntegrationTestAssemblySetup.Containers.X</c>. The fixture itself is created exactly
/// once per assembly run via <c>[AssemblyInitialize]</c>; this base class adds no lifecycle.
/// </summary>
[TestClass]
public abstract class KeycloakContainerTestBase
{
    protected static KeycloakContainerFixture Containers => IntegrationTestAssemblySetup.Containers;
}
