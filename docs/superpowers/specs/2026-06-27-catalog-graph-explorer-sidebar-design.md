# Slice — Catalog graph explorer: detail sidebar + directional expand + local state

**Date:** 2026-06-27
**Stories:** E-04.F-02.S-04 refinement (standalone explorer UX). No new backlog story — refines the explorer shipped on PR #50.
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-graph-explorer-sidebar`
**Follows:** `2026-06-26-catalog-graph-explorer-design.md` (the explorer v1, on PR #50 — must merge first).
**Governing ADR:** ADR-0040 (the standalone explorer "can evolve — saved views, path queries — without cluttering every page"). Renderer ADR-0094/ADR-0088 (React Flow).

---

## 1. Goal

Replace the explorer's v1 interaction (single-click = expand, URL-encoded `?expand` set, a per-node "Open ↗" link) with the conventional graph-explorer model: **click a node → select it → a right-hand detail sidebar**, with **directional expansion** driven from the sidebar. Explorer interaction state (the expand set + the selected node) moves from the URL to **`sessionStorage`**; only the entry `?focus` stays in the URL.

This buys three things: (1) clearer, more discoverable interaction (select / expand / open are distinct affordances, not overloaded onto one click); (2) a richer detail surface than a node can hold; (3) it finally uses the backend `direction` param (built in v1, unused by the UI) for **Expand dependencies / Expand dependents**. Dropping `?expand` from the URL also removes the URL-length concern entirely (superseding the v1 `?expand` cap follow-up).

**Frontend-only.** Reuses the existing `GET /api/v1/catalog/graph` endpoint (incl. `direction`) and the existing `useApplication`/`useService` detail hooks. **No backend change, no new endpoint, no codegen.**

---

## 2. Pre-requisites (on PR #50, must merge first)

- **Explorer v1:** `GraphExplorerPage.tsx` (`?focus`+`?expand`, `useGraph`, `mergeGraphs`→`layoutGraph`→read-only `<ReactFlow>` + Controls/MiniMap, `onNodeClick` toggles `?expand`); `api/graph.ts` `useGraph({focus, expand})` (`useQueries`: focus depth 2 + each expand node depth 1, all `direction:"all"`) → `{results, isLoading, isError, refetch}`; `relationships/graphMerge.ts` `mergeGraphs(GraphResponse[]) → ExplorerGraph {nodes, edges, truncated}`; `relationships/graphLayout.ts` `layoutGraph(graph, focusId) → {nodes: Node<GraphNodeData>[], edges}` (dagre LR; sets `data.side` + `data.detailHref`); `EntityGraphNode.tsx` (renders the optional `detailHref` link); `GraphNodeData` in `relationships/graphModel.ts`.
- **Backend (on master):** `GET /api/v1/catalog/graph?entityKind&entityId&depth&direction` — `direction` ∈ `outgoing|incoming|all`, depth 1–4, node cap 200 → `truncated`. RLS-scoped, `catalog.read`.
- **Entity detail hooks:** `useApplication(id)` / `useService(id)` (`features/catalog/api/applications.ts` / `services.ts`) → full `ApplicationResponse` / `ServiceResponse` (displayName, description, lifecycle, team, health for services).
- **Auth behaviour (the reason this design uses sessionStorage — verified):** `automaticSilentRenew: true` (`shared/auth/authConfig.ts`) renews the access token in the background via the Keycloak refresh token, no redirect → in-memory state survives routine expiry. But on silent-renew failure (idle past SSO-session timeout, or any 401) `ApiAuthBridge`'s `setUnauthorizedHandler` (`app/providers.tsx`) calls `signinRedirect({state:{returnTo}})` — a **full top-level redirect** that reloads the SPA and wipes in-memory state, restoring the URL via OIDC `state`. `sessionStorage` survives that same-tab redirect round-trip (the same mechanism oidc-client-ts uses for the PKCE verifier) and F5 — so explorer state in `sessionStorage` survives both renewal and forced re-auth. Pure in-memory React state would not.

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **`?focus=<kind>:<id>` is the only URL param** (entry seed). Expand set + selected node live in `sessionStorage`, keyed by focus. | User-confirmed. Keeps a tiny shareable entry point ("explore entity X") + survives the 401 re-auth round-trip via OIDC `state`; drops the URL bloat + deep shareable state the user chose to give up. |
| 2 | **`sessionStorage` (not in-memory, not URL) for `{expand, selected}`**, key `graph-explorer:<focus>`. | Survives token-expiry forced re-auth (same-tab redirect) **and** F5 (§2 auth note); not shareable/cross-tab — the accepted trade. |
| 3 | **Body-click a node → select → right sidebar** (replaces click=expand). Selected node highlighted. | User-confirmed (chosen layout). Disambiguates the overloaded v1 click; gives room for richer detail + actions. |
| 4 | **Directional expand from the sidebar:** Expand⇄Collapse **dependencies** (`direction=outgoing`) and **dependents** (`direction=incoming`), each a toggle on a `{node, dir}` entry in the expand set. | User-confirmed. Uses the backend `direction` param (built in v1, unused). Collapse = remove the entry → union recompute prunes its contribution. |
| 5 | **Sidebar also hosts "Set as focus"** (→ `/graph?focus=<node>`, fresh focus key) **and "Open page ↗"** (entity detail route). The v1 per-node "Open ↗" link is **removed from the node**. | One home for actions; node reverts to display + selected styling. "Set as focus" enables graph-to-graph navigation by re-rooting. |
| 6 | **Global "Reset to focus"** clears the expand set; **soft cap ~150 nodes** disables further expansion with a notice. | User-confirmed (toggle + reset + cap). Each fetch is still backend-capped at 200/`truncated`; the soft cap guards canvas readability + fetch fan-out. |
| 7 | **Focus initial fetch stays depth 2 / `all`; expansions are directional depth 1.** | Preserves v1's richer first paint; expansions grow outward on demand. `mergeGraphs` unions regardless of per-fetch depth/direction. |
| 8 | **"Depth from focus" shown in the sidebar is computed client-side** (BFS distance over merged edges from `focusId`), not the per-fetch `depth`. | Per-fetch depth is relative to each fetch root and ambiguous after merging; client BFS over the merged graph is accurate. |
| 9 | **Sidebar entity data via `useApplication`/`useService`** (by selected node's kind); cached by TanStack Query. | No new endpoint; reuses the detail-page data source. |
| 10 | **React Flow + dagre mocked in component tests; Playwright is the real render + token-expiry check.** | Same jsdom limitation as v1. |
| 11 | **S-05 filters stay a separate slice.** | Keeps this under the ceiling; filters are independent explorer chrome. |

---

## 4. Architecture

### 4.1 State — `useExplorerState(focusKey)`

```
type ExpandDir = "out" | "in";
type ExpandEntry = { node: string; dir: ExpandDir };   // node = "kind:id"
type ExplorerState = { expand: ExpandEntry[]; selected: string | null };

