# DoD Ledger — Unified API view per service (sub-slice A)

**Slice:** `2026-07-08-catalog-unified-api-view` · **Branch:** `feat/catalog-unified-api-view` · **HEAD:** `3dce09d`
**PR:** <#NN / url> · **Last updated:** 2026-07-08
**Spec:** `docs/superpowers/specs/2026-07-08-catalog-unified-api-view-design.md`
**Plan:** `docs/superpowers/plans/2026-07-08-catalog-unified-api-view.md`
**Findings telemetry:** `./gate-findings.yaml` — per-gate issues × severity × real/delusion (copy from `templates/gate-findings-template.yaml`)

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.
> This table records each gate's **status**; what each gate **found** (and whether it was real) goes in `gate-findings.yaml`.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ⏳ PENDING | — |
| 2 Per-task subagent reviews | ⏳ PENDING | — |
| 3 Full suite (+ real-seam if wiring) | ⏳ PENDING | — |
| 4 Container build (images CI) | ⏳ PENDING | — |
| 5 `/simplify` | ⏳ PENDING | — |
| 6 Mutation (conditional) | 🟡 OWNER WAIVER (not green) | 2026-07-08 |
| 7 `requesting-code-review` | ⏳ PENDING | — |
| 8 `review-pr` | ⏳ PENDING | — |
| 9 `deep-review` | ⏳ PENDING | — |
| Manual / Playwright (ADR-0084) | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| Pre-push CI mirror (`ci-local.sh`) | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ⏳ PENDING
**Evidence:** <command + output excerpt, or CI run URL>
**At:** <commit / date>

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ⏳ PENDING
**Evidence:** <subagent ids / linked report files>
**At:** <commit / date>

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ⏳ PENDING
**Evidence:** <command + counts, or CI run URL. Note real-seam N/A with reason if frontend-only>
**At:** <commit / date>

### 4 — Container build (images CI job)
**Status:** ⏳ PENDING
**Evidence:** <CI "Container images" check URL>
**At:** <commit / date>

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING
**Evidence:** <link to simplify.md / findings summary>
**At:** <commit / date>

### 6 — Mutation loop (conditional: Domain/Application changes only)
**Status:** 🟡 OWNER WAIVER (not green)
**Evidence:** owner-waived for this slice 2026-07-08
**At:** 2026-07-08

### 7 — `requesting-code-review` at slice boundary
**Status:** ⏳ PENDING
**Evidence:** <link to requesting-code-review.md / findings>
**At:** <commit / date>

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING
**Evidence:** <link to review-pr.md / PR review>
**At:** <commit / date>

### 9 — `deep-review`
**Status:** ⏳ PENDING
**Evidence:** <link to deep-review.md>
**At:** <commit / date>

### Manual / Playwright verification (ADR-0084)
**Status:** ⏳ PENDING
**Evidence:** <screenshots folder / console-clean note, or N/A reason (no UI change)>
**At:** <commit / date>

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ⏳ PENDING
**Evidence:** <command + output / CI run URL>
**At:** <commit / date>

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ⏳ PENDING
**Evidence:** <command + result, or CI run URL (the runner is the mirror's source of truth)>
**At:** <commit / date>
