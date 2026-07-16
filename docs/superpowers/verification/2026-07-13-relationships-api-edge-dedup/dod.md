# DoD Ledger — De-dup API edges from Relationships list (#71)

**Slice:** `2026-07-13-relationships-api-edge-dedup` · **Branch:** `feat/catalog-relationships-api-edge-dedup` · **HEAD:** `5c8eb11`
**PR:** <pending> · **Last updated:** 2026-07-13
**Spec:** `docs/superpowers/specs/2026-07-13-relationships-api-edge-dedup-design.md`
**Plan:** `docs/superpowers/plans/2026-07-13-relationships-api-edge-dedup.md`
**Findings telemetry:** `./gate-findings.yaml`

> Records the Definition of Done from `CLAUDE.md`. Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-07-13 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-07-13 |
| 3 Full suite (+ real-seam) | ✅ PASS | 2026-07-13 |
| 4 Container build (images CI) | ⏳ PENDING | — |
| 5 `/simplify` | ✅ PASS | 2026-07-13 |
| 6 Mutation (conditional) | ⚠️ WAIVED | 2026-07-13 |
| 7 `requesting-code-review` | ✅ PASS | 2026-07-13 |
| 8 `review-pr` | ✅ PASS | 2026-07-13 |
| 9 `deep-review` | ✅ PASS | 2026-07-13 |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| 10 Visual / API verification (ADR-0084) | ✅ PASS | 2026-07-13 |
| 11 CI green on PR | ⏳ PENDING | — |

**Review wave (gates 5/7/8/9) — findings & disposition (HEAD `c23f517`):**
- **[FIXED, commit 657f3ae]** `ConsumesApiFrom` AND-clause untested (gates 7/8/9 + task-1 review) — exclude/default tests now seed both `ProvidesApiFor` + `ConsumesApiFrom`.
- **[FIXED, commit 657f3ae]** J6 pagination-boundary test missing (gates 7/8/9, named spec deliverable) — added `GET_outgoing_excludeApiEdges_paginates_without_short_count`.
- **[FIXED, commit 657f3ae]** `excludeApiEdges` forwarded to incoming hook = no-op (simplify efficiency+simplification, gate 7) — scoped to outgoing hook only.
- **[FIXED, commit c23f517]** snapshot `servers` URL `:5021` regen artifact (gate 7 nit) → `:8080/`.
- **[FIXED, commit c23f517]** spec/plan said string `"true"`; shipped boolean (gates 7/9 nit) — docs synced.
- **[DEFERRED — follow-up]** altitude: `RelationshipTypeRules.IsApiEdge(RelationshipType)` helper vs inline pair (altitude agent). Pair is hardcoded in 3 other sites (`GetApiSurfaceHandler`, `DerivedEdgeLoader`); fixing 1/4 is asymmetric, backfilling all is outside this diff. Inline `Where` matches established `ListApplicationsHandler` convention. Tracked as tech-debt follow-up.
- **[NON-ISSUE]** boolean vs string wire value — boolean is correct per generated type. Conditional-spread idiom kept (matches `applications.ts`/`apis.ts`/`services.ts`).

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ⏳ PENDING — running (`dotnet build Kartova.slnx -c Debug`).

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS — Task 1 (backend) reviewed, Approved (1 Minor: no dedicated ConsumesApiFrom test). Task 3 (frontend) reviewed, Approved (1 Minor: component-boundary false vs undefined, backed by wire-level test). Task 2 (snapshot) + Task 4 (docs) verified inline. Reports under `.superpowers/sdd/task-*-report.md`.
**At:** 5c8eb11 / 2026-07-13

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ⏳ PENDING — during dev: Catalog.IntegrationTests 295/295 (incl. 2 new real-seam excludeApiEdges cases — real Postgres/RLS + real JWT); web 830/830, tsc -b clean. Terminal full-solution run pending.

### 4 — Container build (images CI job)
**Status:** ⏳ PENDING — deferred to CI `images` job (gate 11) / local `docker compose build`.

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING

### 6 — Mutation loop (conditional)
**Status:** ⚠️ WAIVED (owner) — Roman waived mutation testing for this slice (consistent with prior slices). Not counted as green. Note: the two AND-clause mutants and the pre-pagination-placement mutant that Stryker would target are now directly covered by the fix-wave tests (`GET_outgoing_with_excludeApiEdges_omits_provide_and_consume_edges` seeds both `ProvidesApiFor`+`ConsumesApiFrom`; `GET_outgoing_excludeApiEdges_paginates_without_short_count` pins pre-pagination placement).

### 7 — `requesting-code-review` at slice boundary
**Status:** ⏳ PENDING

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING

### 9 — `deep-review`
**Status:** ⏳ PENDING

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ✅ PASS @ c23f517 — `dotnet test Kartova.slnx -c Debug` exit 0, all assemblies Passed! Failed:0 (Catalog.IntegrationTests 296 incl. new J6 + extended ConsumesApiFrom cases; ArchitectureTests 69). Frontend `npx vitest run` 830/830 (115 files), `tsc -b` clean. Build 0 warnings/0 errors under `TreatWarningsAsErrors`.

### 10 — Visual / API verification (observe the running system)
**Status:** ✅ PASS @ c23f517 — full evidence in `./gate10-live-api-evidence.md` + 2 screenshots. Host API `:5021` (branch code) + web dev `:5173` + real Keycloak; real browser OIDC token (admin@orga). Live fixture (service + API + providesApiFor edge) created via API. **API:** default outgoing → 1 providesApiFor; `excludeApiEdges=true` → `{items:[],nextCursor:null}`; api-surface → API under `provides`. **UI:** service Dependencies tab shows API in Provides only (Relationships Outgoing empty); API detail page still lists `Provides API for ← service`; 0 console errors. Playwright MCP available this session.

### 11 — CI green on the PR (terminal; `scripts/ci-local.sh` = pre-push mirror)
**Status:** ⏳ PENDING
