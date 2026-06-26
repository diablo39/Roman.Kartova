# Slice — Catalog: embedded dependency mini-graph

**Date:** 2026-06-26
**Stories:** E-04.F-02.S-01 (embedded 1-level mini dependency graph on the entity page) + the E-04.F-02.S-02 *"table below mini-graph"* arrangement (the Dependencies/Dependents **table** itself already shipped in Slice 1b — this slice puts the graph above it).
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-dependency-mini-graph`
**Follows:** Slice 1b (`2026-06-25-catalog-relationships-ui-surface-design.md`, relationship UI surface — on master).
**Governing ADR:** ADR-0040 (Two-View Dependency Graph Navigation — embedded 1-level mini-graph + deferred standalone explorer).

---

## 1. Goal

Turn the relationship **data** that 1a/1b landed into the thing that makes a dependency catalog useful: a **read-only, 1-hop dependency graph** rendered on both the Application and Service detail pages, directly above the existing Dependencies/Dependents tables. A developer sees, at a glance, what the focused entity depends on (right) and what depends on it (left), with directed edges labelled by relationship type, and can click any neighbour to navigate to its detail page.

This is the **embedded** half of ADR-0040 only. The standalone `/graph` explorer (zoom/pan/filters/path-finding/impact analysis — E-04.F-02.S-03–06) is explicitly deferred to a later, dedicated slice.

The slice is **frontend-only**: it reuses 1b's relationship list endpoint and hooks verbatim, introduces no API change, no backend change, and no codegen. The one new moving part is the graph renderer (`@xyflow/react`) and a pure data-mapper that projects relationships into a node/edge model.

---

## 2. Pre-requisites (already on master)

- **Relationship UI surface (Slice 1b):**
  - `web/src/features/catalog/api/relationships.ts` — `useRelationshipsList({ entityKind, entityId, direction, limit })` (cursor list over `GET /api/v1/catalog/relationships`), `relationshipKeys`, exported types `RelationshipResponse` and `RelationshipDirection`.
  - `web/src/features/catalog/relationships/relationshipTypeRules.ts` — `relationshipTypeLabel` (`{ DependsOn: "Depends on", PartOf: "Part of" }`) and the `RelationshipKind` type (`"application" | "service"`, camelCase wire per ADR-0109).
  - `web/src/features/catalog/components/RelationshipsSection.tsx` — the Dependencies/Dependents tables, already inserted on both detail pages.
- **`RelationshipResponse` shape:** `{ id, source: { kind, id, displayName }, target: { kind, id, displayName }, type, origin, createdByUserId, createdAt }`. Each endpoint carries `kind` + `id` + `displayName` — exactly what a node needs, and **nothing more** (no lifecycle/health — see Decision 9).
- **Endpoint (unchanged, consumed):** `GET /api/v1/catalog/relationships?entityKind=&entityId=&direction=outgoing|incoming|all` → `CursorPage<RelationshipResponse>`. `direction=all` returns every edge where the entity is source **or** target — the entire 1-hop neighbourhood in one query.
- **Detail pages:** `ApplicationDetailPage.tsx` / `ServiceDetailPage.tsx` — `<Card><CardHeader/><CardContent className="space-y-6">…sections…</CardContent></Card>`; both load the entity (`id` from `useParams`, `displayName` available) and already render `<RelationshipsSection>`; both pass an `entityKind` value to it (reuse that exact value for the graph so the two stay consistent).
- **Navigation:** entity detail routes are `/catalog/applications/:id` and `/catalog/services/:id` (the `entityLink(kind,id)` helper convention from 1b).
- **Frontend stack:** React 19 + TypeScript strict, Vite, React Router v7, TanStack Query (ADR-0039); Untitled UI primitives (ADR-0094); `tsc -b` (`npm run build`) is the binding type gate (ADR-0109).

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **`@xyflow/react` (React Flow) as the graph renderer**, added now. | User-confirmed. Sets up the standalone explorer (ADR-0040, one shared renderer) and matches the architecture prose. ADR-0040 calls the library "an implementation detail"; React Flow's prior "chosen" anchor (ADR-0088) is superseded by ADR-0094, but the choice stands and is re-affirmed here. |
| 2 | **One shared `<DependencyMiniGraph entityKind entityId displayName>`** rendered on both detail pages; **no new routes**. | The view is identical for both entity kinds; mirrors 1b's single shared `<RelationshipsSection>`. |
| 3 | **Embedded mini-graph only.** Standalone `/graph` explorer + "Open full graph" button + filters + path-finding + impact analysis (S-03–S-06) deferred to a dedicated later slice. | User-confirmed. `/graph` does not exist; a button to a dead route is worse than none. The explorer is a slice of its own (filters/path-finding are S-04–S-06). |
| 4 | **Hand-computed deterministic layout** (dependents column left → focused node centre → dependencies column right); **no layout engine** (dagre/elk). | 1-hop does not warrant a layout engine; fixed positions are deterministic and unit-testable. Left→right is the Backstage/standard "blast-radius" convention. |
| 5 | **One combined graph** (both directions around the focused node), not two separate graphs. | ADR-0040 "1-level neighbourhood". The split Dependencies/Dependents *tables* already give the per-direction list; the graph's value is the combined picture. |
| 6 | **Read-only canvas.** Add/delete edges stays in the tables below; node click → navigate to that entity. | ADR-0040 "read-only". Avoids duplicating 1b's add/delete affordances on a second surface. |
| 7 | **Reuse the existing `direction=all` list endpoint; no backend, no codegen.** | The 1-hop neighbourhood is exactly one `direction=all` page. No new field, no new route → frontend-only (mirrors the Service-UI-surface / list-filter slices). |
| 8 | **Neighbour cap = 50** (single fetch `limit:50`); if the page has a next cursor, render an **"+N more — see the tables below"** note instead of paging the canvas. | It is a *mini* graph; a node-saturated canvas is unreadable. The tables (and the future explorer) handle scale. Cap is a single tunable constant. |
| 9 | **Nodes show kind + displayName only** — no lifecycle/health badge. | `RelationshipResponse` carries only `{kind,id,displayName}` per endpoint. Lifecycle/health would require a backend enrichment, breaking the frontend-only boundary. Explicit deferral. |
| 10 | **Tables remain the accessible source of truth; the canvas is a visual aid.** | React Flow canvases are weak for screen readers; the SR-accessible path is the table directly below, showing the same data. Noted, not separately solved. |
| 11 | **Lazy-load the React Flow-bearing component** (`React.lazy` + `Suspense`, fallback = the graph skeleton) so the canvas library is a separate chunk fetched only on detail routes, not in the initial app bundle. | Directly serves ADR-0040's "keep entity pages fast" intent and contains the bundle cost of the new dependency. |
| 12 | **React Flow is mocked in unit/component tests; Playwright MCP is the real render check.** | React Flow measures DOM dimensions (ResizeObserver) and does not render meaningfully in jsdom — same class of gap as the react-aria `Table`/`isRowHeader` issue. The pure mapper carries the logic coverage; the real canvas is verified in a browser. |

---

## 4. Architecture

### 4.1 Routes & navigation

**None added.** The graph is embedded in the two existing detail pages, above `<RelationshipsSection>`.

### 4.2 Data flow

```
ApplicationDetailPage / ServiceDetailPage   (id from useParams, entity loaded)
  └ <DependencyMiniGraph entityKind entityId displayName>     (lazy-loaded, Decision 11)
       useRelationshipsList({ entityKind, entityId, direction: "all", limit: 50 })
         → toGraphModel(focused = { kind: entityKind, id: entityId, displayName },
                        items, { cap: 50 })
              nodes : focused (centre, emphasised)
                    + unique neighbours — dependents (left) / dependencies (right)
              edges : one per relationship; arrow follows source → target;
                      label = relationshipTypeLabel[type]
              overflow : neighbours dropped beyond cap (or next-cursor present)
         → <ReactFlow nodes edges nodeTypes={{ entity: EntityGraphNode }}
                      fitView nodesDraggable={false} … >    // read-only
              node onClick → navigate(`/catalog/${kind === "application" ? "applications" : "services"}/${id}`)
       states : loading skeleton · empty placeholder · inline fetch error · overflow note
  └ <RelationshipsSection …>     (unchanged, renders below)
