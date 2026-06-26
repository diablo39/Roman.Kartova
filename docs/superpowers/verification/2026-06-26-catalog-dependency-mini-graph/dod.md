# DoD Ledger — Catalog Dependency Mini-Graph (E-04.F-02.S-01)

**Slice:** `2026-06-26-catalog-dependency-mini-graph` · **Branch:** `feat/catalog-dependency-mini-graph` · **HEAD:** `6b9bc3f`
**PR:** [#46](https://github.com/diablo39/Roman.Kartova/pull/46) · **Last updated:** 2026-06-26
**Spec:** `docs/superpowers/specs/2026-06-26-catalog-dependency-mini-graph-design.md`
**Plan:** `docs/superpowers/plans/2026-06-26-catalog-dependency-mini-graph.md`

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-06-26 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-06-26 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS | 2026-06-26 |
| 4 Container build (images CI) | ✅ PASS | 2026-06-26 |
| 5 `/simplify` | ⏳ PENDING | 2026-06-26 |
| 6 Mutation (conditional) | N/A | 2026-06-26 |
| 7 `requesting-code-review` | ✅ PASS | 2026-06-26 |
| 8 `review-pr` | ⏳ PENDING | 2026-06-26 |
| 9 `deep-review` | ⏳ PENDING | 2026-06-26 |
| Manual / Playwright (ADR-0084) | ✅ PASS | 2026-06-26 |
| Terminal re-verify (build + suite) | ⏳ PENDING | 2026-06-26 |
| Pre-push CI mirror (`ci-local.sh`) | ✅ PASS | 2026-06-26 |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
**Evidence:** CI run [28236067701](https://github.com/diablo39/Roman.Kartova/actions/runs/28236067701) — Frontend (typecheck+build) + Backend checks green.
**At:** PR #46 head, 2026-06-26

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** Spec + quality reviews ran clean for all 5 implementation tasks during the original SDD session (recorded in the `.superpowers/sdd` controller ledger; corroborated by the commit chain `c9a8ec3..1ff557b`). Task 3 fixed a TS2698 spread-cast pre-review.
**At:** PR #46 head, 2026-06-26

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ✅ PASS
**Evidence:** CI run 28236067701 — Frontend (test+typecheck+build) + Backend (arch+unit+integration) green. Real-seam **N/A** — frontend-only slice, no HTTP/auth/DB wiring.
**At:** PR #46 head, 2026-06-26

### 4 — Container build (images CI job)
**Status:** ✅ PASS
**Evidence:** CI run 28236067701 — "Container images (build — Dockerfile/restore gate)" check green (web image restores `@xyflow/react`).
**At:** PR #46 head, 2026-06-26

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING
**Evidence:** Not yet run.
**At:** 2026-06-26

### 6 — Mutation loop (conditional)
**Status:** N/A
**Evidence:** No C# Domain/Application change — frontend-only slice. Mutation gate not applicable.
**At:** 2026-06-26

### 7 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
**Evidence:** Final whole-branch code review (superpowers:requesting-code-review) returned ready-to-merge-with-fixes, no Critical/Important findings; the one fix (untested focused-node style branch) landed in `6b9bc3f`. (A subagent review — not posted to the GitHub PR, hence PR #46 shows 0 reviews.)
**At:** PR #46 head, 2026-06-26

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING
**Evidence:** Not yet run.
**At:** 2026-06-26

### 9 — `deep-review`
**Status:** ⏳ PENDING
**Evidence:** Not yet run.
**At:** 2026-06-26

### Manual / Playwright verification (ADR-0084)
**Status:** ✅ PASS
**Evidence:** Post-merge cold-start Playwright MCP pass against `master` (stack via docker compose, web dev on 5173, KeyCloak login as `admin@orga.kartova.local`):
- Graph renders on an Application detail page — focused node centred + emphasised, dependents left / dependencies right, edges labelled "Depends on", kind labels present. Screenshot: `playwright/minigraph-rendered.png`.
- Neighbour node-click navigates to that entity's detail page (`B App 014` node → `/catalog/applications/294ac406…`).
- Empty state: an Application with no relationships shows "No dependencies yet", no canvas.
- Console clean across graph render + node-click + empty state (0 errors / 0 warnings).
- Known app-level caveat (not graph-related, pre-existing): a cold deep-link/hard-refresh to a detail URL fires `401 /organizations/me/permissions` before the OIDC token attaches and bounces to the catalog list; in-SPA navigation is unaffected. Logged as [#47](https://github.com/diablo39/Roman.Kartova/issues/47).
**At:** master (post-merge of PR #46), 2026-06-26

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ⏳ PENDING
**Evidence:** Pending gates 5/7/8/9.
**At:** 2026-06-26

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ✅ PASS
**Evidence:** CI run 28236067701 green on PR #46 (all 5 jobs) — the runner is the mirror's source of truth.
**At:** PR #46 head, 2026-06-26
