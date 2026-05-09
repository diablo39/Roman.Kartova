namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public abstract class CatalogIntegrationTestBase
{
    protected static KartovaApiFixture Fx { get; private set; } = null!;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassInit(TestContext _)
    {
        Fx = new KartovaApiFixture();
        await Fx.InitializeAsync();
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassDone()
    {
        if (Fx is not null) await ((IAsyncDisposable)Fx).DisposeAsync();
    }
}
