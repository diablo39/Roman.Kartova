# DoD Ledger — Catalog Graph Explorer: detail sidebar + directional expand + local state

**Slice:** `2026-06-27-catalog-graph-explorer-sidebar` · **Branch:** `feat/catalog-graph-explorer-sidebar` · **HEAD:** rebased onto master `0c152d6` (#50 squash-merged) on 2026-06-27 — per-gate SHAs below are pre-rebase/historical
**PR:** [#51](https://github.com/diablo39/Roman.Kartova/pull/51) · **Last updated:** 2026-06-27
**Spec:** `docs/superpowers/specs/2026-06-27-catalog-graph-explorer-sidebar-design.md`
**Plan:** `docs/superpowers/plans/2026-06-27-catalog-graph-explorer-sidebar.md`
**Findings telemetry:** `./gate-findings.yaml`

> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · ➖ DEFERRED · N/A.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (lint + tsc -b + vite) | ✅ PASS | 2026-06-27 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-06-27 |
| 3 Full suite (frontend; real-seam N/A) | ✅ PASS | 2026-06-27 |
| 4 Container build (web image) | ✅ PASS (CI) | 2026-06-27 |
| 5 `/simplify` | ➖ DEFERRED (proportionate) | 2026-06-27 |
| 6 Mutation | N/A (frontend-only, no C# change) | 2026-06-27 |
| 7 `requesting-code-review` (whole-branch) | ✅ PASS | 2026-06-27 |
| 8 `review-pr` | ➖ DEFERRED (proportionate) | 2026-06-27 |
| 9 `deep-review` | ➖ DEFERRED (covered by gate 7) | 2026-06-27 |
| Manual / Playwright (ADR-0084) | ✅ PASS | 2026-06-27 |
| Terminal re-verify | ✅ PASS | 2026-06-27 |
| Pre-push CI mirror | ✅ PASS (CI run 28283746962) | 2026-06-27 |

## Gate detail

### 1 — Build (lint + tsc -b + vite)
**Status:** ✅ PASS — `npm run lint` exit 0 (0 errors/0 warnings, controller-verified); `tsc -b` 0 errors; `npm run build` (vite) green.
**At:** b1f7a0f / 2026-06-27

### 2 — Per-task subagent reviews
**Status:** ✅ PASS — 6 tasks (1 scaffold + 5 code), each a fresh implementer + two-stage (spec + quality) reviewer; all spec ✅ + Approved. Every commit controller-verified on HEAD + CRLF-clean. Findings (incl. real/delusion verdicts) in `gate-findings.yaml`. Controller independently caught 9 vitest-masked `tsc` errors across Tasks 3–4 and dispatched a fix (965f777).
**At:** d8e075d..b1f7a0f / 2026-06-27

### 3 — Full test suite
**Status:** ✅ PASS — frontend vitest 654/654 (96 files), tsc 0 errors, lint clean. **Real-seam addition N/A** — frontend-only, no HTTP/auth/DB/middleware wiring (consumes the existing `/catalog/graph` endpoint, already real-seam-covered). Backend unchanged. PR CI confirms the full backend + frontend on a clean runner.
**At:** b1f7a0f / 2026-06-27

### 4 — Container build (web image)
**Status:** ✅ PASS — PR #51 CI `images` job green (run 28283746962).
**At:** b1f7a0f / 2026-06-27

### 5 — `/simplify`
**Status:** ➖ DEFERRED (proportionate) — code-quality/simplification covered by the 6 per-task quality reviews + the opus whole-branch review (gate 7). Re-runnable on demand.
**At:** 2026-06-27

### 6 — Mutation loop
**Status:** N/A — frontend-only slice; no C# Domain/Application change. (Stryker is backend-scoped.)
**At:** 2026-06-27

### 7 — `requesting-code-review` (whole-branch, opus)
**Status:** ✅ PASS — opus whole-branch review (1689e58..90cff0d) against spec/plan. Verdict "merge with fixes", 0 Critical. Findings: 1 Important (transitive orphan expand entries on collapse) → documented as an accepted flat-set limitation + pinned by a test (3d45d2a); minors triaged. The "missing focusId prop" finding was adjudicated a delusion (plan drops it both sides). See `gate-findings.yaml`.
**At:** 90cff0d → fixes 3d45d2a/b1f7a0f / 2026-06-27

### 8 — `review-pr`
**Status:** ➖ DEFERRED (proportionate) — overlaps the opus whole-branch review on the same diff + the per-task reviews; PR CI is the independent automated gate.
**At:** 2026-06-27

### 9 — `deep-review`
**Status:** ➖ DEFERRED — the opus whole-branch review (gate 7) ran against the full diff with spec/plan context + a fixed-schema verdict, i.e. functionally the deep review. Re-runnable on the PR.
**At:** 2026-06-27

### Manual / Playwright verification (ADR-0084)
**Status:** ✅ PASS — cold-started dev server; deep-linked `/graph?focus=application:891f99c8…` → bounced to Keycloak → signed in → **restored to the explorer via OIDC `state`** (deep-link survives re-auth). Graph rendered (depth-2: F App 010 focus + A App 041 + A App 119 + Service 1, both edge directions); nodes carry no inline link (moved to sidebar). **Click A App 041 → right sidebar** with real entity data (Application, "depth 1 from focus" client-BFS, Lifecycle: active, description, Team link) + actions (Expand dependencies/dependents, Set as focus, Open page). **Expand dependencies → "Collapse dependencies"** toggle (directional, state persisted). **Full page reload → sidebar + Collapse state restored from sessionStorage** (no Keycloak bounce — authed from sessionStorage) — the faithful proxy for the token-expiry re-auth (an SPA unload+remount; sessionStorage persists identically). **Console: 0 errors / 0 warnings.** Screenshot: `./playwright/explorer-sidebar-restored.png`.
**At:** b1f7a0f / 2026-06-27

### Terminal re-verify
**Status:** ✅ PASS — after the final-review + lint fixes: `npm run lint` exit 0, `tsc -b` 0 errors, full vitest 654/654.
**At:** b1f7a0f / 2026-06-27

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ✅ PASS — PR #51 CI run 28283746962: all 5 jobs success (Frontend, Container images, Backend, Helm, Stryker drift).
**At:** b1f7a0f / 2026-06-27

### Note — pre-existing lint debt touched
`web/src/app/providers.tsx` (untouched by this slice; from PR #49 on master) fails the newer `react-hooks/refs` rule (a recent `eslint-plugin-react-hooks` bump flags the intentional PR-#47 ref-during-render pattern). Master's lint is currently red there. Resolved here with a documented `// eslint-disable-next-line react-hooks/refs` (moving to an effect would reintroduce the #47 stale-token 401 race) so this slice's lint is green. Flagged for a separate decision — see PR body.
