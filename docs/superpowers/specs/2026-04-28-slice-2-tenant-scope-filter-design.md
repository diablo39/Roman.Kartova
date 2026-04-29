# Slice 2 Followup — Tenant-Scope Endpoint Filter + EF Enlistment + Layering Cleanup

**Date:** 2026-04-28
**Status:** Approved
**Phase:** 0 (Foundation), Slice 2 followup. Lands before Slice 3 (first Catalog CRUD) so the first tenant-scoped write path executes against a working mechanism.
**Scope:** Replace `TenantScopeMiddleware` with `TenantScopeEndpointFilter` per ADR-0090 §Decision; conditionally add `EnlistInTenantScopeInterceptor` if EF Core 10 does not auto-enlist; cut the `SharedKernel.AspNetCore → SharedKernel.Postgres` project reference via a transport-agnostic `TenantScopeBeginException`; expose `INpgsqlTenantScope.Transaction` publicly; refactor the three existing §6.3 defense-in-depth tests to use the EF write path; add one streaming-response durability regression test; amend ADR-0090 with a dated addendum.

## Problem

PR #1 (slice 2 — auth + multitenancy) shipped a tenant-scope mechanism that diverged from spec §3.1 / ADR-0090 §Decision in three ways:

1. **Middleware instead of endpoint filter.** The spec called for `.AddEndpointFilter<TenantScopeEndpointFilter>()` so commit runs *between* handler return and `IResult.ExecuteAsync`. The shipped `TenantScopeMiddleware` runs commit *after* `await _next(context)` — meaning the response body has already begun executing for streaming results, breaking the "commit failures observable to callers" durability promise.
2. **`EnlistInTenantScopeInterceptor` missing.** Spec §3.1 named this component; it never shipped. EF Core's transaction tracking is not deliberately enlisted in the scope's transaction, so DbContext writes inside a tenant-scoped request *might* share the scope's transaction (EF Core's behavior with a connection that already has a tx is implementation-defined). No tests exercise the EF write path today — the three §6.3 defense-in-depth tests added in PR #4 use raw SQL via reflection on the internal `Transaction` property to bypass the gap.
3. **Layering smell.** `SharedKernel.AspNetCore` references `SharedKernel.Postgres` because the middleware catches `NpgsqlException` to map `BeginAsync` failures to 503. Spec §3.2 specifies these as sibling adapters with no cross-reference; today's reference forces all transport adapters (future Wolverine, Kafka) to inherit a Postgres dependency.

Slice 3 (first Catalog CRUD) will introduce the first tenant-scoped write through `OrganizationDbContext`/`CatalogDbContext`. If the EF enlistment gap is not closed before Slice 3 lands, the slice-3 PR will couple feature work with mechanism repair under feature pressure. This followup PR closes the gaps in isolation, with a coherent diff a reviewer can hold in their head.

## Decisions

