# ASP.NET Core integration testing — WebApplicationFactory basics {#waf}

Section of `knowledge/csharp/aspnet-integration-testing.md`.


`Microsoft.AspNetCore.Mvc.Testing` provides `WebApplicationFactory<Program>`, which hosts the app
on `TestServer` and hands out clients whose requests never leave the process.

```csharp
[TestClass]
public sealed class HealthEndpointTests
{
    [TestMethod]
    public async Task Health_returns_ok()
    {
        var client = IntegrationTestAssemblySetup.Factory.CreateClient();
        var response = await client.GetAsync("/health", CancellationToken.None);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- Minimal-API apps with top-level statements need the entry point visible to the test project: add
  `public partial class Program;` at the end of `Program.cs` (or `InternalsVisibleTo`).
- MSTest has no fixture injection: hold the factory in a static, assembly-scoped `[TestClass]` with
  `[AssemblyInitialize]`/`[AssemblyCleanup]` (see `knowledge/csharp/testing.md#testcontainers`) and
  pair it with `[assembly: DoNotParallelize]`. Each `CreateClient` call is cheap; booting the host
  is not — boot once per assembly.
- `factory.Services` exposes the app's container — resolve services inside a created scope to seed
  or assert state directly.
- `CreateClient` follows redirects and keeps cookies by default; pass
  `new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }` when the test asserts on
  the redirect itself. POSTs to antiforgery-protected endpoints must round-trip the antiforgery
  token and cookie first.
