# Deep PR Review — feat/audit-catalog-event-wiring (merged)

**Target:** `feat/audit-catalog-event-wiring` vs `master` (range 44208e3..0abd691) · **Status:** OPEN (pre-merge gate)
**Spec:** docs/superpowers/specs/2026-06-19-audit-catalog-event-wiring-design.md · **Plan:** docs/superpowers/plans/2026-06-19-audit-catalog-event-wiring-plan.md
**Reviewers:** R1 (opus, deep code-read) + R2 (sonnet, general). Merged with controller adjudication. Findings below reflect the state **after** the gate-9 fix commit `0abd691`.

## Overview

The slice wires the 7 Catalog application mutations (register, edit, 4 lifecycle transitions, team-assign) to the append-only audit log: each handler receives `IAuditWriter` as a DI-resolved parameter on its minimal-API delegate (ADR-0093 direct-dispatch) and calls `AppendAsync` after `SaveChangesAsync` on the success path, inside the shared per-request `ITenantScope` transaction (fail-closed). New artifacts: `CatalogAuditActions`/`CatalogAuditTargetTypes` (Application), the `CatalogAuditEntries.LifecycleChanged` factory (Infrastructure), and test-fixture audit plumbing. Load-bearing invariants (fail-closed in-transaction append, success-path-only, old-value-before-mutation, actor attribution, per-tenant hash-chain integrity, ADR-0082 boundary) hold.

## Blocking-class issues

**None.** Both reviewer-2 "blocking" findings were adjudicated down:

- **CrossTenantWriteTests instantiates `AuditWriter` from `Kartova.Audit.Infrastructure` — REJECTED (false positive).** ADR-0082 and `ModuleBoundaryTests` govern *production* assemblies; the **test** project's reference to `Audit.Infrastructure` is by-design (added in Task 1, mirroring `Organization.IntegrationTests`, to migrate the audit schema + read rows). Constructing the real `AuditWriter` with a stub actor preserves the real seam — mocking it would weaken the ADR-0090 tenant-scope test. Gate-7 (opus whole-branch) explicitly endorsed this construction. The added NetArchTest (`ModuleBoundaryTests.cs:24`) confirms the *production* Catalog assemblies carry no such reference.
- **`RegisterApplicationHandler` XML doc said "Wolverine handler" — downgraded to nit, fixed in `0abd691`.** Pre-existing stale comment (sibling handlers already said "direct-dispatch"); not a DoD failure. Corrected since the file was already being modified.

## Should-fix issues

**1. Chain-intact assertion uses a hand-rolled `AssertChainLinked` rather than the spec-named `IAuditChainVerifier.VerifyAsync` (sources: R1).** — ACCEPTED DEVIATION (deferred).
- Evidence: `Kartova.Catalog.IntegrationTests/AuditWiringTests.cs` `AssertChainLinked` (seq-contiguity + `prev_hash == predecessor row_hash`); spec §7 artifact #1 names `VerifyAsync`.
- Impact: the linkage check does not recompute each `row_hash` from row content, so a writer-side canonicalization bug that still chains correctly would slip past *this* test.
- Disposition: keep as-is. Consistent with the Organization `AuditWiringTests` sibling; full row-hash recomputation is foundation-tested by `Audit.Infrastructure.IntegrationTests/AuditWriterTests` (which calls `VerifyAsync` extensively) and the writer/hasher are unchanged in this slice. Resolving fully would require tenant-scope plumbing in the HTTP-based Catalog fixture. Documented follow-up: promote a shared `VerifyChainAsync` helper to `KartovaApiFixtureBase` when the audit fixtures are consolidated (see nit 1).

## Nits (≤5)

1. **Audit test plumbing duplicated across module fixtures** (sources: R1 + simplify-gate): `ReadAuditLogAsync` + `AuditRowRecord` + `AssertChainLinked` are copied between the Catalog and Organization integration fixtures. Follow-up: promote to `KartovaApiFixtureBase` before a third module is wired.
2. **`CatalogAuditEntries` lives in Infrastructure** though it is a pure (IO-free) payload factory; arguable case for `Kartova.Catalog.Application` (blocked by the `Application` namespace/type clash). Defensible where it is.
3. **`Task.Delay(2000)` in the Decommission/UnDecommission audit tests** (~4s wall-clock) — matches the established `DecommissionApplicationTests`/`UnDecommissionApplicationTests` convention (real server clock; no test injection point). Acceptable.
4. **Register/edit/team-assign `data`-key literals are inline** rather than centralized like the lifecycle factory — the keys are guarded by the integration assertions, so typo risk is covered; centralizing is optional.
5. **`EditApplicationHandler.TryCaptureCurrentVersionAsync` swallows non-cancellation exceptions without logging** — pre-existing (not introduced by this slice); follow-up if the 412 hint ever needs telemetry.

## Missing tests

- **Register "exactly one row"** — `Register_WritesApplicationRegisteredAuditRow` uses `.Single(...)` filtered by action+targetId; a count-of-1 would be marginally stronger (the decommission-negative test already demonstrates the pattern). Low value.
- **Team-assign rejected path** — CLOSED in `0abd691`: `AssignTeam_InvalidTeam_WritesNoAuditRow` proves the `InvalidTeam` early-return writes zero `application.team_assigned` rows.
- **Actor attribution on non-register rows** — CLOSED in `0abd691`: `Deprecate_…` and `AssignTeam_…` now assert `ActorId` + `ActorType="User"`.
- **Fail-closed rollback (append-failure → business rollback)** — no direct test; foundation-proven (shared `AddModuleDbContext` transaction, unchanged) and accepted at gate 7. Documented follow-up: a faulting-`IAuditWriter` integration test asserting the catalog row is absent.

## What looks good

1. **Old-value-before-mutation ordering** is correct in every lifecycle handler (`var from = app.Lifecycle;` before `app.Deprecate/Decommission/Reactivate/UnDecommission`) and `AssignApplicationTeamHandler` (`fromTeamId` before `AssignTeam`) — spec §3 decision 3, unit-pinned by `CatalogAuditEntriesTests.LifecycleChanged_CapturesFromToAndSunsetDate`.
2. **The mid-implementation correction** (direct-dispatch DI, not Wolverine) is implemented exactly and documented in spec §3 decision 1; `CatalogEndpointDelegates` threads `IAuditWriter` to each `handler.Handle(...)` with no constructor churn (ADR-0093).
3. **The pre-existing `CrossTenantWriteTests` was repaired, not weakened** — real `AuditWriter` + stub actor on the scope's `AuditDbContext`, preserving the ADR-0090 assertion, with an explanatory comment.
4. **Reactivate's sunsetDate-clearing** is pinned at both unit (`LifecycleChanged_NullSunsetDate_SerializesAsNull`) and integration (`Reactivate_WritesLifecycleChangedAuditRow`) tiers — spec §4's trickiest payload edge.
5. **Hash-chain integrity verified across module-interleaved rows** (`AssertChainLinked` walks the full per-tenant log, Organization + Catalog) — directly exercises design §6's "every wired caller runs inside an `ITenantScope` txn" invariant.
