# Gate 7 — Final whole-branch review: Visual impact analysis on the graph explorer (E-04.F-02.S-06)

Range: `b4636df..9dc769b` (12 commits). Reviewed against the design (`2026-07-10-catalog-graph-impact-analysis-design.md`) and plan (`2026-07-10-catalog-graph-impact-analysis.md`).

## Overview

The slice is well-built and faithful to the plan's architecture: a pure directed BFS (`ImpactAnalysis.Compute`) over the union of explicit + derived `depends-on` edges in the dependents direction, emitting the reused `GraphResponse` contract with tier carried in `Depth`, then a frontend overlay that merges via `mergeGraphs`, dims non-impacted, glows by tier, and banners the count.

The blast-radius semantics are end-to-end correct and consistent across the three layers I traced:
- **Direction/tier/focus:** `Compute` builds `dependentsOf[target] = [sources]` and BFS's outward, first-seen tier wins (cycle-safe), focus seeded at tier 0 and excluded from `Impacted`. Cap check is pre-insert so the count lands exactly at `nodeCap` with `Truncated` set. (`ImpactAnalysis.cs:23-56`)
- **Explicit ∪ derived:** handler unions explicit `DependsOn` rels (RLS-scoped, array-`.Contains` to dodge the global-filter trap — as noted) with derived svc→svc edges; duplicate (Source,Target) pairs are harmless (traversal dedupes). Closure gates edge inclusion so no dangling edges ship. (`GetImpactAnalysisHandler.cs:27-84`)
- **Merge order (results-first → real degrees win):** `mergeGraphs([...results, impactResult])` with first-write-wins node dedupe means the real `OutDegree/InDegree` from `/graph` survive over impact's `0/0`, so expand affordances stay correct. ✓ (`GraphExplorerPage.tsx:51-54`, `graphMerge.ts:39-53`)
- **Tier source (response, not merged):** glow tier is stamped from `buildTierMap(impactResult)` via `layoutGraph`'s `tierByNodeId`, independent of `merged.depth` (which for a shared node reflects the `/graph` BFS depth, not the impact tier). ✓
- **Banner honesty (base case):** `impactTotal` = count of tier≠0 entries in the tier map = backend `Impacted.Count` = every merged node stamped with a ring (all impact nodes are merged onto the canvas). So banner count == glowing set — **except** when a filter is concurrently active (see Should-fix 1).
- **Auth / RLS / error paths:** route uses `KartovaPermissions.CatalogRead` (no new permission, no 5-sync) ✓. Structural invalidity (`api`/malformed kind/empty id) → 400; unknown or cross-tenant (RLS `lookup.Find` null) → 422 ✓. Six real-seam integration tests cover both.

Verdict: no blocking issues. Two should-fix items are cross-cutting UX/honesty gaps that per-task reviews would not have surfaced (both live in the filter×impact and lifecycle composition that only exists once the page is wired). Everything else is nit-level.

## Blocking

None.

## Should-fix

### 1. Active kind/team filter can dim impacted nodes → banner count exceeds the visibly-glowing set
`web/src/features/catalog/pages/GraphExplorerPage.tsx:59-68`

`dimmed` is the **union** of the filter dim set and the impact dim set. `impactDim` lights the impacted∪focus set, but the filter dim set is unioned on top, so an impacted node whose team/kind is filtered out ends up in `dimmedNodeIds`. In `EntityGraphNode` both classes apply (`opacity-30` + the ring), and `opacity-30` fades the whole node including its ring — so the node reads as dimmed, not glowing, while the banner still counts it.

Scenario: user sets a Team filter, selects a node, clicks Impact analysis. Backend returns 5 dependents, 2 of them in a filtered-out team. Banner says "5 downstream" but only 3 visibly glow. This contradicts both the task's "banner count == glowing set" invariant and spec §6 "Impacted-set ∪ focus stays lit."

Fix: when impact is active, subtract the impacted∪focus id set from the final `dimmedNodeIds` (impacted/focus always win over the filter), then recompute `dimmedEdgeIds` from the reconciled node set. This makes the union "filter-dims minus impacted" and restores banner honesty.

### 2. Impact fetch failure is silent — the button does nothing on error
`web/src/features/catalog/api/impact.ts` + `web/src/features/catalog/pages/GraphExplorerPage.tsx:47-49`

`impactResult = impact.data ?? null`; `impact.isError` / `impact.isLoading` are never read. On a 4xx/5xx (or slow network) the "Impact analysis" click produces no banner, no spinner, no error — the graph is unchanged and the user gets zero feedback (and may re-click). The `/graph` fetch on the same page has an isError banner + Try-again; impact has neither.

Fix: surface `impact.isError` (inline notice or toast near the banner slot) and optionally a pending state on the button while `impact.isFetching`. Low severity but it's a genuine silent-failure on a user-initiated action.

## Nits

- **Close removes impact-only nodes; spec §6 says they remain.** `GraphExplorerPage.tsx:51-54` — `merged` is recomputed from `results` alone once `impactResult` goes null, so nodes discovered only via impact vanish on Close. This satisfies the acceptance criterion ("Close returns normal view") and is arguably cleaner, but the design note ("merged nodes remain on canvas, consistent post-expand behavior") is now stale. Reconcile: update the spec note, or persist discovered nodes into explorer state if retention is actually wanted.
- **Impact-merged nodes are non-expandable.** Because the impact response carries `OutDegree/InDegree = 0`, any node introduced only by impact shows no expand chevron/unloaded count while impact is active (`computeAffordance` sees degree 0). Escape hatch exists (Set as focus / Open page). Acceptable given the by-design 0/0, worth a one-line comment at the merge site.
- **Tier ramp deviates from spec.** `EntityGraphNode.tsx:27-33` distinguishes tier 1 (error) and tier 2 (warning); tier ≥3 all share the brand ring. Spec §6 said "tiers 1–3 distinct, ≥4 shares deepest." Acceptance only requires "glow by tier," so this is cosmetic — but note it (likely a gate-5 simplification).
- **`nodeCap={200}` hardcoded in JSX** (`GraphExplorerPage.tsx:159`) duplicates the backend `DefaultNodeCap`. The `truncated` flag is authoritative, but the displayed "showing first 200" could drift if the backend cap changes. Consider surfacing the cap in the response or a shared const.
- **Tenant-wide edge load per request.** The handler loads all `DependsOn` relationships + all derived edges for the tenant before BFS. Fine at the 200 cap and explicitly deferred (spec §11 "focus-scoped derived-edge loader"), noting for completeness.

## What looks good

- Pure `Compute` is clean, deterministic, cycle-safe, cap-correct; 8 unit tests + 90% mutation is well-matched to the logic.
- Handler correctly gates every emitted edge on the closure (no dangling endpoints), reuses `DerivedEdgeLoader`/`DerivedProvenanceNames` rather than reinventing, and documents the `.Contains` global-filter workaround inline.
- Contract reuse (`GraphResponse`, tier in `Depth`) means zero new merge code on the FE — `mergeGraphs` is untouched and the results-first ordering preserves degrees.
- Error taxonomy (400 structural vs 422 unknown/cross-tenant) is correct, matches the sibling `GetApiSurfaceAsync` convention, and is covered by real-seam tests including cross-tenant RLS.
- `impactModel` functions are pure, small, and unit-tested; the prev-focus render-time guard for clearing stale impact is the correct React pattern.
- Auth reuse (catalog.read, no 5-sync) is the right call and matches `/graph` + `/derived-dependencies`.
