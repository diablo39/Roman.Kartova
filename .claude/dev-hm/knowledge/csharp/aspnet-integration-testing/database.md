# ASP.NET Core integration testing — Real database, not fakes {#database}

Section of `knowledge/csharp/aspnet-integration-testing.md`.


Pair the factory with a Testcontainers fixture and point the app's connection string at the
container (`knowledge/csharp/testing.md#testcontainers` for lifecycle and reuse rules; apply real
migrations, reset state between tests):

```csharp
[TestClass]
public sealed class IntegrationTestAssemblySetup
{
    public static ApiFactory Factory { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task InitAsync(TestContext _)
    {
        Factory = new ApiFactory();          // starts the container, then boots the host
        await Factory.InitializeAsync();
    }

    [AssemblyCleanup]
    public static async Task CleanupAsync() => await Factory.DisposeAsync();
}
```

Wire the container's connection string into the factory before the first client is created (start
the container in the fixture's async initializer, then set `DbConnectionString`). Do not
swap the provider for `Microsoft.EntityFrameworkCore.InMemory` — different engine, no constraints,
no transactions; see `knowledge/csharp/efcore.md#testing`.