useExplorerState(focusKey: string): {
  expand: ExpandEntry[];
  selected: string | null;
  isExpanded(node: string, dir: ExpandDir): boolean;
  toggleExpand(node: string, dir: ExpandDir): void;   // add or remove the {node,dir} entry
  select(node: string | null): void;
  reset(): void;                                       // clear expand (keep nothing); selected → null
}
```
- Backed by `sessionStorage["graph-explorer:" + focusKey]` (JSON). Reads on mount/focus-change; writes on every mutation.
- A new `focusKey` (different entity, or "Set as focus") reads that key's own slot — fresh if none. So each focus has independent exploration state.
- **Defensive:** missing/corrupt JSON or unavailable `sessionStorage` → fall back to `{expand:[], selected:null}` in memory; never throw.

### 4.2 Data flow

```
GraphExplorerPage  (focus = parseRef(?focus); focusId = "kind:id")
  state = useExplorerState(focusId)
  { results } = useGraph({ focus, expand: state.expand })
        useQueries:
          focus          → GET /graph?…&depth=2&direction=all
          {node,"out"}   → GET /graph?…(node)&depth=1&direction=outgoing
          {node,"in"}    → GET /graph?…(node)&depth=1&direction=incoming
  merged = mergeGraphs(results)                 // union dedup (unchanged)
  atCap  = merged.nodes.length >= SOFT_CAP(150)
  { nodes, edges } = layoutGraph(merged, focusId, state.selected)   // dagre; marks focus + selected
  <ReactFlow … onNodeClick={(_,n) => state.select(n.id)} >          // click = SELECT
  state.selected != null →
     <GraphExplorerSidebar
        selected={parseRef(state.selected)} focusId
        depthFromFocus={bfsDepth(merged, focusId, state.selected)}
        isExpanded={state.isExpanded} atCap
        onToggleExpand={state.toggleExpand} onSetFocus={…navigate} onClose={() => state.select(null)} />
  Reset button → state.reset()
  cap reached → notice; sidebar expand buttons disabled
