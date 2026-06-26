# Slice — Catalog: standalone dependency graph explorer

**Date:** 2026-06-26
**Stories:** E-04.F-02.S-03 (Open-full-graph button) + E-04.F-02.S-04 (standalone Dependency Graph Explorer `/graph`).
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-graph-explorer`
**Follows:** the embedded mini-graph slice (`2026-06-26-catalog-dependency-mini-graph-design.md`, on master) and the relationship UI/backend (Slices 1a/1b).
**Governing ADR:** ADR-0040 (Two-View Dependency Graph Navigation — embedded mini-graph + standalone explorer). Renderer per ADR-0094/ADR-0088 (React Flow / `@xyflow/react`).

---

## 1. Goal

Deliver the **second** half of ADR-0040: a standalone, full-content dependency **graph explorer** at `/graph?focus=<kind>:<id>`, reachable via an **"Open full graph"** button on each entity's mini-graph. Where the embedded mini-graph is a fixed 1-hop glance, the explorer lets a developer *navigate the graph itself* — pan/zoom, see a multi-hop neighbourhood, and **expand** any node to pull in its further neighbours, with the expanded set encoded in the URL so a given view is shareable and bookmarkable.

This slice introduces the **backend traversal endpoint** that ADR-0040's "share the same data model" line and the future impact-analysis story (E-04.F-02.S-06) both need: a depth-annotated, cycle-safe, RLS-scoped BFS over the relationships graph. The endpoint is general (depth-N); the explorer composes it via per-node expansion.

**Deferred to their own slices:** graph filters by team/domain/criticality/origin (S-05) and visual impact analysis / blast-radius (S-06).

---

## 2. Pre-requisites (already on master)

- **Relationships backend (Slice 1a):** `relationships` table (RLS + FORCE RLS, tenant-isolated), `db.Relationships` over the RLS-scoped `ITenantScope` connection, `Relationship` aggregate with `Source`/`Target` as `EntityRef { EntityKind Kind, Guid Id }`, `RelationshipType` (`DependsOn`/`PartOf`), `RelationshipOrigin` (`Manual`/…), `RelationshipDirection` (`Outgoing`/`Incoming`/`All`). `ICatalogEntityLookup.Find(kind, id)` resolves `{ DisplayName, TeamId }` for an endpoint (already used by `ListRelationshipsForEntityHandler` and the delete auth path).
- **Relationship list pattern:** `ListRelationshipsAsync` delegate (param parse + 400 validation for `entityKind`/`direction`), `ListRelationshipsForEntityHandler` (RLS query + batched display-name enrichment over distinct refs). The new endpoint mirrors both.
- **Endpoint conventions:** `CatalogModule.cs` maps catalog routes on the RLS-scoped `tenant` group; read endpoints use `RequireAuthorization(KartovaPermissions.CatalogRead)`. Contracts are camelCase on the wire (ADR-0109); enums serialize camelCase. `*Response`/`*Dto` types carry `[ExcludeFromCodeCoverage]` (enforced by `ContractsCoverageRules`).
- **Mini-graph frontend (today's slice):** `@xyflow/react` is a dependency; `EntityGraphNode` (custom node: kind icon + displayName, `focused` variant); `DependencyMiniGraph` (rendered on both detail pages, `<section aria-label="Dependency graph">` with an `<h3>` header); the relationship API hooks/types in `features/catalog/api/relationships.ts`; `relationshipTypeLabel` in `relationships/relationshipTypeRules.ts`.
- **Routing:** `src/app/router.tsx` declarative `<Routes>`; authenticated routes nest under `<ProtectedShell>` (nav rail + app shell). Detail routes `/catalog/applications/:id`, `/catalog/services/:id`.
- **Frontend stack:** React 19 + TS strict, Vite, React Router v7, TanStack Query (ADR-0039), Untitled UI primitives (ADR-0094); `tsc -b` (`npm run build`) is the binding type gate (ADR-0109); generated client + `openapi-snapshot.json` are codegen artifacts (rebuild the API image to expose a new endpoint, then regenerate + commit).

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **New backend traversal endpoint** `GET /api/v1/catalog/graph?entityKind&entityId&depth&direction`, not a frontend BFS over the 1-hop list. | User-confirmed. A depth-N, depth-annotated traversal is the foundation S-06 (impact/blast-radius) needs, and a single server round-trip beats N client calls for a neighbourhood. |
| 2 | **Purpose-built `GraphResponse { nodes, edges, truncated }`**, nodes annotated with `depth` (hop distance from focus, focus=0) **and `teamId`**. | User-confirmed. Server dedups nodes and computes the blast-radius tier (`depth`) once; `teamId` pre-wires the S-05 team filter (`ICatalogEntityLookup` already resolves it, so near-zero extra cost). |
| 3 | **BFS, cycle-safe, bounded:** `depth` 1–4 (default **2**, out-of-range → 400), node cap **200** → `truncated:true`. `direction` reuses `RelationshipDirection` (default `all` = undirected traversal). | A *mini*-explorer, not a whole-org dump; bounds keep the canvas and the query predictable. Strict 400 validation mirrors the existing `direction` handling. Defaults user-approved. |
| 4 | **Standalone `/graph` route under `ProtectedShell`; canvas fills the content area** (keeps the nav rail). | ADR-0040 "full-screen" = fills the content region; staying in the shell is the consistent app experience and lets the user navigate away normally. |
| 5 | **URL is the single source of truth:** `?focus=<kind>:<id>` (root) + `?expand=<kind>:<id>,…` (drilled-into nodes). The rendered graph is a pure function of the URL. | User-chosen interaction model (expand, but URL-encoded). Shareable/bookmarkable, back/forward works, deterministic + testable. |
| 6 | **Expansion composes the endpoint:** focus fetched at depth 2; each `expand` node fetched at depth 1; results **merged** (union, dedup by `kind:id`). | One general endpoint serves both the initial view and incremental expansion. Per-node fetches are individually cached by TanStack Query. Collapse = drop a node from `expand` → the union recomputes from the URL. |
| 7 | **`@dagrejs/dagre` for layout** (LR, left→right), added now. | Multi-hop graphs need a real layout engine — the mini-graph's hand-computed 1-hop columns (its Decision 4) don't generalise. Dagre is the documented React Flow layout integration; LR matches the blast-radius convention. |
| 8 | **Read-only canvas.** Single-click a node → **expand/collapse** (toggle in `expand`); an explicit per-node **"open detail" link** → navigate to the entity page. Add/delete edges stays in the 1b tables. | ADR-0040 "read-only". A dedicated open-detail affordance avoids the click-vs-double-click race and keeps single-click for the primary explorer action (expand). |
| 9 | **Reuse `EntityGraphNode`** across mini-graph and explorer; the open-detail link is data-driven (`data.detailHref`) so it appears only in the explorer. | One shared node renderer (ADR-0040 "share the data model/visuals"); click semantics stay owned by each parent's `onNodeClick`. |
| 10 | **`depth` is returned + tested but not yet visualised** (no per-tier shading in slice 1). | Depth's consumer is S-06 impact analysis. Rendering it now is YAGNI; focused-vs-neighbour styling is enough for exploration. |
| 11 | **Lazy-load the `GraphExplorerPage` route** (`React.lazy` + `Suspense`). | Keeps dagre + the explorer page out of the initial bundle; mirrors the mini-graph's lazy boundary and ADR-0040's "keep entity pages fast" intent. |
| 12 | **React Flow + dagre mocked in component tests; the pure mappers (`graphMerge`, `graphLayout`) carry logic coverage; Playwright MCP is the real render check.** | React Flow measures DOM (ResizeObserver) and dagre needs real layout — neither renders meaningfully in jsdom (same gap noted for the mini-graph). |
| 13 | **No new permission** — `KartovaPermissions.CatalogRead`, same as the relationship list. RLS scopes the traversal to the tenant. | Reading the graph is reading the catalog; no new authority. |
| 14 | **Bounded aggregate, not a cursor list** — ADR-0095 does not apply; the bound is `depth` + node-cap + `truncated` (the `[BoundedListResult]` analogue). | The graph is a neighbourhood projection, not a paged list; cursor pagination is meaningless for it. Documented, not silently skipped. |

---

## 4. Architecture

### 4.1 Backend — traversal endpoint

```
GET /api/v1/catalog/graph?entityKind=&entityId=&depth=&direction=     [RequireAuthorization(CatalogRead)]
  GetCatalogGraphAsync (delegate)
    parse: entityKind→EntityKind (400 if invalid), entityId Guid (400 if empty),
           depth int default 2 (400 if not 1..4), direction→RelationshipDirection default All (400 if invalid)
    → new GraphTraversalQuery(new EntityRef(kind,id), depth, direction)
    → GraphTraversalHandler.Handle(query, db, lookup, ct)
         BFS over db.Relationships (RLS-scoped):
           level 0 = { focus }; visited = { focus }
           for level in 1..depth while node count < cap:
             frontier edges = relationships touching the previous level's nodes,
                              followed per `direction` (Outgoing: source→target,
                              Incoming: target→source, All: both)
             for each new endpoint not in visited: add node (depth=level), mark visited,
                              stop adding (truncated=true) at cap
             collect every traversed edge whose BOTH endpoints are in the node set
           enrich displayName + teamId for all node refs via ICatalogEntityLookup (per-node, N+1 bounded by node cap; batch deferred — consistent with ListRelationshipsForEntityHandler pattern)
    → 200 GraphResponse { nodes[], edges[], truncated }
