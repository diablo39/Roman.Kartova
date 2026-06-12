# Design — Append-only tamper-evident audit log: Phase 1 (foundation)

**Date:** 2026-06-12
**Author:** Roman Głogowski (AI-assisted)
**Status:** Approved (pre-implementation)
**Story:** E-01.F-03.S-03 (Append-only audit log table — MiFID II)
**ADR:** ADR-0018 (controlling decision — append-only tamper-evident audit log); related ADR-0016/0017 (MiFID II + retention), ADR-0090 (tenant scope), ADR-0082 (modular monolith), ADR-0085 (migrator), ADR-0102 (offboard hard-delete — names this slice as the parked audit-trail gap)
**Module:** new `Kartova.Audit` (+ SharedKernel port)

## 1. Why this slice, why now

ADR-0018 specifies a dedicated insert-only, hash-chained audit table for MiFID II tamper-evidence (PRD §7.3). It has been accepted since 2026-04-17 but never built. Slices 9–10 then shipped exactly the high-value events that ADR-0018 is meant to capture — member role changes and **hard-delete offboarding** — with no audit trail. ADR-0102 made the dependency explicit:

> *"audit trail deferred to the ADR-0018 slice (named gap)"*

So this is that slice. Building the trail now avoids backfilling provenance for irreversible mutations (offboard is a hard delete — ADR-0102) that already exist in production code.

**Phasing (user-directed):**

- **Phase 1 — Foundation (this spec):** the *mechanism* to log. The module, table, writer port, DB-enforced insert-only grants, per-tenant hash chain, and chain verification. **No business events are wired** — the slice is provably complete when an integration test appends entries, reads them back, verifies the chain, and confirms `UPDATE`/`DELETE` are rejected.
- **Phase 2 — Usage (separate spec/plan):** each existing org/team mutation handler calls `IAuditWriter.AppendAsync(...)` inside its transaction; an action taxonomy and per-action payload shapes are defined.

## 2. Decisions (resolved during brainstorming)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **Synchronous, in-transaction write, fail-closed.** The writer enlists in the request's `ITenantScope` transaction; if the audit insert fails, the business change rolls back. | A mutation can never commit without its audit row — MiFID-correct "no audit, no action". Idiomatic to the existing direct-dispatch sync handlers (ADR-0093). Resolves the fail-open/closed question ADR-0018 left open. |
| 2 | **Dedicated `Kartova.Audit` module** owns the table, `AuditDbContext`, migrations, and the `IAuditWriter` implementation. The `IAuditWriter` *interface* lives in SharedKernel. | Audit is cross-cutting; no module may reference another's Infrastructure (ADR-0082). Mirrors the existing `ITeamMembershipReader` (SharedKernel port) → `OrganizationTeamMembershipReader` (module impl) split. |
| 3 | **Per-tenant hash chain** (not global). | The writer runs inside a tenant-scoped txn (`SET LOCAL app.current_tenant_id`, ADR-0090); RLS only exposes the tenant's own rows, so a global chain would require RLS-forbidden cross-tenant reads to fetch the predecessor hash. Per-tenant also gives clean per-tenant regulator export. |
| 4 | **`actor_display` is a denormalized snapshot.** | An offboarded user's row must still name who acted after their `users` projection is hard-deleted (ADR-0102). |
| 5 | Inline chaining serialized via `SELECT ... FOR UPDATE` on the tenant's chain head. | Correctness over throughput; per-tenant append contention is negligible at MVP scale. The accepted cost of choosing inline (vs deferred-sealer) chaining. |

**Non-goals / explicitly deferred (called out, not silently dropped):**

| Deferred | Owner / why |
|----------|-------------|
| Monthly **partitioning** (ADR-0018) | Scale optimization; YAGNI at MVP volume. Schema will not preclude adding it. |
| **Retention purge** (180d default / 5yr MiFID, ADR-0017) | Belongs to the Data Retention engine (E-01.F-05.S-01). Foundation only stamps a retention-relevant `occurred_at`. |
| **Cold-storage archival** (ADR-0020) | Later slice. |
| **Regulator-facing export / verification UI/CLI** | Later. Phase 1 ships the verifier as a service + tests; the surfaced wrapper is deferred. |
| **Wiring real events** | Phase 2 by definition. |

## 3. Architecture

Call path — synchronous, sharing the per-request connection + transaction:

```
Endpoint delegate ──► business handler (e.g. OffboardMemberHandler)   [Phase 2 callers]
                          │  (already inside ITenantScope txn — ADR-0090)
                          ├─► db.SaveChangesAsync()          // business change
                          └─► IAuditWriter.AppendAsync(entry) // audit row — SAME txn
                                   │
                          (SharedKernel port) ──► AuditWriter (Audit.Infrastructure)
                                   │
                                   └─► AuditDbContext (AddModuleDbContext → shares the
                                       request's single connection + transaction)
```