| Topic | Decision |
|-------|----------|
| Filter vs middleware | **Hybrid: middleware for `Begin`, endpoint filter for `Commit`.** Originally drafted as filter-only per ADR-0090 §Decision, but ASP.NET Core 10 minimal-API parameter binding resolves DI-injected `DbContext` instances *before* the endpoint filter chain runs — so a filter that calls `BeginAsync` runs too late and `OrganizationDbContext` resolution throws because `scope.Connection` is not active. Resolution: `TenantScopeBeginMiddleware` opens the scope before parameter binding (using a `RequireTenantScopeMarker` metadata check) and `TenantScopeCommitEndpointFilter` commits between handler return and `IResult.ExecuteAsync`. Both layers are tiny; durability and atomicity guarantees from ADR-0090 are preserved. The constraint is documented in the ADR-0090 addendum. |
| `EnlistInTenantScopeInterceptor` necessity | **Probe-test-first.** A probe test (`EF_DbContext_enlists_in_scope_transaction_automatically`) decides. If EF Core 10 + Npgsql auto-enlists when a connection has an active transaction at `UseNpgsql(connection)` time, the interceptor is not shipped — the spec §3.1 component is then redundant and skipping it avoids dead code. If the probe fails, the interceptor ships with its own tests. The plan documents the implementation pattern only conditionally. |
| `TenantScopeBeginException` placement | **`Kartova.SharedKernel.Multitenancy`** namespace (the abstract layer). Wrapping in `TenantScope.BeginAsync` lives in `SharedKernel.Postgres`. The filter (in `SharedKernel.AspNetCore`) catches the agnostic exception. |
| `SharedKernel.AspNetCore → SharedKernel.Postgres` reference | **Removed.** The transport-agnostic exception means `AspNetCore` no longer needs to know `Npgsql` types. Verified by a new architecture rule. |
| `INpgsqlTenantScope.Transaction` exposure | **Public on the interface** (return type `NpgsqlTransaction`). The interface is already Postgres-specific (it exposes `NpgsqlConnection`); exposing the transaction is the same level of abstraction. Replaces the reflection-based test access added in PR #4. |
| `RequireTenantScope()` semantics | **Implicit `RequireAuthorization()` + marker + commit-filter.** Tenant-scoped routes are by definition authenticated (they need a JWT to extract `tenant_id`). `RouteGroupBuilder.RequireTenantScope()` chains: (1) `.RequireAuthorization()`, (2) `.WithMetadata(RequireTenantScopeMarker.Instance)` so the begin-middleware finds tenant-scoped endpoints, (3) `.AddEndpointFilter<TenantScopeCommitEndpointFilter>()`. XML doc on the method describes all three behaviors. |
| ADR-0090 update | **Dated addendum**, in a new "Addenda" section. The decision section is unchanged; the addendum records (1) that slice-2 originally shipped a single middleware that violated commit-before-flush, (2) the durability defect was identified post-merge, and (3) the corrected implementation is a hybrid (begin-middleware + commit-filter) because ASP.NET Core 10 parameter binding resolves DI-injected DbContexts before the endpoint filter chain runs, making a pure-filter design infeasible without sacrificing connection sharing or DbContext-by-injection ergonomics. |
| `RequireTenantScopeMarker` metadata type | **Retained**, with redefined purpose. Originally tagged endpoints for the slice-2 middleware to dispatch on. After this PR, it tags endpoints for `TenantScopeBeginMiddleware` to know which routes need an early `BeginAsync` (before parameter binding). The commit filter is attached directly via `AddEndpointFilter<>`, so the marker is only consumed by the begin-middleware. |
| `TenantScopeMiddleware` | **Deleted** and replaced by `TenantScopeBeginMiddleware` (single responsibility: open scope on marker; rollback on exit). The old combined Begin+Commit middleware is the implementation that violated commit-before-flush; splitting Commit into the filter restores the durability promise. |
| Handle handoff between layers | `HttpContext.Items[TenantScopeBeginMiddleware.HandleKey]` carries the `IAsyncTenantScopeHandle` from middleware to filter. `HandleKey` is a single `internal const string` constant on the middleware. The filter throws `InvalidOperationException` if the key is missing — surfaces a wiring bug (filter attached without the middleware in the pipeline) immediately rather than silently committing nothing. |
| Streaming-response durability test | **Option B** — `IStartupFilter` adds a tenant-scoped streaming endpoint (test-only) that uses `Results.Stream(...)` over a 2 KB chunked source. A `FailingCommitTenantScopeDecorator` (test-only) flips commit-failure on demand. The test asserts the client sees a clean 500 + `application/problem+json`, no streamed body. If TestServer cannot reliably distinguish "headers committed before exception" from "partial body then exception", the test downgrades to asserting the filter's call order (`CommitAsync` before `IResult.ExecuteAsync`) at the component level and the gap is documented. |
| Existing §6.3 tests | **Refactored** to use the EF write path (`db.Add(...) + db.SaveChangesAsync()`) instead of raw SQL via reflection on the internal transaction. The reflection-based `TenantId` setter on the aggregate stays — production aggregates correctly don't allow arbitrary tenant assignment. |
| New arch rule | **`AspNetCore_does_not_reference_Postgres`** — codifies the project-reference cut so it stays cut. |

## Architecture

### 3.1 Project graph (after change)