```

- **Cycle safety:** the `visited` set means a back-edge (A→B→A) is traversed as an edge but never re-enqueues a node; BFS terminates.
- **Edge inclusion:** only edges between two included nodes are returned (no dangling edges to cap-dropped nodes). `direction` filters node *discovery* only; edge inclusion is intentionally undirected-among-kept-nodes — all relationships between any two surviving nodes are shown regardless of direction (cross-edges included by design; S-06 may add a directed-surface filter).
- **Node cap is a handler parameter** (default = the `GraphTraversalHandler.DefaultNodeCap` const, passed by the endpoint). A handler-level test can pass a small cap to exercise truncation without seeding 200 rows.
- **RLS:** `db.Relationships` is tenant-scoped by RLS; cross-tenant nodes/edges can never appear regardless of `depth`.

### 4.2 Frontend — explorer + button

```
<Route path="/graph" element={<GraphExplorerPage/>}>   (lazy, under ProtectedShell)
  GraphExplorerPage
    useSearchParams: focus="<kind>:<id>" (required), expand="<kind>:<id>,…" (optional)
      parse + validate kind ∈ {application, service}; bad/missing focus → empty prompt
    useGraph({ focus, expand }):
       useQueries: [ getGraph(focus, depth:2, all), ...expand.map(n => getGraph(n, depth:1, all)) ]
       → mergeGraphs(results) → { nodes, edges, truncated }     (pure: union dedup by kind:id / edge id)
    layoutGraph(nodes, edges) → positioned React Flow nodes      (pure: dagre LR)
    <ReactFlow nodes edges nodeTypes={{entity: EntityGraphNode}} fitView
               nodesDraggable={false} nodesConnectable={false} elementsSelectable={false}
               onNodeClick={toggleExpand}>     // add/remove node in ?expand → URL push
       <Controls/> <MiniMap/> <Background/>
    states: loading skeleton · error (with retry) · empty (lone focus node) · truncated banner
  EntityGraphNode: data.detailHref present (explorer) → render an "open detail" icon-link
                   (stopPropagation; navigates to /catalog/{apps|services}/{id})

