# Gate 9 — Deep Review: Graph Impact Analysis (E-04.F-02.S-06)

**Diff:** `review-b4636df..9dc769b.diff` (12 commits) · **Spec:** `2026-07-10-catalog-graph-impact-analysis-design.md` · **Plan:** `2026-07-10-catalog-graph-impact-analysis.md`
**Reviewed:** 2026-07-10 · Lens = design-satisfaction + test-proves-behavior, not plain code review.

## Overview

Verdict: **PASS — ship-able.** The slice implements every locked decision and every acceptance-criteria clause with real code backed by tests. No blocking defects. Blast-radius semantics (explicit ∪ derived incoming depends-on; tier = hop distance; focus excluded; cap 200 + `Truncated`) are correct and strongly unit-tested. The api→400 reconciliation (plan refining the spec's original 422) is consistent across handler code and the real-seam integration test. Derived edges are genuinely reused via the shared `DerivedEdgeLoader`/`DerivedProvenanceNames`; the FE consumes `GraphResponse` and does not re-derive. No ADR is contradicted (ADR-0111 §5 honored — API coupling collapses to derived service→service, Api-subject deferred to FU-I1; ADR-0090 RLS enforced with cross-tenant 422 tests; ADR-0095 correctly N/A). Findings are limited to two should-fix items (a silent-failure gap and stale design-doc lines) and two missing tests, all narrow.

Counts: **Blocking 0 · Should-fix 2 · Missing-tests 2 · Nits 3**

## Blocking

None.

## Should-fix

- **Silent failure on impact fetch error/loading.** `GraphExplorerPage` uses only `impact.data`; `impact.isError`/`impact.isLoading` are ignored. If the `/impact` fetch fails, clicking "Impact analysis" does nothing — no banner, no error, no retry — unlike the focus-graph fetch which renders an error banner + "Try again". Failed analysis is indistinguishable from "no downstream". (`GraphExplorerPage.tsx:47-49`.)
- **Design doc §5.3/§5.4 stale vs shipped contract.** The design still states api-focus → **422** and a bespoke `ImpactAnalysisResponse`/`ImpactNode` DTO; both were superseded by the plan (api → **400**; reuse `GraphResponse` with tier in `Depth`). Code + tests follow the plan and the plan documents the reconciliation, but the design doc on paper now disagrees with the implementation on two points. Update or annotate the design so the spec↔code trail is clean.

## Missing tests

- **Tier-3+ glow ring is unverified.** `IMPACT_RING` in `EntityGraphNode.tsx` defines only tiers 1 and 2; tier ≥ 3 falls back to the brand ring. The acceptance-criteria banner explicitly cites *tier-3*, yet `EntityGraphNode.test.tsx` asserts rings only for tier 1, tier 2, and tier-0/undefined. No test proves a tier-3 node renders any glow, so the "glow by tier" clause is only partially covered against its own example.
- **Page-level impact dimming not asserted end-to-end.** The page composes filter-dims ∪ impact-dims, but the `GraphExplorerPage` impact test seeds only impacted nodes (f, a, b) — it contains no node that should dim, so "dims graph except affected downstream path" is proven only at the pure-model level (`impactModel.impactDim`, which does have a strong oracle: x dims, e2 dims, a/f lit). The page-level union wiring is untested. (Low risk given the pure-model coverage.)

## Nits

- No loading indicator while the impact query is in flight — the button appears inert for the fetch duration.
- `nodeCap={200}` is hardcoded in `GraphExplorerPage` JSX while the backend owns `GetImpactAnalysisHandler.DefaultNodeCap = 200` — two sources of truth for the same cap.
- No test at the real 200-node cap boundary (cap is parameterized and unit-tested at cap=2, so acceptable).

## What looks good

- **Semantics match the locked decisions exactly.** `ImpactAnalysis.Compute` does directed BFS over `dependentsOf` (edge Source→Target = "Source depends on Target"), tiers by hop, keeps first-seen tier (cycle-safe), excludes focus, and truncates at cap with no off-by-one (exactly-at-cap ⇒ not truncated; cap+1 ⇒ truncated). Unit oracles are strong: diamond min-tier-once, cycle terminates, leaf → empty, mixed app/service kinds, chain tiers, cap truncation, focus-never-in-result.
- **Edge set = explicit ∪ derived, genuinely reused.** Handler unions `Relationships` (DependsOn) with `DerivedEdgeLoader.LoadAsync` and renders provenance via `DerivedProvenanceNames` — the same B1/B2 primitives, not a re-derivation. `GraphResponse` reused verbatim (tier in `Depth`, degrees 0). FE consumes the response and re-derives nothing. The `dependsOnOnly.Contains(r.Type)` array-predicate (vs a lone `==`) correctly avoids the global-query-filter `WHERE FALSE` collapse. Integration test asserts both a derived edge (C→F) and explicit edges (A→F, B→A) are present, and that the API node is excluded — matching ADR-0111 §5 (no API special-casing).
- **api → 400 internally consistent.** Endpoint returns 400 for `api`/malformed/empty-id and 422 for unknown/cross-tenant; integration tests `Api_focus_returns_400`, `Empty_entityId_returns_400`, `Unknown_entity_returns_422`, `Other_tenant_entity_is_not_visible_422` all agree. Consistent with sibling `GetApiSurfaceAsync`.
- **ADR-0090 / tenancy:** endpoint under the tenant group with `RequireAuthorization(CatalogRead)`; RLS-scoped `lookup.Find` yields cross-tenant 422, proven at the real seam (real Postgres/RLS + real JWT). No new permission, no 5-sync — correct for a catalog-read surface.
- **Acceptance criteria each map to code + test:** button (sidebar, service/app-only, hidden for api — tested); banner "N downstream (a× tier-1, …)" — `ImpactBanner.test` uses the exact criteria numbers (12 downstream, 3× tier-1) as a strong oracle; Close Analysis clears the overlay — asserted in the page test; dim + glow present in code and (partly) tested per above.
- **ADR-0095 correctly N/A** — graph aggregate reusing `GraphResponse`, not a cursor list; design §7 documents this.
