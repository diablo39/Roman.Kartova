# .NET resilience pipelines

Wiring transient-fault handling into .NET services with Polly's resilience-pipeline model and the
`Microsoft.Extensions.Resilience` / `Microsoft.Extensions.Http.Resilience` packages built on it
(versions in `knowledge/shared/versions.md`). What retries, timeouts, and breakers *should do* —
budgets, idempotency preconditions, failure-mode taxonomy — is the quality knowledge in
`knowledge/quality/reliability-resilience.md`; this file maps those rules onto the .NET APIs.

Legacy note: the v7-era `IAsyncPolicy`/`Policy.Handle` API and the deprecated
`Microsoft.Extensions.Http.Polly` package are superseded by `ResiliencePipeline` — migrate when
touching old wiring; do not start new work on them.

## Standard resilience handler for HttpClient {#standard-handler}

The default outbound-HTTP posture is one call on the typed client
(`knowledge/csharp/platform.md#http-clients`):

```csharp
builder.Services.AddHttpClient<InventoryClient>(c => c.BaseAddress = new Uri(baseUrl))
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.DisableForUnsafeHttpMethods();          // POST/PUT/PATCH/DELETE not retried
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8);
    });
```

The handler chains five strategies, outermost first: **rate limiter → total request timeout →
retry → circuit breaker → attempt timeout**. Tuning rules:

- Retries default to handling transient results (5xx, 408, 429, network failures) with jittered
  exponential backoff. Keep that classification; retrying 4xx client errors is a bug.
- `DisableForUnsafeHttpMethods()` is the safe default posture: a retried non-idempotent POST is a
  duplicate-payment generator. Re-enable per method only where the endpoint is provably idempotent
  (idempotency keys — `knowledge/quality/reliability-resilience/retries.md`).
- Total timeout must be ≥ worst-case (attempts × attempt timeout + backoff); the defaults are
  consistent — keep them consistent when tuning, and derive attempt timeouts from the dependency's
  latency budget, not hope.
- The circuit breaker is per handler (per named/typed client); give each downstream dependency its
  own typed client so one failing dependency does not open the breaker for another.
- One resilience layer per call path: if a proxy/mesh or the gRPC channel already retries, do not
  stack another retry here (`knowledge/csharp/grpc.md#retries`).

## Custom pipelines for non-HTTP work {#pipelines}

For database calls, queue publishes, or any operation needing composed strategies, register a named
pipeline and resolve it via `ResiliencePipelineProvider<string>`:

```csharp
builder.Services.AddResiliencePipeline("kv-store", (pipeline, ctx) =>
{
    pipeline
        .AddTimeout(TimeSpan.FromSeconds(10))                          // total
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<TransientKvException>(),
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
        })
        .AddCircuitBreaker(new CircuitBreakerStrategyOptions())
        .AddTimeout(TimeSpan.FromSeconds(2));                          // per attempt
});

// consumer
var pipeline = provider.GetPipeline("kv-store");
await pipeline.ExecuteAsync(async token => await kv.PutAsync(key, value, token), ct);
```

- `ShouldHandle` is an explicit allowlist of transient failures. Handling `Exception` wholesale
  retries bugs and poison messages — never do it.
- Order matters and mirrors the standard handler: total timeout outside retry, attempt timeout
  inside.
- Pipelines are thread-safe singletons; resolve once per component, not per call.
- Pass the caller's `CancellationToken` into `ExecuteAsync`; strategies link it with their own
  timeouts so cancellation still wins.
- EF Core's execution strategy already retries database transients
  (`knowledge/csharp/efcore/lifetime.md`) — do not wrap it in another retry layer.

## Hedging {#hedging}

Hedging races a second attempt while the first is still running — it attacks tail latency, where
retry attacks failure. `AddStandardHedgingHandler()` chains total timeout → hedging → per-endpoint
(rate limiter → circuit breaker → attempt timeout), optionally routing attempts across ordered or
weighted endpoint groups.

Preconditions are stricter than retry's: the call must be idempotent **and** the downstream must
have headroom — hedging multiplies load at exactly the moment the dependency is slow. Reserve it
for read-only, latency-critical calls with a measured long tail; cap attempts low; watch the
downstream's saturation metrics after enabling. It is not a default posture.

## Testing resilience behavior {#testing}

Resilience config is code — the failure-mode tests of
`knowledge/quality/reliability-resilience/failure-mode-testing.md` apply:

- Unit-test pipelines by injecting failures: a stub `HttpMessageHandler` (or fake delegate for
  custom pipelines) that returns 500-then-200 proves retry; N consecutive failures proves the
  breaker opens and `BrokenCircuitException` surfaces fast.
- Pipelines accept a `TimeProvider`; pass `FakeTimeProvider` so backoff and breaker windows advance
  instantly instead of sleeping the suite.
- Assert the non-retry cases too: a 400 must not retry; a POST with `DisableForUnsafeHttpMethods`
  must not retry; cancellation must abort remaining attempts.
- Chaos strategies (`Polly.Simmy`: fault, latency, outcome injection) can run behind a config flag
  in staging soak tests to verify timeout/breaker settings against injected latency — development
  and staging only, never ambient in production.

## Observability of resilience {#observability}

`Microsoft.Extensions.Resilience` pipelines emit metrics and events (attempt counts, breaker state
transitions) through the standard .NET metrics primitives — export them with the OTel wiring in
`knowledge/csharp/observability.md` and alert on breaker-open and retry-rate anomalies, which
usually fire before the dependency's own alarms. Log retries at warn (coping), exhausted retries
and opened breakers at error, per the level contract in
`knowledge/quality/observability/log-levels.md`.
