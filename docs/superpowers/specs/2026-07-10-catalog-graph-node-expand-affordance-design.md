# Slice — Catalog: Node-level expand affordance on the Graph Explorer

**Date:** 2026-07-10
**Epic/Feature:** E-04.F-02 (Dependency graph) — extends S-04 (standalone explorer) / S-05 (filters)
**ADR context:** ADR-0040 (graph as a second view) — extends the explorer contract, no decision reversal.
**Related:** builds on `catalog-graph-explorer` (2026-06-27), `catalog-graph-explorer-sidebar`, `catalog-graph-filters` (2026-06-29), `catalog-derived-dependencies` (2026-07-09).

## 1. Goal

On the standalone Dependency Graph Explorer (`/graph`), each node box surfaces **at-a-glance whether it has unloaded in/out dependencies**, and lets the user **expand/collapse — and set-focus / open — directly from the box**, instead of only through the sidebar.

Two on-node affordances (interaction model "A — Hybrid", confirmed 2026-07-10):
- **Active edge chevrons** — ◂ (dependents / in) on the left, ▸ (dependencies / out) on the right. Clicking one toggles expand↔collapse in that direction (one-click shortcut).
- **A ⋯ menu button** — opens a context menu with Expand/Collapse dependencies, Expand/Collapse dependents, Set as focus, Open page ↗.

## 2. Scope

**In scope:** the standalone `/graph` explorer only (`GraphExplorerPage` + `EntityGraphNode` + graph contract + traversal handler).

**Out of scope (explicit deferrals):**
- Detail-page **mini-graph** (`DependencyMiniGraph`, 1-hop, relationship-based via `toGraphModel`) — unchanged.
- **Sidebar** (`GraphExplorerSidebar`) — left as-is (**additive**, confirmed 2026-07-10). Its Expand/Collapse/Set-focus/Open-page buttons stay; the node menu duplicates them intentionally. No sidebar edit, no sidebar re-test churn.
- **Derived-edge counting** — the expandability signal counts explicit relationship edges only; derived depends-on edges ride along on expand but do not independently drive the affordance. Derived-aware counts deferred.

## 3. The scope-driving fact (why this is full-stack)

The explorer builds its graph by client-merging BFS responses (`mergeGraphs`). `GraphNodeDto` today carries `Kind, Id, DisplayName, Depth, TeamId` — **no degree signal**. The frontend can only count edges already loaded, not whether more exist beyond the traversal boundary. BFS **boundary nodes never have their neighbours fetched** (fetching them would discover the next depth), so degree cannot be inferred from the returned edges. An accurate "expandable?" icon therefore requires a backend addition. (Accurate-signal path chosen over optimistic, 2026-07-10.)

## 4. Architecture

### 4.1 Backend — degree on the node contract
- Add `OutDegree` and `InDegree` (`int`) to `GraphNodeDto` (positional record — one construction site, see §9).
- In `GraphTraversalHandler.Handle`, after `GraphTraversal.BuildAsync` returns and node refs are known, run **one batched, RLS-scoped count query** over the returned node ids (≤ `DefaultNodeCap` = 200):
  - `outDegree[nodeId]` = count of `relationships` rows where `Source.Id == nodeId`
  - `inDegree[nodeId]`  = count of `relationships` rows where `Target.Id == nodeId`
  - Single query: `db.Relationships.Where(r => ids.Contains(r.Source.Id) || ids.Contains(r.Target.Id))` projected to `(SourceId, TargetId)`, grouped in memory. (Avoids two round-trips; bounded by the node cap.)
  - Direction convention matches the explorer: **out = node is edge Source = "dependencies"**, **in = node is edge Target = "dependents"** (consistent with `direction=outgoing/incoming` in `graph.ts`).
- Enrich each `GraphNodeDto` with its degrees alongside the existing displayName/teamId enrichment (same loop).
- Regenerate the OpenAPI snapshot + codegen client to expose the two fields.

### 4.2 Frontend — merge, compute, render, wire
- **`graphMerge.ts`**: `ExplorerNode` carries `outDegree`/`inDegree` (from `GraphNodeDto`).
- **`GraphExplorerPage`**: for each node compute, from the merged edge set,
  - `loadedOut` = # merged edges with `source === nodeId`; `loadedIn` = # with `target === nodeId`;
  - `expandableOut = loadedOut < outDegree`; `expandableIn = loadedIn < inDegree`;
  - `unloadedOut = outDegree − loadedOut` (menu count); `unloadedIn` likewise;
  - per-direction `isExpanded` from `useExplorerState`.
  Pass these into node data (`GraphNodeData`).
