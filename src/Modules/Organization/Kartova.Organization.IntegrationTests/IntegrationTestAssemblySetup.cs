namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Assembly-scoped singletons for both fixture variants used by Organization
/// integration tests. One Postgres container + API host pair for the standard
/// suite, and a separate pair for fault-injection tests. Created exactly once
/// per assembly run regardless of how many test classes derive from each base.
///
/// Requires <c>[assembly: DoNotParallelize]</c> (see <c>Properties/AssemblyInfo.cs</c>):
/// integration tests serialise at the class level, so the single shared fixtures are
/// never accessed concurrently. Phase 9 lesson learned: do NOT use
/// <c>[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]</c> for these
/// fixtures — the heavyweight Postgres + API host pair would be rebuilt once per
/// derived test class, which caused a 6× wall-clock regression.
/// </summary>
[TestClass]
public sealed class IntegrationTestAssemblySetup
{
    public static KartovaApiFixture Fx { get; private set; } = null!;
    public static KartovaApiFaultInjectionFixture FaultFx { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitAsync(TestContext _)
    {
        Fx = new KartovaApiFixture();
        await Fx.InitializeAsync();
        FaultFx = new KartovaApiFaultInjectionFixture();
        await FaultFx.InitializeAsync();
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync()
    {
        if (Fx is not null) await ((IAsyncDisposable)Fx).DisposeAsync();
        if (FaultFx is not null) await ((IAsyncDisposable)FaultFx).DisposeAsync();
    }
}