```

### 4.3 Files

**Created (frontend):**
| File | Purpose | ~LOC |
|---|---|---|
| `features/catalog/relationships/useExplorerState.ts` | `sessionStorage`-backed `{expand, selected}` keyed by focus (§4.1). Pure-ish (storage injected/defaulted) → unit-testable. | 90 |
| `features/catalog/components/GraphExplorerSidebar.tsx` | Right panel: entity metadata (via `useApplication`/`useService`) + depth-from-focus + directional Expand⇄Collapse + Set-as-focus + Open-page + close. | 130 |

**Modified (frontend):**
| File | Change |
|---|---|
| `features/catalog/pages/GraphExplorerPage.tsx` | Drop `?expand`; use `useExplorerState`; `onNodeClick` → select; render sidebar; directional expand/collapse; Reset; soft-cap notice; Set-as-focus via `useNavigate`. |
| `features/catalog/api/graph.ts` | `useGraph({focus, expand: ExpandEntry[]})` builds directional fetch specs (focus depth-2/all; each entry depth-1/outgoing|incoming). |
| `features/catalog/relationships/graphLayout.ts` | `layoutGraph(graph, focusId, selectedId)` — mark `data.selected`; drop `detailHref` wiring. |
| `features/catalog/relationships/graphModel.ts` | `GraphNodeData`: remove `detailHref?`, add `selected?: boolean`. |
| `features/catalog/components/EntityGraphNode.tsx` | Remove the `detailHref` `<Link>`; add selected-node ring styling. |

`DependencyMiniGraph.tsx` is **unchanged** — its "Open full graph" still passes only `?focus`, and its nodes never set `detailHref`/`selected` (so removing the link / adding optional `selected` doesn't affect it; click still navigates via its own `onNodeClick`).

**Estimate ≈ 475 LOC** frontend prod (excl. tests). No backend, no codegen. Under the 800 ceiling.

---

## 5. Components

### 5.1 `useExplorerState` (§4.1) — the state core; fully unit-testable with an injected `Storage`.

### 5.2 `GraphExplorerSidebar`
Props `{ selected:{kind,id}, focusId, depthFromFocus, isExpanded, atCap, onToggleExpand, onSetFocus, onClose }`. Fetches the entity via `useApplication`/`useService` by `selected.kind`. Renders: kind + displayName (header, with close button), team, lifecycle (apps) / health (services), description, "depth N from focus"; then action buttons — **Expand/Collapse dependencies** + **Expand/Collapse dependents** (label from `isExpanded(selected, dir)`; both **disabled when `atCap`** unless already expanded → then "Collapse" stays enabled), **Set as focus**, **Open page ↗** (`<Link>` to the detail route). Entity-fetch error → inline "Couldn't load details"; the expand/focus actions (which need only the node id, not the entity body) still work.

### 5.3 `GraphExplorerPage` (rework) — orchestrates §4.2; owns the cap notice + Reset.

### 5.4 `EntityGraphNode` — display-only: kind icon + displayName, `focused` variant, **`selected` ring**. No link.

---

## 6. Error handling

| Condition | Behaviour |
|---|---|
| `?focus` missing/invalid | "Pick an entity to explore" prompt (unchanged). |
| graph fetch error | inline error + Try-again (`refetch`) — unchanged from v1. |
| sidebar entity fetch error | sidebar shows "Couldn't load details"; node stays selected; expand/collapse/set-focus still work. |
| `sessionStorage` unavailable / corrupt JSON | fall back to empty in-memory state; no crash (§4.1). |
| soft cap reached (~150 nodes) | notice banner; expand buttons disabled (collapse still enabled). |
| token expiry → forced re-auth | OIDC `state` restores `?focus`; `sessionStorage` restores `{expand, selected}` → graph re-renders. (Verified in §7.) |

---

## 7. Testing strategy (gate-2 / gate-5 artifacts)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). **No wiring** (no HTTP/auth/DB/middleware added) → gate-3 real-seam **addition N/A**; the consumed `/graph` endpoint already has real-seam coverage (`GetCatalogGraphTests`). Full existing suite must stay green.

- **Unit — `useExplorerState.test.ts`:** persist→restore by key; `toggleExpand` adds then removes a `{node,dir}`; independent `out`/`in` entries for one node; `select`/`reset`; a different `focusKey` yields independent state; corrupt JSON / throwing `Storage` → safe default (no throw).
- **Unit — `graphMerge.test.ts`:** add a case merging an `outgoing` result + an `incoming` result for the same node → one node, both edges (directional fetches still union correctly).
- **Component — `GraphExplorerSidebar.test.tsx`** (entity hooks mocked): renders metadata + depth; Expand vs Collapse label from `isExpanded`; buttons disabled at cap; each action callback fires; entity-fetch error state.
- **Component — `GraphExplorerPage.test.tsx`** (ReactFlow + `useGraph` + `useExplorerState` mocked): node click → `select`; sidebar appears; directional expand toggles state and the rendered nodes/edges; collapse; Reset; cap notice; Set-as-focus navigates to `/graph?focus=…`.
- **Manual / Playwright (ADR-0084), cold-start dev server:** select→sidebar (metadata correct); Expand dependencies then dependents (graph grows directionally); Collapse (graph shrinks); Reset; Set-as-focus re-roots; **token-expiry restore** — seed a graph, leave the tab idle past silent-renew (or force a 401), do an action → confirm the redirect re-auth returns and the graph (focus from URL + expand/selected from sessionStorage) is restored; console clean. Seed a graph first (DevSeed has no relationships).

---

## 8. List surface (ADR-0095 / ADR-0107)

**N/A.** Not a list screen; no new queryable/user-facing entity field. No registry change.

---

## 9. Definition of Done

The eight always-blocking gates per **CLAUDE.md → Definition of Done** apply verbatim. Maintains `docs/superpowers/verification/2026-06-27-catalog-graph-explorer-sidebar/{dod.md, gate-findings.yaml}`. Slice-specific notes:
- **Gate 3:** real-seam **addition N/A** (frontend-only, no wiring); full suite green.
- **Gate 4 (container build):** no new dep; web image must `tsc -b` + build green.
- **Gate 6 (mutation): N/A** — no C# Domain/Application change (frontend-only).
- **ADR-0084 manual pass required** (incl. the token-expiry restore case, §7).

---

## 10. Out of scope (explicit deferrals)

- **S-05** graph filters (team/domain/criticality/origin) — separate slice.
- Multi-focus / pinning multiple roots; depth-tier shading; saved/named views; cross-tab state sync; URL-shareable deep state (deliberately dropped — Decision 1/2).
- Any backend change, new endpoint, or codegen.
- **Supersedes** the v1 `?expand` URL-length cap follow-up (the spec note in `2026-06-26-catalog-graph-explorer-design.md` §10) — no longer URL-bound; the readability soft-cap (Decision 6) replaces it.

---

## 11. Implementation order (rough — finalised by writing-plans)

1. Copy `dod.md` + `gate-findings.yaml` templates into `verification/2026-06-27-catalog-graph-explorer-sidebar/`.
2. `useExplorerState.ts` + unit tests (TDD).
3. `graph.ts` `useGraph` → directional multi-fetch; `graphMerge` directional-merge test.
4. `graphLayout.ts` (`selectedId` + drop `detailHref`) + `graphModel.ts` `GraphNodeData` (`selected?`, drop `detailHref?`) + `EntityGraphNode.tsx` (selected ring, drop link) + update the v1 EntityGraphNode link test.
5. `GraphExplorerSidebar.tsx` + component test (entity hooks mocked).
6. `GraphExplorerPage.tsx` rework (drop `?expand`; select; sidebar; directional expand/collapse; Reset; cap; Set-as-focus) + component test.
7. `npm run build` + `scripts/ci-local.sh frontend` green; Playwright MCP pass (incl. token-expiry restore); update CHECKLIST note; push → PR → DoD gates.

---

## 12. Self-review

**Spec coverage:** every §3 decision traces to §4–§9; the §7 test files map to §4.3 files that writing-plans will turn into tasks (`useExplorerState`, `graphMerge`, `GraphExplorerSidebar`, `GraphExplorerPage`).

**Placeholder scan:** no TBD/TODO. `SOFT_CAP` = 150 (Decision 6). `useExplorerState`/`useGraph` signatures pinned in §4.

**Internal consistency:** frontend-only / no-backend / no-codegen consistent across §1/§3/§4/§7/§9. State model (`?focus` URL + `{expand,selected}` sessionStorage) consistent across §2/§3#1-2/§4.1/§6. Directional expand (`out`→outgoing, `in`→incoming) consistent across §3#4/§4.2/§5.2/§7. `detailHref` removed + `selected` added consistent across §4.3/§5.2/§5.4 and the mini-graph "unchanged" claim (it never used `detailHref`/`selected`). Supersession of the v1 `?expand` cap stated in §1 and §10.

**Ambiguity check:** "depth from focus" pinned to client-side BFS (§3#8) not per-fetch depth. Cap behaviour pinned (notice + expand disabled, collapse enabled — §5.2/§6). Focus depth-2 vs expansion depth-1 pinned (§3#7). New-focus → independent state pinned (§4.1).

**Scope check:** single PR, frontend-only, 2 created + 5 modified files, ~475 LOC, under ceiling; S-05 explicitly deferred. Depends on PR #50 merging first (§2).

**No blocking issues found.**
