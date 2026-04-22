# ADR-0090: Tenant Scope Mechanism — Transaction-Bound `SET LOCAL` with Shared Connection per Request

**Status:** Accepted
**Date:** 2026-04-22
**Category:** Multi-Tenancy
**Related:** ADR-0006, ADR-0011, ADR-0012, ADR-0014, ADR-0080, ADR-0082

## Context

ADR-0012 mandates PostgreSQL Row-Level Security with `app.current_tenant_id` as the RLS policy input. ADR-0014 mandates extracting tenant from JWT per request. The question this ADR answers: **where and how** the GUC is set on each request so RLS works, without pool leaks, without breaking atomicity (ADR-0080 outbox), without per-handler code, and with durability correctness (commit before response).

Candidate patterns considered:

- **`DbConnectionInterceptor.ConnectionOpenedAsync` + plain `SET`** — pool-unsafe (values stick on pooled connections between requests); some implementations disable pooling entirely (e.g., bytefish.de ASP.NET multi-tenancy article uses `Pooling=false`), which doesn't meet ADR-0074 scale targets.
- **`DbConnectionInterceptor` + SET on open + RESET on close** — relies on .NET code paths running on every close, leak window under crash / close-hook failure.
- **Pinned connection for DbContext lifetime + SET in constructor** — holds pool slot for full request (including response serialization), worse pool utilization; still relies on RESET in dispose.
- **Per-command transactions in `DbCommandInterceptor`** — breaks atomicity; incompatible with ADR-0080 transactional outbox.
- **Commit on `DbContext.DisposeAsync`** — durability bug: response flushed to client before dispose; commit failures silently lost.
- **Per-entry-point middleware duplicating tx + SET LOCAL logic** — DRY violation; every new transport adds risk.

## Decision

Introduce `ITenantScope` as the single abstraction owning the tenant-isolation mechanism.

**API:**
```csharp
public interface ITenantScope
{
    Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct);
}
public interface IAsyncTenantScopeHandle : IAsyncDisposable
{
    Task CommitAsync(CancellationToken ct);
}
```

**Implementation** (`Kartova.SharedKernel.Postgres.TenantScope`, scoped DI):
- `BeginAsync`: opens `NpgsqlConnection` from `NpgsqlDataSource`, begins transaction, issues `SET LOCAL app.current_tenant_id = <id>`.
- `CommitAsync`: commits the transaction. Failures propagate to caller.
- `DisposeAsync`: rolls back if not committed.

**All module DbContexts** register via `AddModuleDbContext<T>` which:
- Reads the scope's `NpgsqlConnection` via factory delegate in DI.
- Adds `EnlistInTenantScopeInterceptor` to enlist in the scope's transaction on DbContext first use.
- Adds `TenantScopeRequiredInterceptor` (SaveChangesInterceptor) as fail-fast assertion.

**Each transport has exactly one adapter** that calls `Begin` / `CommitAsync`:
- ASP.NET: `TenantScopeEndpointFilter` (via `.AddEndpointFilter<>` on the tenant-scoped route group) — commits before response body flush.
- Wolverine: `TenantScopeMiddleware` (Before/After/OnException) — commits before message ack.
- Future transports: one middleware each, same pattern.

**Tenant claim population** is transport-specific (JWT claim, Kafka header, etc.) and handled by a separate lightweight adapter (e.g., `TenantClaimsTransformation` for ASP.NET); transport adapters call `ITenantContext.Id` into `BeginAsync`.

## Rationale

- **Postgres-native cleanup** — `SET LOCAL` is discarded by Postgres on `COMMIT`/`ROLLBACK`. Process crash, connection fault, skipped dispose, exception path: the server handles it, not .NET code. Pool-safe without relying on Npgsql `DISCARD ALL` or pool disable.
- **Durability correctness** — commit happens in the transport filter *before* response flush / ack. Commit failures become HTTP 500 / message retry, not silent data loss.
- **Atomicity preserved** — one request = one transaction. Outbox inserts (ADR-0080) enlist in the same tx. Multi-DbContext handlers (modular monolith) share connection + tx → cross-module atomic.
- **DRY** — one `ITenantScope` implementation; per-transport adapters are tiny and cross-cutting (registered once). New endpoints inherit enforcement by using the DbContext normally; no handler-level plumbing.
- **Defense-in-depth** — server-side RLS + EF global query filter + `TenantScopeRequiredInterceptor` fail-fast + architecture tests enforcing the pattern. Multiple independent layers.

## Alternatives Considered

See "Context" — each is explicitly rejected above with its specific failure mode documented.

## Consequences

**Positive:**
- No per-handler boilerplate.
- Crash-safe cleanup without pool-config assumptions.
- Commit failures observable by callers.
- Multi-DbContext atomicity for modular monolith.

**Negative / Trade-offs:**
- Holds one `NpgsqlConnection` for the full HTTP request duration (including response serialization). At MVP solo-dev scale not a concern; at 1000-tenant target, connection pool sizing + PgBouncer will be a future operational ADR.
- Every module DbContext must use `AddModuleDbContext<T>` — enforced by architecture test.
- Admin bypass paths (e.g., `POST /api/v1/admin/organizations`) run *outside* tenant scope and require a separate DbContext with BYPASSRLS role. Pattern documented; enforcement via isolated assembly + architecture test.

**Neutral:**
- Secondary stores (Elasticsearch per ADR-0013, Kafka per ADR-0003, MinIO per ADR-0004) have their own tenant isolation and do not participate in the Postgres transaction.
- Wolverine outbox (ADR-0080) composes naturally — outbox insert is part of the same transaction as domain writes.

## Implementation notes

- `ITenantScope` scoped in DI; per request.
- `NpgsqlDataSource` registered as singleton; `BeginAsync` acquires connection from it.
- DbContexts registered with `UseNpgsql(scope.Connection)` pattern via `AddModuleDbContext<T>` helper.
- `EnlistInTenantScopeInterceptor.ContextInitializingAsync` calls `Database.UseTransactionAsync(scope.Transaction.GetDbTransaction(), ct)`.
- Architecture tests enforce: no raw `AddDbContext<T>` for module types; `ITenantScope.BeginAsync` only called from transport adapters.
- Bypass path uses `AdminOrganizationDbContext` configured with `kartova_bypass_rls` role connection string; isolated assembly `Kartova.Organization.Infrastructure.Admin`.

## References

- ADR-0006 (KeyCloak), ADR-0011 (1 org = 1 tenant), ADR-0012 (RLS), ADR-0014 (tenant claim from JWT), ADR-0080 (Wolverine outbox), ADR-0082 (modular monolith).
- PostgreSQL docs: `SET LOCAL`, customized options, Row Security Policies.
- Crunchy Data: "Row-Level Security for Tenants in Postgres" — validates the `current_setting('app.xxx')` community idiom.
- bytefish.de "ASP.NET Core Multi-Tenancy" — alternative considered, rejected due to `Pooling=false`.