DependencyMiniGraph (both detail pages):  section header gains an
  "Open full graph" Link → /graph?focus=<entityKind>:<entityId>     (S-03)
```

- **Expand/collapse:** `onNodeClick` on the focus node is a no-op (root); on any other node it toggles membership in `expand` and `setSearchParams` (history push, so back undoes an expansion). The graph re-derives from the new URL.
- **Merged depth:** `depth` is kept from the focus-rooted query; expand-discovered nodes carry no focus-relative depth (slice 1 doesn't render depth — Decision 10).

### 4.3 File map

**Created — backend:**

| File | Layer | Purpose | ~LOC |
|---|---|---|---|
| `Kartova.Catalog.Contracts/GraphResponse.cs` | Contracts | `GraphResponse { IReadOnlyList<GraphNodeDto> Nodes; IReadOnlyList<GraphEdgeDto> Edges; bool Truncated }`, `GraphNodeDto { string Kind; Guid Id; string DisplayName; int Depth; Guid? TeamId }`, `GraphEdgeDto { Guid Id; GraphEndpointDto Source; GraphEndpointDto Target; string Type; string Origin }`, `GraphEndpointDto { string Kind; Guid Id }`. All `[ExcludeFromCodeCoverage]`. | 55 |
| `Kartova.Catalog.Application/GraphTraversalQuery.cs` | Application | `record GraphTraversalQuery(EntityRef Focus, int Depth, RelationshipDirection Direction)`. | 15 |
| `Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs` | Infrastructure | BFS + enrichment (§4.1); `DefaultNodeCap = 200`. | 120 |

**Modified — backend:**

| File | Change |
|---|---|
| `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | `GetCatalogGraphAsync` delegate (parse/validate/dispatch, ~55 LOC). |
| `Kartova.Catalog.Infrastructure/CatalogModule.cs` | `tenant.MapGet("/graph", …).RequireAuthorization(CatalogRead).WithName("GetCatalogGraph").Produces<GraphResponse>(200).ProducesProblem(400/403)`; register `GraphTraversalHandler` (mirror `ListRelationshipsForEntityHandler` registration). |

