# Design — Audit System-actor + invitation-expiry sweep (Phase 2 follow-up)

**Date:** 2026-06-18
**Author:** Roman Głogowski (AI-assisted)
**Status:** Approved (pre-implementation)
**Story:** E-01.F-03.S-03 (Append-only audit log) — closes the deferred half from the audit-event-wiring spec §2.
**Builds on:** [2026-06-12-audit-log-foundation-design.md](2026-06-12-audit-log-foundation-design.md) (the mechanism) · [2026-06-17-audit-event-wiring-design.md](2026-06-17-audit-event-wiring-design.md) (Phase 2 org/people events; named this work as deferred)
**ADRs:** ADR-0018 (append-only tamper-evident audit log — controlling), ADR-0090 (tenant scope; the periodic job is the transport adapter that owns Begin/Commit), ADR-0082 (modular monolith — no cross-module Infrastructure refs), ADR-0099 (leader-elected advisory-lock periodic primitive), ADR-0105 (per-tenant chain checkpoints), ADR-0102 (offboard hard-delete — actor_display snapshot rationale)
**Modules touched:** `Kartova.Audit.Infrastructure` (writer), `Kartova.SharedKernel.Audit` (interface), `Kartova.Organization.Application` (taxonomy), `Kartova.Organization.Infrastructure.Admin` (sweep)

## 1. Why this slice, why now

The audit-event-wiring slice (2026-06-17) wired the 10 user-initiated Organization HTTP mutations to `IAuditWriter`, recording `actor_type=User`. Its §2 explicitly deferred two chunks, naming them rather than silently dropping them:

- **Catalog app events** — same inline pattern, a clean follow-up (deferred again here; scoped next).
- **`System` actor + `invitation.expired` sweep** — *this slice*. The hourly `ExpireInvitationsHostedService` mutates identity state (expires pending invitations, deletes their KeyCloak shadow users) with **no audit trail**, because it runs cross-tenant on the BYPASSRLS `AdminOrganizationDbContext`, outside any `ITenantScope`/transaction, with no HTTP principal. Auditing it needs a `System`-actor write path *and* per-tenant scope establishment so the per-tenant hash chain + RLS `INSERT WITH CHECK` apply.

This was chosen before Catalog because it builds the **reusable `System`-actor path** — the same path the retention engine (E-01.F-05.S-01) and any future background mutation will need. Catalog is pure repetition of the proven inline pattern; the System-actor mechanism is the genuinely new capability.

## 2. Scope

**In scope:**

1. `IAuditWriter.AppendSystemAsync(TenantId tenant, AuditEntry entry, CancellationToken ct)` — a `System`-actor write path on the existing writer.
2. `OrganizationAuditActions.InvitationExpired = "invitation.expired"` taxonomy constant.
3. Refactor `ExpireInvitationsHostedService` to write through the RLS `OrganizationDbContext` inside a per-tenant `ITenantScope` transaction, appending one `invitation.expired` audit row per expired invitation.

**Out of scope (named, deferred):**

| Deferred | Why |
|----------|-----|
| **Catalog app events** (register / edit / lifecycle / assign-team) | Same inline `AppendAsync` pattern as the 10 already-wired Org handlers; no new mechanism. Next slice. |
| **`ServiceAccount` actor** (ADR-0009) | No service-account caller exists yet (deferred to Phase 5 per E-01.F-04.S-03). The enum value is present; no writer path is built until a caller appears. |
| **Auditing other background jobs** (e.g. `AuditCheckpointHostedService`) | Checkpoint creation is verification-maintenance, not a business mutation; not an audited action. |

