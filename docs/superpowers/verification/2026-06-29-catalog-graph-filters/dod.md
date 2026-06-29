# DoD Ledger — 2026-06-29-catalog-graph-filters

**Slice:** `2026-06-29-catalog-graph-filters` · **Branch:** `feat/catalog-graph-filters` · **HEAD:** `fe8cb2d`
**PR:** #52 (https://github.com/diablo39/Roman.Kartova/pull/52) · **Last updated:** 2026-06-29
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
| 4 Container build (web image CI) | ✅ PASS — `docker build -f web/Dockerfile` green locally; CI authoritative on PR | 2026-06-29 |
| 5 `/simplify` | ✅ PASS — 4 agents; 2 in-scope wins applied (735decd), rest wash/refactor-scope skipped w/ reasons | 2026-06-29 |
| 6 Mutation (conditional) | N/A — frontend-only, no C# Domain/Application change | — |
| 7 `requesting-code-review` (final whole-branch) | ✅ PASS — Merge-ready, 0 Blocking/Should-fix | 2026-06-29 |
| 8 `review-pr` | ✅ PASS — type-design + test-coverage agents; 0 critical/blocking. 2 coverage gaps fixed (fe8cb2d); rest skipped/rejected w/ reasons | 2026-06-29 |
| 9 `deep-review` | ✅ PASS — 0 blocking / 0 should-fix / 4 nits / 0 missing-test. See `deep-review.md` | 2026-06-29 |
| Manual / Playwright (ADR-0084) | ✅ PASS — overlay renders; Kind+Team dim correct (focus-exempt, edge styling, partial cross-team), persists; console clean. Nuance: Escape clears the live filter (react-aria listbox default, consistent app-wide; click-outside preserves) | 2026-06-29 |
| Terminal re-verify (build + suite) | ✅ PASS — clean `npm ci` → typecheck + 672/672 + build, all green post gate-5 edits | 2026-06-29 |
| Pre-push CI mirror (`ci-local.sh`) | ✅ PASS — frontend steps (codegen no-drift, typecheck, test, build) + web image build green; `ci-local.sh frontend`'s `npm ci` collided with the running dev server (host file-lock on lightningcss), re-run manually after stopping it | 2026-06-29 |

## Gate detail

### 1 — Build (web `npm run build` = `tsc -b` + vite; backend unchanged)
**Status:** ✅ PASS
**Evidence:** `npm run build` clean (tsc -b + vite, 0 type errors) locally; CI `Frontend` job green on PR HEAD.
**At:** 2026-06-29

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** Tasks 2–8 each reviewed (spec-compliance + code-quality) by a fresh subagent; all spec ✅ + Approved. Per-task reviewer reports in `.superpowers/sdd/task-N-report.md`; ledger `.superpowers/sdd/progress.md`.
**At:** 2026-06-29

### 3 — Full test suite (web vitest; real-seam N/A — frontend-only)
**Status:** ✅ PASS
**Evidence:** `npm test` → 99 files / 674 tests pass (672 + 2 gate-8 additions). Real-seam tier N/A — no HTTP/auth/DB/middleware change. CI `Frontend` job green on PR HEAD.
**At:** 2026-06-29

### 4 — Container build (web image; no API rebuild / codegen)
**Status:** ✅ PASS
**Evidence:** `docker build -f web/Dockerfile -t kartova/web:ci web` green locally; CI `Container images` check green on PR HEAD (authoritative). No API rebuild / codegen — no endpoint change.
**At:** 2026-06-29

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS
**Evidence:** 4 parallel cleanup agents (reuse/simplification/efficiency/altitude). **Applied (735decd):** dropped redundant `active` guard in `applyGraphFilters`; replaced conditional empty-object spread with a `style` ternary in `layoutGraph`. **Skipped w/ reasons:** useGraphFilters→`useSessionState<T>` generalization + functional-updater callbacks (intentional `useExplorerState` pattern match; re-render cost negligible at ≤150 nodes); test-helper dedup, `teamOptions` memo, memo-merge, `isActive` inline (wash at this scale); post-layout visual-state pass + KIND_OPTIONS-from-canonical (genuine but larger refactors touching pre-existing `selected` handling → follow-up, out of slice scope). 8/8 affected tests + tsc green after edits.
**At:** 2026-06-29 / 735decd