- **`EntityGraphNode`** renders:
  - **Chevron per direction** shown when `expandable(dir) || isExpanded(dir)`:
    - collapsed + more to load → **expand** icon (◂/▸); click → `toggleExpand(node, dir)`.
    - expanded → **collapse** icon; click → `toggleExpand(node, dir)`.
    - collapsed + nothing to load → **hidden**.
    - disabled when `atCap && !isExpanded(dir)` (mirrors sidebar cap rule).
  - **⋯ menu button** (react-aria `Menu`/`MenuTrigger` + `Popover`, **portal-rendered** so React Flow zoom/pan doesn't clip or scale it): Expand/Collapse dependencies (`unloadedOut`), Expand/Collapse dependents (`unloadedIn`), Set as focus, Open page ↗. Expand rows disabled at cap; label flips to Collapse when that direction is expanded.
  - **React Flow hygiene:** chevrons and ⋯ carry `nodrag nopan` and `stopPropagation` so interacting with them does **not** trigger node-select; clicking the box body still selects → opens the sidebar (unchanged).
- **`GraphActionsContext`** (new, small): provides `toggleExpand`, `select`, `navigateToFocus`, `atCap`, `entityDetailPath`. `EntityGraphNode` consumes it instead of receiving handlers via node `data` — keeps node objects stable for React Flow memoization and avoids stale closures.

### 4.3 New / edited files
- Backend: `GraphResponse.cs` (contract, +2 fields), `GraphTraversalHandler.cs` (count query + enrichment). Integration + unit tests.
- Frontend: `EntityGraphNode.tsx` (chevrons + menu), new `GraphActionsContext.tsx`, `GraphExplorerPage.tsx` (compute + provide context), `graphMerge.ts` (carry degrees), `graphModel.ts` (`GraphNodeData` fields). Vitest specs for the node + page wiring.
- Generated: `web/src/generated/openapi.*` + `web/openapi-snapshot.json` regenerated.

## 5. Contracts

```csharp
public sealed record GraphNodeDto(
    EntityKind Kind, Guid Id, string DisplayName, int Depth, Guid? TeamId,
    int OutDegree, int InDegree);   // NEW: total explicit-relationship degree, per direction
```
No new endpoint, no new query param, no permission change (read-only `GET /catalog/graph`). No DB migration (query-only).

## 6. Error semantics / edge cases
- Focus node: its direct neighbours are already loaded (depth-2, direction=all), so its chevrons are typically hidden — the same truthful logic applies uniformly, no special-case.
- A node whose out-edges all point at already-present nodes but whose edges weren't returned by any fetch may still read `expandableOut` (loaded edge < degree); expanding then reveals a hidden **edge** (no new node). Accepted — surfacing a hidden dependency edge is legitimate.
- `atCap` (≥150 merged nodes): expand affordances disabled (chevron + menu), collapse still allowed — matches existing sidebar behavior.

## 7. Testing (gate-5 artifacts, per [TESTING-STRATEGY](../../TESTING-STRATEGY.md))
- **Backend real-seam integration** (`KartovaApiFixtureBase`, real Postgres/RLS + real JWT): seed a graph where a returned node is at the traversal **boundary** (its neighbours are absent from the node list); assert its `OutDegree`/`InDegree` still report the true totals. ≥1 happy (multi-edge node, correct in/out split) + ≥1 negative/isolation (cross-tenant relationships do not inflate degree under RLS).
- **Backend unit:** degree grouping (source→out, target→in) incl. a node that is both source and target of different edges.
- **Frontend vitest:** chevron visibility matrix (expandable / expanded / hidden / at-cap-disabled), one-click toggle fires `toggleExpand(node, dir)`, menu enable/disable + Collapse-label + unloaded counts, ⋯ click does not select the node (stopPropagation), focus node hides chevrons.
- No Dockerfile/`COPY` change → container-build gate is unaffected (still runs on PR).

## 8. Impact Analysis (codelens/LSP)
- `GraphNodeDto` is a positional record. Grounded via `find_references` (index reported `project:""` = stale → confirmed with `Grep`): **exactly one construction site** — `GraphTraversalHandler.cs:83`. No test constructs it directly (tests assert on deserialized responses). Adding the two positional params updates that single call site.
- `GraphResponse` / `GraphTraversalHandler.Handle` — behavior extended, signature of `Handle` unchanged; existing graph integration tests continue to pass and gain the new degree assertions.
- Frontend has no code-intelligence MCP — `EntityGraphNode`/`GraphExplorerPage`/`graphMerge`/`graphModel` consumers scoped by Grep/Read during the plan.

## 9. Definition of Done
Governed by CLAUDE.md's ten always-blocking gates (gate 6 conditional). This slice **touches Application/Infrastructure C#** (`GraphTraversalHandler`) → **gate 6 (mutation) is blocking** for the changed backend logic. DoD ledger + gate-findings at `docs/superpowers/verification/2026-07-10-catalog-graph-node-expand-affordance/`.

## 10. Follow-ups (out of this slice)
- **FU-1** Derived-aware expandability (count derived depends-on edges into the out-degree signal).
- **FU-2** Apply the same on-node affordance to the detail-page mini-graph, if desired.
- **FU-3** Deterministic expand/collapse E2E flow in the nightly Playwright suite (`e2e/`), if gate-10 surfaces a regression worth locking.

## 11. Self-review
- Placeholders: none. · Internal consistency: additive-sidebar + active-chevrons + accurate-signal all reflected. · Scope: single slice, ~350–450 prod LOC, under the 800 ceiling. · Ambiguity: chevron state machine (§4.2) and direction convention (§4.1) made explicit.
