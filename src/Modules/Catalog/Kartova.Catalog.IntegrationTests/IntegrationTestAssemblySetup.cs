namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Assembly-scoped singleton that hosts the shared <see cref="KartovaApiFixture"/>
/// for every Catalog API integration test class. Translation of xUnit's
/// <c>ICollectionFixture&lt;KartovaApiFixture&gt;</c> per spec §4.3 (line 190): the granularity
/// widens from collection to assembly, which is acceptable here because the previous
/// xUnit collection already spanned the entire project.
///
/// Test classes derive from <see cref="CatalogIntegrationTestBase"/> and access the
/// fixture via the inherited <c>Fx</c> static — exactly one Postgres Testcontainer +
/// EF migration run + <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{Program}"/>
/// per assembly run, regardless of how many derived test classes exist.
///
/// Safe under <c>[assembly: DoNotParallelize]</c>: integration tests already serialise
/// at the test level, so the single shared fixture is never accessed concurrently.
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
