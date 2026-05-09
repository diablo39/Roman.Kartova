namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Assembly-scoped singleton that hosts the shared <see cref="KartovaApiFixture"/>
/// for every Catalog API integration test class. Exactly one Postgres Testcontainer
/// + EF migration run + <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{Program}"/>
/// per assembly run, regardless of how many derived test classes exist.
///
/// Requires <c>[assembly: DoNotParallelize]</c> (see <c>Properties/AssemblyInfo.cs</c>):
/// integration tests serialise at the class level, so the single shared fixture is
/// never accessed concurrently.
/// </summary>
[TestClass]
public sealed class IntegrationTestAssemblySetup
{
    public static KartovaApiFixture Fx { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitAsync(TestContext _)
    {
        Fx = new KartovaApiFixture();
        await Fx.InitializeAsync();
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        if (Fx is not null) await ((IAsyncDisposable)Fx).DisposeAsync();
    }
}
