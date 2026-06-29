# Slice — Catalog graph explorer: Kind + Team filters

**Date:** 2026-06-29
**Story:** E-04.F-02.S-05 (graph filters by team / domain / criticality / origin).
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-graph-filters`
**Follows:** the standalone explorer (`2026-06-26-catalog-graph-explorer-design.md`, PR #50) and its sidebar refinement (`2026-06-27-catalog-graph-explorer-sidebar-design.md`, PR #51) — both on master.
**Governing ADR:** ADR-0040 (Two-View Dependency Graph Navigation — the standalone explorer "can evolve … without cluttering every page"; filters are a **canvas-overlay** surface that borrows the list-filter control vocabulary, not the list-page `<FilterBar>` chrome). Renderer ADR-0094/ADR-0088 (React Flow / `@xyflow/react`).

---

## 1. Goal

Add **filtering** to the standalone dependency graph explorer (`/graph?focus=<kind>:<id>`): narrow a multi-hop neighbourhood to the nodes a developer cares about by **entity kind** (application / service) and **owning team**. A filter **dims/fades** non-matching nodes (and the edges that touch them) while leaving the graph's topology and layout untouched — so "which of these are X" is answered *in context*, never by fragmenting the picture.

The slice is **frontend-only**. Filtering runs **client-side** over the already-fetched, bounded in-memory graph (≤ ~150 nodes after the soft cap). No backend change, no new endpoint, no codegen, no new web dependency — the data the filters need (`kind`, `teamId`) is already on every `GraphNodeDto` returned by `GET /api/v1/catalog/graph`.

### 1.1 Scope decision — why only Kind + Team

The story names **team / domain / criticality / origin**. A context-gathering pass against the domain model established that only some of those have backing data:

| Facet | Backing data | Decision |
|---|---|---|
| **Kind** (application/service) | `Kind` on every graph node | **build** — client-side, zero backend |
| **Team** | `TeamId` on every graph node | **build** — client-side; option labels from the org teams list |
| **Status** (Lifecycle/Health) | exists on the entities (`Application.Lifecycle`, `Service.Health`) but **not projected onto graph nodes** | **defer** — needs a `GraphNodeDto` enrichment + a clearer combined-status story |
| **Origin** | on every edge (`RelationshipOrigin`), but only `Manual` exists in data today | **defer** — a dead control until Scan/Agent relationships land (Phase 2 / E-06); mirrors the relationships-registry deferral |
| **Domain** | **no field anywhere** | **defer** — a new concept on Application+Service (migrations + create/edit UI + list-surface revisit) = its own epic, not a filter slice |
| **Criticality** | **no field anywhere** | **defer** — same as domain |

Confirmed with the human (filter-set, filter-effect, status-modelling, and team-options each decided explicitly during brainstorming). This slice ships the two facets with backing data and zero backend cost; the rest are recorded as explicit deferrals (§8).

---

## 2. Pre-requisites (already on master)

- **Graph endpoint:** `GET /api/v1/catalog/graph?entityKind&entityId&depth&direction` → `GraphResponse { nodes[], edges[], truncated }`. `GraphNodeDto { EntityKind Kind, Guid Id, string DisplayName, int Depth, Guid? TeamId }` — **`Kind` and `TeamId` are already present on every node** (the latter was pre-wired for this slice in S-04). RLS-scoped, `catalog.read`. No change needed.
- **Explorer page & state (PR #50 + #51):**
  - `features/catalog/pages/GraphExplorerPage.tsx` — reads `?focus`, calls `useGraph`, merges, lays out with dagre, renders `<ReactFlow>` + sidebar; owns the soft cap (~150) + Reset.
  - `features/catalog/relationships/useExplorerState.ts` — `sessionStorage`-backed `{ expand, selected }` keyed `graph-explorer:<focus>` (storage injectable, corrupt/absent → safe default; survives 401 silent-renew re-auth + F5 because only `?focus` lives in the URL).
  - `features/catalog/api/graph.ts` — `useGraph({ focus, expand })` (multi-query `useQueries` + cache per node).
  - `features/catalog/relationships/graphMerge.ts` — pure `mergeGraphs(results) → { nodes, edges, truncated }` (union/dedup by `kind:id` / edge id).
  - `features/catalog/relationships/graphLayout.ts` — pure `layoutGraph(merged, focusId, selectedId) → positioned nodes` (dagre LR; marks focus + selected).
  - `features/catalog/relationships/graphModel.ts` — `GraphNodeData` (the React Flow node `data`: `kind`, `entityId`, `displayName`, focus/selected markers).
  - `features/catalog/components/EntityGraphNode.tsx` — display-only custom node: kind icon + displayName, `focused` variant, `selected` ring.
- **List-filter vocabulary (ADR-0107 / ADR-0095):** `FilterSpec` (`lib/list/filters/types.ts`) and the Untitled UI multi-select primitives used by `FilterBar`. The explorer reuses the **option-list shape and the control primitives**, not the URL-backed `useListFilters`/`useListUrlState`/`<FilterBar>` plumbing (explorer filter state is sessionStorage, not URL — Decision 3; the canvas overlay is a distinct presentation — Decision 4).
- **Org teams list:** the same teams-list query `CatalogListPage` uses to populate its Team filter options (`{ id, displayName }[]`). Reused for the Team facet's options.
- **Frontend stack:** React 19 + TS strict, Vite, React Router v7, TanStack Query (ADR-0039), Untitled UI primitives (ADR-0094); `tsc -b` (`npm run build`) is the binding type gate (ADR-0109). React Flow `<Panel>` is part of `@xyflow/react` (already a dependency).

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **Two facets this slice: Kind + Team.** Status, Origin, Domain, Criticality deferred (§1.1, §8). | Only Kind + Team have backing data on the graph node and cost zero backend. Human-confirmed scope. |
| 2 | **Client-side filtering** over the merged in-memory graph. No backend/endpoint/query-param change. | The graph is a *bounded* aggregate (soft cap ~150 nodes, hard cap 200/`truncated`); it's already fully in memory after fetch. Client-side is instant, needs no server round-trip, and avoids the question of whether server-side pruning should break traversal connectivity. |
| 3 | **Filter state in `sessionStorage`, keyed `graph-explorer-filters:<focus>`**, via a new sibling hook `useGraphFilters(focusKey)`. Only `?focus` stays in the URL. | Consistent with the explorer's interaction state (`useExplorerState` chose sessionStorage per-focus for the same re-auth/F5 survival reasons — sidebar slice §2). A **separate** hook keeps the merged-in `useExplorerState` untouched (smaller blast radius). Shareable filtered deep-links are consciously traded away, exactly as the sidebar slice traded away shareable `expand` state. |
| 4 | **Canvas-overlay control** via React Flow `<Panel position="top-left">`, **not** the list-page `<FilterBar>` disclosure chrome. Reuses Untitled UI multi-select primitives + the `FilterSpec` option shape. | ADR-0040: the explorer is its own surface; the list `<FilterBar>` is a collapsible panel designed for list pages above a `<DataTable>`. An on-canvas overlay is the right idiom for a graph and keeps the control next to what it acts on. |
| 5 | **Dim/fade non-matches; never hide.** Non-matching nodes stay in place at reduced emphasis; matching nodes render full. **The focus node never dims** (it's the anchor). | Human-chosen (filter-effect). Preserves topology + dagre layout, so positions don't jump; sidesteps orphan-edge/fragmentation entirely; answers "which are X" in context. |
| 6 | **An edge dims if *either* endpoint dims.** Only edges between two matching nodes render solid. | Visually isolates the matching subgraph while keeping the surrounding structure as faded context. Simple, deterministic, testable. |
| 7 | **Dimming is visual only — dimmed nodes remain interactive** (click → select → sidebar; expand still works). | A filter narrows attention, it doesn't disable navigation. Keeps the explorer's core interactions intact under any filter. |
| 8 | **Filters apply live (immediately) on change** — no submit button. | Filtering is in-memory and instant, so the list `<FilterBar>`'s submit-driven model (which exists to avoid per-keystroke *fetches*) is unnecessary here. Live feedback is the better UX for a client-side facet. |
| 9 | **Filtering is a pure, data-only annotation applied after merge**: `applyGraphFilters(nodes, edges, filters, focusId)` sets a `dimmed` flag per node/edge; **layout (positions) is unaffected**. | Keeps the match logic pure and unit-testable, and guarantees the graph doesn't re-flow when a filter toggles (Decision 5). |
| 10 | **Facet semantics: AND across facets, OR within a facet; an empty facet imposes no constraint.** A node matches iff `(kinds == ∅ ∨ node.kind ∈ kinds) ∧ (teamIds == ∅ ∨ node.teamId ∈ teamIds)`. The focus node always matches. A node with `teamId == null` never satisfies a non-empty Team facet (so it dims when any team is selected; focus exempt). | The established list-filter convention (matches Applications' lifecycle+team). Null-team behaviour stated explicitly to remove ambiguity. |
| 11 | **Team options = all org teams** (the teams-list query), a stable list — not only teams present in the current neighbourhood. | Human-chosen (team-options): a stable, familiar option set (same source as the Applications filter) over a dynamic one that grows/shrinks as you expand. |
| 12 | **Kind options are the two fixed `EntityKind` values** (`application`, `service`), camelCase on selection to match node `kind` (ADR-0109). | Bounded, known set; no fetch. |
| 13 | **No new ADR.** This is within ADR-0040 (which anticipated filters). No new permission — reading/redrawing a graph the user already fetched. | Filters add no authority and no architectural decision beyond what ADR-0040 already records. |
| 14 | **React Flow mocked in component tests; the pure helpers (`applyGraphFilters`, `useGraphFilters`) carry the logic coverage; Playwright MCP is the real render check.** | Same jsdom limitation as the prior graph slices (React Flow measures DOM / ResizeObserver). |

---

## 4. Architecture

### 4.1 Filter state — `useGraphFilters(focusKey)`

```
type GraphFilters = { kinds: EntityKind[]; teamIds: string[] };   // EntityKind = "application" | "service"

