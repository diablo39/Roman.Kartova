# Catalog Graph Explorer — Detail Sidebar + Directional Expand + Local State — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the explorer's URL-encoded single-click-expand with a select→detail-sidebar model, directional Expand/Collapse, and `sessionStorage`-backed local state (only `?focus` stays in the URL).

**Architecture:** A `sessionStorage`-backed `useExplorerState(focusKey)` holds `{expand: {node,dir}[], selected}`. `GraphExplorerPage` derives the graph from focus (depth-2/all) + one directional depth-1 fetch per expand entry, lays it out with dagre, and renders a read-only React Flow where body-click selects a node into `GraphExplorerSidebar` (entity details via `useApplication`/`useService` + client-BFS depth), which hosts directional Expand⇄Collapse, Set-as-focus, and Open-page.

**Tech Stack:** React 19 + TS strict · React Router v7 · TanStack Query · `@xyflow/react` · `@dagrejs/dagre` · vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-06-27-catalog-graph-explorer-sidebar-design.md`

## Global Constraints

- **Depends on PR #50** (the explorer v1) — this branch forked from it; rebase onto master once #50 lands.
- **Frontend-only** — no backend, no new endpoint, no codegen. Reuses `GET /catalog/graph` (`direction` param) + `useApplication`/`useService`.
- React 19 + TS strict; **`tsc -b` (`npm run build`) is the binding type gate** — must be clean.
- New `.ts`/`.tsx` files use **LF**. Editing existing `.ts`/`.tsx` is HARD-BLOCKED by the serena-guard PreToolUse hook → use Serena MCP tools (`replace_symbol_body`, `replace_content`, `insert_after_symbol`); after committing, confirm `git diff` shows only intended lines (CRLF-clean) and normalize with `sed -i 's/\r$//'` + `git commit --amend --no-edit` if a whole file flips.
- **`sessionStorage` key:** `graph-explorer:<focusKey>` (focusKey = `"<kind>:<id>"`). Reads/writes JSON `{expand,selected}`; missing/corrupt/throwing storage → empty in-memory default, never throw.
- **Directional mapping:** expand `dir:"out"` → `direction:"outgoing"`; `dir:"in"` → `direction:"incoming"`. Focus fetch = depth **2**, `direction:"all"`; each expand fetch = depth **1**.
- **Soft node cap = 150** → notice + expand buttons disabled (collapse still enabled).
- Windows host: `cd web && npx vitest run <file>`; `cmd //c "npx tsc -b"`.
- **DoD:** eight always-blocking gates (CLAUDE.md). Gate 6 mutation **N/A** (no C# change). Maintain `docs/superpowers/verification/2026-06-27-catalog-graph-explorer-sidebar/{dod.md,gate-findings.yaml}`. ADR-0084 Playwright pass required incl. token-expiry restore.

---

## File Structure

**Created:**
- `web/src/features/catalog/relationships/useExplorerState.ts` — `sessionStorage` state hook (`{expand,selected}` keyed by focus) + exported `ExpandDir`/`ExpandEntry` types.
- `web/src/features/catalog/components/GraphExplorerSidebar.tsx` — right detail panel + actions.
- Test: `…/relationships/__tests__/useExplorerState.test.ts`, `…/components/__tests__/GraphExplorerSidebar.test.tsx`.

**Modified:**
- `web/src/features/catalog/api/graph.ts` — `useGraph({focus, expand: ExpandEntry[]})` directional multi-fetch.
- `web/src/features/catalog/relationships/graphMerge.ts` — add pure `bfsDepth(graph, fromId, toId)`.
- `web/src/features/catalog/relationships/graphLayout.ts` — `layoutGraph(graph, focusId, selectedId)`; set `data.selected`; drop `detailHref`.
- `web/src/features/catalog/relationships/graphModel.ts` — `GraphNodeData`: drop `detailHref?`, add `selected?: boolean`.
- `web/src/features/catalog/components/EntityGraphNode.tsx` — drop the `<Link>`; add selected-ring styling.
- `web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx` — replace the detail-link test with a selected-styling test.
- `web/src/features/catalog/pages/GraphExplorerPage.tsx` — rework to the new model.
- `web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx` — rework for select/expand/collapse/reset/cap/set-focus.

`DependencyMiniGraph.tsx` is **untouched** (its nodes never set `detailHref`/`selected`; click still navigates via its own `onNodeClick`).

---

## Task 1: Slice verification scaffold

**Files:** Create `docs/superpowers/verification/2026-06-27-catalog-graph-explorer-sidebar/{dod.md,gate-findings.yaml}`

- [ ] **Step 1: Copy templates + fill headers**

```bash
cd "C:/Projects/Private/Roman.Gig2"
mkdir -p docs/superpowers/verification/2026-06-27-catalog-graph-explorer-sidebar
cp docs/superpowers/templates/dod-ledger-template.md docs/superpowers/verification/2026-06-27-catalog-graph-explorer-sidebar/dod.md
cp docs/superpowers/templates/gate-findings-template.yaml docs/superpowers/verification/2026-06-27-catalog-graph-explorer-sidebar/gate-findings.yaml
```
In `dod.md` set Slice `2026-06-26→2026-06-27-catalog-graph-explorer-sidebar`, Branch `feat/catalog-graph-explorer-sidebar`, HEAD (`git rev-parse --short HEAD`), spec/plan paths. In `gate-findings.yaml` set `slice`/`branch`/`head`, `findings: []`.

- [ ] **Step 2: Commit**

```bash
git add docs/superpowers/verification/2026-06-27-catalog-graph-explorer-sidebar/
git commit -m "chore(catalog): DoD ledger + gate-findings scaffold for explorer sidebar slice"
```

---

## Task 2: `useExplorerState` (TDD)

**Files:**
- Create: `web/src/features/catalog/relationships/useExplorerState.ts`
- Test: `web/src/features/catalog/relationships/__tests__/useExplorerState.test.ts`

**Interfaces:**
- Produces:
  - `type ExpandDir = "out" | "in"`
  - `type ExpandEntry = { node: string; dir: ExpandDir }` (node = `"kind:id"`)
  - `useExplorerState(focusKey: string, storage?: Storage): { expand: ExpandEntry[]; selected: string | null; isExpanded(node: string, dir: ExpandDir): boolean; toggleExpand(node: string, dir: ExpandDir): void; select(node: string | null): void; reset(): void }`

- [ ] **Step 1: Write the failing test**

```ts
// web/src/features/catalog/relationships/__tests__/useExplorerState.test.ts
import { describe, it, expect, beforeEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useExplorerState } from "@/features/catalog/relationships/useExplorerState";

// Minimal in-memory Storage stand-in.
function memStorage(): Storage {
  const m = new Map<string, string>();
  return {
    get length() { return m.size; },
    clear: () => m.clear(),
    getItem: (k) => (m.has(k) ? m.get(k)! : null),
    key: (i) => [...m.keys()][i] ?? null,
    removeItem: (k) => void m.delete(k),
    setItem: (k, v) => void m.set(k, v),
  };
}

describe("useExplorerState", () => {
  let store: Storage;
  beforeEach(() => { store = memStorage(); });

  it("starts empty and toggles a directional expand entry on/off", () => {
    const { result } = renderHook(() => useExplorerState("application:f", store));
    expect(result.current.expand).toEqual([]);
    act(() => result.current.toggleExpand("application:a", "out"));
    expect(result.current.isExpanded("application:a", "out")).toBe(true);
    expect(result.current.isExpanded("application:a", "in")).toBe(false);
    act(() => result.current.toggleExpand("application:a", "out"));
    expect(result.current.isExpanded("application:a", "out")).toBe(false);
  });

  it("persists to storage and restores on a fresh hook with the same key", () => {
    const { result, unmount } = renderHook(() => useExplorerState("application:f", store));
    act(() => result.current.toggleExpand("service:s", "in"));
    act(() => result.current.select("service:s"));
    unmount();
    const { result: r2 } = renderHook(() => useExplorerState("application:f", store));
    expect(r2.current.isExpanded("service:s", "in")).toBe(true);
    expect(r2.current.selected).toBe("service:s");
  });

  it("keeps independent state per focus key", () => {
    const { result, rerender } = renderHook(({ k }) => useExplorerState(k, store), {
      initialProps: { k: "application:f1" },
    });
    act(() => result.current.toggleExpand("application:a", "out"));
    rerender({ k: "application:f2" });
    expect(result.current.expand).toEqual([]); // f2 is fresh
    rerender({ k: "application:f1" });
    expect(result.current.isExpanded("application:a", "out")).toBe(true); // f1 restored
  });

  it("reset clears expand + selected", () => {
    const { result } = renderHook(() => useExplorerState("application:f", store));
    act(() => { result.current.toggleExpand("application:a", "out"); result.current.select("application:a"); });
    act(() => result.current.reset());
    expect(result.current.expand).toEqual([]);
    expect(result.current.selected).toBeNull();
  });

  it("survives corrupt JSON without throwing", () => {
    store.setItem("graph-explorer:application:f", "{not json");
    const { result } = renderHook(() => useExplorerState("application:f", store));
    expect(result.current.expand).toEqual([]);
    expect(result.current.selected).toBeNull();
  });
});
```

- [ ] **Step 2: Run — verify it fails**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/useExplorerState.test.ts`
Expected: FAIL — `useExplorerState` not found.

- [ ] **Step 3: Implement**

```ts
// web/src/features/catalog/relationships/useExplorerState.ts
import { useCallback, useState } from "react";

export type ExpandDir = "out" | "in";
export type ExpandEntry = { node: string; dir: ExpandDir };
export type ExplorerState = { expand: ExpandEntry[]; selected: string | null };

const EMPTY: ExplorerState = { expand: [], selected: null };
const storageKey = (focusKey: string) => `graph-explorer:${focusKey}`;

function read(storage: Storage, focusKey: string): ExplorerState {
  try {
    const raw = storage.getItem(storageKey(focusKey));
    if (!raw) return EMPTY;
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== "object" || !Array.isArray((parsed as ExplorerState).expand)) return EMPTY;
    const p = parsed as ExplorerState;
    return { expand: p.expand, selected: p.selected ?? null };
  } catch {
    return EMPTY;
  }
}

