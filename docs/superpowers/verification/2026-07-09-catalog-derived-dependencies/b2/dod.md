# DoD Ledger — Derived service↔service depends-on (sub-slice B2)

**Slice:** `2026-07-09-catalog-derived-dependencies` (B2) · **Branch:** `feat/catalog-derived-dependencies-b2` · **HEAD:** `fab30c5`
**PR:** [#66](https://github.com/diablo39/Roman.Kartova/pull/66) · **Last updated:** 2026-07-10
**Spec:** `docs/superpowers/specs/2026-07-09-catalog-derived-service-dependencies-design.md`
**Plan:** `docs/superpowers/plans/2026-07-09-catalog-derived-dependencies-b2.md`
**Findings telemetry:** `./gate-findings.yaml`

> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A (reason required).

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-07-09 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-07-09 |
| 3 Full suite (+ real-seam) | ✅ PASS | 2026-07-09 |
| 4 Container build (images CI) | ✅ PASS | 2026-07-09 |
| 5 `/simplify` | ✅ PASS | 2026-07-09 |
| 6 Mutation (conditional → should-do) | N/A (skip, reason) | 2026-07-09 |
| 7 `requesting-code-review` | ✅ PASS | 2026-07-09 |
| 8 `review-pr` | ✅ PASS | 2026-07-09 |
| 9 `deep-review` | ✅ PASS | 2026-07-09 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-07-09 |
| 10 Visual / API verification | ✅ PASS | 2026-07-09 |
| 11 CI green on PR (`ci-local.sh` pre-push) | ✅ PASS | 2026-07-10 |

## Gate detail

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS. Every implementation task (1,2,3,4,6,7) got a spec+quality reviewer; all Approved. T5 (OpenAPI regen) machine-generated additive → no per-task review (final review covers it). T8 docs → no per-task review. Two Minor findings recorded in `gate-findings.yaml` for final triage (T7 self-edge test weak; T7 no service-gating regression test).
**At:** commits 6247a10 · 35f84da · 3802c15 · 7eb4250 · d11ae7a · 3e7e55f

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS — `dotnet build Kartova.slnx` → Build succeeded, 0 Warning(s), 0 Error(s). **At:** fab30c5

### 3 — Full suite (+ real-seam)
**Status:** ✅ PASS — `dotnet test Kartova.slnx --no-build` all assemblies green, 0 failures. Catalog.IntegrationTests 283/283 (incl. new 7 real-seam derived-dependencies + B1), ArchitectureTests 69/69, Catalog.Tests 216/216. Real-seam = real JWT + Postgres/RLS. No flake this run. **At:** fab30c5

### 4 — Container build (images CI)
**Status:** ✅ PASS — `docker compose build` → `kartova/api:dev`, `kartova/migrator:dev`, `kartova/web:dev` all Built. Web image codegen fell back to the committed `openapi-snapshot.json` (expected — no live API in build container) and built successfully, confirming the snapshot carries `/derived-dependencies`. No Dockerfile/COPY change in this slice. **At:** 2e3dd3d

### 5 — `/simplify`
**Status:** ✅ PASS — 4 cleanup agents (reuse/simplification/efficiency/altitude). Applied (commit 8939a6c): shared deduping `derivedViaLabel` used by both mini-graph + B1 `graphMerge` (fixes latent "via X +1" same-API label bug), `toNeighbour` mapper, shared `DerivedTableHeader`. Skipped (documented): per-id `lookup.Find` loop (no batch API; mirrors GraphTraversalHandler deferral), whole-tenant derivation load (deferral, already commented), moving dashed CSS into pure model (styling belongs in view). Build clean + 759 web tests. **At:** 8939a6c

### 6 — Mutation (conditional → should-do)
**Status:** N/A — SKIP with reason. Gate 6 is blocking only when the diff touches **Domain/Application logic**. B2's Application change is a **logicless query record** (`GetDerivedDependenciesQuery`); the derivation core `DerivedDependencies.Compute` is **unchanged** and already B1-mutation-covered (~100%, B1 ledger). B2's new logic is Infrastructure glue (loader fetch, provenance name-join, source/target split) now covered by **9 strong-oracle real-seam tests** (direction split, multi-neighbour both directions, TeamId, provenance direct+via-app, explicit-wins, 400, 422 unknown/cross-tenant/wrong-kind). A full Stryker-over-integration run (Docker, slow) has low marginal value here → not run, per the conditional-gate "skip with noted reason" rule.

### 7 — `requesting-code-review` (whole branch)
**Status:** ✅ PASS (opus, whole-branch). No Blocking. 1 Should-fix (whole-tenant compute — accepted deferral per spec §11, now commented). Findings addressed via commit 796cac1 (handler deferral note + mini-graph service-gating regression test) + 8939a6c (label dedup). Remaining Minors/nits triaged: TeamId carried-but-unrendered (harmless graph parity), legend always shown (cosmetic), 403 integration test (minor — ≥1 happy+≥1 negative already met). **At:** fab30c5 (reviewed) → fixes 796cac1, 8939a6c

### 8 — `review-pr` (pr-review-toolkit agents)
**Status:** ✅ PASS. code-reviewer: clean. silent-failure-hunter: mini-graph swallows `derivedQuery.isError` (CONFIRMED — same as gate 9); handler `ToItem` blank-name fallback undocumented (CONFIRMED); 2 PLAUSIBLE pre-existing B1 fallbacks (skipped — out of slice scope). pr-test-analyzer: missing wrong-kind-id 422 test, no >1-item list test, TeamId never asserted, isLoading skeleton untested (all folded into fix). → fixes below. **At:** 8939a6c

### 9 — `deep-review`
**Status:** ✅ PASS (opus, `./deep-review.md`). 0 blocking · 1 should-fix · 5 nits · 3 missing-test · 5 good. Should-fix: mini-graph swallows `derivedQuery.isError` (degrades to persisted-only silently; sibling section surfaces it). → folded into gate-8+9 fix pass. **At:** 8939a6c

### Terminal re-verify (build + suite)
**Status:** ✅ PASS — after gate 5/7/8/9 fixes (796cac1, 8939a6c, 2e3dd3d): `dotnet build Kartova.slnx` 0W/0E; `dotnet test Kartova.slnx --no-build` all assemblies green, 0 failures (Catalog.IntegrationTests 285/285, ArchitectureTests 69/69). web `npm run build` + vitest green (from gate-8/9 fix run). **At:** 2e3dd3d

### 10 — Visual / API verification (ADR-0084)
**Status:** ✅ PASS — live stack (`docker compose up`), real OIDC login (admin@orga), seeded derived topology (S consumes API ← T instance-of App provides API).
- **API** (`./gate10-api.md`): `GET /derived-dependencies?entityId={consumer}` → `dependencies[0]` = provider T with provenance `{apiName: "B2 Verify Orders API", viaApplicationDisplayName: "B2 Verify Provider App"}`; mirrored call on T → `dependents` = S. Live real-JWT+Postgres/RLS.
- **Visual** (`./gate10-service-detail-derived.png`): service-detail `Derived dependencies` section renders — Dependencies row "B2 Verify Provider" · Derived badge · "via B2 Verify Orders API · B2 Verify Provider App" (linked); Dependents empty state. Mini-graph shows the **dashed** derived edge labeled "via B2 Verify Orders API" + "— explicit / - - derived" legend. **0 console errors.** "Add relationship" dialog opens with the page intact (ADR-0084 `isRowHeader` guard holds through a heavier modal render — no blank-page). **At:** 2e3dd3d

### 11 — CI green on PR
**Status:** ✅ PASS — PR [#66](https://github.com/diablo39/Roman.Kartova/pull/66), run 29072918415. All 5 jobs green: **Backend (arch + unit + integration)**, **Container images**, **Frontend (test + typecheck + build)**, **Helm (lint + template)**, **Stryker config drift**. Pre-push `ci-local.sh` (Release) mirror: build + backend-unit + frontend + helm + stryker all PASS locally; Release backend-integration hung locally (detached-process/Docker overnight flake — same tests green in Debug at gate 3 + terminal re-verify) and passed authoritatively on the CI runner. **At:** 3e16328
