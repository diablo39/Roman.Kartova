# DoD Ledger — 2026-07-05 Catalog API graph UI (FU-A + FU-A1)

**Slice:** `2026-07-05-catalog-api-graph-ui` · **Branch:** `feat/catalog-api-graph-ui` · **HEAD:** `58dc853`
**PR:** [#59](https://github.com/diablo39/Roman.Kartova/pull/59) · **Last updated:** 2026-07-05
**Spec:** `docs/superpowers/specs/2026-07-05-catalog-api-graph-ui-design.md`
**Plan:** `docs/superpowers/plans/2026-07-05-catalog-api-graph-ui.md`
**Findings telemetry:** `./gate-findings.yaml`

> Frontend-only slice. Backend already shipped `EntityKind.Api` + the 3 edge types + per-type rules + unfiltered list/graph handlers before this branch (PR #58). No C#/migration/contract change.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A (with reason).

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ web build (tsc+vite) 0 errors; .NET solution N/A (no C# change) | 2026-07-05 |
| 2 Per-task subagent reviews | ✅ 6 tasks, each spec ✅ + quality approved (3 fix loops resolved) | 2026-07-05 |
| 3 Full suite (+ real-seam if wiring) | ✅ 721/721 web vitest; real-seam N/A (frontend-only, seam covered by PR #58) | 2026-07-05 |
| 4 Container build (images CI) | ⏳ runs on PR (no Dockerfile/COPY change; web image unaffected) | — |
| 5 `/simplify` | ✅ 2 should-fix applied (nested ternaries → lookups), 1 nit declined (type-safety) `58dc853` | 2026-07-05 |
| 6 Mutation (conditional) | N/A — no Domain/Application C# change (frontend-only) | 2026-07-05 |
| 7 `requesting-code-review` | ✅ whole-branch (opus), no Blocking; fixes `1b8be1c` | 2026-07-05 |
| 8 `review-pr` | ✅ code-reviewer clean; test-analyzer no Important gaps | 2026-07-05 |
| 9 `deep-review` | ✅ no Blocking; should-fixes applied `1b8be1c`/`58dc853`; report `./deep-review.md` | 2026-07-05 |
| Manual / Playwright (ADR-0084) | ✅ 5 flows verified, console clean; evidence `verify-1..5-*.png` | 2026-07-05 |
| Terminal re-verify (build + suite) | ✅ 721/721 + build clean on `58dc853` | 2026-07-05 |
| Pre-push CI mirror (`ci-local.sh`) | ⚠️ flaked (host EPERM) — direct Release build+test green; ubuntu CI validates on PR | 2026-07-05 |

## Gate detail

### 1 — Build
✅ `cd web && npm run build` (tsc -b && vite build) → 0 errors (pre-existing chunk-size warning only). No C# touched → .NET solution build unaffected.

### 2 — Per-task reviews
✅ Tasks 1–6 each reviewed by a fresh subagent (spec + quality): all Spec ✅ + Quality Approved. Fix loops: Task 1 (build-green cross-file `GraphExplorerSidebar` type), Task 4 (api-branch/copy coverage gap), Task 6 (stale comment) — all resolved before task close.

### 3 — Full test suite
✅ `npm run test` → 106 files, **721/721** passed. Real-seam Postgres/JWT N/A: no HTTP/auth/DB/middleware seam touched; backend create/list/graph seams covered by PR #58.

### 4 — Container build
⏳ `images` CI job runs on the PR. No Dockerfile/`COPY` change; web image build unaffected.

### 5 — `/simplify`
✅ Applied: `entityDetailPath` nested ternary → `ENTITY_PATH_SEGMENT` Record; `GraphExplorerSidebar` active-query nested ternary → `{ application, service, api }[kind]` lookup (`58dc853`). Declined nit: `useEntitySearch` per-kind GET branches (openapi-fetch requires literal paths → parameterizing loses type inference).

### 6 — Mutation
N/A — the diff touches no Domain/Application C# logic (pure web slice).

### 7 — `requesting-code-review`
✅ Whole-branch review (opus). No Blocking. Should-fix (wasted outgoing fetch on read-only API page) + missing graphFilter api test + DRY entityLink → applied `1b8be1c`.

### 8 — `review-pr`
✅ Code-reviewer: clean, no findings ≥80 confidence (verified ADR-0084 isRowHeader, read-only variant, no permission sync, toast handling, rules-of-hooks). Test-analyzer: coverage strong, no Important gaps (2 minor edge-polish; parseEntityRef + enabled coverage added in `58dc853`).

### 9 — `deep-review`
✅ No Blocking. Should-fixes: enabled behavior lacked direct test (added useCursorList test `58dc853`); gate-findings.yaml missing (created); parseEntityRef malformed case (added `58dc853`). Nits triaged. Full report: `./deep-review.md`.

### Manual / Playwright (ADR-0084)
✅ Cold-start dev server, logged in `admin@orga.kartova.local`. Verified end-to-end (backend stack live):
1. Api detail read-only Incoming list, empty state — `verify-1-api-detail-readonly.png`
2. App "Add outgoing" → "Provides API for" (Type dropdown correct for app source; picker forced to `api`, searched `/catalog/apis`) → row with "Provides API for" badge → `verify-2-app-provides-api-row.png`
3. Row link → `/catalog/apis/:id`; Api detail Incoming now shows the provider, **no add/delete/actions** (read-only) — `verify-3-api-detail-providers-populated.png`
4. Graph explorer renders the `api` node (kind label "API") + "Provides API for" edge — `verify-4-graph-api-node.png`
5. Node click → sidebar shows API name/description/team + "Open page ↗" → `/catalog/apis/:id` — `verify-5-graph-sidebar-api.png`
Dialog opened in a real browser (react-aria blank-page guard held). Console: 0 errors.

### Terminal re-verify
✅ After fix waves: `npm run test` → **721/721**; `npm run build` → 0 errors, on final commit `58dc853`.

### Pre-push CI mirror
⚠️ `scripts/ci-local.sh frontend` failed twice on the **known Windows host flake**: `npm ci` cannot `unlink` `node_modules/lightningcss-win32-x64-msvc/lightningcss.win32-x64-msvc.node` (EPERM — native `.node` held by AV/handle), documented in project memory. This is the install step, not the wrapped checks. The Release-equivalent checks all pass when run directly: `npm run build` (tsc -b && vite build) → 0 errors; `npm run test` → 721/721. Per CLAUDE.md, ci-local runs on the host (not the ubuntu runner) and can't catch/avoid host flakes — the PR's ubuntu `frontend` job does a clean `npm ci` and is the source of truth. Watch the PR check.
