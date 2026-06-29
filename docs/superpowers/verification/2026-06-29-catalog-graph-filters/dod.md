# DoD Ledger — 2026-06-29-catalog-graph-filters

**Slice:** `2026-06-29-catalog-graph-filters` · **Branch:** `feat/catalog-graph-filters` · **HEAD:** `1d24ba2`
**PR:** <pending> · **Last updated:** 2026-06-29
**Spec:** `docs/superpowers/specs/2026-06-29-catalog-graph-filters-design.md`
**Plan:** `docs/superpowers/plans/2026-06-29-catalog-graph-filters.md`
**Findings telemetry:** `./gate-findings.yaml`

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.

**Slice disposition (frontend-only — see spec §9):** Gate 3 real-seam tier **N/A** (no HTTP/auth/DB/middleware change). Gate 4 = **web image build** only (no API rebuild / codegen — no endpoint change). Gate 6 mutation **N/A** (no C# Domain/Application change). ADR-0084 manual pass **required** (UI slice).

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ⏳ PENDING | — |
| 2 Per-task subagent reviews | ⏳ PENDING | — |
| 3 Full suite (+ real-seam if wiring) | ⏳ PENDING | — |
| 4 Container build (images CI) | ⏳ PENDING | — |
| 5 `/simplify` | ⏳ PENDING | — |
| 6 Mutation (conditional) | N/A — frontend-only, no C# Domain/Application change | — |
| 7 `requesting-code-review` | ⏳ PENDING | — |
| 8 `review-pr` | ⏳ PENDING | — |
| 9 `deep-review` | ⏳ PENDING | — |
| Manual / Playwright (ADR-0084) | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| Pre-push CI mirror (`ci-local.sh`) | ⏳ PENDING | — |

## Gate detail

### 1 — Build (web `npm run build` = `tsc -b` + vite; backend unchanged)
**Status:** ⏳ PENDING
**Evidence:** <command + output excerpt, or CI run URL>
**At:** —

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ⏳ PENDING (per-task, see progress ledger `.superpowers/sdd/progress.md`)
**Evidence:** <task reviewer reports per task>
**At:** —

### 3 — Full test suite (web vitest; real-seam N/A — frontend-only)
**Status:** ⏳ PENDING
**Evidence:** <command + counts, or CI run URL>
**At:** —

### 4 — Container build (web image; no API rebuild / codegen)
**Status:** ⏳ PENDING
**Evidence:** <CI "Container images" check URL>
**At:** —

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING
**Evidence:** <findings summary>
**At:** —

### 6 — Mutation loop
**Status:** N/A — frontend-only diff (no C# Domain/Application logic); Stryker.NET out of scope. Pure TS logic covered by graphFilter/useGraphFilters unit tests.
**Evidence:** —
**At:** —

### 7 — `requesting-code-review` at slice boundary (final whole-branch review)
**Status:** ⏳ PENDING
**Evidence:** <reviewer report>
**At:** —

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING
**Evidence:** <review-pr output>
**At:** —

### 9 — `deep-review`
**Status:** ⏳ PENDING
**Evidence:** <deep-review report>
**At:** —

### Manual / Playwright verification (ADR-0084)
**Status:** ⏳ PENDING
**Evidence:** <screenshots folder / console-clean note>. Needs a seeded multi-node graph (DevSeed has no relationships).
**At:** —

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ⏳ PENDING
**Evidence:** <command + output>
**At:** —

### Pre-push CI mirror (`scripts/ci-local.sh web`)
**Status:** ⏳ PENDING
**Evidence:** <command + result, or CI run URL>
**At:** —