```
Kartova.SharedKernel               (zero framework deps)
  + Multitenancy/TenantScopeBeginException.cs
  ↑
Kartova.SharedKernel.Postgres      → Npgsql + EFCore.Npgsql
  - TenantScope.BeginAsync wraps NpgsqlException → TenantScopeBeginException
  - INpgsqlTenantScope exposes Transaction publicly
  - (conditional) EnlistInTenantScopeInterceptor
Kartova.SharedKernel.AspNetCore    → ASP.NET Core only
  - TenantScopeBeginMiddleware (replaces TenantScopeMiddleware; Begin only)
  - TenantScopeCommitEndpointFilter (new; commit between handler return and IResult.ExecuteAsync)
  - RequireTenantScopeMarker (retained, redefined: tags routes the begin-middleware should open a scope on)
  - RequireTenantScope() chains RequireAuthorization + WithMetadata(marker) + AddEndpointFilter
  - NO project reference on SharedKernel.Postgres
Kartova.SharedKernel.Wolverine     → Wolverine (unchanged)
  ↑
Kartova.Api                        composes all three
```

### 3.2 Files added / changed / removed

| File | Action |
|------|--------|
| `src/Kartova.SharedKernel/Multitenancy/TenantScopeBeginException.cs` | **Add.** Plain wrapper exception, no extra state. |
| `src/Kartova.SharedKernel.Postgres/TenantScope.cs` | **Modify.** `BeginAsync` wraps `NpgsqlException` in `TenantScopeBeginException`. `Transaction` property changes from `internal` to public via interface. |
| `src/Kartova.SharedKernel.Postgres/INpgsqlTenantScope.cs` | **Modify.** Add `NpgsqlTransaction Transaction { get; }`. |
| `src/Kartova.SharedKernel.Postgres/EnlistInTenantScopeInterceptor.cs` | **Conditional add.** Implementation pattern decided in plan stage based on probe outcome. Most likely: `IDbCommandInterceptor.CommandCreatedAsync` calls `dbContext.Database.UseTransactionAsync(scope.Transaction)` lazily on first command if not already enlisted. |
| `src/Kartova.SharedKernel.Postgres/AddModuleDbContextExtensions.cs` | **Modify.** Wire enlistment interceptor if shipped. |
| `src/Kartova.SharedKernel.AspNetCore/TenantScopeBeginMiddleware.cs` | **Add.** Replaces the deleted `TenantScopeMiddleware`. Reads `RequireTenantScopeMarker` metadata, calls `BeginAsync` before parameter binding, stores handle in `HttpContext.Items`, owns `DisposeAsync` lifetime in a `try/finally`. Rolls back on any non-committed exit. |
| `src/Kartova.SharedKernel.AspNetCore/TenantScopeCommitEndpointFilter.cs` | **Add.** Endpoint filter attached via `AddEndpointFilter<>`. Calls `next(ctx)` to get the IResult, retrieves the handle from `HttpContext.Items`, calls `handle.CommitAsync` between handler return and `IResult.ExecuteAsync`. |
| `src/Kartova.SharedKernel.AspNetCore/RequireTenantScopeMarker.cs` | **Add.** Sealed type with a single static `Instance`. Pure metadata — no behavior. Consumed only by `TenantScopeBeginMiddleware`. |
| `src/Kartova.SharedKernel.AspNetCore/TenantScopeMiddleware.cs` | **Delete.** Replaced by `TenantScopeBeginMiddleware` + `TenantScopeCommitEndpointFilter`. |
| `src/Kartova.SharedKernel.AspNetCore/TenantScopeRouteExtensions.cs` | **Modify.** `RequireTenantScope()` rewritten to chain `RequireAuthorization()` + `WithMetadata(RequireTenantScopeMarker.Instance)` + `AddEndpointFilter<TenantScopeCommitEndpointFilter>()`. |
| `src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj` | **Modify.** Remove `<ProjectReference>` to `SharedKernel.Postgres`. |
| `src/Kartova.Api/Program.cs` | **Modify.** Replace `app.UseMiddleware<TenantScopeMiddleware>()` with `app.UseMiddleware<TenantScopeBeginMiddleware>()`. The route-group `RequireTenantScope()` call also attaches the commit filter automatically. Pipeline order: `UseAuthentication` → `UseAuthorization` → `UseMiddleware<TenantScopeBeginMiddleware>` → endpoint dispatch. |
| `tests/Kartova.ArchitectureTests/TenantScopeRules.cs` | **Modify.** Add `AspNetCore_does_not_reference_Postgres`. Verify framework-cleanliness rule unchanged. |
| `src/Modules/Organization/Kartova.Organization.IntegrationTests/TenantScopeMechanismTests.cs` | **Refactor.** EF write path; drop `TransactionViaReflection`. |
| `src/Modules/Organization/Kartova.Organization.IntegrationTests/EfEnlistmentProbeTests.cs` | **Add.** Probe test that decides whether the interceptor ships. |
| `src/Modules/Organization/Kartova.Organization.IntegrationTests/StreamingDurabilityTests.cs` | **Add.** Streaming-response regression test. |
| `src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFaultInjectionFixture.cs` | **Add.** Subclass of `KartovaApiFixture` with `FailingCommitTenantScopeDecorator` and an `IStartupFilter` that maps a tenant-scoped streaming test endpoint. |
| `docs/architecture/decisions/ADR-0090-tenant-scope-mechanism.md` | **Modify.** Add dated addendum. |

