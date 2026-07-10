# DoD Ledger — Catalog: Visual impact analysis on the graph explorer

**Slice:** `2026-07-10-catalog-graph-impact-analysis` · **Branch:** `feat/catalog-graph-impact-analysis` · **HEAD:** `57b7d5b`
**PR:** #TBD · **Last updated:** 2026-07-10
**Spec:** `docs/superpowers/specs/2026-07-10-catalog-graph-impact-analysis-design.md`
**Plan:** `docs/superpowers/plans/2026-07-10-catalog-graph-impact-analysis.md`
**Findings telemetry:** `./gate-findings.yaml`

> Story E-04.F-02.S-06 (closes E-04.F-02). Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A (with reason).

## Summary

| Gate | Status | Notes |
|------|--------|-------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ | ci-local backend Release build: 0 Warning(s), 0 Error(s). |
| 2 Per-task subagent reviews | ✅ | 9 tasks; spec+quality verdicts each (T1 / T2+T3 / T4 / T5–T8 cluster / T9). All Approved. |
| 3 Full suite (+ real-seam) | ✅ | All assemblies pass; Catalog integ 293/293 incl. 6 new `/impact` real-seam (real KeyCloak JWT + Postgres/RLS). `Api.IntegrationTests` flaked in the combined run on a Docker-endpoint saturation error; **re-ran isolated → 6/6 pass**. |
| 4 Container build (images CI) | ✅ | ci-local `images` job PASS (web image builds; codegen snapshot fallback). |
| 5 `/simplify` | ✅ | code-simplifier on branch diff: one change (redundant `>0` nit), commit 9dc769b; 393 FE tests pass. |
| 6 Mutation (conditional → **blocking**) | ✅ | Diff adds Application logic → blocking. Stryker on `ImpactAnalysis.cs` = **90.48%** (≥80%). |
| 7 `requesting-code-review` (final whole-branch) | ✅ | opus. ready-to-merge, 0 Blocking, 2 Should-fix (fixed), 5 Nits (recorded). |
| 8 `review-pr` (pr-review-toolkit) | ✅ | code-reviewer (1 Important: tier-3 ring — fixed) + silent-failure-hunter (1 HIGH: impact error/loading — fixed). Guideline checks clean. |
| 9 `deep-review` | ✅ | opus, against spec/plan/ADRs. 0 Blocking, 2 Should-fix + 2 Missing-tests (all fixed). |
| Terminal re-verify (build + suite) | ✅ | After gates 5–9 fixes (commit 57b7d5b): ci-local backend/images/frontend/helm + isolated Api.IntegrationTests all green. |
| 10 Visual / API verification (ADR-0084) | ✅ | Real-browser: seeded 3 svc + 2 depends-on via live API; impact glow-by-tier + banner "2 downstream (1× tier-1, 1× tier-2)" + Close restore; 0 console errors; live `GET /impact` 200 (depths 0/1/2). Screenshots committed. (Non-impacted **dimming** not shown — 3-node seed has nothing outside the radius; covered by page + unit tests.) |
| 11 CI green on PR (terminal) | ⏳ | Pre-push mirror (`ci-local.sh`) green on 57b7d5b (backend confirmed via isolated re-run). PR CI pending push. |

## Gate detail

### 1 — Build
`ci-local.sh backend` Release build → `0 Warning(s), 0 Error(s)`. Evidence: `.superpowers/sdd/gate-ci-local.log`.

### 2 — Per-task reviews
Task 1 (Compute) Approved; Tasks 2+3 (endpoint red/green) Approved — EF `.Contains(r.Type)` global-query-filter workaround verified RLS-safe; Task 4 (codegen) mechanical, path present; Tasks 5–8 (FE leaf units) Approved — no-op-glow risk cleared (arbitrary-value rings resolve to real theme vars); Task 9 (page wiring) Approved — merge order results-first, tier from response, focus-key guard, dim union all verified. Reports: sibling `gate*.md` + `.superpowers/sdd/task-*-report.md`.

### 3 — Full suite / real-seam
`ci-local.sh backend` (`dotnet test Kartova.slnx -c Release`). New real-seam: `GetImpactAnalysisTests` — multi-tier blast radius incl. a derived C→F edge; `entityKind=api`→400; unknown→422; empty→400; cross-tenant→422. Flake: `Api.IntegrationTests` assembly-init `KeycloakBuilder` Docker-endpoint error under concurrent Docker load; isolated re-run 6/6 — `.superpowers/sdd/gate3-api-it-isolated.log`.

### 4 — Container build
`images` job PASS (`docker compose build`). No Dockerfile/COPY change in the slice.

### 5 — /simplify
`gate5-simplify-report.md`. Only the pre-flagged redundant `> 0` removed; rest of the slice already minimal.

### 6 — Mutation (blocking)
`gate6-mutation-summary.log`. `dotnet stryker --mutate **/ImpactAnalysis.cs` → final mutation score **90.48%**. Strong-oracle unit tests (exact tiers + counts + cap boundary).

### 7/8/9 — Review gates
`gate7-final-review.md`, `gate8-code-review.md`, `gate8-silent-failures.md`, `gate9-deep-review.md`. Consolidated Should-fix/Important fixed in commit **57b7d5b** (`gate-fixes-report.md`): (a) impact fetch error+loading surfaced; (b) impact overlay supersedes filter dim → banner==glowing honesty; (c) tier-3 distinct ring (`--color-bg-success-solid`) + test; (d) design-doc §5.3/§5.4/§6 reconciled to shipped reality; (e) page-level dimming/honesty test. 397 FE tests + build clean after fixes.

### 10 — Visual / API
`gate10-visual-report.md`, `impact-active.png`, `impact-closed.png`, `impact-api-response.json`. Cold-start dev server + real OIDC login; live-API seed; glow-by-tier + banner + Close verified; 0 console errors.

### 11 — CI on PR
Pre-push mirror green. **Terminal gate: pending the PR's CI run.**

## Nits / follow-ups (non-blocking; final-review triage)
- `nodeCap={200}` literal in `GraphExplorerPage` JSX duplicates `GetImpactAnalysisHandler.DefaultNodeCap` (`truncated` flag is authoritative). — accepted nit.
- Impact-merged nodes show no expand affordance (impact response carries degree 0). — by design while impact active.
- Impact-only nodes removed on Close (design note reconciled). — acceptable ("Close returns to normal").
- Tenant-wide edge load per request (deferred focus-scoped loader — spec §11). — inherited from B2.
- Follow-up **FU-I1**: Api-as-subject impact (first hop via `consumes-api-from`).
- Follow-up: convert the gate-10 flow to a nightly `e2e/` regression spec (no-folding rule).
- Follow-up: defensive comment in `EfRelationshipConfiguration.cs` re bare `==` vs the global `Contains` filter (from T3 review).
