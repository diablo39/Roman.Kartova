# .NET observability wiring

How to wire OpenTelemetry into a .NET service. What good telemetry *is* — signal design, log
levels, cardinality budgets, SLI/SLO wiring, the 3 a.m. test — lives in
`knowledge/quality/observability.md`; this file is the C#/.NET implementation of it. Package
versions are in `knowledge/shared/versions.md`.

The .NET model: **libraries instrument with BCL primitives, the app configures collection and
export.** Traces come from `System.Diagnostics.ActivitySource`, metrics from
`System.Diagnostics.Metrics.Meter`, logs from `ILogger` — no OpenTelemetry package needed to
*produce* telemetry. The OTel SDK subscribes to named sources and exports them over OTLP, so the
backend stays swappable and library code carries no vendor dependency.

## Host wiring {#wiring}

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("orders-api"))       // service.name; version/env via attrs
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource("Orders.Api")                              // our ActivitySource(s)
        .AddOtlpExporter())
    .WithMetrics(m => m
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()                          // GC, thread pool, exceptions
        .AddMeter("Orders.Api")                               // our Meter(s)
        .AddOtlpExporter());

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeScopes = true;
    o.AddOtlpExporter();
});
```

- Configure the OTLP endpoint by environment (`OTEL_EXPORTER_OTLP_ENDPOINT`,
  `OTEL_SERVICE_NAME`, `OTEL_RESOURCE_ATTRIBUTES`), not in code — the same build ships to every
  environment.
- Instrumentation packages cover ASP.NET Core/Kestrel and `HttpClient`; add the client-library
  ones the service uses (SQL client, gRPC client). Npgsql emits its own `ActivitySource`/`Meter` —
  subscribe by name rather than adding a wrapper package.
- A service that forgets `AddSource`/`AddMeter` for its own names silently drops its custom
  telemetry — treat the names as configuration reviewed with the code.
- Aspire service defaults wrap this same wiring (plus health endpoints) in one call; fine as an
  on-ramp, same primitives underneath.

## Custom traces {#traces}

One `ActivitySource` per component, created once (static or DI singleton):

```csharp
private static readonly ActivitySource Source = new("Orders.Api");

using var activity = Source.StartActivity("order.submit");     // null if nothing listens — fine
activity?.SetTag("order.id", order.Id);
try { await Process(order, ct); }
catch (Exception ex)
{
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    activity?.AddException(ex);
    throw;
}
```

- `StartActivity` returns null when no listener is registered — always use `?.`; never gate
  business logic on the activity existing.
- Auto-instrumentation already spans inbound requests, outbound HTTP, and database calls; add
  manual spans only for meaningful internal stages (per
  `knowledge/quality/observability/traces.md`). Name tags by the OTel semantic conventions where
  one exists.
- Context propagates automatically across `await` (it rides `AsyncLocal` inside `Activity.Current`)
  and onto outbound HTTP as W3C `traceparent`. It does **not** cross a hand-rolled queue: when
  publishing to a message broker, inject the context into message headers and extract it in the
  consumer (`Propagators.DefaultTextMapPropagator`), so consumer spans join the producer's trace
  (`knowledge/quality/observability/propagation.md`).
- Head sampling is SDK config (`SetSampler`, `ParentBased(TraceIdRatioBased)`); tail-based
  keep-the-errors sampling belongs in the collector, not the app.

## Custom metrics {#metrics}

Create meters through DI so tests can observe them:

```csharp
public sealed class OrderMetrics
{
    private readonly Counter<long> _submitted;
    private readonly Histogram<double> _processMs;

    public OrderMetrics(IMeterFactory factory)
    {
        var meter = factory.Create("Orders.Api");
        _submitted = meter.CreateCounter<long>("orders.submitted", unit: "{order}");
        _processMs = meter.CreateHistogram<double>("orders.process.duration", unit: "ms");
    }

    public void Submitted(string paymentMethod) =>
        _submitted.Add(1, new KeyValuePair<string, object?>("payment.method", paymentMethod));
}
```

- Register the class as a singleton; instruments are cheap to record on and thread-safe.
- Durations are histograms, counts are counters, current states are observable gauges — and tag
  values must stay low-cardinality (no user IDs, no raw URLs); the budget rules are in
  `knowledge/quality/observability/metrics.md`.
- ASP.NET Core's built-in meters (`http.server.request.duration` and friends) already provide the
  RED set per route — wire dashboards to those before inventing parallel request metrics.

## Logs {#logs}

`ILogger` with message templates is already structured logging; the OTel logging provider exports
the state pairs and stamps every record with the current trace and span ID — the log↔trace
correlation comes free once tracing is on. Hot-path logging uses the `LoggerMessage` generator
(`knowledge/csharp/source-generators.md#logging`). Level discipline and the
error-means-a-person-acts contract: `knowledge/quality/observability/log-levels.md`. No secrets or
personal data in any signal — spans and metric tags included
(`knowledge/security/secrets-and-keys/secrets-out-of-logs-and-telemetry.md`).

## Health checks {#health}

```csharp
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddNpgSql(connectionString, tags: ["ready"]);          // dependency checks are readiness-only

app.MapHealthChecks("/health/live",  new() { Predicate = r => r.Tags.Contains("live") });
app.MapHealthChecks("/health/ready", new() { Predicate = r => r.Tags.Contains("ready") });
```

- Liveness answers "should the orchestrator restart me" — process-internal only; a dependency
  outage must not make every pod restart-loop. Readiness answers "route traffic to me" and is
  where dependency checks belong.
- Health endpoints are unauthenticated infrastructure surface: keep responses terse (status only,
  no dependency details) or bind them to the management port; detailed writers are for internal
  dashboards.
- Deep dependency checks run on a timer/cached result if the dependency is expensive — a health
  probe storm must not become the load that kills the dependency.

## Testing telemetry {#testing}

Telemetry the dashboards depend on gets tests, matching the verification bar in
`knowledge/quality/observability.md`:

- **Spans**: add the `OpenTelemetry.Exporter.InMemory` exporter in the test host
  (`.AddInMemoryExporter(exportedActivities)`) and assert names, tags, parent/child structure, and
  error status on the failure path. Pure unit tests can use an `ActivityListener` instead.
- **Metrics**: `AddInMemoryExporter` on the meter provider, or the
  `Microsoft.Extensions.Diagnostics.Testing` `MetricCollector<T>` against the instrument; assert
  the series and tags a new endpoint is expected to emit.
- **Logs**: `FakeLogger`/`FakeLoggerProvider` (`Microsoft.Extensions.Diagnostics.Testing`) to
  assert level, template, and state fields on failure paths.
- In `WebApplicationFactory` suites, register the in-memory exporters via `ConfigureTestServices`
  (`knowledge/csharp/aspnet-integration-testing/assertions.md`) and flush the provider before
  asserting.

## Review checklist hooks {#review}

Per-diff observability review runs through the quality oracle's QUA-040…045 walk
(`knowledge/quality/review-method/observability-review.md`). The .NET-specific translations: new
endpoint → covered by built-in HTTP metrics and appears in the SLI series; new outbound dependency
→ instrumentation package or `ActivitySource` subscribed; new background/queue path → context
propagated through headers; new failure path → error-level log with operation + correlation
fields, span status set to error.