function write(storage: Storage, focusKey: string, state: ExplorerState): void {
  try {
    storage.setItem(storageKey(focusKey), JSON.stringify(state));
  } catch {
    /* storage unavailable (private mode / quota) — degrade to in-memory only */
  }
}

export function useExplorerState(
  focusKey: string,
  storage: Storage = window.sessionStorage,
) {
  const [state, setState] = useState<ExplorerState>(() => read(storage, focusKey));
  // Render-time reconcile when the focus key changes (project pattern: derive
  // state from props in render with a prev-value guard, not in an effect).
  const [prevKey, setPrevKey] = useState(focusKey);
  if (prevKey !== focusKey) {
    setPrevKey(focusKey);
    setState(read(storage, focusKey));
  }

  const commit = useCallback(
    (next: ExplorerState) => {
      write(storage, focusKey, next);
      setState(next);
    },
    [storage, focusKey],
  );

  const isExpanded = useCallback(
    (node: string, dir: ExpandDir) => state.expand.some((e) => e.node === node && e.dir === dir),
    [state.expand],
  );
  const toggleExpand = useCallback(
    (node: string, dir: ExpandDir) => {
      const exists = state.expand.some((e) => e.node === node && e.dir === dir);
      const expand = exists
        ? state.expand.filter((e) => !(e.node === node && e.dir === dir))
        : [...state.expand, { node, dir }];
      commit({ ...state, expand });
    },
    [state, commit],
  );
  const select = useCallback((node: string | null) => commit({ ...state, selected: node }), [state, commit]);
  const reset = useCallback(() => commit({ expand: [], selected: null }), [commit]);

  return { expand: state.expand, selected: state.selected, isExpanded, toggleExpand, select, reset };
}
```

- [ ] **Step 4: Run — verify pass**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/useExplorerState.test.ts`
Expected: PASS (5/5).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/relationships/useExplorerState.ts web/src/features/catalog/relationships/__tests__/useExplorerState.test.ts
git commit -m "feat(web): sessionStorage-backed useExplorerState for the graph explorer"
```

---

## Task 3: `useGraph` directional fetch + `bfsDepth` (TDD)

**Files:**
- Modify: `web/src/features/catalog/api/graph.ts`
- Modify: `web/src/features/catalog/relationships/graphMerge.ts` (add `bfsDepth`)
- Test: `web/src/features/catalog/relationships/__tests__/graphMerge.test.ts` (add cases)

**Interfaces:**
- Consumes: `ExpandEntry` (Task 2), `ExplorerGraph` (graphMerge).
- Produces:
  - `useGraph({ focus: GraphFocus; expand: ExpandEntry[] }): { results: GraphResponse[]; isLoading; isError; refetch }`
  - `bfsDepth(graph: ExplorerGraph, fromId: string, toId: string): number | null` (undirected hop distance; 0 if `fromId===toId`; `null` if unreachable)

- [ ] **Step 1: Add `bfsDepth` test cases to `graphMerge.test.ts`**

Append to the existing describe in `graphMerge.test.ts` (built-in Edit is blocked — use Serena `insert_after_symbol`/`replace_content`; or if the file allows, add a new `describe`). Add:

```ts
import { bfsDepth } from "@/features/catalog/relationships/graphMerge";

