# Modern .NET platform patterns — Dependency injection and testability

Section of `knowledge/csharp/platform.md`.


The generic host (`Host.CreateApplicationBuilder` / `WebApplication.CreateBuilder`) owns the DI
container. Design for the container, not around it.

| Rule | Why | Anti-pattern |
|---|---|---|
| Constructor injection only | Dependencies are explicit and mockable | Service locator, `IServiceProvider` passed around |
| Depend on interfaces / abstractions | Swap real for fake in tests | `new HttpClient()` inside a service |
| Register with the narrowest lifetime that is correct | Avoids captive dependencies | Singleton capturing a scoped service |
| No static mutable state | Static state defeats isolation and parallel tests | `static` caches, `DateTime.Now` in logic |
| Inject `TimeProvider` for time | Deterministic, testable clocks | `DateTime.UtcNow` sprinkled in domain code |

Lifetimes: `Singleton` (stateless, thread-safe, app-lifetime), `Scoped` (per request/unit of work,
e.g. `DbContext`), `Transient` (cheap, stateless). A singleton must never depend on a scoped
service — resolve scoped work through `IServiceScopeFactory` instead. Enable scope validation in
development (`ValidateScopes`, `ValidateOnBuild`) to catch captive dependencies at startup.

Prefer keyed services (`AddKeyedSingleton`, `[FromKeyedServices]`) over marker subclasses when two
implementations of one interface coexist.
