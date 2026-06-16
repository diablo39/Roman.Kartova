# Deep PR Review — feat/audit-log-foundation

**Branch:** `feat/audit-log-foundation`
**Status:** OPEN (pre-merge gate)
**Reviewer:** deep-review (Claude Opus 4.8), fresh-eyes pass
**Date:** 2026-06-15
**Read against:** spec `2026-06-12-audit-log-foundation-design.md`, plan `2026-06-12-audit-log-foundation-plan.md`, ADR-0018, ADR-0090, ADR-0102, CLAUDE.md §Definition of Done, `mutation-report-surviving.md`.

---

## Overview

This slice ships Phase-1 of the ADR-0018 append-only tamper-evident audit log: a new `Kartova.Audit` module (Domain — canonical serializer, SHA-256 row hasher, `AuditLogEntry` entity, pure chain inspector + result; Infrastructure — `AuditDbContext` with jsonb/bytea mapping, `InitialAuditLog` migration with RLS ENABLE/FORCE + a `tenant_isolation` policy (USING + WITH CHECK) + REVOKE UPDATE/DELETE/TRUNCATE from `kartova_app`/`kartova_bypass_rls`, an in-transaction advisory-locked `AuditWriter`, an `AuditChainVerifier`, `AuditModule`), a SharedKernel `IAuditWriter` + `AuditEntry` port, and host/migrator/arch-test wiring. No business events are wired (deferred to Phase 2 by design). Tests: 41 Domain unit tests plus 9 Testcontainers integration tests (insert-only grants incl. RLS isolation, writer e2e, jsonb round-trip stability, fail-closed via tenant-mismatch RLS rejection, per-tenant chain independence).

The implementation is high-quality and faithful to the spec on every architectural axis. The findings below are confined to (a) two acceptance criteria from spec §7 / plan architecture that are claimed but **not actually covered by a test**, and (b) stale/uncommitted DoD evidence. None compromise the shipped mechanism; they are gaps between claimed and actual verification.

---

## Blocking-class issues

**None.**

The DB-enforced insert-only guarantee (the core ADR-0018 promise) is implemented and tested (`AuditLogGrantsAndRlsTests` asserts 42501 on UPDATE/DELETE/TRUNCATE). Fail-closed, per-tenant RLS isolation, and chain verification are all exercised against a real Postgres. The mechanism is merge-ready. The items below are should-fix verification gaps, not defects in the shipped code.

---

## Should-fix issues

### 1. Per-tenant append serialization (advisory lock) is implemented but has no concurrency test.

- **Evidence:** `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditWriter.cs:36-38` acquires `pg_advisory_xact_lock(...)` to serialize concurrent same-tenant appends. The plan architecture (plan §"Architecture") and spec §3 decision 5 ("`SELECT ... FOR UPDATE`" → realized here as the advisory lock) make this serialization the correctness guarantee against duplicate `seq` / forked chains under concurrency. The integration suite (`AuditWriterTests.cs`, `AuditLogGrantsAndRlsTests.cs`) contains **9 test methods, none concurrent** — every writer test appends sequentially in one scope. `[assembly: DoNotParallelize]` (`Properties/AssemblyInfo.cs:6`) further guarantees nothing concurrent runs.
- **Impact:** The single most subtle correctness property of the writer — that two simultaneous appends for the same tenant cannot both read the same chain head and produce two rows with the same `seq` (or two `seq=N+1` rows chaining off the same `prev_hash`) — is unverified. A regression that drops or weakens the lock (e.g. wrong lock key, lock acquired after the head read) would pass the entire suite. The `UNIQUE (tenant_id, seq)` index is a backstop against the dupe-`seq` case, but not against the forked-`prev_hash` case, and the index backstop would surface only as an opaque 23505 at runtime, not as a tested behavior.
- **Fix:** Add `AuditWriterTests.Concurrent_appends_for_same_tenant_serialize_into_one_unbroken_chain`: open N parallel scopes (e.g. `Task.WhenAll` over 5 writers) each appending one entry for the same tenant, commit all, then assert `VerifyAsync` is intact AND row count == N AND seq is exactly `1..N` with no gaps/dupes. This is the test the prompt's "already-addressed" list claims exists ("concurrent-append lock test") but which is not present in the diff.

### 2. End-to-end DB-tamper detection has no integration test; tamper detection is proven only at the pure-domain layer.