describe("bfsDepth", () => {
  const g = {
    nodes: [
      { id: "service:f", kind: "service", entityId: "f", displayName: "F" },
      { id: "service:a", kind: "service", entityId: "a", displayName: "A" },
      { id: "service:b", kind: "service", entityId: "b", displayName: "B" },
      { id: "service:x", kind: "service", entityId: "x", displayName: "X" },
    ],
    edges: [
      { id: "e1", source: "service:f", target: "service:a", label: "Depends on" },
      { id: "e2", source: "service:a", target: "service:b", label: "Depends on" },
    ],
    truncated: false,
  } as const;

  it("returns 0 for the focus itself", () => { expect(bfsDepth(g, "service:f", "service:f")).toBe(0); });
  it("counts undirected hops", () => {
    expect(bfsDepth(g, "service:f", "service:a")).toBe(1);
    expect(bfsDepth(g, "service:f", "service:b")).toBe(2);
  });
  it("returns null for an unreachable node", () => { expect(bfsDepth(g, "service:f", "service:x")).toBeNull(); });
});
```

Also add a directional-merge case to the existing `mergeGraphs` describe:

```ts
it("unions an outgoing result and an incoming result for the same node", () => {
  const out: GraphResponse = { nodes: [node("a","A",0), node("b","B",1)], edges: [edge("e1","a","b")], truncated: false };
  const inc: GraphResponse = { nodes: [node("a","A",0), node("c","C",1)], edges: [edge("e2","c","a")], truncated: false };
  const g = mergeGraphs([out, inc]);
  expect(g.nodes.map((n) => n.id).sort()).toEqual(["service:a","service:b","service:c"]);
  expect(g.edges.map((e) => e.id).sort()).toEqual(["e1","e2"]);
});
```

- [ ] **Step 2: Run — verify it fails**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphMerge.test.ts`
Expected: FAIL — `bfsDepth` not exported.

- [ ] **Step 3: Add `bfsDepth` to `graphMerge.ts`** (Serena `insert_after_symbol` on `mergeGraphs`)

```ts
// append to web/src/features/catalog/relationships/graphMerge.ts
export function bfsDepth(graph: ExplorerGraph, fromId: string, toId: string): number | null {
  if (fromId === toId) return 0;
  const adj = new Map<string, string[]>();
  for (const e of graph.edges) {
    (adj.get(e.source) ?? adj.set(e.source, []).get(e.source)!).push(e.target);
    (adj.get(e.target) ?? adj.set(e.target, []).get(e.target)!).push(e.source);
  }
  const seen = new Set<string>([fromId]);
  let frontier = [fromId];
  let depth = 0;
  while (frontier.length) {
    depth++;
    const next: string[] = [];
    for (const id of frontier) {
      for (const nb of adj.get(id) ?? []) {
        if (nb === toId) return depth;
        if (!seen.has(nb)) { seen.add(nb); next.push(nb); }
      }
    }
    frontier = next;
  }
  return null;
}
```

- [ ] **Step 4: Refactor `useGraph` to directional fetch** (Serena `replace_symbol_body` / `replace_content` on `graph.ts`)

Replace `fetchGraph`, `graphKeys.node`, and `useGraph` with:

