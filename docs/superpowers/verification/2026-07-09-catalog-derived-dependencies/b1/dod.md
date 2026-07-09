# DoD Ledger — Derived service↔service depends-on (sub-slice B1)

**Slice:** `2026-07-09-catalog-derived-dependencies` (B1 — graph explorer) · **Branch:** `feat/catalog-derived-dependencies`
**Story:** E-02.F-03 (sub-slice B1 of the S-03 + FU-B decomposition) · **Last updated:** 2026-07-09
**Spec:** `docs/superpowers/specs/2026-07-09-catalog-derived-service-dependencies-design.md`
**Plan:** `docs/superpowers/plans/2026-07-09-catalog-derived-dependencies-b1.md`
**Findings telemetry:** `./gate-findings.yaml` · **Deep review:** `./deep-review.md` · **Mutation:** `./mutation-summary.md` · **Gate 10:** `./gate10-visual-api.md`

> Legend: ✅ PASS · ⏳ PENDING · 🟡 WAIVER · N/A (with reason).

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ 0W/0E per task; Release re-verify via ci-local (gate 11) | 2026-07-09 |
| 2 Per-task subagent reviews | ✅ 5 tasks, each spec ✅ + quality Approved (fix loops on T2 dedup-tests, T3 tenant-oracle, T5 dedup-oracle resolved) | 2026-07-09 |
| 3 Full suite (+ real-seam) | ✅ Catalog unit 216/216; **14/14** real-seam `GetCatalogGraphTests` (real JWT + Postgres/RLS); web 745/745 | 2026-07-09 |
| 4 Container build (images) | ✅ ci-local `images` PASS (api + web); no Dockerfile/COPY change | 2026-07-09 |
| 5 `/simplify` | ✅ derivedByTarget O(1) lookup + dropped redundant frontier filter + sentinel comment (`1740d52`); skips: shared-helper extraction (out-of-diff follow-up), Task.WhenAll (false positive — DbContext not thread-safe), tenant-scan (accepted D1) | 2026-07-09 |
| 6 Mutation (blocking) | ✅ **89.74%** on Kartova.Catalog.Application (target 80%); derivation core `DerivedDependencies.cs` **100% killed**; 4 survivors in pre-existing `GraphTraversal.cs` direction ternaries — `./mutation-summary.md` | 2026-07-09 |
| 7 `requesting-code-review` (whole-branch, opus) | ✅ ready-to-merge, 0 Critical/0 Important, 4 Minor (label distinct-count fixed; provenance deferred to B2; scan/self-edge accepted) | 2026-07-09 |
| 8 `review-pr` (5 lenses) | ✅ code-reviewer + comment-analyzer clean; silent-failure fallback unreachable-today (comment added); type-design `GraphTraversalEdge`=defensible tech-debt; test-analyzer 2 gaps → 1 addressed (directional test), 1 documented (cap, by-construction) | 2026-07-09 |
| 9 `deep-review` (opus, spec/plan/ADR) | ✅ 0 blocking, 1 should-fix (provenance defer — resolved), 3 nits (accepted), 3 missing-test (1 added, 2 by-construction) — `./deep-review.md` | 2026-07-09 |
| Manual / API (gate 10) | ✅ API: 14 real-seam tests + live-stack shape + 401 auth-enforced; 🟡 **Visual: PENDING USER VERIFICATION** (Playwright MCP not connected) — `./gate10-visual-api.md` | 2026-07-09 |
| Terminal re-verify (build + suite) | ✅ ci-local Release: backend PASS (full suite), frontend PASS, on final commit `62644e8` | 2026-07-09 |
| 11 CI green (PR runner) | ✅ **PR #65 all 5 checks green** — Backend, Container images, Frontend, Helm, Stryker-config-drift. (ci-local pre-push mirror also green; the earlier local backend FAIL was the Docker-saturation flake, cleared by isolated 6/6 + backend-alone PASS and confirmed by the runner.) | 2026-07-09 |

## Notes
- **Gate 6 is BLOCKING** for B1 (Application derivation logic) — not waived; passed at 89.74%.
- **Gate 10 visual is the one honest gap:** the dashed-edge/legend browser render is unit-covered but not screenshotted (Playwright MCP unavailable this session). Flagged pending user verification per CLAUDE.md; E2E spec is the regression follow-up.
- Fixes from gates 5/7/8/9 landed in `1740d52` (simplify) and `62644e8` (review follow-ups); terminal re-verify runs on the final commit via ci-local.

## Follow-ups registered
- **B2** (next plan): `/derived-dependencies` bounded endpoint + mini-graph derived-edge merge + read-only `DerivedDependenciesSection` (consumes the `provenance` field B1 carries).
- Shared `LoadApiNames`/`LoadApplicationNames` extraction across `GraphTraversalHandler` + `GetApiSurfaceHandler` (deferred — touches an existing tested handler).
- `GraphTraversalEdge` typed derived channel (discriminated union) — tech-debt if a 2nd producer appears.
- E2E spec for the derived-edge explorer render (gate-10 regression home).