### 6 — Mutation loop
**Status:** N/A — frontend-only diff (no C# Domain/Application logic); Stryker.NET out of scope. Pure TS logic covered by graphFilter/useGraphFilters unit tests.
**Evidence:** —
**At:** —

### 7 — `requesting-code-review` at slice boundary (final whole-branch review)
**Status:** ✅ PASS
**Evidence:** Final whole-branch review (opus) over the code-only branch diff with spec/plan as context. Verdict **Merge-ready** — 0 Blocking, 0 Should-fix; confirmed all integration seams (`useGraph→mergeGraphs→applyGraphFilters→layoutGraph→ReactFlow/EntityGraphNode`, `useGraphFilters`, `GraphFilterControls`), the `dimmed` memo deps (no stale-filter bug), filters surviving expand/collapse + re-root, null-teamId end-to-end, and positions-unaffected-by-filtering. Carry-over minors all triaged accept.
**At:** 2026-06-29

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS
**Evidence:** Ran the two highest-signal lenses beyond gates 7/9 — `type-design-analyzer` + `pr-test-analyzer` — on the PR diff. **0 critical / 0 blocking** from both. **Fixed (fe8cb2d):** 2 new-branch coverage gaps in `useGraphFilters` (render-time focus-key reconcile; throwing-`setItem` keeps in-memory state). **Skipped w/ reasons:** `dimmed?`→required (rejected — `toGraphModel`/mini-graph builds `GraphNodeData` without `dimmed`, so optional is required); MultiSelect mutual-exclusion (already documented by the `selectedKeys` JSDoc; console.warn skipped to avoid a runtime side-effect in a shared base); "all"/Ctrl+A sentinel test (pre-existing branch, not added this slice; jsdom-unreliable); `DimmedSets` named-type extraction, `teamIds` JSDoc, both-endpoints edge assertion (nits). Type ratings: `applyGraphFilters`/`GraphFilterControlsProps` strongest (9s); no type rated as a defect.
**At:** 2026-06-29 / fe8cb2d

### 9 — `deep-review`
**Status:** ✅ PASS
**Evidence:** `./deep-review.md` — branch vs master, cross-checked spec/plan/ADR-0040/0094/0107/0109. 0 blocking, 0 should-fix, 4 nits (Escape-clears accepted; KIND_OPTIONS parallel-decl; dim opacity in two layers; page-test hygiene), 0 missing-test (one drafted finding retracted on verification — page test already covers focus-never-dims at `GraphExplorerPage.test.tsx:175`). 5 specific strengths. Verdict: merge-ready.
**At:** 2026-06-29

### Manual / Playwright verification (ADR-0084)
**Status:** ✅ PASS
**Evidence:** `./evidence/` (01 baseline 8-node graph · 02 Kind=Service → 5 apps dimmed to opacity 0.3, focus + both services full · 03 Team=jjj → cross-team partial dim, focus + jjj service full). Verified in real browser (logged in, seeded an 8-node cross-team graph via API from focus `A App 015`): overlay renders in a React Flow `<Panel>`; controlled react-aria MultiSelect popovers work; Kind & Team dimming correct (focus never dims, edge styling applies, active-count badge "Filters (1)"); filter persists in sessionStorage (`graph-explorer-filters:<focus>`) and across F5. Console clean (0 errors / 0 warnings).
**Known nuance (accepted):** pressing **Escape** while a filter dropdown is open clears that facet's selection — this is react-aria's `ListBox` default (`escapeKeyBehavior="clearSelection"`), is consistent with every other MultiSelect in the app (e.g. FilterBar lifecycle/team), and the common dismissal (click-outside) preserves the selection. `escapeKeyBehavior="none"` was trialed but does not suppress the clear in the Dialog/Popover composition; a real fix means fighting react-aria's keyboard layer in a shared a11y primitive — deferred as not worth the risk for a low-severity nuance. Filtering is a non-destructive view state, trivially re-applied.
**At:** 2026-06-29 / HEAD d5534ca (+ verification seed data)

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ✅ PASS
**Evidence:** after the gate-5 `/simplify` edits (735decd), on a fresh `npm ci`: `npm run typecheck` (tsc -b --noEmit) clean · `npm test` 99 files / 672 tests pass · `npm run build` clean. `npm run codegen` produced no diff.
**At:** 2026-06-29

### Pre-push CI mirror (`scripts/ci-local.sh frontend` + web image)
**Status:** ✅ PASS (with host caveat)
**Evidence:** Ran the CI `frontend` job's steps — `npm run codegen` (no drift), `npm run typecheck`, `npm test` (672/672), `npm run build` — all green on a clean `npm ci`, plus `docker build -f web/Dockerfile -t kartova/web:ci web` (gate-4 web image) green. **Caveat:** the scripted `ci-local.sh frontend` `npm ci` failed once with a Windows `EPERM` unlinking `lightningcss.win32-x64-msvc.node` — the running vite dev server held the native module. Stopped the dev server, reinstalled, re-ran the steps manually. Host-only artifact (CI's clean ubuntu runner is unaffected; no dependency change this slice). CI remains authoritative for the backend/images/helm jobs (all unchanged by this frontend-only slice).
**At:** 2026-06-29
