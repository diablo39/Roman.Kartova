# DoD Ledger — Fix nightly-red E2E: system-list-surface member seed

**Slice:** `2026-09-17-e2e-system-list-surface-fix` · **Branch:** `master` · **HEAD:** `<pending commit>`
**PR:** direct-to-local-master · **Last updated:** 2026-09-17
**Spec/Plan:** none — bounded test fix (systematic-debugging; root cause below)

> DoD from `CLAUDE.md`. Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A · WAIVER.

## Root cause (systematic-debugging)

Nightly E2E red (run `35178475298`, deterministic 3/3 retries) on `system-list-surface.spec.ts:100` — `expect(row filtered by systemName).toBeVisible()` timed out. **Not** the CSP/auth work: the nightly ran on `4b5dc521` (pre-CSP docs commit). Cause = **test↔seed contract mismatch from PR #93 (TD-006)**: DevSeed seeds exactly one System ("Payments Platform") deliberately **memberless**, but the spec reads that (only) System and requires it to have ≥1 assigned application to surface a filtered row. No `PartOf` was ever seeded. Process gap: #93 changed System seeding (a flow this spec traverses) but E2E is nightly-only → merged red (the CLAUDE.md E2E-impact-trigger scenario).

## Fix (Option A — approved)

The spec now **supplies its own membership**: new `assignApplicationToSystem(appId, systemId)` fixture in `e2e/fixtures/db.ts` (mirrors `insertDriftEdge` — RLS-bypass insert of a `PartOf` app→System edge + cleanup fn; clears any prior PartOf for determinism). The spec captures a real application id from the list, seeds the edge before filtering, asserts the column+filter agreement, and cleans up in `finally`. Decoupled from seed member-state; honors ADR-0111 (`PartOf` = the membership edge).

**Diff:** `e2e/fixtures/db.ts` (+helper) · `e2e/tests/system-list-surface.spec.ts` (capture app id, seed, try/finally). E2E-only; no product code.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | N/A | 2026-09-17 |
| 2 Per-task subagent review | ✅ PASS | 2026-09-17 |
| 3 Full suite (unit/arch/integration) | N/A | 2026-09-17 |
| 4 Container build | N/A | 2026-09-17 |
| 5 `/simplify` | N/A | 2026-09-17 |
| 6 `requesting-code-review` | WAIVER (owner, 2026-09-17) | 2026-09-17 |
| 7 `review-pr` | WAIVER (owner, 2026-09-17) | 2026-09-17 |
| 8 `deep-review` | ✅ PASS | 2026-09-17 |
| 9 E2E on running stack (the fix's own gate) | ✅ PASS | 2026-09-17 |
| 10 CI green (nightly / dispatch) | ✅ PASS | 2026-09-17 |

## Gate detail

### 1 / 3 / 4 / 5 — N/A
Diff is **E2E test code only** (`e2e/**`): no C# (build/arch/integration unaffected — gate 1/3), no Dockerfile/restore surface (gate 4), no product logic to simplify (gate 5). The meaningful verification for a test fix is running it (gate 9).

### 2 — Per-task subagent review
**Status:** ✅ PASS — `typescript-code-reviewer`: **clean, no findings**. Cross-checked the SQL column order against `EfRelationshipConfiguration.cs`, the `source=Application/target=System` semantics against the production `SetComponentSystemHandler` (`Relationship.CreateManual(component, System, PartOf)`), the pre-delete scope against the `ux_relationships_one_system` partial-unique-index migration, and confirmed the read-side (`IsMemberOfAnySystem` / `SystemsForComponentsAsync` / filter EXISTS) keys off the same shape. Determinism confirmed: `displayName asc` default, `workers: 1`, no other spec registers apps; pre-delete self-heals stale edges. `try/finally` + `client.end()` resource handling correct.

### 6 / 7 — requesting-code-review / review-pr
**Status:** WAIVER (owner, 2026-09-17) — E2E-test-only fix; gate 2 (thorough, cross-checked against production) + gate 8 lensed the diff. Waiver, not green.

### 8 — deep-review
**Status:** ✅ PASS — reviewed the diff. `assignApplicationToSystem` matches the `insertDriftEdge` convention (RLS-bypass role, parameterized, `client.end()` in `finally`, cleanup deletes exactly the inserted id). Pre-delete of prior `PartOf` makes it deterministic and honors the at-most-one invariant (ADR-0111). "First application" is stable under the list's `displayName asc` default; assigning it then filtering keeps the test's real purpose (column render + `?systemId=` round-trip). `try/finally` runs cleanup even if an assertion throws. No Blocking/Should-fix.

### 9 — E2E on the running stack
**Status:** ✅ PASS — `e2e/run.sh system-list-surface.spec.ts` against the freshly-built compose stack (real KeyCloak + API + web + Postgres): **1 passed (8.7s)**. Confirms the seeded `PartOf` edge surfaces the app under the System column + `systemId` filter, column↔filter agree (no em-dash rows), clean console. Also exercised the current web image (CSP Report-Only) end-to-end without issue.

### 10 — CI green (nightly)
**Status:** ✅ PASS — pushed to `origin/master` (`6e9ab44`); E2E `workflow_dispatch` run [35202881345](https://github.com/diablo39/Roman.Kartova/actions/runs/35202881345) = **completed / success**, clearing the nightly-red from run 35178475298. (Push CI `ci.yml` also green on the same head: backend/frontend/images.)
