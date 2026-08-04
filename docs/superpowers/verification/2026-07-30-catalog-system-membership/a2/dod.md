# DoD Ledger — System list surface (E-03.F-03.S-01 sub-slice A2)

**Slice:** `2026-07-30-catalog-system-membership/a2` · **Branch:** `feat/catalog-system-list-surface-a2` · **HEAD:** `1abb9603`
**PR:** [#83](https://github.com/diablo39/Roman.Kartova/pull/83) · **Last updated:** 2026-08-02
**Spec:** `docs/superpowers/specs/2026-07-30-catalog-system-membership-assignment-design.md` (§4 = A2)
**Plan:** `docs/superpowers/plans/2026-08-02-catalog-system-list-surface-a2.md` (gitignored scratch)
**Plan review:** two agent waves, 9 agents — see `docs/superpowers/templates/plan-wave-review.md` §6 and the plan's own wave-1/wave-2 triage records
**Findings telemetry:** `./gate-findings.yaml` — per-gate issues × severity × real/delusion (copy from `templates/gate-findings-template.yaml`)

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.
> This table records each gate's **status**; what each gate **found** (and whether it was real) goes in `gate-findings.yaml`.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-08-02 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-08-02 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS | 2026-08-02 |
| 4 Container build (images CI) | ✅ PASS | 2026-08-02 |
| 5 `/simplify` | ✅ PASS | 2026-08-02 |
| 6 `requesting-code-review` | ✅ PASS | 2026-08-02 |
| 7 `review-pr` | ✅ PASS | 2026-08-02 |
| 8 `deep-review` | ✅ PASS | 2026-08-02 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-08-02 |
| 9 Visual / API verification (ADR-0084) | ✅ PASS | 2026-08-02 |
| 10 CI green on PR (`ci-local.sh` = pre-push mirror) | ✅ PASS | 2026-08-02 |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
**Evidence:** `cmd //c "dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true"` -> **0 Warning(s), 0 Error(s)**, 34 projects.
**At:** `1abb9603`

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** All 15 tasks reviewed individually (spec-compliance + code-quality), interleaved during dev - see the per-task entries in the SDD ledger. Found 1 Critical (vacuous forged-cursor assertion, fixed `7dd98119`) + 6 should-fix. **Impact-analysis re-grounding:** the plan flagged its own blast radius as grep-only pending a codelens re-run. Attempted and FAILED - `roslyn-codelens` resolved symbols but returned empty reference sets for every type and 1-of-2 callers for methods, surviving `rebuild_solution`. The built-in `LSP` tool answered correctly (`findReferences` @ `CurrentMembershipQueries.cs:25:44` -> 15 references across 7 files) once the character offset landed inside the symbol name; at `:39` it returned 1. CLAUDE.md switched to LSP in `3349d965`. The two changed records are constructed at one production site each with named arguments, so a missed caller is a compile error - gate 1 green confirms.
**At:** `1abb9603`

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ✅ PASS
**Evidence:** `cmd //c "dotnet test Kartova.slnx"` -> exit 0, zero failures. Catalog unit **272**, Catalog integration **396** (real Postgres + RLS + real JWT via `KartovaApiFixtureBase`), plus Organization 83, Audit integration 35, SharedKernel.Identity integration 8, Api integration 6. Frontend `npx vitest run src/features/catalog` -> **538** tests / 72 files. Real-seam requirement met: all System filter/enrichment behaviour is covered against real Postgres, because EF InMemory cannot materialize `Relationship` at all (ComplexProperty + a value conversion make its shaper throw) - a documented deviation from the spec test line, decided at plan time.
**At:** `1abb9603`

### 4 — Container build (images CI job)
**Status:** ✅ PASS
**Evidence:** `docker compose build` -> exit 0. Images built: `kartova/web:dev`, `kartova/api:dev`, `kartova/migrator:dev`.
**At:** `1abb9603`

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS
**Evidence:** Four cleanup agents in parallel (reuse / simplification / efficiency / altitude). Three converged on the same three findings. Six fixes applied in `56996e52`: extracted `TryDedupAndCap` (cap block was duplicated per delegate), extracted `CursorFilterValues.Join` with `StringComparer.Ordinal` baked in (canonicalization was hand-written at six sites with a comment warning readers away from the adjacent culture-sensitive `.Order()`), routed the new EXISTS through `CurrentMembershipQueries`, plus three comment/reuse corrections. Deferred with reasons: the 200-item facet fetch (mirrors an accepted 15+-site precedent) and a `useEntityFilterFacet` extraction (would need to cover all existing sites, not 2).
**At:** `56996e52`

### 6 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
**Evidence:** Whole-branch review on the most capable model. **1 Blocking:** a System assign/move/clear never invalidated the applications/services list caches, so the new column showed stale data for up to 30s - exactly the flow gate 9 drives. Fixed at the shared seam (`invalidateAfterRelationshipChange`) in `70f293f` with a regression test that fails before and passes after. 5 should-fix + 5 nits triaged; the 7 ledger items it was asked to adjudicate are recorded in `gate-findings.yaml`.
**At:** `70f293f`

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS
**Evidence:** Run for real, three lenses (silent-failure-hunter, pr-test-analyzer, comment-analyzer) - not folded into gates 6/8. **1 HIGH:** the cap 400 rendered a generic error card whose Reset button could not clear the offending filter, so every click replayed the identical 400 (an infinite loop for a user who selects 51 Systems). Fixed in `1abb9603`: the server ProblemDetails detail is surfaced and the card gained a working clear action. Also found the Services-side cap/union/wire test gap (closed by `8ff25e58`) and four inaccurate comments. The silent-failure lens explicitly cleared the enrichment's deliberate drops (DistinctBy, `names.ContainsKey`) as correct rather than swallowed.
**At:** `1abb9603`

### 8 — `deep-review`
**Status:** ✅ PASS
**Evidence:** Read against spec section 4, the ADRs and the tests. **3 Blocking, all closed:** (a) this ledger and `gate-findings.yaml` were unfilled templates while six gates had run - the only record was the gitignored SDD ledger, so the telemetry would have vanished at merge and no completion claim would have been citable; (b) plan case 15's "run it on both lists" criterion was unmet - the Services cap had no test at any tier (closed by `8ff25e58`, reached independently of gate 7); (c) the plan's Impact Analysis was grep-only and never re-grounded (see gate 2). 5 should-fix + 5 nits; the deferred ones are recorded in `gate-findings.yaml` with reasons.
**At:** `1abb9603`

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ✅ PASS
**Evidence:** Re-ran build + full solution suite on the FINAL commit after gates 5-8 applied fixes. Build 0 warnings / 0 errors; `dotnet test Kartova.slnx` exit 0, zero failures.
**At:** `1abb9603`

### 9 — Visual / API verification (observe the running system)
**Status:** ✅ PASS
**Evidence:** Driven on the real stack (`docker compose up -d`: postgres + keycloak + migrator + api + web), authenticated through Keycloak, navigated in-SPA (ADR-0084). Committed evidence in this folder: `gate9-applications-system-column.png` (the System column between Team and Created by; "A App 015" renders **Payments Platform** as a link, unassigned rows render an em dash; the **All systems** facet is in the FilterBar), `gate9-systems-list.png`, and `gate9-applications-system-filter.png` (a `?systemId=` deep link hydrates the facet to "Payments Platform", shows "Filters (1 active)", and returns exactly **1 result** whose System column is populated - zero em-dash rows). Console clean: the spec fails on any console error or pageerror and passed. **API half:** the live OpenAPI document exposes `systemId` on `GET /api/v1/catalog/applications` (params: sortBy, sortOrder, cursor, limit, displayNameContains, lifecycle, teamId, createdByUserId, systemId), and the API log shows the list request returning 200.

**Converted to a permanent regression spec** (the "any bug it finds becomes a regression test" rule): `e2e/tests/system-list-surface.spec.ts`, run green with `npx playwright test tests/system-list-surface.spec.ts`. It asserts the full header order (so a skeleton/header skew or a column moving reddens), one row header per row, the ADR-0107 wire format (repeated `?systemId=` params, never an `f=` map), and filter/column agreement.

**E2E-impact trigger (CLAUDE.md):** checked - no existing spec asserts a column count or an `nth-child` cell index on either list (`smoke.spec.ts` counts rows only), so the two added columns break nothing. No existing spec needed updating.

**Two observations from driving it, neither introduced by this slice:** (1) `/catalog` is an alias that redirects to `/catalog/applications` and DROPS the query string, so a filter deep-link through the alias silently loses its filter - a shared filter URL must use the canonical path; (2) the System multi-select popover does not dismiss on Escape and intercepts a subsequent click, which is why the spec deep-links instead of clicking through (the page-level vitest specs already cover the click path). Both are recorded in `gate-findings.yaml`.
**At:** `1abb9603` (+ the spec commit) / 2026-08-02

### 10 — CI green on the PR (terminal; `scripts/ci-local.sh` = required pre-push mirror)
**Status:** ✅ PASS
**Evidence:** PR [#83](https://github.com/diablo39/Roman.Kartova/pull/83), run 30936287860 - **all five jobs green**: Backend (arch + unit + integration) 3m44s, Container images 2m4s, Frontend (test + typecheck + build) 3m26s, Helm 6s, Stryker config drift 5s. The runner is the source of truth.

**Pre-push mirror:** `scripts/ci-local.sh` (Release). First aggregate run reported backend FAIL; re-ran `scripts/ci-local.sh backend` standalone -> PASS, and CI then passed backend on the runner. Cause is consistent with the known local Docker-saturation flake (the aggregate run builds images and runs Testcontainers concurrently on one host); it did not reproduce in isolation and did not reproduce on CI. Recorded rather than re-pushed blindly, per the gate-10 rule.
**At:** `1abb9603`+ / 2026-08-02