- **Evidence:** Tamper detection (recomputed `row_hash` ≠ stored, or broken `prev_hash` link) is tested only in `AuditChainInspectorTests.cs` (in-memory, reflection-forged rows). Spec §5 / §7 frame the verifier's purpose as detecting tampering of *stored* rows ("silent modification by a compromised DBA or operator", ADR-0018). No integration test mutates a persisted row (e.g. via the `kartova_bypass_rls` connection, which can bypass RLS) and then asserts `VerifyAsync` reports `Broken` at the right `seq`. The prompt's "already-addressed" list claims an "end-to-end DB-tamper-detection test" was added under review-pr; it is not in the diff.
- **Impact:** The full read-back path (jsonb normalization + bytea round-trip + EF materialization → inspector) is only proven to report *intact* (round-trip test), never to correctly report *broken* on a real persisted mutation. A normalization bug that made the recomputed hash silently match a tampered stored value — i.e. a false negative, the worst failure mode for a tamper-evidence store — would not be caught.
- **Fix:** Add `AuditWriterTests.Tampered_persisted_row_is_detected_by_verifier`: append 3 rows + commit; using `Fx.BypassConnectionString`, run an unrestricted `UPDATE audit_log SET data = '{"new_role":"Tampered"}' WHERE seq = 2` (the bypass role retains UPDATE on its own session only if not revoked — note the migration revokes UPDATE from `kartova_bypass_rls` too, so use a superuser/admin connection from the fixture instead, or `SET row_security = off` as table owner); then `VerifyAsync` must return `Intact == false` with `FirstBrokenSeq == 2`. This closes the false-negative gap end-to-end.

### 3. The only mutation report present is stale and uncommitted; it contradicts the claimed mutation score.

- **Evidence:** `mutation-report-surviving.md` (repo root) reports **95.65%, 2 surviving mutants**, and both survivors cite `AuditCanonicalSerializer.cs:43`/`:47` referencing a line `var occurredAtMicros = new DateTimeOffset(occurredAtUtc.Ticks - (occurredAtUtc.Ticks % 10), ...)`. That truncation line **no longer exists** in `AuditCanonicalSerializer.cs` (current file: lines 19/42 use the `"…ffffff…"` format specifier; the truncation was removed in commit `e3909e7` "drop redundant serializer truncation"). The report is therefore stale — generated against a pre-`e3909e7` tree. The file is **untracked** (`git ls-files` does not list it; it is absent from `master...HEAD`). The prompt states "mutation 100% (killed all survivors)", which neither matches the only artifact present (95.65% / 2 survivors) nor a fresh run.
- **Impact:** DoD gate 7 ("document the score and any surviving mutants") cannot be satisfied by citable evidence — the cited artifact describes deleted code and is not in the branch. A reviewer cannot confirm the current mutation score.
- **Fix:** Re-run `/misc:mutation-sentinel` against the current Domain tree, confirm ≥80% (and the claimed 100%), and commit the regenerated `mutation-report-surviving.md` (or record the score in the slice evidence file `docs/superpowers/evidence/.../db-verification.md`). One of the two stale survivors (the truncation-direction arithmetic mutant) maps to a still-relevant test gap — see Missing tests.

---

## Nits

### 1. `tenant_id` is hard-asserted non-empty in `AuditLogEntry.Create`, but the writer can pass `Guid.Empty`.

- **Evidence:** `AuditLogEntry.cs:42` throws if `tenantId == Guid.Empty`. `AuditWriter.cs:33` sets `tenantId = tenant.Id.Value`; `AuditWriterTests.cs:27-28` documents that an unpopulated `ITenantContext` yields `TenantId.Empty` → nil GUID. This is correct defense, but the failure (an `ArgumentException` from the domain rather than a clear "tenant scope not begun" signal) is a slightly indirect diagnostic. Minor; the production transport always populates the context.

### 2. `idx_audit_log_tenant_target` indexes unbounded `text` `target_id`.

- **Evidence:** `AuditLogEntryConfiguration.cs:56` + migration `:38-40`. `target_id` is `text` (spec §4 — deliberately, to accept non-uuid targets). A pathologically long `target_id` could exceed the btree row-size limit (~2704 bytes) and fail the INSERT. Not a Phase-1 risk (all Phase-2 targets are GUIDs/short ids), but worth a note when the taxonomy is defined.

### 3. `AuditModule.Slug` comment references "Catalog's 'catalog'" convention but Phase 1 exposes no route.

- **Evidence:** `AuditModule.cs:30-32`. The `Slug`/`MapEndpoints` are dead surface in Phase 1 (correctly empty). Harmless; the comment is accurate about intent. No action needed beyond awareness that the slug is currently unused.

---

## Missing tests

