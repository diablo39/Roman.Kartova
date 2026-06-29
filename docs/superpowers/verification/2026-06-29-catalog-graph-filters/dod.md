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
| 1 Build (web `npm run build`) | ✅ PASS — tsc -b + vite clean | 2026-06-29 |
| 2 Per-task subagent reviews | ✅ PASS — 7/7 tasks spec✅+Approved | 2026-06-29 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS — 672/672 (real-seam N/A, frontend-only) | 2026-06-29 |
| 4 Container build (web image CI) | ⏳ PENDING — CI authoritative | — |
| 5 `/simplify` | ⏳ PENDING | — |
| 6 Mutation (conditional) | N/A — frontend-only, no C# Domain/Application change | — |
| 7 `requesting-code-review` (final whole-branch) | ✅ PASS — Merge-ready, 0 Blocking/Should-fix | 2026-06-29 |
| 8 `review-pr` | ⏳ PENDING — on PR | — |
| 9 `deep-review` | ⏳ PENDING | — |
| Manual / Playwright (ADR-0084) | ✅ PASS — overlay renders; Kind+Team dim correct (focus-exempt, edge styling, partial cross-team), persists; console clean. Nuance: Escape clears the live filter (react-aria listbox default, consistent app-wide; click-outside preserves) | 2026-06-29 |
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
**Status:** ✅ PASS
**Evidence:** `./evidence/` (01 baseline 8-node graph · 02 Kind=Service → 5 apps dimmed to opacity 0.3, focus + both services full · 03 Team=jjj → cross-team partial dim, focus + jjj service full). Verified in real browser (logged in, seeded an 8-node cross-team graph via API from focus `A App 015`): overlay renders in a React Flow `<Panel>`; controlled react-aria MultiSelect popovers work; Kind & Team dimming correct (focus never dims, edge styling applies, active-count badge "Filters (1)"); filter persists in sessionStorage (`graph-explorer-filters:<focus>`) and across F5. Console clean (0 errors / 0 warnings).
**Known nuance (accepted):** pressing **Escape** while a filter dropdown is open clears that facet's selection — this is react-aria's `ListBox` default (`escapeKeyBehavior="clearSelection"`), is consistent with every other MultiSelect in the app (e.g. FilterBar lifecycle/team), and the common dismissal (click-outside) preserves the selection. `escapeKeyBehavior="none"` was trialed but does not suppress the clear in the Dialog/Popover composition; a real fix means fighting react-aria's keyboard layer in a shared a11y primitive — deferred as not worth the risk for a low-severity nuance. Filtering is a non-destructive view state, trivially re-applied.
**At:** 2026-06-29 / HEAD d5534ca (+ verification seed data)

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ⏳ PENDING
**Evidence:** <command + output>
**At:** —

### Pre-push CI mirror (`scripts/ci-local.sh web`)
**Status:** ⏳ PENDING
**Evidence:** <command + result, or CI run URL>
**At:** —