**Created — frontend:**

| File | Purpose | ~LOC |
|---|---|---|
| `features/catalog/api/graph.ts` | `useGraph({focus, expand})` (TanStack `useQueries` over the generated `getCatalogGraph`), `graphKeys`, exported `GraphResponse` type alias. | 50 |
| `features/catalog/relationships/graphMerge.ts` | Pure `mergeGraphs(results) → { nodes, edges, truncated }` — union/dedup nodes by `kind:id`, edges by id. | 45 |
| `features/catalog/relationships/graphLayout.ts` | Pure `layoutGraph(nodes, edges) → positioned nodes` via dagre (LR). | 60 |
| `features/catalog/pages/GraphExplorerPage.tsx` | URL parse, fetch+merge+layout, `<ReactFlow>` + Controls/MiniMap, expand/collapse, states. | 165 |

**Modified — frontend:**

| File | Change |
|---|---|
| `app/router.tsx` | Lazy `<Route path="/graph" element={<GraphExplorerPage/>}>` under `ProtectedShell`. |
| `features/catalog/components/DependencyMiniGraph.tsx` | "Open full graph" Link in the section header → `/graph?focus=<kind>:<id>` (S-03). |
| `features/catalog/components/EntityGraphNode.tsx` | Optional data-driven "open detail" icon-link (`data.detailHref`). |
| `web/package.json` + lockfile | Add `@dagrejs/dagre` (+ `@types/dagre` if needed; pin in executing-plans). |
| `web/src/generated/openapi.ts` + `openapi-snapshot.json` | Regenerated for the new endpoint (rebuild API image first). |

**Created — tests:** see §7.

**Estimate ≈ 280 LOC backend + ≈ 345 LOC frontend production ≈ 625** (excl. tests, generated client, lockfile). Under the ~800 ceiling but on the high side — if executing-plans estimates over, the **URL-encoded expansion** (`mergeGraphs` + multi-query `useGraph` + collapse) is the clean split point: a slice 1 of *re-focus-only* explorer (single fetch) + a slice 2 adding expansion. Flagged, not pre-emptively split.

---

## 5. Components

### 5.1 `GraphTraversalHandler` (Infrastructure)
The logic core. Iterative BFS over `db.Relationships` (RLS-scoped), per-level frontier queries (bounded by frontier size, scales better than loading all tenant edges). Tracks `visited` (cycle safety), assigns `depth` per discovery level, enforces the node cap (`truncated`), includes only edges between two included nodes, then batch-enriches `displayName` + `teamId` via `ICatalogEntityLookup` (one `Find` per distinct ref — the established `ListRelationshipsForEntityHandler` pattern). If the mutation loop later flags survivors on pure bookkeeping, extract the visited/depth/cap logic into a pure helper + unit test (standard mutation remediation).