### 3.3 Component contracts

#### `TenantScopeBeginException`

```csharp
namespace Kartova.SharedKernel.Multitenancy;

public sealed class TenantScopeBeginException : Exception
{
    public TenantScopeBeginException(string message, Exception innerException)
        : base(message, innerException) { }
}
```

#### `INpgsqlTenantScope` (after change)

```csharp
public interface INpgsqlTenantScope : ITenantScope
{
    NpgsqlConnection Connection { get; }
    NpgsqlTransaction Transaction { get; }   // newly public
}
```

#### `RequireTenantScopeMarker` (sketch)

```csharp
public sealed class RequireTenantScopeMarker
{
    public static readonly RequireTenantScopeMarker Instance = new();
}
```

Pure metadata. Consumed only by `TenantScopeBeginMiddleware`.

#### `TenantScopeBeginMiddleware` (sketch)

```csharp
public sealed class TenantScopeBeginMiddleware
{
    internal const string HandleKey = "Kartova.TenantScope.Handle";

    private readonly RequestDelegate _next;
    public TenantScopeBeginMiddleware(RequestDelegate next) { _next = next; }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var needsScope = endpoint?.Metadata.GetMetadata<RequireTenantScopeMarker>() is not null;
        if (!needsScope)
        {
            await _next(context);
            return;
        }

        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        if (!tenantContext.IsTenantScoped)
        {
            await Results.Problem(
                type: ProblemTypes.MissingTenantClaim,
                title: "JWT is missing the tenant_id claim",
                statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
            return;
        }

        var scope = context.RequestServices.GetRequiredService<ITenantScope>();
        var ct = context.RequestAborted;

        IAsyncTenantScopeHandle handle;
        try
        {
            handle = await scope.BeginAsync(tenantContext.Id, ct);
        }
        catch (TenantScopeBeginException)
        {
            await Results.Problem(
                type: ProblemTypes.ServiceUnavailable,
                title: "Database is currently unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
            return;
        }

        // Hand off to the commit filter via Items; middleware retains DisposeAsync ownership
        // so rollback fires on exception or non-committed paths.
        context.Items[HandleKey] = handle;
        try
        {
            await _next(context);   // parameter binding (DbContext OK now) + filter chain + IResult.ExecuteAsync
        }
        finally
        {
            await handle.DisposeAsync();   // commit happened in filter; this is rollback-or-cleanup
        }
    }
}
```

The scope is *active* throughout `await _next(context)`, so DI-injected `OrganizationDbContext` resolves successfully during parameter binding (the failure mode the original filter-only design hit).

#### `TenantScopeCommitEndpointFilter` (sketch)

