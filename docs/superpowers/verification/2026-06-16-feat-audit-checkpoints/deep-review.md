# Deep Review — Audit-Chain Checkpoints & Streaming Verification

**Target:** branch `feat/audit-log-foundation` (staged changes) vs `master`
**Status:** OPEN (pre-merge gate)
**Date:** 2026-06-16
**Design context:** [ADR-0105](../../architecture/decisions/ADR-0105-audit-chain-checkpoints-and-external-anchoring.md) (governing), [audit-log-foundation spec](../specs/2026-06-12-audit-log-foundation-design.md), `CLAUDE.md §Definition of Done`
**Reviewed against:** ADR-0105, ADR-0018, ADR-0090, ADR-0099, ADR-0097 (testing), DoD gates.

> Process note: this change has no dedicated `docs/superpowers/` spec/plan — it was driven directly with ADR-0105 as the design of record. ADR-0105 is detailed enough to review against; flagging the workflow deviation as a nit, not a code finding.

## Overview

The slice converts `AuditChainVerifier` from a materialize-all (`ToListAsync`) walk to a streaming `AsAsyncEnumerable` walk (flat memory), extracts the per-row walk into a shared `AuditChainWalker`, and adds Tier-1 audit-chain checkpointing per ADR-0105: an insert-only/RLS `audit_checkpoint` table, an `AuditCheckpointer` that writes a checkpoint only over a verified-intact prefix, a `VerifyFromCheckpointAsync` fast path, and a daily `LeaderElectedPeriodicService` sweep that enumerates tenants via a BYPASSRLS context and checkpoints each through the tenant-scoped path. Tier-2 (external anchoring) is correctly deferred.

## Blocking-class issues

**None.**

(One blocking-class issue was found and resolved during this review cycle: the daily sweep originally discarded the `ChainBroken` outcome, silently swallowing a detected chain break — the highest-severity event a compliance audit log can produce. Now surfaced at `LogCritical` with tenant + `FirstBrokenSeq` + `Reason` — `AuditCheckpointHostedService.cs:54-66` — and covered by `AuditCheckpointHostedServiceTests.Sweep_isolates_a_broken_chain_tenant_and_still_checkpoints_the_healthy_one`.)

## Should-fix issues

**1. `JsonException` → `Broken(0, …)` reports a sentinel seq, not the failing row.**
- **Evidence:** `AuditChainVerifier.cs:78-82`, `AuditCheckpointer.cs:55-59`. Both convert a streamed-row deserialization failure to `Broken(0, …)`; seq 0 is not a real chain position (genesis is seq 1).
- **Impact:** On a genuine jsonb corruption/tamper, the investigator gets no row pointer. Low likelihood (insert-only + canonical string-only `data` per spec §5), so not blocking.
- **Fix:** Track the last successfully-stepped seq in `AuditChainWalker` (expose `NextExpectedSeq`) and report `Broken(nextExpected, …)` in the catch. Accepted as-is for now is defensible (documented sentinel); revisit if a real corruption path appears. *Carried from the original verifier — pre-existing behavior.*

## Nits

**1. No dedicated superpowers spec/plan for the checkpoint work.** Design lives in ADR-0105 only. Acceptable (ADR is thorough), but the CLAUDE.md workflow expects a spec/plan; note it so the absence isn't mistaken for an oversight.

**2. `AdminAuditDbContext` exposes writable `DbSet`s though documented read-only** — `AdminAuditDbContext.cs:18,20`. Consider `ChangeTracker.QueryTrackingBehavior = NoTracking` in the ctor to make the read-only intent structural. Low value at current usage.

**3. Asymmetry vs Organization's `.Admin` project.** The bypass context + hosted service live in `Kartova.Audit.Infrastructure` rather than a separate `.Admin` assembly (Organization split only to dodge an endpoint circular-ref absent here). Fine and documented in `Program.cs:175-178`; noting so the asymmetry is intentional-on-record.

**4. Daily interval is hardcoded** — `AuditCheckpointHostedService.cs:36`. Matches the `ExpireInvitationsHostedService` precedent; one-line change if config-driven cadence is wanted.