### 5.2 `GetCatalogGraphAsync` (delegate)
Mirrors `ListRelationshipsAsync`: parse `entityKind`→`EntityKind` (400), `entityId` non-empty Guid (400), `depth` int default 2 / range 1–4 (400), `direction`→`RelationshipDirection` default `All` (400); build the query; dispatch; `Results.Ok(graph)`.

### 5.3 `graphMerge.ts` (pure)
`mergeGraphs(results: GraphResponse[]) → { nodes, edges, truncated }`. Nodes deduped by `${kind}:${id}` (first wins; `depth` retained from the focus result, absent for expand-only nodes). Edges deduped by `id`. `truncated = results.some(r => r.truncated)`. No React import.

### 5.4 `graphLayout.ts` (pure)
`layoutGraph(nodes, edges)` runs dagre (`rankdir: "LR"`, sane node-size/sep constants) and returns each node with a computed `position`. Deterministic for a given node/edge set → unit-testable.

### 5.5 `GraphExplorerPage.tsx`
Reads `focus`/`expand` from the URL, calls `useGraph`, then `mergeGraphs` → `layoutGraph` → `<ReactFlow>` (read-only: `nodesDraggable/Connectable=false`, `fitView`, `Controls`+`MiniMap`+`Background`, `proOptions.hideAttribution`). `onNodeClick` toggles the clicked node in `expand` (no-op for the focus node) via `setSearchParams`. States: loading skeleton, inline error + retry, empty (focus with no neighbours → lone focus node + hint), truncated banner. Each non-focus node carries `data.detailHref` so `EntityGraphNode` renders the open-detail link.

### 5.6 `DependencyMiniGraph` change
Add an "Open full graph" `Link` (Untitled UI button/link, small) in the existing `<section>` header, beside the "Dependency graph" `<h3>`, targeting `/graph?focus=<entityKind>:<entityId>`. Shown whenever the mini-graph renders (both detail pages); harmless when there are no edges (the explorer shows the lone focus node).

---

## 6. Error handling

| Condition | Behaviour |
|---|---|
| invalid `entityKind` / `entityId` / `depth` / `direction` (backend) | `ProblemDetails` 400 (`ProblemTypes.ValidationFailed`), mirroring `ListRelationshipsAsync`. |
| unauthenticated | 401 (JwtBearer); missing `catalog.read` → 403. |
| graph fetch error (frontend) | inline error in the canvas area + a retry control; the rest of the shell is unaffected. |
| focus has no relationships | empty state: the lone focus node + "No dependencies yet" hint (not a blank canvas). |
| > cap nodes reachable | server returns the first `cap` (BFS order) with `truncated:true`; the page shows a "Showing a partial graph (limit N nodes)" banner. |
| malformed `focus` / unknown kind in URL | empty prompt ("Pick an entity to explore") rather than a crash. |

No new `ProblemDetails` types; reuses the relationship endpoints' error envelopes.

---

## 7. Testing strategy (gate-2 / gate-5 artifacts)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). **This is a wiring slice** (new HTTP endpoint + auth + DB traversal), so the real-seam tier **is in scope**.

### 7.1 Real-seam integration — `GetCatalogGraphTests` (gate 3/5)
`KartovaApiFixtureBase`, real Postgres/RLS + real `JwtBearer`. Seed a small graph, then assert over the HTTP response:
- **happy multi-hop:** focus→A→B and focus→C, `depth=2` → nodes `{focus(depth 0), A(1), C(1), B(2)}` with correct `displayName` + `teamId`; edges correct; `truncated=false`.
- **depth boundary:** `depth=1` → `B` excluded; `depth` out of 1–4 → 400.
- **direction:** `outgoing` vs `incoming` return the correct subset around the focus.
- **cycle:** A↔focus (both directions) → terminates, focus appears once, both edges present.
- **truncation:** handler-level test with a small cap → `truncated=true`, exactly `cap` nodes, dangling edges absent.
- **cross-tenant isolation:** relationships seeded in tenant B never appear for a tenant-A caller (RLS).
- **negatives:** invalid `entityKind` → 400; unauthenticated → 401. (≥1 happy + ≥1 negative — the wiring floor, exceeded.)

