# Design — Audit event wiring (Phase 2 follow-up): Catalog application events

**Date:** 2026-06-19
**Author:** Roman Głogowski (AI-assisted)
**Status:** Approved (pre-implementation)
**Story:** E-01.F-03.S-03 (Append-only audit log) — the last deferred chunk (Catalog events); closes the story fully.
**Builds on:** [2026-06-17-audit-event-wiring-design.md](2026-06-17-audit-event-wiring-design.md) (org/people events) and [2026-06-12-audit-log-foundation-design.md](2026-06-12-audit-log-foundation-design.md) (the mechanism).
**ADRs:** ADR-0018 (append-only tamper-evident audit log — controlling), ADR-0090 (tenant scope), ADR-0082 (modular monolith — no cross-module references), ADR-0093 (direct-dispatch sync handlers), ADR-0096 (If-Match/ETag on edit), ADR-0073 (lifecycle transitions), ADR-0103 (required owning team).
**Modules touched:** `Kartova.Catalog` (Application + Infrastructure) only.

## 1. Why this slice, why now

The audit *mechanism* shipped 2026-06-12; org/people events were wired 2026-06-17, which explicitly deferred **Catalog app events** as "a clean follow-up slice — same inline pattern, no new mechanism." The System-actor + invitation-expiry sweep (2026-06-18) closed the other deferred chunk. This slice wires the remaining Catalog mutations, after which E-01.F-03.S-03 is fully closed.

Crucially, the 2026-06-17 slice already added `ICurrentUser.DisplayName` and made `AuditWriter` set `actorDisplay` from it. **This slice therefore changes no audit mechanism** — it is pure handler wiring plus a Catalog-local action taxonomy.

## 2. Scope

**In scope — 7 user-initiated Catalog HTTP mutations, `User` actor only**, emitting 4 action strings:

| Handler | File | Action emitted | `data` keys |
|---|---|---|---|
| `RegisterApplicationHandler` | Catalog.Infrastructure | `application.registered` | `displayName`, `teamId` |
| `EditApplicationHandler` | Catalog.Infrastructure | `application.edited` | `displayName`, `description` (new state) |
| `DeprecateApplicationHandler` | Catalog.Infrastructure | `application.lifecycle_changed` | `from`, `to`, `sunsetDate` |
| `DecommissionApplicationHandler` | Catalog.Infrastructure | `application.lifecycle_changed` | `from`, `to`, `sunsetDate` |
| `ReactivateApplicationHandler` | Catalog.Infrastructure | `application.lifecycle_changed` | `from`, `to`, `sunsetDate` |
| `UnDecommissionApplicationHandler` | Catalog.Infrastructure | `application.lifecycle_changed` | `from`, `to`, `sunsetDate` |
| `AssignApplicationTeamHandler` | Catalog.Infrastructure | `application.team_assigned` | `fromTeamId`, `toTeamId` |

The four lifecycle handlers emit a **single** `application.lifecycle_changed` action distinguished by `from`/`to` in `data` (chosen during brainstorming over one-action-per-transition: fewer stable constants; querying a specific transition filters on `data` rather than `action`).

