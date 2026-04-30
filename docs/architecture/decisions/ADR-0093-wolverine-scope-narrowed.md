# ADR-0093: Wolverine Scope — Outbox/Async Only, Direct Dispatch for Sync HTTP

**Status:** Accepted
**Date:** 2026-04-30
**Deciders:** Roman Głogowski (solo developer)
**Category:** Application Architecture
**Related:** ADR-0028 (Clean Architecture + CQRS via Wolverine), ADR-0080 (Wolverine outbox + Kafka outbound), ADR-0081 (KafkaFlow inbound), ADR-0090 (tenant scope mechanism)
**Supersedes:** Narrows scope of ADR-0028 (Wolverine is no longer mandatory for synchronous HTTP request handlers).

## Context

ADR-0028 mandates Wolverine as the in-process CQRS mediator for all command and query dispatch. Slice 3 (Catalog: register Application) surfaced a concrete incompatibility between that mandate and ADR-0090's tenant scope mechanism:

- ADR-0090 requires one `NpgsqlConnection` + transaction per HTTP request, with `SET LOCAL app.current_tenant_id` issued by `TenantScopeBeginMiddleware` on the **ASP.NET request scope**.
- `Wolverine.IMessageBus.InvokeAsync<T>` opens its **own internal IoC scope** for handler dispatch. That scope is a sibling of the request scope, not a child.
- Resolving `CatalogDbContext` (or anything depending on `ITenantScope`) inside Wolverine's scope throws "TenantScope is not active" because the tenant scope was begun on the request scope, not Wolverine's.

Three options were considered:

1. **Adopt `WolverineFx.Http`** so endpoint-as-handler runs in the ASP.NET request scope and uses the bus.
2. **Direct dispatch** — endpoint delegates resolve handlers from the request `IServiceProvider` and call `Handle(...)` directly.
3. **Force tenant scope into Wolverine's scope** — bypass the middleware and rely on Wolverine middleware to begin the scope.

Option 3 inverts ADR-0090's transport-agnostic design (HTTP middleware owns request lifecycle, not the mediator). Option 1 is viable but adds a second routing/discovery surface that doesn't pay for itself at the current handler count (~1–15 sync HTTP handlers expected through slice 6, no async handlers yet). Option 2 is the smallest change that keeps ADR-0090 intact.

Slice 3 shipped with option 2 as a tactical workaround. This ADR formalizes that decision and narrows ADR-0028's scope so a second module copying the precedent doesn't constitute drift.

## Decision

Three decisions, deliberately separated so they can evolve independently:

### 1. Synchronous HTTP handlers use direct dispatch

Endpoint delegates resolve command/query handlers from the request `IServiceProvider` and invoke them directly. Handlers are plain classes with constructor injection (`CatalogDbContext`, `ITenantContext`, `ICurrentUser`, etc.) — no Wolverine attributes, no bus lookup.

```csharp
// In CatalogEndpointDelegates
public static async Task<IResult> RegisterApplicationAsync(
    RegisterApplicationRequest request,
    [FromServices] RegisterApplicationHandler handler,
    [FromServices] ITenantContext tenant,
    [FromServices] ICurrentUser user,
    CancellationToken ct)
{
    var response = await handler.Handle(
        new RegisterApplicationCommand(request.Name, request.Description),
        tenant, user, ct);
    return Results.Created($"/api/v1/catalog/applications/{response.Id}", response);
}
```

The handler shares the request scope with `TenantScopeBeginMiddleware`, so `ITenantScope` is active and `CatalogDbContext` resolves correctly.

### 2. Wolverine remains mandatory for async dispatch and Kafka outbound

Wolverine is retained — and remains the **only** approved approach — for:

- The transactional outbox (ADR-0080) when handlers publish domain events.
- Async in-process messaging (fire-and-forget commands, scheduled messages, retries with backoff).
- Kafka outbound publishing (ADR-0080) — events written to the outbox table in the same transaction as the data change, then published to Kafka by Wolverine's outbox worker.
- Sagas and durable workflows when those become necessary.

Inside a synchronous handler, publishing an event still goes through Wolverine:

```csharp
public sealed class RegisterApplicationHandler
{
    public async Task<ApplicationResponse> Handle(
        RegisterApplicationCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IMessageBus bus,           // injected for publishing only
        CancellationToken ct)
    {
        var app = Application.Create(cmd.Name, cmd.Description, user.UserId, tenant.TenantId);
        db.Applications.Add(app);
        await bus.PublishAsync(new ApplicationRegistered(app.Id));  // outbox catches it
        await db.SaveChangesAsync(ct);  // atomic: data + outbox row
        return new ApplicationResponse(...);
    }
}
```

Wolverine's outbox is wired into `Program.cs` config now, even before the first event is published, so handlers can publish via `IMessageBus` from day one without a future re-plumbing slice.

### 3. WolverineFx.Http evaluation is deferred

`WolverineFx.Http` is **not** adopted at this time. Trigger for re-evaluation: after slice 6 ships (Service entity + members + first async handlers), when there is real data on:

- Total handler count across HTTP and async (uniformity benefit scales with count).
- Friction from maintaining two patterns vs. cost of learning the framework.
- Whether Wolverine middleware (vs. ASP.NET endpoint filters) becomes the natural place for cross-cutting concerns.

If adopted later, slice 3's handlers can be migrated mechanically — handler signatures don't change.

## Consequences

**Positive:**

- ADR-0090's tenant scope works without contortions; transport adapters (HTTP middleware) own request lifecycle as designed.
- One less abstraction layer between endpoint and handler — easier to debug, easier to test, no scope mismatch surprises.
- Outbox capability is wired and ready before it's needed; first event-publishing slice doesn't pay setup cost.
- ADR-0028's "mandatory mediator" framing is replaced with precise scope: Wolverine is mandatory **where it's load-bearing** (outbox, async, Kafka), optional elsewhere.

**Negative:**

- Two handler-invocation patterns coexist (direct dispatch for sync HTTP, Wolverine for async). Mitigated by the simple rule: "if it's an HTTP endpoint, direct dispatch; if it's a message, Wolverine."
- Endpoint code knows about handler types directly (no mediator decoupling). Acceptable trade — endpoints already know which command they map to; the "second caller" the mediator pattern protects against doesn't exist for HTTP.
- Migrating to `WolverineFx.Http` later, if chosen, touches every endpoint delegate. Mechanical work; risk is low.

**Neutral:**

- KafkaFlow inbound (ADR-0081) is unaffected — it was never coupled to Wolverine's mediator role.

## Implementation notes

- Handlers are plain `sealed class` types in `*.Infrastructure` (where they can reference EF Core `DbContext` types directly without violating module boundaries).
- Handlers are registered as `Scoped` in module DI (`AddScoped<RegisterApplicationHandler>()`), explicitly via the module's `RegisterServices`.
- No `[WolverineHandler]` attributes on sync HTTP handlers. These are not Wolverine-managed.
- Wolverine config in `Program.cs` calls `module.ConfigureWolverine(opts)` on each `IModule` for any module-specific async wiring; the outbox configuration (Postgres + EF integration) is set up centrally.

## Follow-ups

- Update ADR-0028's status field to "Superseded by ADR-0093 in part — Wolverine remains mandatory for async/outbox; sync HTTP handlers use direct dispatch."
- Slice 3 handlers already follow this pattern; no retrofit needed.
- Slice 4+ planning references this ADR explicitly when introducing new handlers.