```csharp
public sealed class TenantScopeCommitEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var result = await next(ctx);   // handler returns IResult — NOT yet executed

        if (!ctx.HttpContext.Items.TryGetValue(TenantScopeBeginMiddleware.HandleKey, out var obj)
            || obj is not IAsyncTenantScopeHandle handle)
        {
            throw new InvalidOperationException(
                "TenantScopeCommitEndpointFilter ran without an active scope handle. " +
                "TenantScopeBeginMiddleware must be wired in the request pipeline.");
        }

        await handle.CommitAsync(ctx.HttpContext.RequestAborted);
        return result;   // ASP.NET runs IResult.ExecuteAsync AFTER commit succeeds
    }
}
```

The defensive `InvalidOperationException` surfaces a wiring bug (filter attached without the middleware in the pipeline) immediately rather than silently committing nothing.

#### `RequireTenantScope` (after change)

```csharp
public static RouteGroupBuilder RequireTenantScope(this RouteGroupBuilder builder)
{
    builder.RequireAuthorization();
    builder.WithMetadata(RequireTenantScopeMarker.Instance);
    builder.AddEndpointFilter<TenantScopeCommitEndpointFilter>();
    return builder;
}
```

### 3.4 Request lifecycle (matches ADR-0090 §4.1, with hybrid two-piece adapter)

```
Client → GET /api/v1/organizations/me + Bearer <JWT>
  ↓
UseAuthentication() — JwtBearerHandler validates against KeyCloak JWKS
  ↓
TenantClaimsTransformation — populates scoped ITenantContext
  ↓
UseAuthorization() — passes (RequireAuthorization implicit from RequireTenantScope())
  ↓
TenantScopeBeginMiddleware:
    if endpoint has no RequireTenantScopeMarker → just await _next(context)
    if !ITenantContext.IsTenantScoped → 401 (missing-tenant-claim) and return
    try scope.BeginAsync(tenantContext.Id, ct)
        catch TenantScopeBeginException → 503 (service-unavailable) and return
    Items[HandleKey] = handle
    try { await _next(context) } finally { await handle.DisposeAsync() }
  ↓ (await _next dispatches into endpoint pipeline)
Parameter binding — OrganizationDbContext resolves OK because scope.Connection is active
  ↓
TenantScopeCommitEndpointFilter:
    var result = await next(ctx)            ← handler runs, returns IResult (NOT yet executed)
    handle = Items[HandleKey]
    await handle.CommitAsync(ct)            ← throws → bubbles → ExceptionHandler → 500
    return result                            ← ASP.NET runs IResult.ExecuteAsync AFTER commit
  ↓
ASP.NET writes response body
  ↓ (filter chain unwinds, control returns to middleware)
TenantScopeBeginMiddleware finally: handle.DisposeAsync
    if committed → resource cleanup only
    if not committed (handler exception, commit failure, etc.) → ROLLBACK
```

## Error handling

| When | Status | Problem-details `type` | Notes |
|------|--------|------------------------|-------|
| No JWT / invalid JWT | 401 | (standard JwtBearer challenge) | Unchanged |
| Valid JWT, missing `tenant_id` claim | 401 | `missing-tenant-claim` | Filter check on `ITenantContext.IsTenantScoped` |
| Role check fails | 403 | `forbidden` | `RequireAuthorization` policy before filter |
| `BeginAsync` throws `TenantScopeBeginException` | 503 | `service-unavailable` | New mapping; replaces direct `NpgsqlException` catch |
| Handler throws | bubbles | (centralized via `UseExceptionHandler` + ProblemDetails) | `await using handle` rolls back |
| `CommitAsync` throws | bubbles → 500 | `internal-server-error` | Critically: rethrow happens before `IResult.ExecuteAsync` so client sees clean 500 |
| Tenant token tries to access another tenant's resource | 404 | `resource-not-found` | RLS hides the row |

The filter's `try/catch` is *only* around `BeginAsync` because that's where 503 has spec semantics. Commit failures and handler exceptions intentionally bubble to ASP.NET's exception pipeline so the centralized handler enforces ADR-0091.

## Testing

### 6.1 Probe test — decides interceptor inclusion