## 3. Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **Add `AppendSystemAsync(TenantId, AuditEntry, ct)` to `IAuditWriter`** (Approach A). Extract a private `AppendCoreAsync(Guid tenantId, AuditActorType actorType, Guid? actorId, string? actorDisplay, AuditEntry, ct)` carrying the existing lock → head-read → seq/prevHash → `Create` → save logic. `AppendAsync` delegates with `(tenant.Id.Value, User, currentUser.UserId, currentUser.DisplayName, …)`; `AppendSystemAsync` delegates with `(tenant.Value, System, actorId: null, actorDisplay: AuditWriter.SystemActorDisplay, …)`. | The User path stays byte-for-byte (the 10 shipped wirings + their tests are untouched). Tenant is an **explicit arg** on the system path — matching `AuditCheckpointer`'s explicit-tenant style — so the sweep never has to populate `ITenantContext` merely to satisfy the writer. Rejected Approach B (replace `ICurrentUser` in the writer with an actor-provider abstraction): correct layering but ripples DI + tests across all 10 shipped wirings for no functional gain. |
| 2 | **`System` actor = `actor_type=System`, `actor_id=NULL`, `actor_display="System"`.** Constant `SystemActorDisplay = "System"` on `AuditWriter`. | The domain already supports it: `AuditActorType.System=2` exists, and `AuditLogEntry.Create`'s non-empty-actorId guard is **User-only** (`actorType == User && actorId is null/empty` throws). A null actor id is the honest encoding of "no user acted". No domain or schema change. The hasher already includes `actorType` + nullable `actorId`, so System rows hash and verify. |
| 3 | **Sweep enumerates cross-tenant via BYPASSRLS `AdminOrganizationDbContext` (read-only), but writes through the RLS `OrganizationDbContext` inside a per-tenant `ITenantScope` transaction.** Mirrors `AuditCheckpointHostedService` exactly. | The per-tenant hash chain + RLS `audit_log INSERT WITH CHECK` only work when `app.current_tenant_id` is set on the connection (`SET LOCAL`, ADR-0090) and the write goes through the app role. Enumeration is legitimately cross-tenant (find all due invitations everywhere) so it keeps using the BYPASSRLS read pool; the *mutation* is moved onto the RLS seam. The periodic job acts as the transport adapter that owns `BeginAsync`/`CommitAsync` (ADR-0090). |
| 4 | **One `ITenantScope` transaction per tenant** wrapping all of that tenant's due invitations; commit once. Per-tenant `try/catch` isolation. | Matches the `AuditCheckpointHostedService` one-scope-per-tenant shape. At hourly cadence with low per-tenant invitation volume, holding the txn across the tenant's sequential KC `DELETE` calls is acceptable. A transient failure on one tenant is logged and skipped without aborting the others (strictly better than today's single global batch that aborts everything on one KC error). |
| 5 | **KeyCloak delete stays before the DB commit; idempotent on `NotFound`.** Order per invitation: KC `DeleteUserAsync` → `MarkExpired` (RLS ctx) → `AppendSystemAsync`. The tenant txn commits after the loop. | No new failure mode vs. today. If the commit (or an audit append) fails after a successful KC delete, the invitation stays `Pending`; the next hourly tick re-runs, KC delete returns `NotFound` (swallowed), and the expiry+audit retries. Fail-closed: an audit-append failure rolls back that tenant's `MarkExpired`. Same posture as the existing sweep and `CreateInvitationHandler`. |

## 4. `AppendSystemAsync` — writer surface

```
// Kartova.SharedKernel.Audit.IAuditWriter
Task AppendSystemAsync(TenantId tenant, AuditEntry entry, CancellationToken ct);
```

`AuditWriter` (Kartova.Audit.Infrastructure) refactors its current `AppendAsync` body into:

```
private async Task AppendCoreAsync(
    Guid tenantId, AuditActorType actorType, Guid? actorId, string? actorDisplay,
    AuditEntry entry, CancellationToken ct)
{ /* existing: advisory_xact_lock → head read → seq/prevHash → occurredAt → Create → Add → SaveChanges */ }

public Task AppendAsync(AuditEntry entry, CancellationToken ct)
    => AppendCoreAsync(tenant.Id.Value, AuditActorType.User, currentUser.UserId, currentUser.DisplayName, entry, ct);

public Task AppendSystemAsync(TenantId t, AuditEntry entry, CancellationToken ct)
    => AppendCoreAsync(t.Value, AuditActorType.System, actorId: null, actorDisplay: SystemActorDisplay, entry, ct);
```

The System path **does not touch `ICurrentUser` or `ITenantContext`** — both stay injected only for the User path. Multiple `AppendSystemAsync` calls within one tenant txn chain correctly: each `SaveChanges` flushes its row within the transaction, so the next append's head-read (same connection, same txn) sees it; `pg_advisory_xact_lock` re-acquired by the same txn is a no-op.

## 5. `invitation.expired` payload

| Action constant | `action` | targetType / targetId | `data` keys |
|---|---|---|---|
| `OrganizationAuditActions.InvitationExpired` | `invitation.expired` | `Invitation` / `invitationId` | `email`, `role` |

Symmetric with the existing `invitation.created` row (same keys), so the created→expired lifecycle reads consistently in the log. Values are strings only (jsonb-hash-stability rule).

## 6. Refactored sweep — `ExpireInvitationsHostedService.ExpireDueAsync`

Stays in `Kartova.Organization.Infrastructure.Admin` (it owns the BYPASSRLS `AdminOrganizationDbContext`; the project already references `Organization.Infrastructure` for the RLS `OrganizationDbContext`, `Organization.Application` for the taxonomy, and SharedKernel for `IAuditWriter` — **no new project reference**, ADR-0082 green).

New shape:

1. **Enumerate (cross-tenant, read-only):** via `AdminOrganizationDbContext`, project past-due `Pending` invitations to `(TenantId, Id, KeycloakUserId, Email, Role)`. Group by `TenantId`.
2. **Per tenant** (fresh DI scope from `IServiceScopeFactory`, mirroring `AuditCheckpointHostedService.CheckpointTenantAsync`):
   - `var handle = await tenantScope.BeginAsync(new TenantId(tenantId), ct);` (job = transport adapter)
   - resolve the RLS `OrganizationDbContext` + `IAuditWriter` from the scope
   - load this tenant's due invitations by id through the RLS context (re-filtered `Status == Pending` to ignore any concurrently-accepted/revoked ones)
   - for each: `kc.DeleteUserAsync` (swallow `NotFound`) → `inv.MarkExpired(clock)` → `audit.AppendSystemAsync(tenant, new AuditEntry("invitation.expired", "Invitation", inv.Id, { email, role }), ct)`
   - `await db.SaveChangesAsync(ct)` then `await handle.CommitAsync(ct)`
   - wrap the per-tenant block in `try/catch (Exception ex) when (ex is not OperationCanceledException)`; log + continue to the next tenant.
3. Log a single summary line (`{Expired} expired across {Tenants} tenant(s), {Failed} errored.`).

The base `LeaderElectedPeriodicService` (lock acquisition, scope, leader election) is unchanged. `ExpireDueAsync(IServiceProvider, CancellationToken)` stays public for direct unit testing (the existing testing seam).

## 7. Architecture invariants

- **No cross-module Infrastructure reference** (ADR-0082) — the sweep depends only on the SharedKernel `IAuditWriter`; DI binds the `Kartova.Audit.Infrastructure` impl. NetArchTest stays green.
- **Per-tenant hash chain + RLS** (ADR-0018, ADR-0090) — each expiry is written through the app role inside its tenant's `SET LOCAL` transaction, so the chain advances per-tenant and the RLS `WITH CHECK` prevents writing a row for the wrong tenant even by mistake.
- **Fail-closed, in-transaction** — `AuditDbContext` shares the per-tenant scope connection/transaction (`AddModuleDbContext`); an append failure rolls back that tenant's `MarkExpired`.

## 8. Testing strategy (five-tier, ADR-0097; per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md))

This slice wires a DB-write seam (audit row) onto a background sweep that mutates RLS-scoped tenant data, so gate-5 evidence is **real-seam** integration tests — `KartovaApiFixtureBase`, real Postgres/RLS, real per-tenant scope — driving `ExpireDueAsync` directly against the scope's `IServiceProvider`.

**Gate-5 artifacts (named deliverables, `Kartova.Organization.IntegrationTests`):**

1. **Happy — expiry writes a correct System audit row.** Seed a past-due `Pending` invitation in tenant A → run `ExpireDueAsync` → assert the invitation is `Expired` *and* exactly one `audit_log` row exists with `action=invitation.expired`, `actor_type=System`, `actor_id IS NULL`, `actor_display='System'`, `data` carrying `email`+`role`; the tenant's chain is intact.
2. **Multi-tenant isolation.** Due invitations in tenants A and B → each `invitation.expired` row lands in its **own** tenant's chain; neither chain contains the other tenant's row (RLS-scoped). Both chains intact.
3. **Negative + idempotency.** (a) A non-due `Pending` invitation → it stays `Pending` and **zero** audit rows are written. (b) A past-due invitation whose KC user is already gone (`DeleteUserAsync` → `NotFound`) → still expires + writes its audit row (idempotent retry path).

**Chain-integrity assertion — how (amendment, 2026-06-18):** artifacts 1 & 2 assert "chain intact" structurally via the `AssertChainLinked` helper in `Kartova.Organization.IntegrationTests` (contiguous `seq` from 1 + `prev_hash == predecessor.row_hash`, genesis = 32 zero bytes), **not** by calling `IAuditChainVerifier.VerifyAsync` — because that verifier lives in `Kartova.Audit.Infrastructure` and the Organization integration project must not reference another module's Infrastructure (ADR-0082). The stronger **recomputation-based** verification (re-deriving `row_hash` from fields to catch a hasher/field-omission defect) is exercised against a System chain in `AuditWriterTests.AppendSystem_writes_System_actor_row_with_null_actor_and_chains` (`Kartova.Audit.Infrastructure.IntegrationTests`), which calls `AuditChainVerifier.VerifyAsync` directly. Together the two cover linkage (Org suite) + recomputation (Audit suite) without a cross-module reference.

**Unit (`Kartova.Audit.Infrastructure.Tests`):**

- `AuditWriter.AppendSystemAsync` writes `actor_type=System`, `actor_id=null`, `actor_display="System"`, and chains correctly: genesis row (prevHash = GenesisHash) and a follow-on row (prevHash = prior RowHash, seq increments). Asserts the User-path `AppendAsync` behavior is unchanged (regression guard on the extraction).

**Architecture (NetArchTest):** existing rules stay green — `IAuditWriter` in SharedKernel; no `Organization → Audit.Infrastructure` reference; taxonomy constant is logic-free and exercised by the integration tests (no `[ExcludeFromCodeCoverage]` needed).

**Container build (gate 4):** no Dockerfile/`COPY` change (no new project/module/file outside already-copied paths), so the existing `images` CI job covers it.

## 9. Definition of Done

Per CLAUDE.md's eight always-blocking gates + the conditional mutation gate. **Gate 6 (mutation) is BLOCKING** here: the slice changes Application/Infrastructure *logic* — the writer's actor/chain path and the sweep's per-tenant transaction restructure — not just wiring. Run `/misc:mutation-sentinel` → `/misc:test-generator` on `AuditWriter` + `ExpireInvitationsHostedService`; target ≥80% (`stryker-config.json`); document survivors.

Slice size: ~250–350 prod LOC (writer refactor + 1 interface method + 1 taxonomy constant + sweep restructure), within the ~400 target.

On completion, update `docs/product/CHECKLIST.md`: keep `E-01.F-03.S-03` and refine its note — Phase 2 follow-up: System-actor path + `invitation.expired` sweep wired; **Catalog app events remain the last deferred chunk.**
