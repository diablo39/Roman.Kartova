# gRPC on .NET

Contract-first RPC with `Grpc.AspNetCore` (server) and the gRPC client factory (versions in
`knowledge/shared/versions.md`). gRPC fits service-to-service APIs with strong contracts,
streaming, or polyglot consumers; JSON minimal APIs fit public/browser-facing surfaces — the
selection criteria extend `knowledge/csharp/platform.md#api-surface-minimal-apis-vs-controllers`.
Committing a service boundary to gRPC (vs REST/JSON or messaging) shapes every consumer and is
hard to reverse — for a load-bearing boundary, run the choice through
`knowledge/shared/three-framing-analysis.md` and record the ADR. Browser or REST consumers of an
existing gRPC service: JSON transcoding or gRPC-Web, rather than a second hand-written API.

## Contract and project shape {#contract}

The `.proto` files are the contract and live under source control as the source of truth;
`Grpc.Tools` generates server bases and clients at build. Contract evolution follows protobuf
rules: never reuse or renumber a field, mark removed fields `reserved`, additive changes only —
the API-compatibility discipline of `knowledge/quality/test-adequacy.md` applies to the proto.

Server registration is minimal-API-shaped:

```csharp
builder.Services.AddGrpc();
app.MapGrpcService<OrdersService>();
```

Implementations override the generated base, stay thin, and delegate to injected services —
the same handler discipline as HTTP endpoints.

## Deadlines and cancellation {#deadlines}

gRPC calls have **no default deadline** — an unset deadline is an unbounded call holding server
resources. Treat a deadline on every call as the outbound-timeout rule of
`knowledge/quality/reliability-resilience/timeouts.md`:

```csharp
var reply = await client.GetOrderAsync(request,
    deadline: timeProvider.GetUtcNow().AddSeconds(2).UtcDateTime, cancellationToken: ct);
```

- Servers: pass `context.CancellationToken` into every async call the handler makes. When the
  client's deadline expires the HTTP/2 stream aborts, but the handler keeps running until it
  observes the token — an ignored token turns cancelled calls into background load.
- Nested calls (service A → B → C): register clients through the factory with
  `EnableCallContextPropagation()` so the caller's deadline and cancellation flow to child calls
  automatically; the smallest deadline in the chain wins.
- Client factory integrates with `IHttpClientFactory`:
  `builder.Services.AddGrpcClient<Orders.OrdersClient>(o => o.Address = ...)`, one channel reused
  per target (channels are expensive; calls are cheap).

## Errors, retries, streaming {#retries}

- Fail with `RpcException` and a semantically correct `StatusCode` (`NotFound`,
  `PermissionDenied`, `InvalidArgument`, `Unavailable`...); message and trailers must not leak
  internals — the error-handling rules of `knowledge/csharp/review-checklist/error-handling.md`
  apply unchanged. Unhandled exceptions surface as `Unknown`; map them in an interceptor instead.
- Transient-fault retries are channel config (`ServiceConfig`/`MethodConfig` retry policy on
  `Unavailable`), which understands gRPC status semantics. Do not stack a second retry layer from
  `knowledge/csharp/resilience.md` on the same call path — one owner per concern; non-idempotent
  methods get no retry policy at all.
- Streaming: server-streaming responses write to `IServerStreamWriter` inside the handler and
  should honor the token between writes; client code consumes `ResponseStream.ReadAllAsync(ct)` as
  an `IAsyncEnumerable` — pull-based backpressure and buffering rules per
  `knowledge/csharp/async-patterns.md#iasyncenumerable`. Bidirectional streams are long-lived
  stateful connections; prefer unary calls unless the shape genuinely needs a stream.
- Cross-cutting concerns (auth enrichment, error mapping, correlation) go in client/server
  interceptors, the pipeline's middleware equivalent.

## Security {#security}

gRPC rides HTTP/2 over TLS; production channels are `https` with real certificate validation —
the transport rules of `knowledge/security/transport-protection.md`, including mTLS where peer
identity is required. AuthN/AuthZ reuse the ASP.NET Core pipeline
(`knowledge/csharp/aspnet-security.md`): bearer tokens via `CallCredentials`/metadata on the
client, `[Authorize]` policies on service classes or methods on the server, deny-by-default
fallback policy covering mapped gRPC endpoints like any other.

## Observability and health {#observability}

Tracing and metrics come from the standard wiring (`knowledge/csharp/observability.md`): the
ASP.NET Core instrumentation covers inbound gRPC; add the gRPC client instrumentation package for
outbound calls, and W3C trace context propagates in metadata automatically.
`Grpc.AspNetCore.HealthChecks` maps the standard gRPC health protocol
(`grpc.health.v1.Health`) onto ASP.NET Core health-check results, which load balancers and
orchestrators consume natively — same liveness/readiness split as
`knowledge/csharp/observability.md#health`.

## Native AOT {#aot}

gRPC services are a supported ASP.NET Core Native AOT workload (see the AOT row in
`knowledge/shared/versions.md`): protobuf serialization is generated code, no reflection at the
contract boundary. `<PublishAot>true</PublishAot>` (or `dotnet new grpc --aot`), keep dependencies
AOT-clean per `knowledge/csharp/platform.md#native-aot-and-trimming`, and publish frequently so
trim warnings surface early.

## Testing {#testing}

- **Unit**: gRPC service classes are ordinary DI classes — instantiate with substituted
  dependencies and call the method. Handlers take a `ServerCallContext`; build one with
  `TestServerCallContext.Create(...)` from the `Grpc.Core.Testing` package (set the deadline and
  cancellation token the test needs). Generated clients expose virtual methods, so a consumer of
  `Orders.OrdersClient` can substitute it directly for client-side unit tests.
- **Integration**: host the real service with `WebApplicationFactory` and connect a channel over
  the in-memory handler — full pipeline (auth, interceptors, deadlines) without a socket; setup in
  `knowledge/csharp/aspnet-integration-testing/grpc.md`. Assert status codes with
  `Assert.ThrowsExactlyAsync<RpcException>` and check `StatusCode`, including the `PermissionDenied`/
  `Unauthenticated` negatives driven by real tokens (`aspnet-integration-testing.md#auth`).
- Deadline behavior is testable: a handler that ignores `context.CancellationToken` fails a test
  that sets a short deadline and asserts prompt `DeadlineExceeded`.