1. **Concurrency / advisory-lock serialization (spec §3 dec. 5; plan architecture).** No test. Add `Concurrent_appends_for_same_tenant_serialize_into_one_unbroken_chain` (Infrastructure.IntegrationTests, `AuditWriterTests`): N parallel `AppendAsync` for one tenant → assert chain intact, row count == N, `seq` == `1..N` contiguous. (See Should-fix 1.)

2. **End-to-end persisted-row tamper detection (spec §5/§7; ADR-0018).** No test. Add `Tampered_persisted_row_is_detected_by_verifier` (Infrastructure.IntegrationTests, `AuditWriterTests`): commit a chain, mutate a stored row out-of-band (admin/owner connection with `row_security = off`), assert `VerifyAsync` → `Intact == false`, `FirstBrokenSeq` at the mutated seq. (See Should-fix 2.)

3. **Mutation survivor — canonical timestamp truncation direction (`AuditCanonicalSerializer.cs:43` in the stale report; still relevant to the live `ffffff`-format path).** The stale survivor `Ticks - (Ticks % 10)` → `Ticks + (...)` maps, post-refactor, to the writer's truncation at `AuditWriter.cs:52-53`. Add a Domain/unit test (or extend an existing serializer test) that pins the **exact** canonical byte output / hash for a timestamp with sub-microsecond ticks (e.g. `…:00.1234567Z`), asserting the 7th fractional digit is dropped (truncated, not rounded), so a sign/round-direction mutation fails. Re-run mutation after to confirm 0 logic survivors.

---

## What looks good

1. **`WITH CHECK` on `tenant_isolation` is a genuine improvement over the established pattern, and it is what makes the fail-closed test deterministic.** `20260615062248_InitialAuditLog.cs:59-61` adds an explicit `WITH CHECK` clause that the existing Organization migration (`20260423080230_InitialOrganization.cs:37-38`, USING only) omits. `AuditWriterTests.Failed_append_propagates_and_commits_no_row` (`AuditWriterTests.cs:226-258`) then weaponizes it: populating `ITenantContext` with tenant A while the connection GUC is tenant B forces the WITH CHECK to reject the INSERT (42501), proving fail-closed without any test-only schema hacks. Clean, deterministic, and ADR-0090-faithful.

2. **`AuditChainInspector` as a pure, DB-free core (`AuditChainInspector.cs`) with the verifier (`AuditChainVerifier.cs`) as a thin RLS-scoped loader.** This is exactly the testability split the spec §5 asks for: the break-mode logic (non-contiguous seq, prev_hash break, forged row_hash) is unit-tested exhaustively in-memory, and the inspector derives `Intact` from `FirstBrokenSeq` (commit `e92f14a`) so an incoherent "broken-but-intact" result is unrepresentable.

3. **Defensive immutability in `AuditLogEntry.Create`.** `AuditLogEntry.cs:68` deep-copies `prevHash` (`.ToArray()`) so a caller reusing the buffer can't retroactively alter a row's chain linkage, and the `Enum.IsDefined` + `User`-requires-`actorId` guards (`:50-53`) reject malformed actors at construction. The hash is computed in the factory, so a constructed row is always internally consistent.

4. **The advisory-lock choice over `SELECT … FOR UPDATE` is correct for the genesis case and well-documented.** `AuditWriter.cs:35-38` + class doc: `FOR UPDATE` cannot lock a not-yet-existent genesis row, so a per-tenant `pg_advisory_xact_lock` (auto-released at txn end) is the right primitive. The reasoning is captured in the doc comment, not just the spec. (Tested coverage is the gap — see Should-fix 1 — but the design call is right.)

5. **Order-independent jsonb value comparer aligned with the order-independent canonical hash.** `AuditLogEntryConfiguration.cs:38-47` replaced a string-equality comparer with set-equality + XOR-of-entry-hashes, matching the sorted-key canonical serializer so EF change-tracking and the hash agree on "same data regardless of key order" (commit `52facfd`). This is the kind of subtle consistency that prevents false tamper alarms.

---

## Verdict

**Merge-ready as a mechanism — no blocking findings.** The shipped code faithfully implements ADR-0018 Phase 1 and the spec on every architectural axis, with the core insert-only/RLS/fail-closed guarantees tested against a real Postgres. Before merge, close the two verification gaps (concurrency-serialization test; end-to-end persisted-tamper test) and refresh + commit the mutation evidence so DoD gate 7 is citable. These are should-fix because the underlying behavior is implemented and partially proven; they harden the test net around the two properties a tamper-evidence store most needs to never silently regress.
