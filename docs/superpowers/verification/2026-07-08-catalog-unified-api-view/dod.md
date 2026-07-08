# DoD Ledger — Unified API view per service (sub-slice A)

**Slice:** `2026-07-08-catalog-unified-api-view` · **Branch:** `feat/catalog-unified-api-view` · **HEAD:** `788859b` (+ this docs/verification commit)
**PR:** <filled on open> · **Last updated:** 2026-07-08
**Spec:** `docs/superpowers/specs/2026-07-08-catalog-unified-api-view-design.md`
**Plan:** `docs/superpowers/plans/2026-07-08-catalog-unified-api-view.md`
**Findings telemetry:** `./gate-findings.yaml`

> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · 🟡 WAIVER · N/A.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-07-08 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-07-08 |
| 3 Full suite (+ real-seam) | ✅ PASS | 2026-07-08 |
| 4 Container build (images CI) | ✅ PASS | 2026-07-08 |
| 5 `/simplify` | ✅ PASS | 2026-07-08 |
| 6 Mutation (conditional) | 🟡 OWNER WAIVER (not green) | 2026-07-08 |
| 7 `requesting-code-review` | ✅ PASS | 2026-07-08 |
| 8 `review-pr` | ✅ PASS | 2026-07-08 |
| 9 `deep-review` | ✅ PASS | 2026-07-08 |
| Manual / Playwright (ADR-0084) | ✅ PASS | 2026-07-08 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-07-08 |
| Pre-push CI mirror (`ci-local.sh`) | ✅ PASS (flakes cleared) | 2026-07-08 |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS — 0 warnings / 0 errors. `dotnet build Kartova.slnx` (Debug per-task) + Release build via `ci-local backend` and the terminal re-verify.
**At:** 788859b / 2026-07-08

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS — every task (1,2,4,5,6) got an implementer + a task-reviewer (spec ✅ + quality Approved). Task 3 (codegen) is machine-generated JSON, verified by `tsc -b` (no hand-authored code to review). Task 5 reviewer nit (hr/panel order) adjudicated non-issue.
**Evidence:** `.superpowers/sdd/progress.md` ledger; task briefs/reports under `.superpowers/sdd/`.

### 3 — Full test suite (unit + arch + integration; real-seam)
**Status:** ✅ PASS. Backend all assemblies green in `ci-local` (Release); the one failure (`Kartova.SharedKernel.Identity.IntegrationTests`, `System.TimeoutException` on `DockerContainer.StartAsync`) was the documented Testcontainers-saturation flake → re-run isolated **Passed! 8/8**. Terminal re-verify on final commit: **Catalog.Tests 204/204, Catalog.IntegrationTests 271/271** (Release, real-seam: real JWT + real Postgres/RLS via `KartovaApiFixtureBase`). Frontend **740/740** (109 files). New real-seam coverage: `GetApiSurfaceTests` (8) — derivation, direct-wins, application-no-derivation, tenant isolation (422), unknown (422), entityKind=api (400), empty id (400), hasSpec=true end-to-end.
**At:** 788859b / 2026-07-08

### 4 — Container build (images CI job)
**Status:** ✅ PASS — `ci-local images` PASS (api + web images build); api image also rebuilt + run for the codegen step.
**At:** 2026-07-08

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS — 4 cleanup agents. Applied: reuse `API_STYLE_LABEL`/`API_STYLES`; `TableSkeleton` loading state; merged 3 source-edge queries → 1 round-trip; batched via-app names (dropped N+1). Skipped (out-of-slice / speculative): entityKind-parse helper extraction, has-spec query helper, dead defensive guards, 422-asymmetry (spec decision).
**Evidence:** commits 306b9e2; `.superpowers/sdd/progress.md` "Gate 5".

### 6 — Mutation loop (conditional)
**Status:** 🟡 OWNER WAIVER (not green) — diff touches Domain/Application logic (normally blocking); owner-waived for this slice. Compensating: mapper unit tests (8) + real-seam integration (8).
**At:** 2026-07-08

### 7 — `requesting-code-review` (final whole-branch, opus)
**Status:** ✅ PASS — verdict ready-to-merge; 0 Critical, 0 Important, 3 Minor. M1 (nondeterministic via) + M2 (hasSpec=true untested) fixed (commit 9f561b7). M3 (per-via N+1) resolved by /simplify batch.
**Evidence:** `.superpowers/sdd/progress.md` "Gate 7".

### 8 — `review-pr` (pr-review-toolkit, 4 lenses)
**Status:** ✅ PASS — silent-failure / type-design / test-analyzer / comment-analyzer. **0 code defects.** Doc/comment accuracy fixes applied (commit 788859b). 3 integration test-depth gaps (multi-app instance-of, empty-surface-at-seam, provide+consume-same-api) deferred to sub-slice B (analyzer: non-blocking). ContainsKey silent-drop = currently unreachable, comment made honest.
**Evidence:** `.superpowers/sdd/progress.md` "Gate 8"; `gate-findings.yaml`.

### 9 — `deep-review` (opus)
**Status:** ✅ PASS — merge-ready, no blockers. Confirmed derivation faithful to ADR-0111 §Decision 3, RLS soundness end-to-end, dedupe determinism, contract/serialization. S1 (422-vs-empty-200 divergence from sibling read endpoints + misattributed spec rationale) → kept 422 (single-entity focus view), corrected spec Decision-11 rationale, surfaced in PR for revisit. N1/N2 nits accepted.
**Evidence:** `.superpowers/sdd/progress.md` "Gate 9".

### Manual / Playwright verification (ADR-0084)
**Status:** ✅ PASS — cold-start dev server (5173) + live API (8080), authenticated (`admin@orga.kartova.local`), in-SPA navigation. **Application detail** (`A App 015`): populated **Provides** table renders — `aaaapi` (REST, v1, Spec badge, Direct), `Playwright Orders API` (GraphQL, v1, Direct); empty **Consumes**; panel placed below the dependency graph, above relationships; no blank-page, `isRowHeader` table renders. **Service detail** (`Graph Filter Demo Svc`): panel mounts, both empty states render. Derived-origin render is test-covered (271 integ + mapper units + gate-9) — not separately screenshotted (dev DB has no `instance-of` topology; headless react-aria dialog seeding deprioritized to avoid flake).
**Evidence:** `./api-surface-empty-panel.png` (Application, populated), `./api-surface-service-panel.png` (Service).
**At:** 788859b / 2026-07-08

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ✅ PASS — after all fixes (final commit 788859b): solution build 0-warnings; Catalog.Tests 204/204 + Catalog.IntegrationTests 271/271 (Release); frontend tsc + vitest 740/740 + vite build.
**At:** 788859b / 2026-07-08

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ✅ PASS (flakes cleared) — `ci-local backend images frontend helm` (stryker skipped per gate-6 waiver): images PASS, helm PASS; backend + frontend initial FAIL were both **environment flakes** (Testcontainers saturation on the Keycloak assembly; npm-ci EPERM from a stale vite dev server holding lightningcss). Both root-caused and cleared — backend re-run isolated 8/8, frontend recovered (killed stale dev server) → tsc + 740/740 + build green.
**Evidence:** `.superpowers/sdd/ci-local.log`, `keycloak-rerun.log`, `fe-verify.log`.
**At:** 788859b / 2026-07-08