### 7.2 Backend unit
Direct `GraphTraversalHandler` tests for any logic not naturally covered by §7.1 (e.g. truncation with a small cap, edge-inclusion filtering). Pure-helper extraction only if the mutation loop demands it.

### 7.3 Frontend unit
- `graphMerge.test.ts` — union/dedup (same node across focus + expand results → one node; same edge → one edge), `truncated` OR-fold, empty inputs.
- `graphLayout.test.ts` — dagre wrapper assigns a `position` to every node, deterministic for a fixed graph.

### 7.4 Frontend component — `GraphExplorerPage.test.tsx` (React Flow + dagre + query mocked)
- renders the expected nodes/edges for a stubbed `useGraph` result;
- node click toggles the node in the `expand` search-param (URL updated);
- focus-node click is a no-op;
- open-detail link navigates to the entity route;
- empty / error / truncated copy; ≥1 happy + ≥1 negative (error).

### 7.5 Container build (gate 4)
New deps (`@dagrejs/dagre`) must be in `package.json` **and** the lockfile; the web image must `npm run build` (`tsc -b` + vite) green. The new endpoint requires rebuilding the **API image** so the generated client + `openapi-snapshot.json` regenerate against it.

### 7.6 Mutation (gate 6) — **blocking**
The diff touches Catalog **Application/Infrastructure** logic (the BFS traversal: direction switch, depth loop, cap/visited bookkeeping, edge inclusion). Run `/misc:mutation-sentinel` → `/misc:test-generator` on `GraphTraversalHandler` (+ helper if extracted); target ≥80% (`stryker-config.json`); document survivors.

### 7.7 Manual verification (ADR-0084)
Playwright MCP, **cold-start dev server first**:
- From an Application detail page → "Open full graph" → lands on `/graph?focus=…`, graph renders, focus emphasised.
- Click a neighbour → it expands (URL gains `expand=…`), new nodes appear, layout re-flows; click again → collapses (URL + graph revert); browser Back undoes an expansion.
- Open-detail link navigates to the entity page.
- Service focus → same. Entity with no relationships → lone focus node + hint. Truncation banner on a large graph (or noted pending seed data). Console clean.

Flagged **pending user verification** if the dev stack is unavailable in-session (DevSeed has 120 apps but few/no relationships — may need seeding a graph to see multi-hop; note in the plan).

---

## 8. List surface (ADR-0095 / ADR-0107)

**N/A.** `/graph` is not a list screen and the endpoint is a **bounded aggregate** (depth + node-cap + `truncated`), the `[BoundedListResult]` analogue — cursor pagination is meaningless for a neighbourhood projection (Decision 14). No new queryable/user-facing field is added to any entity that has a list screen (the node `teamId`/`depth` live only in the graph projection), so the **field-addition trigger does not fire** → no `list-filter-registry.md` change.

---

## 9. Definition of Done

The eight always-blocking gates as defined in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; this slice does not restate them. This slice maintains both `docs/superpowers/verification/2026-06-26-catalog-graph-explorer/dod.md` (status) and `gate-findings.yaml` (per-finding real/delusion telemetry) — copied from the templates at slice start. Slice-specific gate notes:
- **Gate 3 (full suite + real-seam):** real-seam tier **in scope** — new endpoint + auth + DB traversal (§7.1).
- **Gate 4 (container build):** new dep restores + type-checks in the web image; API image rebuilt for codegen (§7.5).
- **Gate 6 (mutation):** **blocking** — C# Application/Infrastructure traversal logic changed (§7.6).
- **ADR-0084 manual pass** required (UI slice, §7.7).

Run `scripts/ci-local.sh` (Release mirror) green before push.

---

## 10. Out of scope (explicit deferrals)