```ts
import type { ExpandEntry } from "@/features/catalog/relationships/useExplorerState";

type GraphDirection = "outgoing" | "incoming" | "all";

export const graphKeys = {
  all: ["catalog", "graph"] as const,
  node: (f: GraphFocus, depth: number, direction: GraphDirection) =>
    [...graphKeys.all, f.kind, f.id, depth, direction] as const,
};

function parseNode(node: string): GraphFocus {
  const [kind, id] = node.split(":");
  return { kind: kind as RelationshipKind, id };
}

async function fetchGraph(f: GraphFocus, depth: number, direction: GraphDirection): Promise<GraphResponse> {
  const { data, error } = await apiClient.GET("/api/v1/catalog/graph", {
    params: { query: { entityKind: f.kind, entityId: f.id, depth, direction } },
  });
  if (error) throw error;
  return unwrapData(data);
}

export function useGraph({ focus, expand }: { focus: GraphFocus; expand: ExpandEntry[] }) {
  const queries = useQueries({
    queries: [
      { queryKey: graphKeys.node(focus, FOCUS_DEPTH, "all"), queryFn: () => fetchGraph(focus, FOCUS_DEPTH, "all") },
      ...expand.map((e) => {
        const f = parseNode(e.node);
        const direction: GraphDirection = e.dir === "out" ? "outgoing" : "incoming";
        return {
          queryKey: graphKeys.node(f, EXPAND_DEPTH, direction),
          queryFn: () => fetchGraph(f, EXPAND_DEPTH, direction),
        };
      }),
    ],
  });
  return {
    results: queries.map((q) => q.data).filter((d): d is GraphResponse => !!d),
    isLoading: queries.some((q) => q.isLoading),
    isError: queries.some((q) => q.isError),
    refetch: () => queries.forEach((q) => q.refetch()),
  };
}
```

