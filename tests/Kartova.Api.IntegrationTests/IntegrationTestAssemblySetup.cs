namespace Kartova.Api.IntegrationTests;

/// <summary>
/// Assembly-scoped singleton that hosts the shared <see cref="KeycloakContainerFixture"/>
/// (Postgres + Keycloak Testcontainers) for every API integration test class. Exactly one
/// Postgres + Keycloak container pair per assembly run, regardless of how many derived
/// test classes exist.
///
/// Requires <c>[assembly: DoNotParallelize]</c> (see <c>Properties/AssemblyInfo.cs</c>):
/// integration tests mutate process-global env vars (ConnectionStrings__*,
/// Authentication__*, Cors__*) when the WebApplicationFactory boots, so classes
/// must serialise. Do NOT use <c>[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]</c>
/// for this fixture — that rebuilds the heavyweight Postgres + Keycloak pair once per
/// derived class.
/// </summary>
[TestClass]
public sealed class IntegrationTestAssemblySetup
{
    public static KeycloakContainerFixture Containers { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitAsync(TestContext _)
    {
        Containers = new KeycloakContainerFixture();
        await Containers.InitializeAsync();
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        // MSTest only invokes [AssemblyCleanup] if [AssemblyInitialize] completed —
        // Containers is guaranteed non-null here.
        await Containers.DisposeAsync();
    }
}