useGraphFilters(focusKey: string): {
  filters: GraphFilters;
  setKinds(kinds: EntityKind[]): void;
  setTeamIds(ids: string[]): void;
  clear(): void;
  isActive: boolean;          // any facet non-empty
  activeCount: number;        // kinds.length + teamIds.length (for the badge)
}
```

- `sessionStorage` key `graph-explorer-filters:<focusKey>`; read on mount, written on every mutation (mirrors `useExplorerState`).
- A different `focusKey` (re-rooted via "Set as focus", or a different entry entity) reads its own slot → each focus has independent filters; fresh if none.
- **Defensive:** missing / corrupt JSON / unavailable `sessionStorage` → fall back to `{ kinds: [], teamIds: [] }` in memory; never throw (same contract as `useExplorerState`). Storage is injectable for unit tests.

### 4.2 Pure filter application — `graphFilter.ts`

```
applyGraphFilters(
  nodes: MergedNode[], edges: MergedEdge[],
  filters: GraphFilters, focusId: string,
): { nodes: (MergedNode & { dimmed: boolean })[]; edges: (MergedEdge & { dimmed: boolean })[] }
```

- Per node: `matched = isFocus(node, focusId) || ((filters.kinds.length === 0 || filters.kinds.includes(node.kind)) && (filters.teamIds.length === 0 || (node.teamId != null && filters.teamIds.includes(node.teamId))))`; `dimmed = !matched`.
- Per edge: `dimmed = nodeDimmed(edge.source) || nodeDimmed(edge.target)` (Decision 6).
- No filter active (`kinds == ∅ && teamIds == ∅`) → nothing dimmed (every `dimmed = false`). Pure, no React import → unit-testable.

### 4.3 Data flow (GraphExplorerPage)

```
GraphExplorerPage (focus = parseRef(?focus); focusId = "kind:id")
  state   = useExplorerState(focusId)          // expand + selected (unchanged)
  filters = useGraphFilters(focusId)           // NEW
  { results } = useGraph({ focus, expand: state.expand })
  merged   = mergeGraphs(results)              // unchanged
  filtered = applyGraphFilters(merged.nodes, merged.edges, filters.filters, focusId)   // NEW — adds dimmed flags
  { nodes, edges } = layoutGraph(filtered, focusId, state.selected)                    // positions unchanged by dimmed
  <ReactFlow nodes edges …>
    <Panel position="top-left"><GraphFilterControls filters={filters} teams={teamsList}/></Panel>   // NEW
    <Controls/> <MiniMap/> <Background/>
  selected → <GraphExplorerSidebar …/>          // unchanged