- [ ] **Step 5: Run tests + type-check**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphMerge.test.ts && cmd //c "npx tsc -b"`
Expected: graphMerge tests PASS; `tsc -b` clean (note: `GraphExplorerPage` still passes `expand: GraphFocus[]` and will type-error until Task 6 — if so, this step's `tsc` is checked at Task 6; run only the vitest here and defer the full `tsc -b` to Task 6). Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphMerge.test.ts` → PASS.

- [ ] **Step 6: Commit** (CRLF-check the two edited files)

```bash
git add web/src/features/catalog/api/graph.ts web/src/features/catalog/relationships/graphMerge.ts web/src/features/catalog/relationships/__tests__/graphMerge.test.ts
git commit -m "feat(web): directional useGraph fetch + bfsDepth for explorer"
git show --stat HEAD | head; echo "--- vs -w ---"; git show -w --stat HEAD | head
```

---

## Task 4: Node selection styling (graphModel + graphLayout + EntityGraphNode)

**Files:**
- Modify: `web/src/features/catalog/relationships/graphModel.ts`
- Modify: `web/src/features/catalog/relationships/graphLayout.ts`
- Modify: `web/src/features/catalog/components/EntityGraphNode.tsx`
- Test: `web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx`

**Interfaces:**
- Produces: `GraphNodeData` gains `selected?: boolean`, loses `detailHref?`. `layoutGraph(graph, focusId, selectedId)` marks `data.selected = (n.id === selectedId)`.

- [ ] **Step 1: Update `GraphNodeData`** (Serena `replace_symbol_body` on the `GraphNodeData` type in `graphModel.ts`)

```ts
export type GraphNodeData = {
  kind: RelationshipKind;
  entityId: string;
  displayName: string;
  side: GraphSide;
  selected?: boolean; // explorer: the currently-selected node (sidebar open on it)
};
```

- [ ] **Step 2: Update `layoutGraph`** (Serena `replace_symbol_body` on `layoutGraph` in `graphLayout.ts`; also delete the now-unused `detailHref` helper + `RelationshipKind` import if it becomes unused)

```ts
export function layoutGraph(
  graph: ExplorerGraph,
  focusId: string,
  selectedId: string | null,
): { nodes: Node<GraphNodeData>[]; edges: Edge[] } {
  const g = new dagre.graphlib.Graph();
  g.setGraph({ rankdir: "LR", nodesep: 40, ranksep: 120 });
  g.setDefaultEdgeLabel(() => ({}));
  for (const n of graph.nodes) g.setNode(n.id, { width: NODE_W, height: NODE_H });
  for (const e of graph.edges) g.setEdge(e.source, e.target);
  dagre.layout(g);

  const nodes: Node<GraphNodeData>[] = graph.nodes.map((n) => {
    const pos = g.node(n.id);
    return {
      id: n.id,
      type: "entity",
      position: { x: pos.x - NODE_W / 2, y: pos.y - NODE_H / 2 },
      data: {
        kind: n.kind,
        entityId: n.entityId,
        displayName: n.displayName,
        side: n.id === focusId ? "focused" : "dependency",
        selected: n.id === selectedId,
      },
    };
  });

  const edges: Edge[] = graph.edges.map((e) => ({ id: e.id, source: e.source, target: e.target, label: e.label }));
  return { nodes, edges };
}
```

- [ ] **Step 3: Update `EntityGraphNode`** (Serena `replace_symbol_body` on `EntityGraphNode`; remove the `import { Link }` line via `replace_content`)

```tsx
import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";

const KIND_LABEL: Record<string, string> = { application: "Application", service: "Service" };

export function EntityGraphNode({ data }: NodeProps<Node<GraphNodeData>>) {
  const base = "rounded-lg bg-primary px-3 py-2";
  const variant = data.selected
    ? "border-2 border-brand-solid shadow-md"
    : data.side === "focused"
      ? "border-2 border-secondary font-semibold shadow-sm"
      : "border border-secondary shadow-xs";
  return (
    <div className={`${base} ${variant}`}>
      <Handle type="target" position={Position.Left} className="!border-0 !bg-transparent" />
      <div className="text-sm text-primary">{data.displayName}</div>
      <div className="text-xs text-tertiary">{KIND_LABEL[data.kind] ?? data.kind}</div>
      <Handle type="source" position={Position.Right} className="!border-0 !bg-transparent" />
    </div>
  );
}
```

- [ ] **Step 4: Replace the detail-link test with a selected-styling test** (Serena `replace_content` on `EntityGraphNode.test.tsx` — replace the `renders an open-detail link when detailHref is set` case)

```tsx
it("applies selected styling when data.selected is true", () => {
  render(
    <EntityGraphNode data={{ kind: "service", entityId: "a", displayName: "A", side: "dependency", selected: true }} /* + minimal NodeProps as in the existing harness */ />,
  );
  expect(screen.getByText("A").closest("div[class*='border-brand-solid']")).not.toBeNull();
});
```
(Match the file's existing `EntityGraphNode` render harness for `NodeProps`. If the existing test imported `MemoryRouter` only for the link, drop that import.)

- [ ] **Step 5: Run tests + type-check**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/EntityGraphNode.test.tsx`
Expected: PASS. (`tsc -b` will still error in `GraphExplorerPage` until Task 6 — full clean is asserted in Task 6.)

- [ ] **Step 6: Commit** (CRLF-check)

```bash
git add web/src/features/catalog/relationships/graphModel.ts web/src/features/catalog/relationships/graphLayout.ts web/src/features/catalog/components/EntityGraphNode.tsx web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx
git commit -m "feat(web): selected-node styling on graph nodes; drop node detail link"
git show --stat HEAD | head; echo "--- vs -w ---"; git show -w --stat HEAD | head
```

---

## Task 5: `GraphExplorerSidebar` (TDD)

**Files:**
- Create: `web/src/features/catalog/components/GraphExplorerSidebar.tsx`
- Test: `web/src/features/catalog/components/__tests__/GraphExplorerSidebar.test.tsx`

**Interfaces:**
- Consumes: `useApplication`/`useService`, `ExpandDir` (Task 2).
- Produces: `GraphExplorerSidebar(props: { selected: { kind: "application" | "service"; id: string }; depthFromFocus: number | null; isExpanded: (node: string, dir: ExpandDir) => boolean; atCap: boolean; onToggleExpand: (node: string, dir: ExpandDir) => void; onSetFocus: () => void; onClose: () => void })`

- [ ] **Step 1: Write the failing test**

```tsx
// web/src/features/catalog/components/__tests__/GraphExplorerSidebar.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { GraphExplorerSidebar } from "@/features/catalog/components/GraphExplorerSidebar";

const mockApp = vi.fn();
const mockSvc = vi.fn();
vi.mock("@/features/catalog/api/applications", () => ({ useApplication: (id: string) => mockApp(id) }));
vi.mock("@/features/catalog/api/services", () => ({ useService: (id: string) => mockSvc(id) }));

const appData = { id: "a", displayName: "A App 041", description: "Seeded #42", lifecycle: "Active", teamId: "t1" };

function renderSidebar(props: Partial<Parameters<typeof GraphExplorerSidebar>[0]> = {}) {
  return render(
    <MemoryRouter>
      <GraphExplorerSidebar
        selected={{ kind: "application", id: "a" }}
        depthFromFocus={1}
        isExpanded={() => false}
        atCap={false}
        onToggleExpand={vi.fn()}
        onSetFocus={vi.fn()}
        onClose={vi.fn()}
        {...props}
      />
    </MemoryRouter>,
  );
}

describe("GraphExplorerSidebar", () => {
  beforeEach(() => {
    mockApp.mockReturnValue({ data: appData, isLoading: false, isError: false });
    mockSvc.mockReturnValue({ data: undefined, isLoading: false, isError: false });
  });

  it("renders entity metadata + depth", () => {
    renderSidebar();
    expect(screen.getByText("A App 041")).toBeInTheDocument();
    expect(screen.getByText(/Active/)).toBeInTheDocument();
    expect(screen.getByText(/depth 1/i)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /open page/i })).toHaveAttribute("href", "/catalog/applications/a");
  });

  it("shows Expand when not expanded and calls onToggleExpand with the direction", () => {
    const onToggleExpand = vi.fn();
    renderSidebar({ onToggleExpand });
    fireEvent.click(screen.getByRole("button", { name: /expand dependencies/i }));
    expect(onToggleExpand).toHaveBeenCalledWith("application:a", "out");
  });

  it("shows Collapse when already expanded", () => {
    renderSidebar({ isExpanded: (_n, d) => d === "in" });
    expect(screen.getByRole("button", { name: /collapse dependents/i })).toBeInTheDocument();
  });

  it("disables Expand at cap but leaves Collapse enabled", () => {
    renderSidebar({ atCap: true, isExpanded: (_n, d) => d === "out" });
    expect(screen.getByRole("button", { name: /expand dependents/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /collapse dependencies/i })).toBeEnabled();
  });

  it("shows an error state but keeps the actions usable", () => {
    mockApp.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    const onToggleExpand = vi.fn();
    renderSidebar({ onToggleExpand });
    expect(screen.getByText(/couldn.t load details/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /expand dependencies/i }));
    expect(onToggleExpand).toHaveBeenCalledWith("application:a", "out");
  });
});
```

- [ ] **Step 2: Run — verify it fails**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/GraphExplorerSidebar.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement**

```tsx
// web/src/features/catalog/components/GraphExplorerSidebar.tsx
import { Link } from "react-router-dom";
import { useApplication } from "@/features/catalog/api/applications";
import { useService } from "@/features/catalog/api/services";
import type { ExpandDir } from "@/features/catalog/relationships/useExplorerState";

type Selected = { kind: "application" | "service"; id: string };

const KIND_LABEL = { application: "Application", service: "Service" } as const;