```

### 4.3 File map

**Created — frontend (`web/src/features/catalog/`):**

| File | Purpose | ~LOC |
|---|---|---|
| `relationships/graphModel.ts` | Pure `toGraphModel(focused, relationships, { cap }) → { nodes, edges, overflow }`. Dedupes neighbours by `(kind,id)`; assigns column/side (left = dependent, right = dependency; a node that is both lands on the dependency side with the incoming edge curving back); computes deterministic `{x,y}` positions; maps each edge to `{ id, source, target, label, markerEnd }`. No React import. | 80 |
| `components/EntityGraphNode.tsx` | Custom React Flow node: kind icon (App vs Service) + displayName; `focused` variant (distinct border/fill); handles for edge anchoring. | 50 |
| `components/DependencyMiniGraph.tsx` | Fetches via `useRelationshipsList(direction:"all")`, builds the model, renders `<ReactFlow>`; loading / empty / error / overflow states; node-click → `useNavigate`. Imports React Flow base CSS. | 150 |

**Created — frontend tests (gate-5 / gate-2 artifacts):**

| File | Purpose |
|---|---|
| `relationships/__tests__/graphModel.test.ts` | Exhaustive mapper coverage (see §7.1). |
| `components/__tests__/DependencyMiniGraph.test.tsx` | React Flow mocked: correct nodes/edges passed; empty / error / overflow states; node-click → navigate. |

**Modified — frontend:**

| File | Change |
|---|---|
| `pages/ApplicationDetailPage.tsx` | Insert lazy `<DependencyMiniGraph entityKind=<app kind value> …>` above `<RelationshipsSection>`. |
| `pages/ServiceDetailPage.tsx` | Same, with the Service `entityKind` value. |
| `web/package.json` + lockfile | Add `@xyflow/react` (latest compatible with React 19; version pinned in executing-plans). |

**Estimate ≈ 280 LOC frontend production** (excl. tests, lockfile). Comfortably under the ~800 ceiling; no backend, no generated client.

---

## 5. Components

### 5.1 `graphModel.ts` (pure)

```ts
export type GraphSide = "focused" | "dependency" | "dependent";
export type GraphNode = { id: string; kind: RelationshipKind; entityId: string;
                          displayName: string; side: GraphSide; position: { x: number; y: number } };
