# Modern .NET platform patterns — HTTP clients

Section of `knowledge/csharp/platform.md`.


Never `new HttpClient()` per call — socket exhaustion. Use `IHttpClientFactory`:

```csharp
builder.Services.AddHttpClient<GitHubClient>(c => c.BaseAddress = new Uri("https://api.github.com"))
    .AddStandardResilienceHandler();   // Microsoft.Extensions.Http.Resilience: retry, circuit breaker, timeout
```

Typed clients get the configured `HttpClient` injected and stay unit-testable by mocking the typed
client's own interface, or by injecting a test `HttpMessageHandler`. Tuning the standard handler,
custom resilience pipelines, and hedging: `knowledge/csharp/resilience.md`.