export function GraphExplorerSidebar(props: {
  selected: Selected;
  depthFromFocus: number | null;
  isExpanded: (node: string, dir: ExpandDir) => boolean;
  atCap: boolean;
  onToggleExpand: (node: string, dir: ExpandDir) => void;
  onSetFocus: () => void;
  onClose: () => void;
}) {
  const { selected, depthFromFocus, isExpanded, atCap, onToggleExpand, onSetFocus, onClose } = props;
  const nodeKey = `${selected.kind}:${selected.id}`;
  const detailHref = `/catalog/${selected.kind === "application" ? "applications" : "services"}/${selected.id}`;

  // Both hooks always called (rules of hooks); the inactive one is disabled via id="".
  const appQ = useApplication(selected.kind === "application" ? selected.id : "");
  const svcQ = useService(selected.kind === "service" ? selected.id : "");
  const active = selected.kind === "application" ? appQ : svcQ;
  const entity = active.data as
    | { displayName: string; description?: string; lifecycle?: string; health?: string; teamId?: string }
    | undefined;

  const dirRow = (dir: ExpandDir, label: string) => {
    const on = isExpanded(nodeKey, dir);
    return (
      <button
        type="button"
        disabled={atCap && !on}
        onClick={() => onToggleExpand(nodeKey, dir)}
        className="w-full rounded-md border border-secondary px-3 py-1.5 text-sm text-primary disabled:opacity-50"
      >
        {on ? "Collapse" : "Expand"} {label}
      </button>
    );
  };

  return (
    <aside className="flex w-72 shrink-0 flex-col gap-3 overflow-y-auto border-l border-secondary p-4" aria-label="Node details">
      <div className="flex items-start justify-between">
        <div>
          <div className="text-sm font-semibold text-primary">{entity?.displayName ?? selected.id}</div>
          <div className="text-xs text-tertiary">{KIND_LABEL[selected.kind]}</div>
        </div>
        <button type="button" onClick={onClose} aria-label="Close details" className="text-tertiary">✕</button>
      </div>

      {active.isError ? (
        <p className="text-sm text-error-primary">Couldn&apos;t load details.</p>
      ) : (
        <dl className="space-y-1 text-sm">
          {depthFromFocus != null && (
            <div className="text-tertiary">depth {depthFromFocus} from focus</div>
          )}
          {entity?.lifecycle && <div><span className="text-tertiary">Lifecycle:</span> {entity.lifecycle}</div>}
          {entity?.health && <div><span className="text-tertiary">Health:</span> {entity.health}</div>}
          {entity?.description && <p className="text-secondary">{entity.description}</p>}
          {entity?.teamId && (
            <Link to={`/teams/${entity.teamId}`} className="text-xs text-brand-secondary underline">Team ↗</Link>
          )}
        </dl>
      )}

      <div className="mt-auto space-y-2">
        {dirRow("out", "dependencies")}
        {dirRow("in", "dependents")}
        <button type="button" onClick={onSetFocus} className="w-full rounded-md border border-secondary px-3 py-1.5 text-sm text-primary">
          Set as focus
        </button>
        <Link to={detailHref} className="block w-full rounded-md bg-brand-solid px-3 py-1.5 text-center text-sm text-white">
          Open page ↗
        </Link>
      </div>
    </aside>
  );
}
```
> `ApplicationResponse` has `lifecycle`+`teamId` (no `health`); `ServiceResponse` has `health`+`teamId`. The optional-field reads above render whichever is present. If `lifecycle` is an object/enum rather than a string in the generated type, coerce to its string form (`String(entity.lifecycle)`) — let `tsc -b` decide.

- [ ] **Step 4: Run — verify pass**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/GraphExplorerSidebar.test.tsx`
Expected: PASS (5/5).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/GraphExplorerSidebar.tsx web/src/features/catalog/components/__tests__/GraphExplorerSidebar.test.tsx
git commit -m "feat(web): GraphExplorerSidebar — entity detail + directional expand/collapse actions"
```

---

## Task 6: `GraphExplorerPage` rework (TDD)

**Files:**
- Modify: `web/src/features/catalog/pages/GraphExplorerPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx`

**Interfaces:**
- Consumes: `useExplorerState`, `useGraph` (directional), `mergeGraphs`/`bfsDepth`, `layoutGraph(…, selectedId)`, `GraphExplorerSidebar`.

- [ ] **Step 1: Rewrite the component test** (Serena `replace_content` / overwrite the test file)

```tsx
// web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import { GraphExplorerPage } from "@/features/catalog/pages/GraphExplorerPage";

const mockUseGraph = vi.fn();
vi.mock("@/features/catalog/api/graph", () => ({ useGraph: (a: unknown) => mockUseGraph(a) }));

// ReactFlow stub: render each node as a clickable button.
vi.mock("@xyflow/react", () => ({
  ReactFlow: ({ nodes, onNodeClick }: any) => (
    <div data-testid="rf">
      {nodes.map((n: any) => (
        <button key={n.id} data-testid={`node-${n.id}`} onClick={() => onNodeClick({}, n)}>{n.data.displayName}</button>
      ))}
    </div>
  ),
  Background: () => null, Controls: () => null, MiniMap: () => null,
}));
// Sidebar stub: expose the expand callback + close.
vi.mock("@/features/catalog/components/GraphExplorerSidebar", () => ({
  GraphExplorerSidebar: ({ selected, onToggleExpand, onClose }: any) => (
    <div data-testid="sidebar">
      <span>sidebar:{selected.kind}:{selected.id}</span>
      <button onClick={() => onToggleExpand(`${selected.kind}:${selected.id}`, "out")}>expand-out</button>
      <button onClick={onClose}>close</button>
    </div>
  ),
}));

const result = {
  nodes: [
    { kind: "service", id: "f", displayName: "Focus", depth: 0, teamId: null },
    { kind: "service", id: "a", displayName: "A", depth: 1, teamId: null },
  ],
  edges: [{ id: "e1", source: { kind: "service", id: "f" }, target: { kind: "service", id: "a" }, type: "dependsOn", origin: "manual" }],
  truncated: false,
};

function renderAt(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Routes><Route path="/graph" element={<GraphExplorerPage />} /></Routes>
    </MemoryRouter>,
  );
}

beforeEach(() => {
  sessionStorage.clear();
  mockUseGraph.mockReturnValue({ results: [result], isLoading: false, isError: false, refetch: vi.fn() });
});