export type GraphEdge = { id: string; source: string; target: string; label: string };
export type GraphModel = { nodes: GraphNode[]; edges: GraphEdge[]; overflow: number };

export function toGraphModel(
  focused: { kind: RelationshipKind; id: string; displayName: string },
  relationships: RelationshipResponse[],
  opts?: { cap?: number },
): GraphModel;
```

- The **focused** node is `(focused.kind, focused.id)`, centre column.
- For each relationship, the **other** endpoint is the one that is not the focused entity. If the focused entity is the `source`, the other endpoint is a **dependency** (right); if it is the `target`, the other endpoint is a **dependent** (left).
- Neighbours are **deduped by `(kind,id)`** (an entity reachable by multiple edges appears once; a node that is both a dependency and a dependent is placed on the dependency side). Every relationship still yields its own edge.
- Positions: focused at centre; dependents stacked down the left column, dependencies down the right column, even vertical spacing.
- `overflow` = neighbours dropped beyond `cap` (default 50). Edges to dropped neighbours are dropped with them.
- Node React Flow `id` = `${kind}:${entityId}` (stable, unique, drives dedupe). Edge `id` = the relationship `id`.

### 5.2 `EntityGraphNode.tsx`

Custom node registered as `nodeTypes.entity`. Renders the kind icon + `displayName`; the `focused` node gets an emphasised style. Source/target handles positioned for the left→right flow. Click is handled at the `<ReactFlow onNodeClick>` level (navigate), not inside the node.

### 5.3 `DependencyMiniGraph.tsx`

Props `{ entityKind, entityId, displayName }`. `useRelationshipsList({ entityKind, entityId, direction: "all", limit: 50 })` → `toGraphModel`. Renders a card with:
- **loading** → skeleton box (also the `Suspense` fallback);
- **error** → inline error text (the tables below still render);
- **empty** (no edges) → compact "No dependencies yet" placeholder;
- **populated** → `<ReactFlow>` (read-only: `nodesDraggable={false}`, `nodesConnectable={false}`, `fitView`, controls/minimap omitted for a mini view), with an **overflow note** beneath when `overflow > 0`.

`onNodeClick` reads the node's `(kind, entityId)` and navigates to the entity detail route.

---

## 6. Error handling (frontend surfacing)

| Condition | UI behaviour |
|---|---|
| relationship list fetch error | inline error text inside the graph card; `<RelationshipsSection>` below renders independently. |
| no relationships | "No dependencies yet" placeholder (not a blank canvas). |
| > cap neighbours | render the first `cap`; show "+N more — see the tables below". |

No new `ProblemDetails` types; the endpoint and its error mapping are unchanged from 1a/1b.

---

## 7. Testing strategy (gate-2 / gate-5 artifacts)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md).

**Real-seam tier — N/A (no wiring).** This slice wires **no** HTTP/auth/DB/middleware: it adds no endpoint, no handler, no migration, no auth gate. It consumes an endpoint that already has real-seam coverage from 1a/1b (`CreateRelationshipTests`/`ListRelationshipsTests`/`DeleteRelationshipTests`). So the gate-3 real-seam **addition** is not in scope; the full existing suite must still be green.

### 7.1 Unit — `graphModel.test.ts` (pure, strong oracle)
- empty relationships → `{ nodes: [focused], edges: [], overflow: 0 }`.
- outgoing-only → neighbours on the **dependency** (right) side; arrows focused → neighbour.
- incoming-only → neighbours on the **dependent** (left) side; arrows neighbour → focused.
- mixed in/out → correct side per edge; correct edge count.
- `PartOf` (`Service → Application`) → label "Part of"; direction preserved.
- **duplicate neighbour across two edges** (e.g. a cycle: focused depends-on X *and* X depends-on focused) → X is **one** node, **two** edges.
- cap overflow → exactly `cap` neighbours kept, `overflow` = remainder, dropped edges absent.
- defensive: a relationship that does not reference the focused entity is ignored (never produces a self-loop on focused).

### 7.2 Component — `DependencyMiniGraph.test.tsx` (React Flow mocked)
- `vi.mock("@xyflow/react")` → assert `<ReactFlow>` receives the expected `nodes`/`edges` for a stubbed query result.
- empty / error / overflow states render the right copy.
- node-click handler invokes `navigate` with the correct detail route for the clicked `(kind,id)`.
- ≥1 happy + ≥1 negative (error state) — the per-unit floor.

### 7.3 Container build (gate 4)
No Dockerfile/`COPY` change, but the web image compiles TS and restores deps — `@xyflow/react` must be in `package.json` **and** the lockfile, and `npm run build` (`tsc -b` + vite) must be green so the image build restores and type-checks the new dependency.

### 7.4 Mutation (gate 6) — N/A
The diff is frontend TypeScript only; no C# Domain/Application change. Stryker.NET is backend-scoped, so the mutation gate does not apply (documented reason, not a skip).

### 7.5 Manual verification (ADR-0084)
Playwright MCP, **cold-start dev server first** (HMR can mask config/dep errors):
- Application detail page with dependencies → graph renders, focused node centred, neighbours on correct sides, edges labelled.
- Click a neighbour node → navigates to its detail page.
- Service detail page → same.
- Entity with no relationships → "No dependencies yet" placeholder.
- Console clean.

Flagged **pending user verification** if the dev stack is unavailable in-session.

---

## 8. List surface (ADR-0095 / ADR-0107)

**N/A.** The mini-graph is not a list screen and adds no queryable/user-facing field to any entity — it re-projects the relationship data 1a already registered in `docs/design/list-filter-registry.md`. The field-addition trigger does not fire; **no registry change**.

---

## 9. Definition of Done

The eight always-blocking gates as defined in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; this slice does not restate them. Slice-specific gate notes:
- **Gate 3 (full suite green):** applies; the real-seam **addition** is N/A (no HTTP/auth/DB wiring — §7).
- **Gate 4 (container build):** applies — the new dependency must restore and type-check in the web image (§7.3).
- **Gate 6 (mutation):** **N/A** — no C# Domain/Application change (§7.4).

Run `scripts/ci-local.sh` (Release mirror; `frontend` subset is the relevant one) green before push.

---

## 10. Out of scope (explicit deferrals)

- Standalone `/graph` explorer, "Open full graph" button, graph filters (team/domain/criticality/origin), path-finding, visual impact analysis (E-04.F-02.S-03–06).
- Lifecycle/health (or any field beyond `{kind,id,displayName}`) on nodes — needs backend enrichment of the relationship list or per-node fetches.
- Multi-hop / expandable neighbours — ADR-0040 pins the embedded view to 1 level.
- Add/delete/edit edges from the canvas — stays in the 1b tables.
- Any backend change, new endpoint, or codegen.
- Layout engine (dagre/elk), saved views, in-graph search, cycle detection / graph analytics.

---

## 11. Implementation order (rough — finalised by writing-plans)

1. Add `@xyflow/react` to `package.json`; import its base CSS where the graph renders; confirm `npm run build` green.
2. `relationships/graphModel.ts` + `graphModel.test.ts` (TDD, RED → GREEN) — the logic core.
3. `components/EntityGraphNode.tsx`.
4. `components/DependencyMiniGraph.tsx` (fetch + states + navigate) + `DependencyMiniGraph.test.tsx` (React Flow mocked).
5. Wire lazy `<DependencyMiniGraph>` into `ApplicationDetailPage.tsx` + `ServiceDetailPage.tsx`, above `<RelationshipsSection>`.
6. `npm run build` + `scripts/ci-local.sh` frontend subset green; Playwright MCP manual pass; update `docs/product/CHECKLIST.md` (mark E-04.F-02.S-01; note the S-02 "table below graph" arrangement); push → PR → DoD gates.

---

## 12. Self-review

**Spec coverage:** every §3 decision traces to §4–§9; both gate-5/gate-2 test files in §7 are named files in §4.3 that writing-plans will turn into tasks.

**Placeholder scan:** no TBD/TODO. §5 code is illustrative; final code lands in executing-plans. `@xyflow/react` version intentionally left to executing-plans (pin latest React-19-compatible).

**Internal consistency:**
- Frontend-only / no-backend / no-codegen consistent across §1, §3 #7, §4, §7, §9.
- React Flow renderer + lazy-load consistent across §3 #1/#11, §4.2, §5.3, §7.2/§7.3.
- `RelationshipResponse` carrying only `{kind,id,displayName}` per endpoint is the stated reason for both the node content (§3 #9) and the no-backend boundary (§3 #7).
- 1-hop / read-only / tables-stay-below consistent with ADR-0040 and the two answered scope questions.

**Scope check:** single PR; 3 created + 2 modified production files + 1 dependency add; ~280 LOC production, under the 800 ceiling.

**Ambiguity check:**
- Neighbour that is both dependency and dependent resolved (one node, both edges, placed on the dependency side — §5.1, unit-tested §7.1).
- Overflow resolved to a cap + "+N more" note pointing at the tables (§3 #8, §6).
- Node-click navigation target pinned per `kind` (§4.2, §5.3, tested §7.2).

**No blocking issues found.**