```

- `applyGraphFilters` slots **between merge and layout**, but since `dimmed` is data-only, layout positions are identical with or without filters (Decision 9). `layoutGraph` simply carries the `dimmed` flag through onto `GraphNodeData`; edges get a `dimmed` style/className.
- `EntityGraphNode` reads `data.dimmed` and renders a muted/reduced-opacity variant (composes with `focused` / `selected` — a selected-but-dimmed node still shows its selection ring; the focus node is never dimmed).

### 4.4 File map

**Created (frontend):**

| File | Purpose | ~LOC |
|---|---|---|
| `features/catalog/relationships/graphFilter.ts` | Pure `applyGraphFilters` (§4.2). | 50 |
| `features/catalog/relationships/useGraphFilters.ts` | `sessionStorage` filter state keyed by focus (§4.1); storage injectable. | 80 |
| `features/catalog/components/GraphFilterControls.tsx` | Canvas-overlay panel: Kind multi-select (fixed 2) + Team multi-select (org teams) + active-count badge + Clear; live-apply (Decision 8). | 120 |

**Modified (frontend):**

| File | Change | ~LOC |
|---|---|---|
| `features/catalog/relationships/graphModel.ts` | Add `dimmed?: boolean` to `GraphNodeData`. | ~5 |
| `features/catalog/relationships/graphLayout.ts` | Thread `dimmed` from input node onto the positioned node's `data`. | ~5 |
| `features/catalog/components/EntityGraphNode.tsx` | `dimmed` visual variant (opacity/mute), composes with focused/selected. | ~15 |
| `features/catalog/pages/GraphExplorerPage.tsx` | Wire `useGraphFilters` + `applyGraphFilters` + render `<Panel><GraphFilterControls/></Panel>`; dimmed-edge style. | ~40 |

**Created (tests):** see §7. **Estimate ≈ 315 LOC** frontend production (excl. tests). No backend, no codegen, no new dependency, no lockfile change. Well under the ~800 ceiling.

---

## 5. Components

### 5.1 `useGraphFilters` (§4.1)
State core; fully unit-testable with an injected `Storage`. Independent slot per focus key; safe default on corrupt/absent/throwing storage.

### 5.2 `applyGraphFilters` (§4.2)
Pure match + dim logic. The focus node is always matched; the AND/OR/empty-facet semantics and the edge-dim rule live here (Decision 6/10). If the test pass flags survivors on the boolean predicate, this is the natural place to harden coverage.

### 5.3 `GraphFilterControls`
Props `{ filters: ReturnType<typeof useGraphFilters>, teams: { id; displayName }[] }`. Renders an Untitled UI overlay card inside `<Panel position="top-left">`:
- **Kind** multi-select — fixed options `Application` / `Service` (values `application` / `service`).
- **Team** multi-select — options from `teams` (`{ value: id, label: displayName }`), stable list (Decision 11). Loading/empty teams → the control renders with no options (graceful; Kind still works).
- **Active-count badge** (`filters.activeCount`) + **Clear** (calls `filters.clear()`), shown only when `isActive`.
- Changes call `setKinds` / `setTeamIds` immediately (live-apply). No submit button.

### 5.4 `GraphExplorerPage` (wiring)
Adds the `useGraphFilters` call, the `applyGraphFilters` step, and the `<Panel>` overlay; passes the teams list (reusing the existing teams-list query). Everything else (merge, layout, sidebar, expand, cap, reset) is unchanged. Edges receive a `dimmed` class/style so React Flow fades them.

### 5.5 `EntityGraphNode` change
Reads `data.dimmed`; when true, applies a muted/reduced-opacity treatment that visually recedes the node while keeping it legible and clickable. Layers cleanly under the existing `focused` emphasis and `selected` ring (a dimmed+selected node keeps its ring; the focus node is never dimmed).

---

## 6. Error handling

| Condition | Behaviour |
|---|---|
| `sessionStorage` unavailable / corrupt filter JSON | fall back to empty filters in memory; never throw (mirrors `useExplorerState`). |
| teams-list query loading / error | Team facet renders with no options (or a quiet "no teams" state); Kind facet unaffected; graph unaffected. |
| filter matches nothing but the focus | not an error — the focus node renders full, everything else faded; the active-count badge signals why. No special empty state. |
| node with `teamId == null` under a non-empty Team facet | dims (doesn't match), except the focus node. |

No new `ProblemDetails`, no network path — filtering is entirely client-side.

---

## 7. Testing strategy (gate-2 / gate-5 artifacts)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). **This is a frontend-only slice** — no HTTP/auth/DB/middleware change, no new endpoint — so the **real-seam integration tier is N/A** (nothing new touches the seam). Logic coverage lives in pure-unit + component tests; the real render is the Playwright check.

### 7.1 Frontend unit
- **`graphFilter.test.ts`** — kind-only filter dims the other kind; team-only dims other teams; both facets AND; empty facets → nothing dimmed; **focus node never dimmed** (even when it doesn't match); `teamId == null` dims under a non-empty team facet (focus exempt); **edge dims iff either endpoint dims** (solid only between two matches).
- **`useGraphFilters.test.ts`** — persist→restore by focus key; `setKinds`/`setTeamIds`/`clear`; `isActive`/`activeCount`; independent state per `focusKey`; corrupt JSON / throwing / absent `Storage` → safe empty default (no throw).

### 7.2 Frontend component
- **`GraphFilterControls.test.tsx`** — renders fixed Kind options + team options from props; selecting fires `setKinds`/`setTeamIds`; active-count badge reflects selection; Clear calls `clear`; empty teams renders gracefully.
- **`GraphExplorerPage.test.tsx`** (React Flow + `useGraph` + `useExplorerState` + `useGraphFilters` mocked) — applying a filter marks the expected nodes/edges `dimmed` in the rendered output; the focus node stays un-dimmed; a dimmed node is still selectable (click → `select`). ≥1 happy + ≥1 boundary (filter that dims everything but focus).

### 7.3 Container build (gate 4)
No new dependency, no Dockerfile/`COPY` change, **no API rebuild / codegen** (no endpoint change). The web image must still `npm run build` (`tsc -b` + vite) green — the binding type gate for the new `dimmed` field and the new modules.

### 7.4 Mutation (gate 6) — **N/A**
The diff is **frontend-only** (no C# Domain/Application logic). Per CLAUDE.md gate 6 is conditional/blocking only when the diff touches Domain/Application; it does not here. (Stryker.NET doesn't cover TS; the pure `graphFilter`/`useGraphFilters` logic is covered by §7.1.) Noted, not silently skipped.

### 7.5 Manual verification (ADR-0084)
Playwright MCP, **cold-start dev server first**:
- From an entity's mini-graph → "Open full graph" → `/graph?focus=…` renders.
- Open the filter overlay; select **Kind = Application** → service nodes (and edges touching them) fade, application nodes stay full; focus stays full regardless.
- Select a **Team** → nodes not on that team fade; combine with Kind (AND); clear → all restored.
- Confirm a faded node is still clickable (select → sidebar) and expandable.
- Filters persist across an expand and across **F5** (sessionStorage); re-rooting via "Set as focus" starts fresh filters for the new focus.
- Console clean.

**Flag pending user verification** if the dev stack is unavailable in-session — **DevSeed has 120 apps but no relationships**, so a multi-node graph must be seeded to exercise dimming meaningfully (note in the plan).

---

## 8. List surface (ADR-0095 / ADR-0107)

`/graph` is **not a list screen** and the endpoint is a **bounded aggregate** (no cursor pagination) — but this slice *does* add a user-facing filter surface, so per ADR-0107 the decision is recorded.

**Filter Proposal (per-facet outcome):**

| Facet | Control | Outcome |
|---|---|---|
| Kind | multi-select (application/service) | **implement-now** |
| Team | multi-select (all org teams) | **implement-now** |
| Status (Lifecycle/Health) | multi-select | **defer** — needs `GraphNodeDto` enrichment + a combined-status story |
| Origin | multi-select | **defer** — only `Manual` exists; revisit when Scan/Agent land (Phase 2 / E-06) |
| Domain | — | **defer** — no backing field; new-field epic |
| Criticality | — | **defer** — no backing field; new-field epic |

**Registry:** move the `Dependency-graph filters` row (`/graph`) in [docs/design/list-filter-registry.md](../../design/list-filter-registry.md) from **pending → built** for `kind` + `teamId` (canvas-overlay, sessionStorage-backed, live-apply); note the four deferrals with their targets.

**Field-addition trigger:** this slice adds **no new queryable/user-facing field** to any entity (`kind`/`teamId` already exist; we deferred domain/criticality). So the trigger does **not** fire → no column/sort/filter revisit on the Applications or Services list screens.

---

## 9. Definition of Done

The eight always-blocking gates in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; this slice does not restate them. It maintains `docs/superpowers/verification/2026-06-29-catalog-graph-filters/dod.md` (status) and `gate-findings.yaml` (per-finding real/delusion telemetry), copied from the templates at slice start. Slice-specific gate notes:
- **Gate 3 (full suite + real-seam):** real-seam tier **N/A** — frontend-only, nothing new touches HTTP/auth/DB/middleware (§7). Full unit + component + architecture suite must be green.
- **Gate 4 (container build):** web image `npm run build` green; **no API rebuild / codegen** (no endpoint change) (§7.3).
- **Gate 6 (mutation):** **N/A** — no C# Domain/Application change (§7.4).
- **ADR-0084 manual pass** required (UI slice, §7.5) — flag pending if dev stack unavailable + needs a seeded graph.

Run `scripts/ci-local.sh` (Release mirror) green before push. After gate 9, re-run build + full suite (terminal re-verify).

---

## 10. Out of scope (explicit deferrals)

- **Status filter** (Lifecycle for apps / Health for services) — deferred; needs a `GraphNodeDto` enrichment (project the entity's status onto the node) + a clearer combined-status model. Revisit as a follow-up.
- **Origin filter** — deferred; every relationship is `Manual` today, so the control would be dead until Scan/Agent relationships exist (Phase 2 / E-06).
- **Domain / Criticality filters** — deferred; no backing field exists. Introducing them is a new-field epic (Application+Service migrations + create/edit UI + the ADR-0107 field-addition trigger on the Applications/Services list screens), not a filter slice.
- **Hide (vs dim) mode**, saved filter presets, URL-shareable filtered deep-links (sessionStorage chosen — Decision 3), filtering by depth/tier, filtering on edge `type`, search-within-graph.
- Any backend change, new endpoint, query param, or codegen.

---

## 11. Implementation order (rough — finalised by writing-plans)

1. Copy `dod.md` + `gate-findings.yaml` templates into `verification/2026-06-29-catalog-graph-filters/`.
2. `graphFilter.ts` + `graphFilter.test.ts` (TDD: match/dim semantics, focus-exempt, edge rule, empty-facet).
3. `useGraphFilters.ts` + `useGraphFilters.test.ts` (TDD: per-focus persist/restore, clear, safe default).
4. `graphModel.ts` `dimmed?` + `graphLayout.ts` thread-through + `EntityGraphNode.tsx` dimmed variant (update its existing test if the variant changes rendering).
5. `GraphFilterControls.tsx` + `GraphFilterControls.test.tsx`.
6. `GraphExplorerPage.tsx` wiring (`useGraphFilters` → `applyGraphFilters` → `<Panel>`; dimmed-edge style) + `GraphExplorerPage.test.tsx` additions.
7. `npm run build` + full suite + `scripts/ci-local.sh` green.
8. Update `docs/design/list-filter-registry.md` (`/graph` row → built + deferrals) and `docs/product/CHECKLIST.md` (mark E-04.F-02.S-05).
9. Playwright MCP pass (or flag pending + seed note); push → PR → DoD gates (update `dod.md` + `gate-findings.yaml` as each runs); terminal re-verify.

---

## 12. Self-review

**Spec coverage:** every §3 decision traces to §4–§9. The §7 test files are named modules in §4.4 that writing-plans will turn into tasks (`graphFilter.test`, `useGraphFilters.test`, `GraphFilterControls.test`, `GraphExplorerPage.test`).

**Placeholder scan:** no TBD/TODO. §4–§5 code is illustrative; final code lands in executing-plans. Exact org-teams hook name and Untitled UI multi-select primitive are pinned in writing-plans against the live `CatalogListPage`/`FilterBar` usage.

**Internal consistency:**
- Frontend-only / client-side / zero-backend consistent across §1, §3 (#1/#2), §4, §7 (real-seam N/A), §9.
- Dim-not-hide + focus-never-dims + edge-dims-if-either-endpoint consistent across §3 (#5/#6), §4.2, §5.2/§5.5, §6, §7.1.
- sessionStorage-per-focus + separate hook consistent across §3 (#3), §4.1, §5.1, §7.1.
- Kind+Team-only + deferrals consistent across §1.1, §3 (#1), §8, §10.
- Position-stability (data-only annotation) consistent across §3 (#9), §4.3.

**Scope check:** single PR, frontend-only, 3 created + 4 modified files, ~315 LOC production, no dep/codegen — comfortably under the 800 ceiling.

**Ambiguity check:**
- Facet semantics pinned (AND across / OR within / empty = unconstrained; null-team behaviour) — §3 #10, §4.2.
- Edge-dim rule pinned (either endpoint) — §3 #6, §4.2.
- Live-apply (no submit) pinned — §3 #8, §5.3.
- Team options = all org teams (stable) pinned — §3 #11, §5.3.
- Filter persistence (sessionStorage, not URL) pinned — §3 #3, §10.

**No blocking issues found.**