describe("GraphExplorerPage", () => {
  it("renders nodes and opens the sidebar on node click", () => {
    renderAt("/graph?focus=service:f");
    expect(screen.getByTestId("node-service:a")).toBeInTheDocument();
    expect(screen.queryByTestId("sidebar")).toBeNull();
    fireEvent.click(screen.getByTestId("node-service:a"));
    expect(screen.getByText("sidebar:service:a")).toBeInTheDocument();
  });

  it("directional expand from the sidebar updates useGraph's expand arg", () => {
    renderAt("/graph?focus=service:f");
    fireEvent.click(screen.getByTestId("node-service:a"));
    fireEvent.click(screen.getByText("expand-out"));
    // last useGraph call received the new expand entry
    const lastArg = mockUseGraph.mock.calls.at(-1)![0];
    expect(lastArg.expand).toContainEqual({ node: "service:a", dir: "out" });
  });

  it("Reset clears the sidebar selection", () => {
    renderAt("/graph?focus=service:f");
    fireEvent.click(screen.getByTestId("node-service:a"));
    fireEvent.click(screen.getByRole("button", { name: /reset/i }));
    expect(screen.queryByTestId("sidebar")).toBeNull();
  });

  it("shows the missing-focus prompt", () => {
    renderAt("/graph");
    expect(screen.getByText(/pick an entity/i)).toBeInTheDocument();
  });

  it("shows the cap notice when nodes exceed the soft cap", () => {
    const big = { nodes: Array.from({ length: 151 }, (_, i) => ({ kind: "service", id: `n${i}`, displayName: `N${i}`, depth: 1, teamId: null })), edges: [], truncated: false };
    mockUseGraph.mockReturnValue({ results: [big], isLoading: false, isError: false, refetch: vi.fn() });
    renderAt("/graph?focus=service:n0");
    expect(screen.getByText(/large graph/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run — verify it fails**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx`
Expected: FAIL (old page still uses `?expand`/toggle, no sidebar).

- [ ] **Step 3: Rewrite `GraphExplorerPage.tsx`** (overwrite the file — it's a near-total rework; Serena `replace_content` of the whole body, or the built-in Write is acceptable for a full rewrite of an existing file only if serena-guard permits — it does not, so use Serena `replace_symbol_body` on `GraphExplorerPage` + `replace_content` for the imports)

```tsx
import { useMemo } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { ReactFlow, Background, Controls, MiniMap, type Node, type Edge } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useGraph, type GraphFocus } from "@/features/catalog/api/graph";
import { mergeGraphs, bfsDepth } from "@/features/catalog/relationships/graphMerge";
import { layoutGraph } from "@/features/catalog/relationships/graphLayout";
import { useExplorerState } from "@/features/catalog/relationships/useExplorerState";
import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import { GraphExplorerSidebar } from "@/features/catalog/components/GraphExplorerSidebar";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const NODE_TYPES = { entity: EntityGraphNode };
const SOFT_CAP = 150;

function parseRef(token: string | undefined | null): GraphFocus | null {
  if (!token) return null;
  const [kind, id] = token.split(":");
  if ((kind === "application" || kind === "service") && id) return { kind: kind as RelationshipKind, id };
  return null;
}

export function GraphExplorerPage() {
  const [params] = useSearchParams();
  const navigate = useNavigate();

  const focus = parseRef(params.get("focus"));
  const focusId = focus ? `${focus.kind}:${focus.id}` : "";
  const { expand, selected, isExpanded, toggleExpand, select, reset } = useExplorerState(focusId);

  const safeFocus = focus ?? { kind: "application" as RelationshipKind, id: "" };
  const { results, isLoading, isError, refetch } = useGraph({ focus: safeFocus, expand });

  const merged = useMemo(
    () => (focus ? mergeGraphs(results) : { nodes: [], edges: [], truncated: false }),
    [results, focus],
  );
  const atCap = merged.nodes.length >= SOFT_CAP;
  const { nodes, edges } = useMemo(
    () => (focus ? layoutGraph(merged, focusId, selected) : { nodes: [] as Node<GraphNodeData>[], edges: [] as Edge[] }),
    [merged, focusId, selected],
  );

  // Only show the sidebar for a node actually present in the current graph.
  const selectedRef = selected && merged.nodes.some((n) => n.id === selected) ? parseRef(selected) : null;
  const depthFromFocus = useMemo(
    () => (selected ? bfsDepth(merged, focusId, selected) : null),
    [merged, focusId, selected],
  );

  if (!focus) {
    return <div className="p-8 text-sm text-tertiary">Pick an entity to explore its dependency graph.</div>;
  }

  return (
    <div className="flex h-[calc(100vh-8rem)] flex-col gap-2 p-4">
      <div className="flex items-center justify-between">
        <h1 className="text-lg font-semibold text-primary">Dependency graph</h1>
        <button type="button" onClick={reset} className="text-sm text-brand-primary underline">Reset to focus</button>
      </div>
      {isLoading ? (
        <Skeleton className="h-full w-full" />
      ) : isError ? (
        <div className="flex items-center gap-3">
          <p className="text-sm text-error-primary">Couldn&apos;t load the dependency graph.</p>
          <button type="button" className="text-sm text-brand-primary underline" onClick={() => refetch()}>Try again</button>
        </div>
      ) : (
        <>
          {atCap && (
            <p className="text-xs text-warning-primary">Large graph (≥{SOFT_CAP} nodes) — collapse a branch or Reset to keep it readable.</p>
          )}
          <div className="flex min-h-0 flex-1 gap-2">
            <div className="min-h-0 flex-1 overflow-hidden rounded-lg ring-1 ring-secondary">
              <ReactFlow
                nodes={nodes}
                edges={edges}
                nodeTypes={NODE_TYPES}
                fitView
                nodesDraggable={false}
                nodesConnectable={false}
                elementsSelectable={false}
                proOptions={{ hideAttribution: true }}
                onNodeClick={(_, node) => select(node.id)}
              >
                <Background />
                <Controls showInteractive={false} />
                <MiniMap pannable zoomable />
              </ReactFlow>
            </div>
            {selectedRef && (
              <GraphExplorerSidebar
                selected={selectedRef}
                depthFromFocus={depthFromFocus}
                isExpanded={isExpanded}
                atCap={atCap}
                onToggleExpand={toggleExpand}
                onSetFocus={() => navigate(`/graph?focus=${selectedRef.kind}:${selectedRef.id}`)}
                onClose={() => select(null)}
              />
            )}
          </div>
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Run the page test + full type-check**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx && cmd //c "npx tsc -b"`
Expected: page test PASS (5/5); `tsc -b` clean across the app.

- [ ] **Step 5: Full frontend suite + commit** (CRLF-check)

Run: `cd web && npx vitest run`
Expected: all green.

```bash
git add web/src/features/catalog/pages/GraphExplorerPage.tsx web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx
git commit -m "feat(web): explorer select→sidebar + directional expand/collapse + reset + sessionStorage state (drops ?expand)"
git show --stat HEAD | head; echo "--- vs -w ---"; git show -w --stat HEAD | head
```

---

## Task 7: Verification, DoD gates, PR

**Files:** Modify `docs/product/CHECKLIST.md` (note the refinement), `docs/superpowers/verification/2026-06-27-catalog-graph-explorer-sidebar/{dod.md,gate-findings.yaml}`

- [ ] **Step 1: Lint + build + full frontend suite (gates 1, 3)**

Run: `cd web && npm run lint && npm run build && npx vitest run`
Expected: 0 lint errors; `tsc -b` + vite build clean; all tests green. Record in `dod.md`.

- [ ] **Step 2: Container build + CI mirror (gate 4 + pre-push)**

Run: `scripts/ci-local.sh frontend images` (when Docker is free; stop the dev stack first if Testcontainers contention is a risk — though `frontend`/`images` don't need it). Record results; the PR CI is authoritative.

- [ ] **Step 3: /simplify (gate 5)**

Run `/simplify` against the branch diff; address should-fix items; log real/delusion verdicts in `gate-findings.yaml`.

- [ ] **Step 4: Reviews (gates 2, 7, 8, 9)**

Per-task reviews (gate 2) interleaved; then a whole-branch review (gate 7) against spec + diff; `review-pr` (8) + `deep-review` (9) per proportionate judgement. Log findings in `gate-findings.yaml`.

- [ ] **Step 5: Manual / Playwright (ADR-0084) — cold-start dev server**

Seed a graph first (DevSeed has no relationships — add 1–2 dependencies via the 1b dialog). Verify: node click → sidebar (correct metadata + depth); **Expand dependencies** then **Expand dependents** grow the graph directionally; **Collapse** shrinks; **Reset** clears; **Set as focus** re-roots (URL `?focus` changes, fresh state); **token-expiry restore** — seed, leave idle past silent-renew (or force a 401), act → confirm re-auth returns and the graph restores from `?focus` + sessionStorage; console clean. Save screenshots under `verification/2026-06-27-catalog-graph-explorer-sidebar/playwright/`.

- [ ] **Step 6: Checklist + terminal re-verify + PR**

Update `docs/product/CHECKLIST.md` (note under E-04.F-02.S-04 that the explorer gained the detail sidebar + directional expand + local state). Re-run build + suite after gates 5–9. Push; open the PR (base = master once #50 merged, else stack on #50). Completion claim cites `docs/superpowers/verification/2026-06-27-catalog-graph-explorer-sidebar/dod.md`.

```bash
git add docs/product/CHECKLIST.md docs/superpowers/verification/2026-06-27-catalog-graph-explorer-sidebar/
git commit -m "docs(catalog): explorer-sidebar DoD evidence + CHECKLIST note"
git push -u origin feat/catalog-graph-explorer-sidebar
```

---

## Self-Review

**1. Spec coverage:** §3#1-2 state model → Tasks 2, 6 (`useExplorerState` + page uses `?focus` only). #3 select→sidebar → Tasks 4 (selected styling) + 5 (sidebar) + 6 (onNodeClick=select). #4 directional expand → Tasks 3 (useGraph dir) + 5 (buttons) + 6 (wiring). #5 set-focus/open-page/remove-node-link → Tasks 4 (drop link) + 5 (actions) + 6 (navigate). #6 reset + soft cap → Task 6. #7 focus depth-2/expand depth-1 → Task 3. #8 client BFS depth → Task 3 (`bfsDepth`) + 6. #9 sidebar entity hooks → Task 5. #10 mocked tests + Playwright → Tasks 5/6 + 7. #11 S-05 deferred → not built. Gate artifacts (`useExplorerState`/`graphMerge`/`GraphExplorerSidebar`/`GraphExplorerPage` tests) each owned by a task; Playwright incl. token-expiry → Task 7.

**2. Placeholder scan:** No TBD/TODO. `SOFT_CAP=150`. The `lifecycle` string-coercion + the EntityGraphNode test-harness reuse are the only "let tsc/the existing file decide" notes, each with the concrete fallback stated.

**3. Type consistency:** `ExpandDir`/`ExpandEntry` defined in Task 2, consumed identically in Tasks 3/5/6. `useGraph({focus, expand: ExpandEntry[]})` defined Task 3, called Task 6. `layoutGraph(graph, focusId, selectedId)` defined Task 4, called Task 6. `GraphNodeData.selected?` added Task 4, set by `layoutGraph` (4) + read by `EntityGraphNode` (4). `bfsDepth(graph, fromId, toId)` defined Task 3, called Task 6. `GraphExplorerSidebar` prop shape identical across Tasks 5 and 6. `node` key format `"kind:id"` consistent across state, fetch (`parseNode`), layout, and sidebar (`nodeKey`).

No gaps found.
