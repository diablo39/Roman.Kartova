# Modern .NET platform patterns

Production C# on the current .NET LTS. Target framework, C# language version, and package
versions are pinned in `knowledge/shared/versions.md` — this file describes the patterns, not the
numbers. Enable nullable reference types and treat warnings as errors on every project.

## Project baseline

Set once in `Directory.Build.props` so every project inherits it:

```xml
<PropertyGroup>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

Central Package Management (`Directory.Packages.props` with `ManagePackageVersionsCentrally`) keeps
one version per package across a solution. Pin the SDK in `global.json` for reproducible builds.

## Dependency injection and testability

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

## Options pattern (`IOptions` family)

Bind configuration sections to typed classes; validate at startup so misconfiguration fails the
deployment, not the first request.

```csharp
builder.Services.AddOptions<SmtpOptions>()
    .Bind(builder.Configuration.GetSection(SmtpOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

| Accessor | Lifetime / semantics | Use for |
|---|---|---|
| `IOptions<T>` | Singleton, computed once | Static config that never changes at runtime |
| `IOptionsSnapshot<T>` | Scoped, recomputed per request | Config that may change between requests |
| `IOptionsMonitor<T>` | Singleton with change notifications | Singletons that must react to reloads (`OnChange`) |

The options-validation source generator can replace the reflective `ValidateDataAnnotations()`
call, but it is opt-in: declare an `[OptionsValidator]` partial class implementing
`IValidateOptions<T>` and register it in DI — only then may `ValidateDataAnnotations()` be
removed (removing it without that registration silently disables validation). The config-binding
generator makes `Bind`/`Get` reflection-free. See
`knowledge/csharp/source-generators.md#configuration`.

## HTTP clients

Never `new HttpClient()` per call — socket exhaustion. Use `IHttpClientFactory`:

```csharp
builder.Services.AddHttpClient<GitHubClient>(c => c.BaseAddress = new Uri("https://api.github.com"))
    .AddStandardResilienceHandler();   // Microsoft.Extensions.Http.Resilience: retry, circuit breaker, timeout
```

Typed clients get the configured `HttpClient` injected and stay unit-testable by mocking the typed
client's own interface, or by injecting a test `HttpMessageHandler`. Tuning the standard handler,
custom resilience pipelines, and hedging: `knowledge/csharp/resilience.md`.

## API surface: minimal APIs vs controllers

Both are first-class and can coexist in one app. Choose by shape, not fashion.

| Choose | When |
|---|---|
| Minimal APIs | Focused services, microservices, gateways, serverless, AOT targets; few endpoints; lowest startup and request overhead; native OpenAPI |
| Controllers (MVC) | Large APIs, many endpoints, several contributors, convention-based filters/model binding, heavy content negotiation |

Minimal API guidance: group with `MapGroup`, share cross-cutting concerns via endpoint filters,
return `TypedResults` (typed, testable, OpenAPI-accurate), inject services as parameters. Request
validation uses a source-generator-based validator (reflection-free, AOT-friendly) — annotate DTOs
with data-annotation attributes and enable minimal-API validation. Document with the built-in
OpenAPI package (`Microsoft.AspNetCore.OpenApi`); it emits OpenAPI 3.1 including YAML.

Keep endpoint delegates thin: parse/validate, delegate to an injected service, map the result.
Business logic lives in services, not in the endpoint or controller.

## Async and cancellation

- Async all the way down; do not block on async with `.Result`, `.Wait()`, or
  `GetAwaiter().GetResult()` outside a documented top-level sync boundary.
- Public async methods accept a `CancellationToken` and forward it to every downstream async call
  that accepts one.
- Use `async void` only for event handlers whose signature the framework requires; everything else
  returns `Task`/`Task<T>`/`ValueTask`.
- Library code that does not touch a synchronization context can use `ConfigureAwait(false)`; apply
  one policy consistently across an assembly rather than mixing. ASP.NET Core has no
  synchronization context, so app code there does not need it.
- Return `IAsyncEnumerable<T>` with `[EnumeratorCancellation]` for streaming; do not buffer large
  sequences into a `List` first.

Composition depth — `ValueTask` consumption rules, `WhenAll` error aggregation, linked
cancellation, channels, streaming backpressure, background work — is in
`knowledge/csharp/async-patterns.md`.

## Modern language features that change design

| Feature | Use it for |
|---|---|
| `record` / `record struct` | Immutable DTOs and value objects; structural equality; `with` expressions |
| `required` members + primary constructors | Enforce initialization without boilerplate constructors |
| Pattern matching / `switch` expressions | Exhaustive branching over shapes and enums; replaces type-check ladders |
| Collection expressions `[...]` and spreads | Concise, allocation-aware collection init |
| `field` keyword (field-backed properties) | Custom accessor logic without a hand-declared backing field |
| Extension members | Add methods/properties to types you do not own, including static and interface members |
| Nullable reference types | Encode nullability in the type system; annotate APIs precisely |

Nullability discipline: annotate public APIs precisely, avoid the null-forgiving `!` operator
except where an invariant is provably established, and do not silence the compiler with
`#nullable disable`.

## EF Core and data access

- One `DbContext` per unit of work, registered `Scoped`; do not share across threads.
- Parameterize every query. LINQ parameterizes automatically; for raw SQL use
  `FromSql`/`FromSqlInterpolated` (interpolated arguments become parameters), never string
  concatenation. See `knowledge/csharp/review-checklist/data-access.md`.
- Project to DTOs with `Select` to avoid over-fetching; use `AsNoTracking` for read-only queries.
- Avoid N+1: load related data with `Include`/split queries or explicit projection.
- Wrap multi-statement writes in a transaction or the built-in execution-strategy retry.

Depth — context lifetimes and pooling, split/compiled queries, `ExecuteUpdate`/`ExecuteDelete`,
concurrency tokens, interceptors, N+1 diagnostics, and rolling-deploy migrations — is in
`knowledge/csharp/efcore.md`.

## Structured logging

Use `ILogger<T>` with message templates and named placeholders, or `LoggerMessage`-generated
methods for hot paths (see `knowledge/csharp/source-generators.md#logging`). Do not build log text
with string interpolation — it defeats structured logging and allocates even when the level is
disabled.

## Native AOT and trimming

Native AOT is production-viable for ASP.NET Core minimal-API services and console apps on the
current LTS. Design AOT-ready from the start when the target is AOT: prefer source generators over
reflection everywhere (JSON, config, logging, regex, DI), annotate any unavoidable reflection with
`[RequiresDynamicCode]`/`[RequiresUnreferencedCode]`, and enable `<IsAotCompatible>true</IsAotCompatible>`
to surface trim/AOT analyzer warnings in the build. Not every library is AOT-safe — verify
dependencies before committing to it.