```
EF_DbContext_enlists_in_scope_transaction_automatically:
  1. Build host scope; resolve ITenantScope from sp.
  2. await tenantScope.BeginAsync(SeededOrgs.OrgA, ct).
  3. Resolve OrganizationDbContext.
  4. Compare db.Database.CurrentTransaction?.GetDbTransaction() to npgScope.Transaction.

Outcome:
  - Pass → EF auto-enlisted; do not ship EnlistInTenantScopeInterceptor.
  - Fail → ship the interceptor; re-run probe with the interceptor wired (must pass after).
```

### 6.2 Architecture rule additions

| Rule | Intent |
|------|--------|
| `AspNetCore_does_not_reference_Postgres` | NetArchTest `Types.InAssembly(SharedKernelAspNetCore).Should().NotHaveDependencyOn("Kartova.SharedKernel.Postgres")` — codifies the project-reference cut |

### 6.3 Refactored existing §6.3 tests

`TenantScopeMechanismTests.cs` rewritten to drive writes through `OrganizationDbContext` instead of raw SQL on the connection's transaction. The reflection-based `Transaction` access via `TransactionViaReflection` is removed; tests use `db.Add(org) + db.SaveChangesAsync()`. The reflection-based `TenantId` setter on the `Organization` aggregate stays — production correctly forbids arbitrary tenant assignment via `Organization.Create`.

### 6.4 New regression test — streaming-response durability

```
Test name: Commit_failure_on_streaming_response_returns_5xx_before_body_starts
Approach: B (IStartupFilter)

Setup (KartovaApiFaultInjectionFixture):
  - FailingCommitTenantScopeDecorator wraps the real ITenantScope.
    Singleton flag in DI flips commit to throw on demand.
  - IStartupFilter maps a tenant-scoped streaming endpoint /__test/stream
    that returns Results.Stream over a 2 KB chunked source.

Test:
  1. Set the flag to fail commit.
  2. GET /__test/stream + valid Org A token.
  3. Assert: response.StatusCode == 500.
  4. Assert: Content-Type == "application/problem+json".
  5. Assert: body parses as ProblemDetails (not the streamed bytes).
  6. Verify via bypass connection: no orphan row was committed.

Fallback (if TestServer cannot reliably distinguish "headers committed
before exception" from "partial body then exception"):
  Downgrade to a component-level assertion that TenantScopeEndpointFilter
  calls handle.CommitAsync BEFORE returning the IResult. Document the gap
  in the test docstring.
```

### 6.5 Test inventory

| Tier | Existing | New | Refactored | Net |
|------|----------|-----|------------|-----|
| Architecture | 29 | 1 | 0 | 30 |
| Integration (Organization) | 12 | 2 (probe + streaming) + (0–1 conditional for interceptor) | 3 (rewrite without reflection on Transaction) | 14–15 |
| Unit | 38 | 0 | 0 | 38 |

## Success criteria

1. Probe test runs and either skips or ships `EnlistInTenantScopeInterceptor`. Either path is correct; the test itself is the artifact.
2. `dotnet build Kartova.slnx -c Debug` clean (`TreatWarningsAsErrors`).
3. All architecture tests green, including new `AspNetCore_does_not_reference_Postgres`.
4. All unit tests green.
5. Organization integration tests green (probe + refactored §6.3 + streaming).
6. KeyCloak smoke test (`Kartova.Api.IntegrationTests`) still green.
7. `app.UseMiddleware<TenantScopeMiddleware>()` replaced with `app.UseMiddleware<TenantScopeBeginMiddleware>()` in `Program.cs`; `TenantScopeMiddleware.cs` deleted; `TenantScopeBeginMiddleware.cs` and `TenantScopeCommitEndpointFilter.cs` and `RequireTenantScopeMarker.cs` added.
8. ADR-0090 has a dated addendum at the end recording the slice-2-followup correction.
9. `Kartova.SharedKernel.AspNetCore.csproj` no longer references `Kartova.SharedKernel.Postgres.csproj`.
10. `INpgsqlTenantScope.Transaction` is public; reflection-based access in tests is removed.

## Out of scope (firm)

- Any new feature endpoints in production code. The streaming-response test endpoint is test-only via `IStartupFilter` + `ConfigureTestServices`.
- Wolverine middleware updates (slice 4+ when message handlers exist).
- Performance / connection-pool sizing (separate operational ADR).
- Removing the existing `TenantScopeRequiredInterceptor` SaveChanges fail-fast — that complements enlistment and remains valuable.
- Changes to `TenantClaimsTransformation`, JWT validation, or KeyCloak realm configuration.