- **S-05** graph filters (team / domain / criticality / origin). (`teamId` is returned now to pre-wire the team filter, but no filter UI/param this slice.)
- **S-06** visual impact analysis / blast-radius highlighting (the returned `depth` is its foundation; no visualisation this slice).
- Depth/tier shading, saved views, in-graph search, graph analytics, multi-focus, layout-engine alternatives (elk), node drag-to-reposition persistence.
- Lifecycle/health on nodes (would need further backend enrichment).
- Add/delete/edit edges from the canvas (stays in the 1b tables).
- A top-level `/graph` nav entry (reached via the button + direct URL this slice; promoting to nav can follow once filters land).

---

## 11. Implementation order (rough — finalised by writing-plans)

1. Copy `dod.md` + `gate-findings.yaml` templates into `verification/2026-06-26-catalog-graph-explorer/`.
2. Backend contracts (`GraphResponse` + DTOs, `[ExcludeFromCodeCoverage]`); `GraphTraversalQuery`.
3. `GraphTraversalHandler` (TDD via real-seam + unit): BFS, depth, direction, cap/truncation, cycle, enrichment.
4. `GetCatalogGraphAsync` delegate + `CatalogModule` mapping + DI registration; real-seam `GetCatalogGraphTests` green.
5. Rebuild API image; regenerate generated client + `openapi-snapshot.json`; commit.
6. Add `@dagrejs/dagre`; `graphMerge.ts` + `graphLayout.ts` (+ unit tests, RED→GREEN).
7. `features/catalog/api/graph.ts` (`useGraph` multi-query).
8. `GraphExplorerPage.tsx` + `GraphExplorerPage.test.tsx`; lazy `/graph` route.
9. `EntityGraphNode` open-detail link; "Open full graph" button in `DependencyMiniGraph`.
10. `npm run build` + `scripts/ci-local.sh` green; mutation loop on the handler; Playwright MCP pass; update `docs/product/CHECKLIST.md` (mark E-04.F-02.S-03 + S-04); push → PR → DoD gates (update `dod.md` + `gate-findings.yaml` as each runs).

---

## 12. Self-review

**Spec coverage:** every §3 decision traces to §4–§9; the §7 gate-2/gate-5 test files are named files in §4.3 that writing-plans will turn into tasks (real-seam `GetCatalogGraphTests`, unit `graphMerge`/`graphLayout`, component `GraphExplorerPage.test`).

**Placeholder scan:** no TBD/TODO. §4–§5 code is illustrative; final code lands in executing-plans. `@dagrejs/dagre` version intentionally left to executing-plans (pin latest React-19-compatible). Truncation banner copy ("limit N nodes") is final-wording-flexible.

**Internal consistency:**
- New-backend-endpoint / wiring-slice / real-seam-in-scope consistent across §1, §3 #1, §4.1, §7.1, §9.
- Purpose-built depth+teamId response consistent across §3 #2, §4.3, §5.1, §7.1, and the S-05/S-06 deferrals in §10.
- URL-as-source-of-truth + expand/collapse consistent across §3 #5/#6, §4.2, §5.5, §7.4.
- dagre (new dep, pure layout util, mocked in tests) consistent across §3 #7/#12, §4.3, §5.4, §7.3/§7.4/§7.5.
- Mutation gate **blocking** justified by the C# traversal logic (§3 #1, §7.6, §9), unlike the mini-graph slice (frontend-only, N/A).

**Scope check:** single PR; 3 created + 2 modified backend, 4 created + 5 modified frontend, 1 dep add; ~625 LOC production, under the 800 ceiling with a flagged split point (§4.3) if executing-plans estimates higher.

**Ambiguity check:**
- Merged depth across roots resolved (focus-rooted depth retained; expand-only nodes carry none; not rendered in slice 1 — §4.2, #10).
- Click semantics pinned: single-click = expand/collapse, focus-node = no-op, open-detail = dedicated link (§3 #8, §5.5, tested §7.4).
- Truncation pinned to a server cap + `truncated` flag + banner; edge inclusion pinned to both-endpoints-present (§4.1, §6).
- `direction` for multi-hop pinned to per-hop edge-following (§4.1).

**No blocking issues found.**
