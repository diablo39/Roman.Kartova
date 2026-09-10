# Deep Review — tech-debt TD-001/002/003

**Target:** branch `chore/tech-debt-td-001-002-003` vs `master` (`da17dbb..b483c5b`)
**Spec:** `docs/engineering/tech-debt.md` (TD-001/002/003 acceptance lines) · **ADR:** ADR-0095 (amended 2026-09-10) · **DoD:** CLAUDE.md §Definition of Done
**Date:** 2026-09-10

## Overview

Three cross-cutting tech-debt fixes. Build (warnings-as-errors) green; full backend suite green; FE tsc + 1094 vitest green; `InfrastructureVmSortTests` 18/18 on real Postgres. The null-safe keyset predicate is correct across all four asc/desc × null/non-null boundary cases (independently traced), the cursor format extension is backward compatible, and the byte-identical JSONB partial-index path is untouched. One acceptance gap (TD-003 mixed-key) and a cluster of robustness/test-depth items below.

## Blocking

1. **TD-003 acceptance not fully met — a 400 mixing a mapped + an unmapped key still silently drops the unmapped message.** `web/src/shared/forms/problemDetails.ts:52-60` folds `handled` as a single OR over applied keys, so a payload like `{errors:{displayName:[…], teamId:[…]}}` (teamId is `useState`-managed, not an RHF field) returns `handled=true`; the caller `if (handled) return;` skips the toast and the `teamId` error vanishes — the exact silent no-op TD-003's acceptance says must never happen. Fix: `handled` true only when ≥1 field applied **and** zero keys were left unmapped; a mix returns false so the caller both highlights the mapped field and toasts. Update the test at `problemDetails.test.ts:56-64` (it currently enshrines the swallow).

## Should-fix

2. **TD-002 `is uint` narrowing silently skips a non-uint token with no diagnostic.** `src/Kartova.SharedKernel.AspNetCore/ConcurrencyTokenCapture.cs:56` — the doc advertises "any aggregate … one concurrency token," but a `byte[]`/`int`/`Guid` token resolves the property, reads a non-null value, fails `is uint`, and returns with no hint and no log (never reaches the `catch`). Latent (both live tokens are `uint`). Log a warning on the else branch; align the class doc to the `uint`/xmin shape.
3. **`ex.Data["currentVersion"]` is a stringly-typed cross-file handshake.** Written in `ConcurrencyTokenCapture.cs:60`, read in `ConcurrencyConflictExceptionHandler.cs:46`; a rename on one side compiles clean and silently drops the hint. Hoist to a shared `internal const string`.
4. **`zodFieldPaths` runs untrapped on the 400 error path.** `web/src/shared/forms/zodFieldPaths.ts` — defensive + memoized, so it won't throw in practice, but an unhandled throw during error handling would mask the original error. Wrap the walk to fail safe (return empty set → all keys unmapped → toast).
5. **`SortSpec.IsNullable` defaults to the unsafe value with no enforcement.** `src/Kartova.SharedKernel/Pagination/SortSpec.cs:32` — the doc itself notes a `false` spec over a runtime-nullable key "reintroduces the silent-truncation bug"; nothing (type, arch test, EF metadata) catches it. **Design decision for the owner:** make `IsNullable` a required positional record parameter (no default → every SortSpec states it; ~mechanical one-time churn across all sort specs), or add an arch test that fails a nullable-EF-property spec with `IsNullable=false`. Flagged independently by the altitude + type-design lenses as the top item. Not applied pending the owner's call (public shared type, broad).

## Nits

6. `CursorCodec.cs` — the `CursorNullSortValue` doc justifies the sentinel by "distinct from empty string," but Decode already rejects JSON null, so plain `object?` would be unambiguous too; the sentinel's real merit is keeping `SortValue` non-null (a visible, pattern-matchable state). Keep the sentinel; fix the rationale. Optionally add a `n:true ⟹ s absent` decode guard.
7. `InfrastructureVmSortTests.cs` (`ListVms_cursor_is_stable_...` docstring) — "a page boundary necessarily falls inside the trailing null block" reads as if the page-1 boundary is the null one; it's a *later* boundary. Reword.

## Missing tests

8. **DESC null-boundary never runs on real Postgres.** Both integration boundary tests use `sortOrder=asc` (`InfrastructureVmSortTests.cs` via `AssertNullProviderBoundaryStableAsync`); the desc/NULLS-FIRST predicate + `OrderByDescending(nullFlag)` are proven only on SQLite, defeating the portability point of the explicit null-flag. Add a desc null-provider boundary integration assertion.
9. **No dialog test exercises the TD-003 regression.** All dialog "fallback-to-toast" tests use flat problems (no `errors` map). Add a dialog test (e.g. EditVmDialog) sending `errors` with (a) an all-unmapped key and (b) a mixed mapped+unmapped payload, asserting a toast fires and the dialog stays open — this also guards against a wrong-schema wiring among the 14 callers.
10. *(Optional)* Direct `ConcurrencyTokenCapture` unit test for the metadata branches (multi-token → swallowed, no-token → no hint, non-uint → no hint). Deferred: both real token paths are integration-covered with value assertions; the misconfig branches are best-effort-by-design.

## What looks good

- Null-safe keyset predicate correctness across all four boundary cases, with the id-tiebreaker direction preserved (`QueryablePagingExtensions.cs` `ApplyKeysetFilter`). Independently traced true.
- Non-nullable path guarded byte-for-byte (`if (!sort.IsNullable)`), so the VM JSONB selectors' partial-index EXPLAIN match is preserved (`VmSortSpecs.cs` unchanged; `InfrastructureVmSortTests` index tests still pass).
- TD-002 metadata resolution via `IProperty.IsConcurrencyToken` is the right general mechanism — the model the other two items aspire to; both Application + VM paths wired incl. the manual `ILogger` pass in `CatalogEndpointDelegates.EditApplicationAsync`.
- FE `knownFields` is derived from the exact schema object passed to `zodResolver`, so it cannot drift from the RHF-registered field set (all 14 callers).
- Cursor backward compatibility: old cursors omit `n` → decode identically; `""` vs NULL boundaries are distinct (`CursorCodecTests`).

## DoD status

Gates 1 (build/WAE) ✅ · 3 (full suite incl. real-seam) ✅ · 4 (container) N/A · 5 (/simplify) ✅ · 6 (code-review) ✅ · 7 (review-pr, 4 lenses) ✅ · 8 (deep-review) this. Remaining: 9 (visual/API) · 10 (CI on PR). Blocking #1 + should-fix cluster to be applied before final re-verify.