## Risks & mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| ASP.NET Core 10 endpoint-filter ordering relative to parameter binding | (resolved) | DI-injected DbContext failed to resolve when filter-only design was attempted | Empirically confirmed during Task 7's first attempt: minimal-API parameter binding runs *before* the filter chain, so `OrganizationDbContext` resolution triggered `scope.Connection` while the scope was still inactive. Hybrid design (begin-middleware + commit-filter) addresses this by opening the scope before parameter binding. Documented in §Decisions and ADR-0090 addendum. |
| EF Core 10 auto-enlistment is brittle / version-dependent | Medium | Future EF upgrade silently breaks slice-3 writes | Probe test guards the assumption — re-runs in CI on every PR. If EF behavior changes, probe fails first. |
| `EnlistInTenantScopeInterceptor` (if shipped) doesn't fire at the right lifecycle hook | Low | Silent non-enlistment | Plan-stage research before writing it; the interceptor's correctness is verified by the same probe test running with it wired |
| Streaming-response test cannot reliably observe "no body sent" via TestServer | Medium | Durability promise tested only at component level | Documented fallback: component-level `CommitAsync called before IResult.ExecuteAsync` assertion |
| `TenantScopeBeginException` introduction breaks an existing reference | Low | Compile failure in `Kartova.Api` or tests | Comprehensive grep before commit; CI build catches anyway |
| Removing `SharedKernel.AspNetCore → SharedKernel.Postgres` reveals a hidden cross-call | Low | Compile failure | The new arch rule + a clean build verifies; if a real cross-call exists, it surfaces and forces a discussion |
| `RequireAuthorization()` on `RequireTenantScope()` regresses an endpoint that worked anonymously | Very low | 401 on a previously-200 endpoint | All current tenant-scoped endpoints under `MapGroup("/api/v1")` already require auth in practice; verified by grep + the existing `AuthErrorTests.No_token_returns_401` test |

## Implementation phasing

The plan stage will sequence tasks roughly as follows; final ordering decided in the plan document:

1. Probe test (decides interceptor inclusion) — RED first.
2. `TenantScopeBeginException` + wrap in `TenantScope.BeginAsync` + remove project reference + new arch rule.
3. `INpgsqlTenantScope.Transaction` exposure + delete `TransactionViaReflection` from tests.
4. Hybrid filter conversion: add `RequireTenantScopeMarker`, `TenantScopeBeginMiddleware`, `TenantScopeCommitEndpointFilter`; rewrite `RequireTenantScope()` to chain auth + marker + commit-filter; delete `TenantScopeMiddleware`; replace `app.UseMiddleware<TenantScopeMiddleware>()` with `app.UseMiddleware<TenantScopeBeginMiddleware>()`.
5. (Conditional) `EnlistInTenantScopeInterceptor`.
6. Refactor `TenantScopeMechanismTests` to EF write path.
7. `KartovaApiFaultInjectionFixture` + streaming durability test.
8. ADR-0090 addendum.
9. Full test suite green; commit; PR.

## References

- ADR-0090: tenant-scope mechanism (the source of truth this PR brings the implementation back to)
- ADR-0091: RFC 7807 problem details (the error-response contract this PR honors via centralized exception handling)
- Spec: `docs/superpowers/specs/2026-04-22-slice-2-auth-multitenancy-design.md` §3.1 (project layout), §3.2 (dependency graph), §4.1 (request lifecycle), §6.3 (test inventory)
- PR #1 (slice 2), #2 (review fixes), #3 (housekeeping), #4 (defense-in-depth tests) — incremental context this PR builds on
- PR #4 review references: cc.md / pr-review-cc.md raised B1 (middleware vs filter) and B2 (missing §6.3 tests); review-01 raised the AddModuleDbContext double-UseNpgsql concern (resolved in PR #2) and the test-vs-prod grant divergence (resolved in PR #2). All cited reviews were retrospective audits, not blockers.