**Explicitly out of scope:** Service/API/Infrastructure entity events (those entities don't exist yet — they arrive with E-02.F-02+). When they land, each adds its own `data` shaping under the same pattern. List/read endpoints are never audited (we audit state changes, not reads).

## 3. Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **Method-parameter injection of `IAuditWriter`** — add `IAuditWriter audit` to each `Handle(...)` signature. | Wolverine resolves method parameters exactly as it already does for `CatalogDbContext db` / `ITenantContext tenant` / `ICurrentUser user`. Avoids constructor churn and matches Catalog's existing handler style. A deliberate, mild deviation from Organization's constructor injection — justified by the differing handler shape. |
| 2 | **`AppendAsync` after the business `SaveChangesAsync`, success path only.** | We audit committed *intent to change state*. Handlers returning `null` (RLS-hidden / not-found), `AssignApplicationTeamResult.NotFound`/`InvalidTeam`, or throwing `InvalidLifecycleTransitionException`/`ArgumentException` never reach the append. The shared transaction means an append failure still rolls the business change back (fail-closed). |
| 3 | **Old-value capture before the domain mutation.** Lifecycle handlers read `app.Lifecycle` (→ `from`) before calling the domain transition; `AssignApplicationTeamHandler` reads `app.TeamId` (→ `fromTeamId`) before `AssignTeam`. | Domain methods mutate in place, so the prior value must be snapshotted first — mirrors `member.role_changed`'s old/new capture. `to` is the post-transition `app.Lifecycle`. |
| 4 | **Taxonomy in `Kartova.Catalog.Application`** — new `CatalogAuditActions` (4 consts) + `CatalogAuditTargetTypes` (`Application = "Application"`). | Action/target strings are the stable `audit_log` contract; the taxonomy belongs to the owning module. Catalog cannot reference `Organization.Application`'s `AuditTargetTypes` (ADR-0082 — no cross-module references), so a Catalog-local target-type const is required. |

## 4. Action taxonomy & payloads

`AuditEntry.Data` is `IReadOnlyDictionary<string, string?>?` — **string values only** (jsonb-hash-stability rule); `targetId` is a string. `targetType = "Application"`, `targetId = app.Id.Value.ToString()` for all rows.

| Action constant | `action` string | `data` keys | Old-value capture |
|---|---|---|---|
| `ApplicationRegistered` | `application.registered` | `displayName`, `teamId` | — (new entity; actor_id already = creator) |
| `ApplicationEdited` | `application.edited` | `displayName`, `description` | — (new state) |
| `ApplicationLifecycleChanged` | `application.lifecycle_changed` | `from`, `to`, `sunsetDate` | `from` = `app.Lifecycle` read before transition; `to` = post-transition `app.Lifecycle`; `sunsetDate` = post-transition `app.SunsetDate` (null → `null` string) |
| `ApplicationTeamAssigned` | `application.team_assigned` | `fromTeamId`, `toTeamId` | `fromTeamId` = `app.TeamId` read before `AssignTeam` |

Lifecycle `sunsetDate`: Deprecate/UnDecommission set it; Decommission leaves it; Reactivate clears it (recorded as `null`). Serialize as `DateTimeOffset.ToString("O")` (round-trip) when present.

## 5. Handler wiring

`AssignApplicationTeamHandler` returns the `AssignApplicationTeamResult` discriminated union — append only on the `Success(app)` case (not `NotFound`/`InvalidTeam`). `RegisterApplicationHandler` already injects `ICurrentUser user`; the others load the entity themselves and gain `IAuditWriter audit` as a new method parameter. No constructor signature changes.

`CatalogDbContext` is registered via `AddModuleDbContext<CatalogDbContext>` (confirmed in `CatalogModule.RegisterServices`), so every wired handler already runs inside the per-request `ITenantScope` transaction (`SET LOCAL app.current_tenant_id`, ADR-0090); the audit row rides the same connection + transaction.

## 6. Architecture invariants (unchanged from Phase 1)

- **Synchronous, in-transaction, fail-closed** — `AuditDbContext` shares the request connection; an append failure rolls the business change back.
- **No cross-module Infrastructure reference** (ADR-0082) — handlers see only the SharedKernel `IAuditWriter`; DI binds the Audit impl. NetArchTest stays green.
- **Per-tenant hash chain + RLS** — every wired caller runs inside an `ITenantScope` txn, so the writer's existing chain logic applies untouched.

## 7. Testing strategy (five-tier, ADR-0097; per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md))

This slice wires a DB-write seam onto existing authenticated HTTP mutations, so gate-5 evidence is **real-seam** integration tests — `KartovaApiFixtureBase`, real Postgres/RLS, real `JwtBearer` validation — not mocked handlers.

**Gate-5 artifacts (named deliverables, `Kartova.Catalog.IntegrationTests`):**

1. **Happy — register writes a correct row.** `POST /api/v1/catalog/applications` (real JWT) → exactly one `audit_log` row with `action=application.registered`, `actor_id` = caller `sub`, `actor_display` = caller `name` claim, `data` carrying `displayName`+`teamId`; `IAuditChainVerifier.VerifyAsync` reports the tenant chain intact.
2. **Happy — lifecycle transition writes from/to.** Deprecate an Active application → one `application.lifecycle_changed` row with `data` `from=Active`, `to=Deprecated`, `sunsetDate` set.
3. **Negative — rejected transition writes no row.** Decommission before the sunset date → `409` and **zero** new `audit_log` rows. Proves only successful mutations are audited.

**Unit:** `application.lifecycle_changed` payload shaping — `from` is the pre-transition lifecycle (asserts the old value is read before the domain mutation), `to`/`sunsetDate` reflect post-transition state.

**Architecture (NetArchTest):** existing rules stay green — `IAuditWriter` in SharedKernel; no Catalog → `Audit.Infrastructure` reference; taxonomy constants are logic-free literals exercised by the integration tests (no `[ExcludeFromCodeCoverage]` needed).

**Container build (gate 4):** no Dockerfile/`COPY` change in this slice (no new project/module), so the existing `images` CI job covers it; flag if a new file lands outside an already-copied path.

## 8. Definition of Done

Per CLAUDE.md's eight always-blocking gates + the conditional mutation gate. **Gate 6 (mutation): should-do** here — the slice adds wiring + constants + payload shaping; it does not touch Domain/Application *business* logic. Run it on the `lifecycle_changed` `from`/`to` shaping if practical, else skip with this note.

Slice size: ~150–200 prod LOC (7 handler edits + taxonomy), well within the ~400 target.

On completion, update `docs/product/CHECKLIST.md` E-01.F-03.S-03 note: Phase 2 follow-up (audit-catalog-event-wiring, 2026-06-19) — 7 Catalog application mutations (register/edit/lifecycle/team-assign) wired to `IAuditWriter`; audit event-wiring fully closed.
