# ASP.NET Core integration testing — Customizing the host {#customize}

Section of `knowledge/csharp/aspnet-integration-testing.md`.


Derive a factory once per test suite and override `ConfigureWebHost`; use `WithWebHostBuilder` for
one-off per-test tweaks.

```csharp
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public string DbConnectionString { get; set; } = "";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:app-db", DbConnectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();          // Microsoft.Extensions.DependencyInjection.Extensions
            services.AddSingleton<IEmailSender, RecordingEmailSender>();
        });
    }
}
```

- `ConfigureTestServices` runs after the app's own registrations, so replacements win. Replace
  process-external edges (mail, payment, third-party HTTP) with recording fakes; keep everything
  in-process real — replacing your own services turns the integration test back into a unit test.
- Configuration overrides via `UseSetting`/`ConfigureAppConfiguration` beat editing
  `appsettings.Testing.json` copies; the test states its own config next to the code that needs it.
- Capture app logs into the test output by adding a logger provider in `ConfigureLogging` that
  writes through MSTest's `TestContext.WriteLine` — silent 500s become readable failures.
- Outbound `HttpClient` edges: prefer substituting the typed client's abstraction; for handler-level
  behavior (retries, headers) inject a stub `HttpMessageHandler` via
  `ConfigurePrimaryHttpMessageHandler`.