- **`IAuditWriter`** (SharedKernel): `Task AppendAsync(AuditEntry entry, CancellationToken ct)`. `AuditEntry` is a SharedKernel input record (`action`, `targetType`, `targetId`, optional structured `data`) — **not** the EF entity. The writer derives actor + tenant + timestamp itself (callers don't pass ambient context). Mirrors the `ITeamMembershipReader` port split.
- **`AuditWriter`** (Audit.Infrastructure): resolves actor from `ICurrentUser` (`System` actor fallback for background jobs with no HTTP principal), reads `TimeProvider` for `occurred_at`, computes hash-chain fields, inserts via `AuditDbContext`. Because that context is registered with `AddModuleDbContext`, it rides the same connection/transaction as the business change → atomic; fail-closed falls out for free (an insert exception bubbles up and the request transaction rolls back).
- **Hash-chain head:** `SELECT row_hash, seq FROM audit_log WHERE tenant_id = <current> ORDER BY seq DESC LIMIT 1 FOR UPDATE`. `FOR UPDATE` serializes concurrent appends per-tenant. Genesis row (no predecessor) chains off a fixed all-zero hash; its `seq = 1`.
- **No cross-module reference:** Phase 2 callers (Catalog, Organization) see only the SharedKernel `IAuditWriter`; DI binds the Audit impl. NetArchTest stays green.

## 4. Data model

`audit_log` (PostgreSQL 18, tenant-scoped, RLS on `tenant_id`):

| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` PK | App-generated (deterministic GUID v7 not required). |
| `tenant_id` | `uuid` NOT NULL | RLS discriminator. |
| `seq` | `bigint` NOT NULL | Per-tenant monotonic chain position (starts at 1). |
| `occurred_at` | `timestamptz` NOT NULL | UTC, via `TimeProvider` — never `DateTime.UtcNow`. |
| `actor_type` | `text` NOT NULL | `User` \| `System` \| `ServiceAccount`. |
| `actor_id` | `uuid` NULL | KeyCloak subject; null for `System`. |
| `actor_display` | `text` NULL | Denormalized snapshot (decision 4). |
| `action` | `text` NOT NULL | Taxonomy string, e.g. `member.offboarded`. (Values defined in Phase 2.) |
| `target_type` | `text` NOT NULL | e.g. `User`, `Team`, `Application`. |
| `target_id` | `text` NOT NULL | String to accommodate non-uuid targets. |
| `data` | `jsonb` NULL | Action-specific structured diff (before/after where relevant). |
| `prev_hash` | `bytea` NOT NULL | Predecessor's `row_hash`; all-zero (32 bytes) for genesis. |
| `row_hash` | `bytea` NOT NULL | `SHA-256` over canonical serialization (see §5). |

**Constraints / indexes:**
- PK `(id)`; UNIQUE `(tenant_id, seq)` — enforces no gaps/dupes in the chain position.
- Index `(tenant_id, occurred_at)` for time-range queries; `(tenant_id, target_type, target_id)` for per-target history (Phase 2 read paths).

**Insert-only enforcement (Kartova.Migrator, ADR-0085 — never at app startup):**
- App DB role: `GRANT INSERT, SELECT ON audit_log` — **no** `UPDATE`, **no** `DELETE`. This is the DB-enforced ADR-0018 guarantee; application-level discipline is not relied upon.
- RLS: `ENABLE ROW LEVEL SECURITY` + `INSERT WITH CHECK (tenant_id = current_setting('app.current_tenant_id')::uuid)` + `SELECT USING (tenant_id = current_setting('app.current_tenant_id')::uuid)`.

## 5. Hash chain + verification

- **Canonical serialization:** a stable, field-ordered byte encoding of `(tenant_id, seq, occurred_at, actor_type, actor_id, action, target_type, target_id, data, prev_hash)`. JSONB `data` is serialized via a canonical (sorted-key, no-insignificant-whitespace) form so the hash is reproducible. Encoding is centralized in one function so the writer and verifier agree by construction.
- `row_hash = SHA-256(canonical_bytes)`.
- **`IAuditChainVerifier.VerifyAsync(tenantId, ct)`** walks the tenant's rows ordered by `seq` and asserts: (a) `seq` is contiguous from 1, (b) each row's `prev_hash` equals the prior row's `row_hash`, (c) each recomputed `row_hash` matches the stored value. Returns a result describing the first break (seq + reason) or "intact". Phase 1 ships it as an injectable service exercised by tests; the regulator-facing wrapper is deferred (§2).

## 6. Failure policy (resolves ADR-0018 open question)

- **Fail-closed.** `AppendAsync` throwing propagates out of the business handler → `ITenantScope` transaction rolls back → endpoint returns 500. No mutation commits without its audit row.
- **Accepted consequence:** an `audit_log` outage blocks audited mutations. Intended for a tamper-evidence store ("no audit, no action"); documented here as a known trade-off, not a defect.

## 7. Testing strategy (five-tier, ADR-0097)

- **Unit:** canonical-serialization stability (same input → same bytes; `data` key-order-independent); `row_hash` determinism; genesis handling (`seq=1`, zero `prev_hash`); verifier detects each break mode — tampered `data`, forged `row_hash`, missing `seq`, reordered chain.
- **Architecture (NetArchTest):** `IAuditWriter` resides in SharedKernel; no module references `Audit.Infrastructure`; every Contracts type + `*Dto`/`*Request`/`*Response` carries `[ExcludeFromCodeCoverage]`; `AuditModule` (composition) excluded.
- **Integration (Testcontainers, Postgres):**
  1. Append N entries inside a request transaction → rows visible after commit; `seq` contiguous; `VerifyAsync` returns intact.
  2. **`UPDATE audit_log` and `DELETE FROM audit_log` are rejected** by role grants — the core ADR-0018 guarantee.
  3. RLS blocks cross-tenant `SELECT`.
  4. **Fail-closed:** force `AppendAsync` to throw mid-request → the accompanying business write is rolled back (no orphaned business change, no audit row). *This test proves the slice's premise and is mandatory.*

## 8. Definition of Done

Per CLAUDE.md nine-point gate. Specifically, because this slice wires DB schema + grants + RLS + a transactional writer, item 5 (real `docker compose up` + happy-path + negative-path) is satisfied by: bringing the migrator up to apply grants/RLS, then exercising append-and-verify plus a rejected `UPDATE`/`DELETE` against the live container — unit + architecture tests alone are the wrong layer for grant/RLS semantics.