## Missing tests

After this cycle's additions, the security-critical negative paths are covered:
- Broken chain → `ChainBroken` + **no checkpoint persisted** — `AuditCheckpointerTests.Create_over_a_broken_chain_returns_ChainBroken_and_writes_no_checkpoint`.
- Broken tail after a checkpoint preserves the prior checkpoint — `Create_with_a_broken_tail_preserves_the_existing_checkpoint`.
- Verify-from-checkpoint tampered tail — `Verify_from_checkpoint_detects_a_tampered_tail_row_after_a_valid_checkpoint`.
- Missing vs mismatch head pre-check branches distinguished — `..._rejects_a_checkpoint_whose_attested_row_is_missing` / `..._whose_hash_differs_from_the_live_row`.
- Sweep isolates a broken-chain tenant — `Sweep_isolates_a_broken_chain_tenant_and_still_checkpoints_the_healthy_one`.

Remaining gaps (low priority, **not** blocking — mutation loop deferred by request):
- **`JsonException` catch path** (`AuditChainVerifier.cs:78`, `AuditCheckpointer.cs:55`) has no test — forcing it needs malformed jsonb written via the bypass role. *Accepted gap; document or add if the seq-reporting fix above lands.*
- **`VerifyFromCheckpointAsync` with ≥2 checkpoints** picking the latest (`AuditChainVerifier.cs:39-43`) — only single-checkpoint paths are exercised. A mutant flipping `OrderByDescending`→`OrderBy` would survive unless paired with a tail tamper between the two checkpoints.

## What looks good

1. **Shared `AuditChainWalker` unifies three call sites** (`AuditChainInspector`, `AuditChainVerifier`, `AuditCheckpointer`) so in-memory and streaming verification cannot diverge — `AuditChainWalker.cs:38-77`. The genesis-vs-checkpoint seeding is a clean constructor distinction, not a special-case branch in the walk loop.
2. **Checkpoint table mirrors `audit_log`'s trust model line-for-line** — `20260616073336_AddAuditCheckpoint.cs:46-66`: `ENABLE`+`FORCE` RLS, `tenant_isolation` USING+WITH CHECK, `REVOKE UPDATE,DELETE,TRUNCATE` from both app + bypass roles. Faithful to ADR-0105's "same trust model as the chain" and ADR-0018's insert-only mandate.
3. **Checkpoints written through the tenant-scoped path, not the bypass context** — `AuditCheckpointHostedService.cs:96-108`. The sweep reads cross-tenant via BYPASSRLS but each INSERT passes RLS `WITH CHECK`, so the maintenance job structurally cannot write a checkpoint for the wrong tenant. Stronger than ADR-0105 strictly required.
4. **A checkpoint is only ever written over a verified-intact prefix** — `AuditCheckpointer.cs:46-52` returns `ChainBroken` before any INSERT. Matches ADR-0105's core invariant and is now regression-guarded.
5. **Streaming with early-exit** — `AuditChainVerifier.cs:73-76` breaks the `await foreach` on first detected break, so a broken chain stops reading rather than draining the whole tail.

## DoD status (cited)

| Gate | Status |
|------|--------|
| 1. Build, `TreatWarningsAsErrors` | ✅ 0 warnings / 0 errors (full `Kartova.slnx`) |
| 2. Per-task spec + quality review | ✅ this cycle (code-reviewer, silent-failure, type-design agents) |
| 3. Slice-boundary review | ✅ this report |
| 4. Unit + arch + integration | ✅ Domain 66, Architecture 69, Audit integration 34, API integration 5 |
| 5. docker compose + real HTTP | ⚠️ N/A for HTTP (no endpoint); DB/RLS/grant semantics covered by Testcontainers integration + migrator-applied schema + API boot |
| 6. `/simplify` | ✅ clean (2 efficiency micro-opts consciously skipped) |
| 7. Mutation loop | ⏭️ **deferred by request** |
| 8. PR-review-toolkit | ✅ specialized agents run against staged diff (no GitHub PR) |
| 9. Deep review | ✅ this report |
