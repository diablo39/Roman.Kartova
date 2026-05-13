namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Assembly-scoped singletons for both fixture variants used by Organization
/// integration tests. One Postgres container + API host pair for the standard
/// suite, and a separate pair for fault-injection tests. Created exactly once
/// per assembly run regardless of how many test classes derive from each base.
///
/// Requires <c>[assembly: DoNotParallelize]</c> (see <c>Properties/AssemblyInfo.cs</c>).
/// Do NOT use <c>[ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]</c>
/// for these fixtures — that rebuilds the heavyweight Postgres + API host pair once
/// per derived class (~6× wall-clock cost).
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
        // MSTest only invokes [AssemblyCleanup] if [AssemblyInitialize] completed —
        // both properties are guaranteed non-null here.
        await ((IAsyncDisposable)Fx).DisposeAsync();
        await ((IAsyncDisposable)FaultFx).DisposeAsync();
    }
}
