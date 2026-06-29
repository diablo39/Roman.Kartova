# Graph Explorer Filters (Kind + Team) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add client-side **Kind** and **Team** filters to the standalone dependency graph explorer (`/graph`) that **dim/fade** non-matching nodes and their edges, leaving topology and layout untouched.

**Architecture:** Pure helper `applyGraphFilters` computes dimmed node/edge id sets from the in-memory merged graph; a `sessionStorage`-backed `useGraphFilters` hook (keyed per focus, mirroring `useExplorerState`) holds the selection; a canvas-overlay `GraphFilterControls` (React Flow `<Panel>`) drives it live; `layoutGraph` threads a `dimmed` flag onto node `data` and edge `style`; `EntityGraphNode` renders the muted variant. No backend, no endpoint, no codegen.

**Tech Stack:** React 19 + TypeScript (strict), Vite, Vitest + @testing-library/react, React Router v7, TanStack Query, `@xyflow/react` (React Flow) + `@dagrejs/dagre`, Untitled UI / react-aria-components.

**Spec:** `docs/superpowers/specs/2026-06-29-catalog-graph-filters-design.md`. **Story:** E-04.F-02.S-05. **Branch:** `feat/catalog-graph-filters` (already created; spec already committed).

## Global Constraints

- **Frontend-only.** No backend, no new endpoint, **no query param**, no codegen / API-image rebuild, no new npm dependency. (`teamId` is already on the wire — `GraphNodeDto.teamId: null | string` — and React Flow `<Panel>` ships with `@xyflow/react`.)
- **TypeScript strict; `npm run build` (`tsc -b` + vite) is the binding type gate** (ADR-0109). It must be green before push.
- **Editing an EXISTING `.ts`/`.tsx` is blocked by the serena-guard hook** — use Serena (`replace_symbol_body` / `replace_content` / `insert_*`) for modifications to existing files (Tasks 2, 5, 6, 8 and the test-extension steps). **Creating a new file uses the Write tool** (Tasks 3, 4, 7 sources + new test files). New test files under existing `__tests__/` dirs are new files → Write is fine.
- **CRLF gotcha:** editing existing `.cs`/`.ts`/`.tsx` via Serena flips LF→CRLF on this Windows host (repo is LF). After each commit that touches an existing file, verify `git show --stat` matches `git show -w --stat`; if line endings flipped, normalize (`sed -i 's/\r$//' <file>` then `git add` + `git commit --amend --no-edit`).
- **Filter semantics (verbatim):** a node matches iff `isFocus OR ((kinds == ∅ OR node.kind ∈ kinds) AND (teamIds == ∅ OR (node.teamId != null AND node.teamId ∈ teamIds)))`. **AND across facets, OR within a facet, empty facet = no constraint.** The **focus node never dims.** An **edge dims iff either endpoint dims.** Dimming is visual only — dimmed nodes stay clickable/expandable.
- **Filters apply live** (no submit button); state in `sessionStorage` key `graph-explorer-filters:<focus>`; only `?focus` stays in the URL.
- **Do NOT change `FilterBar` behaviour.** The `MultiSelect` change (Task 5) is strictly additive (optional controlled mode); FilterBar uses it uncontrolled and must keep working.
- **Kind type:** reuse the existing `RelationshipKind` (`"application" | "service"`) from `@/features/catalog/relationships/relationshipTypeRules` — do not introduce a new kind union.
- **Run a single test file:** `cd web && npx vitest run <path>`. **Full web suite:** `cd web && npm test`. **Type gate:** `cd web && npm run build`.

---

### Task 1: Slice setup — DoD ledger

**Files:**
- Create: `docs/superpowers/verification/2026-06-29-catalog-graph-filters/dod.md` (copy of `docs/superpowers/templates/dod-ledger-template.md`)
- Create: `docs/superpowers/verification/2026-06-29-catalog-graph-filters/gate-findings.yaml` (copy of `docs/superpowers/templates/gate-findings-template.yaml`)

**Interfaces:** none (bookkeeping).

- [ ] **Step 1: Copy the templates into the slice verification folder**

```bash
mkdir -p docs/superpowers/verification/2026-06-29-catalog-graph-filters
cp docs/superpowers/templates/dod-ledger-template.md docs/superpowers/verification/2026-06-29-catalog-graph-filters/dod.md
cp docs/superpowers/templates/gate-findings-template.yaml docs/superpowers/verification/2026-06-29-catalog-graph-filters/gate-findings.yaml
```

- [ ] **Step 2: Fill the ledger header** — set the slice title to "S-05 Graph filters (Kind + Team)", date `2026-06-29`, branch `feat/catalog-graph-filters`, and link the spec path. Note slice-specific gate dispositions from spec §9: **Gate 3 real-seam N/A** (frontend-only), **Gate 4 no API rebuild/codegen**, **Gate 6 mutation N/A** (no C# Domain/Application), **ADR-0084 manual pass required**.

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/verification/2026-06-29-catalog-graph-filters/
git commit -m "chore(superpowers): S-05 graph-filters DoD ledger + gate-findings (E-04.F-02.S-05)"
```

---

### Task 2: Thread `teamId` through the merged graph

The Team filter matches on `node.teamId`, but `mergeGraphs` currently drops it (it maps only `{ id, kind, entityId, displayName, depth }`). Add `teamId` to `ExplorerNode` and populate it from the response.

**Files:**
- Modify: `web/src/features/catalog/relationships/graphMerge.ts` (`ExplorerNode` type + the node-mapping block in `mergeGraphs`)
- Test: `web/src/features/catalog/relationships/__tests__/graphMerge.test.ts` (extend)

**Interfaces:**
- Produces: `ExplorerNode` gains `teamId?: string` (option-`value`-style string id; `undefined` when the wire value is `null`). `GraphResponse` node shape on the wire: `{ kind, id, displayName, depth: number|string, teamId: null|string }`.

- [ ] **Step 1: Write the failing test** — append to `__tests__/graphMerge.test.ts`:

```ts
it("threads teamId from the response onto the merged node (null → undefined)", () => {
  const merged = mergeGraphs([
    {
      truncated: false,
      nodes: [
        { kind: "application", id: "a1", displayName: "App 1", depth: 0, teamId: "team-1" },
        { kind: "service", id: "s1", displayName: "Svc 1", depth: 1, teamId: null },
      ],
      edges: [],
    } as unknown as GraphResponse,
  ]);
  expect(merged.nodes.find((n) => n.id === "application:a1")?.teamId).toBe("team-1");
  expect(merged.nodes.find((n) => n.id === "service:s1")?.teamId).toBeUndefined();
});
```

If `GraphResponse` isn't already imported in the test file, add `import type { GraphResponse } from "@/features/catalog/api/graph";` at the top.

- [ ] **Step 2: Run it and confirm it fails**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphMerge.test.ts`
Expected: FAIL — `teamId` is `undefined` for `application:a1` (the field isn't threaded yet).

- [ ] **Step 3: Add `teamId` to the type and the mapping** (Serena edit on the existing file).

In `ExplorerNode`, add the field:

```ts
export type ExplorerNode = {
  id: string;
  kind: RelationshipKind;
  entityId: string;
  displayName: string;
  depth?: number;
  teamId?: string;
};
```

In `mergeGraphs`, inside the `if (!nodes.has(id))` block, add `teamId`:

```ts
nodes.set(id, {
  id,
  kind: n.kind as RelationshipKind,
  entityId: n.id,
  displayName: n.displayName,
  depth: Number(n.depth),
  teamId: n.teamId ?? undefined,
});
```

- [ ] **Step 4: Run the test and confirm it passes**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphMerge.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit** (then check CRLF per Global Constraints)

```bash
git add web/src/features/catalog/relationships/graphMerge.ts web/src/features/catalog/relationships/__tests__/graphMerge.test.ts
git commit -m "feat(web): thread teamId through merged graph nodes (E-04.F-02.S-05)"
```

---

### Task 3: Pure `applyGraphFilters` + `GraphFilters` type

**Files:**
- Create: `web/src/features/catalog/relationships/graphFilter.ts`
- Test: `web/src/features/catalog/relationships/__tests__/graphFilter.test.ts`

**Interfaces:**
- Consumes: `ExplorerGraph` (from `graphMerge`, now with `teamId`), `RelationshipKind`.
- Produces:
  - `type GraphFilters = { kinds: RelationshipKind[]; teamIds: string[] }`
  - `applyGraphFilters(graph: ExplorerGraph, filters: GraphFilters, focusId: string): { dimmedNodeIds: Set<string>; dimmedEdgeIds: Set<string> }`

- [ ] **Step 1: Write the failing test** — create `__tests__/graphFilter.test.ts`:

```ts
import { describe, it, expect } from "vitest";
import { applyGraphFilters, type GraphFilters } from "@/features/catalog/relationships/graphFilter";
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";

const graph: ExplorerGraph = {
  truncated: false,
  nodes: [
    { id: "application:focus", kind: "application", entityId: "focus", displayName: "Focus", teamId: "t1" },
    { id: "application:a1", kind: "application", entityId: "a1", displayName: "App 1", teamId: "t1" },
    { id: "service:s1", kind: "service", entityId: "s1", displayName: "Svc 1", teamId: "t2" },
    { id: "service:s2", kind: "service", entityId: "s2", displayName: "Svc 2", teamId: undefined },
  ],
  edges: [
    { id: "e-focus-a1", source: "application:focus", target: "application:a1", label: "depends on" },
    { id: "e-a1-s1", source: "application:a1", target: "service:s1", label: "depends on" },
  ],
};
const focusId = "application:focus";
const empty: GraphFilters = { kinds: [], teamIds: [] };

describe("applyGraphFilters", () => {
  it("dims nothing when no facet is active", () => {
    const { dimmedNodeIds, dimmedEdgeIds } = applyGraphFilters(graph, empty, focusId);
    expect(dimmedNodeIds.size).toBe(0);
    expect(dimmedEdgeIds.size).toBe(0);
  });

  it("kind filter dims the other kind but never the focus", () => {
    const { dimmedNodeIds } = applyGraphFilters(graph, { kinds: ["application"], teamIds: [] }, focusId);
    expect(dimmedNodeIds.has("service:s1")).toBe(true);
    expect(dimmedNodeIds.has("service:s2")).toBe(true);
    expect(dimmedNodeIds.has("application:a1")).toBe(false);
    expect(dimmedNodeIds.has("application:focus")).toBe(false); // focus exempt
  });

  it("team filter dims other teams and null-team nodes (focus exempt)", () => {
    const { dimmedNodeIds } = applyGraphFilters(graph, { kinds: [], teamIds: ["t1"] }, focusId);
    expect(dimmedNodeIds.has("service:s1")).toBe(true);  // t2
    expect(dimmedNodeIds.has("service:s2")).toBe(true);  // null team
    expect(dimmedNodeIds.has("application:a1")).toBe(false); // t1
    expect(dimmedNodeIds.has("application:focus")).toBe(false);
  });

  it("ANDs facets together", () => {
    const { dimmedNodeIds } = applyGraphFilters(graph, { kinds: ["application"], teamIds: ["t2"] }, focusId);
    // only focus is exempt; a1 is application but t1 (fails team); s1 is t2 but service (fails kind)
    expect(dimmedNodeIds.has("application:a1")).toBe(true);
    expect(dimmedNodeIds.has("service:s1")).toBe(true);
    expect(dimmedNodeIds.has("application:focus")).toBe(false);
  });

  it("dims an edge iff either endpoint is dimmed", () => {
    const { dimmedEdgeIds } = applyGraphFilters(graph, { kinds: ["application"], teamIds: [] }, focusId);
    expect(dimmedEdgeIds.has("e-focus-a1")).toBe(false); // both endpoints applications
    expect(dimmedEdgeIds.has("e-a1-s1")).toBe(true);     // s1 dimmed
  });
});
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphFilter.test.ts`
Expected: FAIL — module `graphFilter` not found.

- [ ] **Step 3: Create the implementation** — `graphFilter.ts`:

```ts
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

export type GraphFilters = { kinds: RelationshipKind[]; teamIds: string[] };

/**
 * Pure filter pass over the in-memory merged graph. Returns the ids to dim;
 * never mutates the graph and never changes layout. The focus node is always
 * exempt. An edge dims iff either endpoint is dimmed. (S-05, spec §4.2.)
 */
export function applyGraphFilters(
  graph: ExplorerGraph,
  filters: GraphFilters,
  focusId: string,
): { dimmedNodeIds: Set<string>; dimmedEdgeIds: Set<string> } {
  const dimmedNodeIds = new Set<string>();
  const active = filters.kinds.length > 0 || filters.teamIds.length > 0;

  if (active) {
    for (const n of graph.nodes) {
      if (n.id === focusId) continue; // focus never dims
      const kindOk = filters.kinds.length === 0 || filters.kinds.includes(n.kind);
      const teamOk =
        filters.teamIds.length === 0 || (n.teamId != null && filters.teamIds.includes(n.teamId));
      if (!(kindOk && teamOk)) dimmedNodeIds.add(n.id);
    }
  }

  const dimmedEdgeIds = new Set<string>();
  for (const e of graph.edges) {
    if (dimmedNodeIds.has(e.source) || dimmedNodeIds.has(e.target)) dimmedEdgeIds.add(e.id);
  }

  return { dimmedNodeIds, dimmedEdgeIds };
}
```

- [ ] **Step 4: Run the test and confirm it passes**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphFilter.test.ts`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/relationships/graphFilter.ts web/src/features/catalog/relationships/__tests__/graphFilter.test.ts
git commit -m "feat(web): pure applyGraphFilters dim helper (E-04.F-02.S-05)"
```

---

### Task 4: `useGraphFilters` sessionStorage hook

**Files:**
- Create: `web/src/features/catalog/relationships/useGraphFilters.ts`
- Test: `web/src/features/catalog/relationships/__tests__/useGraphFilters.test.ts`

**Interfaces:**
- Consumes: `GraphFilters` (from `graphFilter`), `RelationshipKind`.
- Produces: `useGraphFilters(focusKey: string, storage?: Storage)` →
  `{ filters: GraphFilters; setKinds(kinds: RelationshipKind[]): void; setTeamIds(ids: string[]): void; clear(): void; isActive: boolean; activeCount: number }`.
  sessionStorage key `graph-explorer-filters:<focusKey>`.

- [ ] **Step 1: Write the failing test** — create `__tests__/useGraphFilters.test.ts`:

```ts
import { describe, it, expect } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useGraphFilters } from "@/features/catalog/relationships/useGraphFilters";

function makeStorage(): Storage {
  const m = new Map<string, string>();
  return {
    get length() { return m.size; },
    clear: () => m.clear(),
    getItem: (k) => m.get(k) ?? null,
    key: (i) => [...m.keys()][i] ?? null,
    removeItem: (k) => void m.delete(k),
    setItem: (k, v) => void m.set(k, v),
  };
}

describe("useGraphFilters", () => {
  it("defaults to empty filters", () => {
    const { result } = renderHook(() => useGraphFilters("application:focus", makeStorage()));
    expect(result.current.filters).toEqual({ kinds: [], teamIds: [] });
    expect(result.current.isActive).toBe(false);
    expect(result.current.activeCount).toBe(0);
  });

  it("persists and restores per focus key", () => {
    const storage = makeStorage();
    const first = renderHook(() => useGraphFilters("application:focus", storage));
    act(() => first.result.current.setKinds(["service"]));
    act(() => first.result.current.setTeamIds(["t1", "t2"]));
    expect(first.result.current.isActive).toBe(true);
    expect(first.result.current.activeCount).toBe(3);
    // a fresh hook on the same key reads the persisted value
    const second = renderHook(() => useGraphFilters("application:focus", storage));
    expect(second.result.current.filters).toEqual({ kinds: ["service"], teamIds: ["t1", "t2"] });
  });

  it("keeps independent state per focus key", () => {
    const storage = makeStorage();
    const a = renderHook(() => useGraphFilters("application:a", storage));
    act(() => a.result.current.setKinds(["application"]));
    const b = renderHook(() => useGraphFilters("service:b", storage));
    expect(b.result.current.filters).toEqual({ kinds: [], teamIds: [] });
  });

  it("clear() resets to empty", () => {
    const storage = makeStorage();
    const { result } = renderHook(() => useGraphFilters("application:focus", storage));
    act(() => result.current.setTeamIds(["t1"]));
    act(() => result.current.clear());
    expect(result.current.filters).toEqual({ kinds: [], teamIds: [] });
  });

  it("falls back to empty on corrupt JSON without throwing", () => {
    const storage = makeStorage();
    storage.setItem("graph-explorer-filters:application:focus", "{not json");
    const { result } = renderHook(() => useGraphFilters("application:focus", storage));
    expect(result.current.filters).toEqual({ kinds: [], teamIds: [] });
  });
});
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/useGraphFilters.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Create the hook** — `useGraphFilters.ts` (mirrors `useExplorerState`'s storage + prev-key-reconcile pattern):

```ts
import { useCallback, useState } from "react";
import type { GraphFilters } from "@/features/catalog/relationships/graphFilter";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const EMPTY: GraphFilters = { kinds: [], teamIds: [] };
const storageKey = (focusKey: string) => `graph-explorer-filters:${focusKey}`;

const isKind = (k: unknown): k is RelationshipKind => k === "application" || k === "service";

function read(storage: Storage, focusKey: string): GraphFilters {
  try {
    const raw = storage.getItem(storageKey(focusKey));
    if (!raw) return EMPTY;
    const parsed: unknown = JSON.parse(raw);
    if (!parsed || typeof parsed !== "object") return EMPTY;
    const p = parsed as Partial<GraphFilters>;
    return {
      kinds: Array.isArray(p.kinds) ? p.kinds.filter(isKind) : [],
      teamIds: Array.isArray(p.teamIds) ? p.teamIds.filter((t): t is string => typeof t === "string") : [],
    };
  } catch {
    return EMPTY;
  }
}

function write(storage: Storage, focusKey: string, filters: GraphFilters): void {
  try {
    storage.setItem(storageKey(focusKey), JSON.stringify(filters));
  } catch {
    /* storage unavailable (private mode / quota) — degrade to in-memory only */
  }
}

export function useGraphFilters(focusKey: string, storage: Storage = window.sessionStorage) {
  const [filters, setFilters] = useState<GraphFilters>(() => read(storage, focusKey));
  // Render-time reconcile when the focus key changes (project pattern, same as useExplorerState).
  const [prevKey, setPrevKey] = useState(focusKey);
  if (prevKey !== focusKey) {
    setPrevKey(focusKey);
    setFilters(read(storage, focusKey));
  }

  const commit = useCallback(
    (next: GraphFilters) => {
      write(storage, focusKey, next);
      setFilters(next);
    },
    [storage, focusKey],
  );

  const setKinds = useCallback((kinds: RelationshipKind[]) => commit({ ...filters, kinds }), [filters, commit]);
  const setTeamIds = useCallback((teamIds: string[]) => commit({ ...filters, teamIds }), [filters, commit]);
  const clear = useCallback(() => commit(EMPTY), [commit]);

  const isActive = filters.kinds.length > 0 || filters.teamIds.length > 0;
  const activeCount = filters.kinds.length + filters.teamIds.length;

  return { filters, setKinds, setTeamIds, clear, isActive, activeCount };
}
```

- [ ] **Step 4: Run the test and confirm it passes**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/useGraphFilters.test.ts`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/relationships/useGraphFilters.ts web/src/features/catalog/relationships/__tests__/useGraphFilters.test.ts
git commit -m "feat(web): useGraphFilters sessionStorage state keyed per focus (E-04.F-02.S-05)"
```

---

### Task 5: Add an optional controlled mode to `MultiSelect`

The overlay applies filters **live** and is **not** inside a `<form>`, so it needs a controlled `MultiSelect` (`selectedKeys` drives selection, `onChange` reports it). The change is **additive** — when neither prop is passed, `MultiSelect` behaves exactly as today (uncontrolled, used by `FilterBar`).

**Files:**
- Modify: `web/src/components/base/multi-select/multi-select.tsx`
- Test: `web/src/components/base/multi-select/__tests__/multi-select.test.tsx` (new)

**Interfaces:**
- Produces: `MultiSelectProps` gains `selectedKeys?: string[]` (controlled selection) and `onChange?: (values: string[]) => void` (fires on every selection change with the sorted selected `value`s). When `selectedKeys` is provided the component is controlled; otherwise it uses `defaultSelectedKeys` internal state as before.

- [ ] **Step 1: Write the failing test** — create `__tests__/multi-select.test.tsx`:

```tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MultiSelect } from "@/components/base/multi-select/multi-select";

const OPTIONS = [
  { label: "Application", value: "application" },
  { label: "Service", value: "service" },
];

describe("MultiSelect controlled mode", () => {
  it("reflects selectedKeys in the summary", () => {
    render(<MultiSelect name="k" aria-label="Kind" options={OPTIONS} selectedKeys={["application"]} onChange={() => {}} />);
    expect(screen.getByText("Application")).toBeInTheDocument();
  });

  it("fires onChange with the selected values when an option is picked", async () => {
    const onChange = vi.fn();
    render(<MultiSelect name="k" aria-label="Kind" options={OPTIONS} selectedKeys={[]} onChange={onChange} />);
    await userEvent.click(screen.getByLabelText("Kind"));
    await userEvent.click(screen.getByText("Service"));
    expect(onChange).toHaveBeenCalledWith(["service"]);
  });
});
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `cd web && npx vitest run src/components/base/multi-select/__tests__/multi-select.test.tsx`
Expected: FAIL — `selectedKeys`/`onChange` not honoured (onChange never called / summary not driven by prop).

- [ ] **Step 3: Make the component dual-mode** (Serena edit on the existing file).

In `MultiSelectProps`, add the two optional props (keep all existing ones):

```ts
  /** Uncontrolled initial selection (option `value`s). */
  defaultSelectedKeys?: string[];
  /** Controlled selection (option `value`s). When provided, the component is
   *  controlled and `defaultSelectedKeys` is ignored. */
  selectedKeys?: string[];
  /** Fires on every selection change with the sorted selected `value`s.
   *  Enables live-apply use outside a <form> (e.g. the graph filter overlay). */
  onChange?: (values: string[]) => void;
```

In the component, destructure the new props and derive `selected` from controlled prop when present:

```tsx
export const MultiSelect = ({
  name,
  label,
  options,
  defaultSelectedKeys,
  selectedKeys,
  onChange,
  placeholder = "Select…",
  size = "sm",
  className,
  ref,
  ...props
}: MultiSelectProps) => {
  const [internal, setInternal] = useState<Set<string>>(() => new Set(defaultSelectedKeys ?? []));
  const isControlled = selectedKeys !== undefined;
  const selected = isControlled ? new Set(selectedKeys) : internal;

  const handleChange = (keys: Selection) => {
    const next = keys === "all" ? new Set(options.map((o) => o.value)) : new Set([...keys].map((k) => String(k)));
    if (!isControlled) setInternal(next);
    onChange?.([...next].sort());
  };
```

Then replace the `onSelectionChange={onChange}` reference on `<AriaListBox>` with `onSelectionChange={handleChange}` (the local `onChange` const was renamed to `handleChange` to avoid colliding with the new `onChange` prop). All other JSX (the hidden inputs derived from `selected`, the summary, the popover) is unchanged.

- [ ] **Step 4: Run the new test and confirm it passes**

Run: `cd web && npx vitest run src/components/base/multi-select/__tests__/multi-select.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 5: Confirm FilterBar's uncontrolled usage still works** — run the existing list-page tests that exercise FilterBar multi-selects:

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/CatalogListPage.test.tsx`
Expected: PASS (no regression — FilterBar passes neither `selectedKeys` nor `onChange`, so the uncontrolled path is unchanged).

- [ ] **Step 6: Commit** (check CRLF per Global Constraints)

```bash
git add web/src/components/base/multi-select/multi-select.tsx web/src/components/base/multi-select/__tests__/multi-select.test.tsx
git commit -m "feat(web): optional controlled mode for MultiSelect (selectedKeys + onChange) (E-04.F-02.S-05)"
```

---

### Task 6: Thread `dimmed` through node data, layout, and the node renderer

**Files:**
- Modify: `web/src/features/catalog/relationships/graphModel.ts` (`GraphNodeData`)
- Modify: `web/src/features/catalog/relationships/graphLayout.ts` (`layoutGraph` signature + mapping)
- Modify: `web/src/features/catalog/components/EntityGraphNode.tsx` (muted variant)
- Test: `web/src/features/catalog/relationships/__tests__/graphLayout.test.ts` (extend)
- Test: `web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx` (extend)

**Interfaces:**
- Consumes: nothing from Task 3's module (decoupled — uses plain id sets).
- Produces:
  - `GraphNodeData` gains `dimmed?: boolean`.
  - `layoutGraph(graph, focusId, selectedId, dimmed?)` — new optional 4th arg `dimmed: { nodeIds: Set<string>; edgeIds: Set<string> }` (default both empty). Sets `data.dimmed` on nodes and `style: { opacity: 0.2 }` on dimmed edges.

- [ ] **Step 1: Write the failing layout test** — append to `__tests__/graphLayout.test.ts`:

```ts
it("threads dimmed flags onto node data and edge style", () => {
  const graph = {
    truncated: false,
    nodes: [
      { id: "application:focus", kind: "application", entityId: "focus", displayName: "Focus" },
      { id: "service:s1", kind: "service", entityId: "s1", displayName: "Svc 1" },
    ],
    edges: [{ id: "e1", source: "application:focus", target: "service:s1", label: "depends on" }],
  } as const;
  const { nodes, edges } = layoutGraph(
    graph as unknown as Parameters<typeof layoutGraph>[0],
    "application:focus",
    null,
    { nodeIds: new Set(["service:s1"]), edgeIds: new Set(["e1"]) },
  );
  expect(nodes.find((n) => n.id === "service:s1")?.data.dimmed).toBe(true);
  expect(nodes.find((n) => n.id === "application:focus")?.data.dimmed).toBe(false);
  expect(edges.find((e) => e.id === "e1")?.style?.opacity).toBe(0.2);
});
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphLayout.test.ts`
Expected: FAIL — `layoutGraph` ignores the 4th arg; `data.dimmed` undefined / edge has no `style`.

- [ ] **Step 3: Add `dimmed?` to `GraphNodeData`** (Serena edit `graphModel.ts`):

```ts
export type GraphNodeData = {
  kind: RelationshipKind;
  entityId: string;
  displayName: string;
  side: GraphSide;
  selected?: boolean; // explorer: the currently-selected node (sidebar open on it)
  dimmed?: boolean; // explorer: faded because it doesn't match the active filters (focus never dims)
};
```

- [ ] **Step 4: Thread `dimmed` through `layoutGraph`** (Serena edit `graphLayout.ts`) — new optional param + node/edge mapping:

```ts
export function layoutGraph(
  graph: ExplorerGraph,
  focusId: string,
  selectedId: string | null,
  dimmed: { nodeIds: Set<string>; edgeIds: Set<string> } = { nodeIds: new Set(), edgeIds: new Set() },
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
        dimmed: dimmed.nodeIds.has(n.id),
      },
    };
  });

  const edges: Edge[] = graph.edges.map((e) => ({
    id: e.id,
    source: e.source,
    target: e.target,
    label: e.label,
    ...(dimmed.edgeIds.has(e.id) ? { style: { opacity: 0.2 } } : {}),
  }));
  return { nodes, edges };
}
```

- [ ] **Step 5: Run the layout test and confirm it passes**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphLayout.test.ts`
Expected: PASS (existing tests unaffected — the new param defaults to empty sets).

- [ ] **Step 6: Write the failing node-variant test** — append to `__tests__/EntityGraphNode.test.tsx`:

```tsx
it("applies a muted class when data.dimmed is true", () => {
  const { container } = render(
    <ReactFlowProvider>
      <EntityGraphNode
        {...({
          data: { kind: "service", entityId: "s1", displayName: "Svc 1", side: "dependency", dimmed: true },
        } as never)}
      />
    </ReactFlowProvider>,
  );
  expect(container.querySelector(".opacity-30")).not.toBeNull();
});
```

If `ReactFlowProvider` / `render` / `EntityGraphNode` aren't already imported in the file, mirror the imports already used by the existing tests in this file (they render `EntityGraphNode`); add `import { ReactFlowProvider } from "@xyflow/react";` if missing.

- [ ] **Step 7: Run it and confirm it fails**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/EntityGraphNode.test.tsx`
Expected: FAIL — no `opacity-30` element (dimmed treatment not applied yet).

- [ ] **Step 8: Add the muted variant** (Serena edit `EntityGraphNode.tsx`):

```tsx
export function EntityGraphNode({ data }: NodeProps<Node<GraphNodeData>>) {
  const base = "rounded-lg bg-primary px-3 py-2";
  const variant = data.selected
    ? "border-2 border-brand-solid shadow-md"
    : data.side === "focused"
      ? "border-2 border-secondary font-semibold shadow-sm"
      : "border border-secondary shadow-xs";
  const dim = data.dimmed ? "opacity-30" : "";
  return (
    <div className={`${base} ${variant} ${dim}`}>
      <Handle type="target" position={Position.Left} className="!border-0 !bg-transparent" />
      <div className="text-sm text-primary">{data.displayName}</div>
      <div className="text-xs text-tertiary">{ENTITY_KIND_LABEL[data.kind] ?? data.kind}</div>
      <Handle type="source" position={Position.Right} className="!border-0 !bg-transparent" />
    </div>
  );
}
```

- [ ] **Step 9: Run the node test and confirm it passes**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/EntityGraphNode.test.tsx`
Expected: PASS.

- [ ] **Step 10: Commit** (check CRLF per Global Constraints)

```bash
git add web/src/features/catalog/relationships/graphModel.ts web/src/features/catalog/relationships/graphLayout.ts web/src/features/catalog/components/EntityGraphNode.tsx web/src/features/catalog/relationships/__tests__/graphLayout.test.ts web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx
git commit -m "feat(web): dimmed variant on graph nodes + edges (E-04.F-02.S-05)"
```

---

### Task 7: `GraphFilterControls` canvas-overlay panel

**Files:**
- Create: `web/src/features/catalog/components/GraphFilterControls.tsx`
- Test: `web/src/features/catalog/components/__tests__/GraphFilterControls.test.tsx`

**Interfaces:**
- Consumes: `MultiSelect` (controlled mode from Task 5), `RelationshipKind`.
- Produces: `GraphFilterControls` with props
  `{ kinds: RelationshipKind[]; teamIds: string[]; teams: { id: string; displayName: string }[]; activeCount: number; onKindsChange: (k: RelationshipKind[]) => void; onTeamIdsChange: (ids: string[]) => void; onClear: () => void }`.

- [ ] **Step 1: Write the failing test** — create `__tests__/GraphFilterControls.test.tsx`:

```tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { GraphFilterControls } from "@/features/catalog/components/GraphFilterControls";

const teams = [{ id: "t1", displayName: "Team One" }, { id: "t2", displayName: "Team Two" }];

describe("GraphFilterControls", () => {
  it("fires onKindsChange when a kind is picked", async () => {
    const onKindsChange = vi.fn();
    render(
      <GraphFilterControls
        kinds={[]} teamIds={[]} teams={teams} activeCount={0}
        onKindsChange={onKindsChange} onTeamIdsChange={() => {}} onClear={() => {}}
      />,
    );
    await userEvent.click(screen.getByLabelText("Filter by kind"));
    await userEvent.click(screen.getByText("Service"));
    expect(onKindsChange).toHaveBeenCalledWith(["service"]);
  });

  it("shows the active count and Clear, and Clear fires onClear", async () => {
    const onClear = vi.fn();
    render(
      <GraphFilterControls
        kinds={["application"]} teamIds={["t1"]} teams={teams} activeCount={2}
        onKindsChange={() => {}} onTeamIdsChange={() => {}} onClear={onClear}
      />,
    );
    expect(screen.getByText("Filters (2)")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: "Clear" }));
    expect(onClear).toHaveBeenCalled();
  });

  it("renders without Clear when no filter is active", () => {
    render(
      <GraphFilterControls
        kinds={[]} teamIds={[]} teams={[]} activeCount={0}
        onKindsChange={() => {}} onTeamIdsChange={() => {}} onClear={() => {}}
      />,
    );
    expect(screen.getByText("Filters")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Clear" })).toBeNull();
  });
});
```

- [ ] **Step 2: Run it and confirm it fails**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/GraphFilterControls.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Create the component** — `GraphFilterControls.tsx`:

```tsx
import { MultiSelect } from "@/components/base/multi-select/multi-select";
import { Button } from "@/components/base/buttons/button";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const KIND_OPTIONS = [
  { label: "Application", value: "application" },
  { label: "Service", value: "service" },
];

export interface GraphFilterControlsProps {
  kinds: RelationshipKind[];
  teamIds: string[];
  teams: { id: string; displayName: string }[];
  activeCount: number;
  onKindsChange: (kinds: RelationshipKind[]) => void;
  onTeamIdsChange: (ids: string[]) => void;
  onClear: () => void;
}

/**
 * Canvas-overlay filter control for the graph explorer (ADR-0040). Live-apply
 * (no submit) — filtering is client-side and instant. Reuses the controlled
 * MultiSelect; not the list-page FilterBar chrome. (S-05.)
 */
export function GraphFilterControls({
  kinds,
  teamIds,
  teams,
  activeCount,
  onKindsChange,
  onTeamIdsChange,
  onClear,
}: GraphFilterControlsProps) {
  const teamOptions = teams.map((t) => ({ label: t.displayName, value: t.id }));
  const isActive = activeCount > 0;
  return (
    <div className="flex w-60 flex-col gap-2 rounded-lg bg-primary p-3 shadow-lg ring-1 ring-secondary">
      <div className="flex items-center justify-between">
        <span className="text-xs font-semibold text-secondary">
          Filters{isActive ? ` (${activeCount})` : ""}
        </span>
        {isActive && (
          <Button size="sm" color="link-gray" onClick={onClear}>
            Clear
          </Button>
        )}
      </div>
      <MultiSelect
        name="graph-kind"
        aria-label="Filter by kind"
        placeholder="Any kind"
        options={KIND_OPTIONS}
        selectedKeys={kinds}
        onChange={(v) => onKindsChange(v as RelationshipKind[])}
      />
      <MultiSelect
        name="graph-team"
        aria-label="Filter by team"
        placeholder="All teams"
        options={teamOptions}
        selectedKeys={teamIds}
        onChange={onTeamIdsChange}
      />
    </div>
  );
}
```

- [ ] **Step 4: Run the test and confirm it passes**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/GraphFilterControls.test.tsx`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/GraphFilterControls.tsx web/src/features/catalog/components/__tests__/GraphFilterControls.test.tsx
git commit -m "feat(web): GraphFilterControls canvas-overlay (Kind + Team) (E-04.F-02.S-05)"
```

---

### Task 8: Wire filters into `GraphExplorerPage`

**Files:**
- Modify: `web/src/features/catalog/pages/GraphExplorerPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx` (extend)

**Interfaces:**
- Consumes: `useGraphFilters` (Task 4), `applyGraphFilters` (Task 3), `layoutGraph` 4-arg form (Task 6), `GraphFilterControls` (Task 7), `useTeamsList` (`@/features/teams/api/teams`), React Flow `Panel`.

- [ ] **Step 1: Write the failing page test** — append to `__tests__/GraphExplorerPage.test.tsx`. Match the file's existing mock setup for `useGraph` / `useExplorerState` / React Flow; this case mocks the filter hook to a fixed active state and asserts the rendered nodes carry `dimmed` for the non-matching kind while the focus stays un-dimmed. Adapt the mock identifiers to the ones already used at the top of the file.

```tsx
it("dims nodes that don't match the active kind filter, never the focus", async () => {
  // Arrange: a graph with the focus (application) + one service neighbour, and an
  // active Kind=application filter. (Reuse the file's existing useGraph mock shape.)
  mockUseGraph.mockReturnValue({
    results: [
      {
        truncated: false,
        nodes: [
          { kind: "application", id: "focus", displayName: "Focus", depth: 0, teamId: "t1" },
          { kind: "service", id: "s1", displayName: "Svc 1", depth: 1, teamId: "t1" },
        ],
        edges: [{ id: "e1", source: { kind: "application", id: "focus" }, target: { kind: "service", id: "s1" }, type: "DependsOn", origin: "Manual" }],
      },
    ],
    isLoading: false, isError: false, expandError: false, refetch: vi.fn(),
  });
  mockUseGraphFilters.mockReturnValue({
    filters: { kinds: ["application"], teamIds: [] },
    setKinds: vi.fn(), setTeamIds: vi.fn(), clear: vi.fn(), isActive: true, activeCount: 1,
  });

  renderPage("/graph?focus=application:focus"); // file's existing render helper

  // The captured ReactFlow nodes (the file already captures props via the ReactFlow mock):
  const passed = capturedReactFlowNodes();
  expect(passed.find((n) => n.id === "service:s1")?.data.dimmed).toBe(true);
  expect(passed.find((n) => n.id === "application:focus")?.data.dimmed).toBe(false);
});
```

If the existing test file does not yet mock `useGraphFilters`, add `vi.mock("@/features/catalog/relationships/useGraphFilters", () => ({ useGraphFilters: (...args) => mockUseGraphFilters(...args) }));` alongside the file's other `vi.mock` calls, and a default `mockUseGraphFilters` return of `{ filters: { kinds: [], teamIds: [] }, setKinds: vi.fn(), setTeamIds: vi.fn(), clear: vi.fn(), isActive: false, activeCount: 0 }` in `beforeEach` so the pre-existing tests keep passing. Also mock `useTeamsList` to `{ items: [] }`.

- [ ] **Step 2: Run it and confirm it fails**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx`
Expected: FAIL — nodes have no `dimmed` flag (page doesn't apply filters yet).

- [ ] **Step 3: Wire the page** (Serena edit `GraphExplorerPage.tsx`). Add imports:

```tsx
import { ReactFlow, Background, Controls, MiniMap, Panel, type Node, type Edge } from "@xyflow/react";
import { useGraphFilters } from "@/features/catalog/relationships/useGraphFilters";
import { applyGraphFilters } from "@/features/catalog/relationships/graphFilter";
import { GraphFilterControls } from "@/features/catalog/components/GraphFilterControls";
import { useTeamsList } from "@/features/teams/api/teams";
```

After the `useExplorerState` line, add the filter hook + teams query:

```tsx
  const { filters, setKinds, setTeamIds, clear, activeCount } = useGraphFilters(focusId);
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
```

Compute dimmed sets and pass them to `layoutGraph` (replace the existing `merged` → `layoutGraph` block):

```tsx
  const dimmed = useMemo(
    () => applyGraphFilters(merged, filters, focusId),
    [merged, filters, focusId],
  );
  const { nodes, edges } = useMemo(
    () =>
      focusId
        ? layoutGraph(merged, focusId, selected, { nodeIds: dimmed.dimmedNodeIds, edgeIds: dimmed.dimmedEdgeIds })
        : { nodes: [] as Node<GraphNodeData>[], edges: [] as Edge[] },
    [merged, focusId, selected, dimmed],
  );
```

Inside `<ReactFlow>…</ReactFlow>` (alongside `<Background/>` etc.), add the overlay panel:

```tsx
                <Panel position="top-left">
                  <GraphFilterControls
                    kinds={filters.kinds}
                    teamIds={filters.teamIds}
                    teams={teamsList.items ?? []}
                    activeCount={activeCount}
                    onKindsChange={setKinds}
                    onTeamIdsChange={setTeamIds}
                    onClear={clear}
                  />
                </Panel>
```

- [ ] **Step 4: Run the page tests and confirm they pass**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx`
Expected: PASS (new case + all pre-existing cases).

- [ ] **Step 5: Type gate + full web suite**

Run: `cd web && npm run build`
Expected: `tsc -b` clean (the new `dimmed`, `selectedKeys`/`onChange`, and `Panel` usages all type-check), vite build succeeds.

Run: `cd web && npm test`
Expected: full web suite green.

- [ ] **Step 6: Commit** (check CRLF per Global Constraints)

```bash
git add web/src/features/catalog/pages/GraphExplorerPage.tsx web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx
git commit -m "feat(web): wire Kind+Team filters into the graph explorer (E-04.F-02.S-05)"
```

---

### Task 9: Registry + checklist docs

**Files:**
- Modify: `docs/design/list-filter-registry.md` (the `/graph` row)
- Modify: `docs/product/CHECKLIST.md` (mark E-04.F-02.S-05)

**Interfaces:** none (docs).

- [ ] **Step 1: Update the list-filter registry** — change the dependency-graph row from **pending** to **built** for `kind` + `teamId`, and record the deferrals. Replace the existing `/graph` row with:

```markdown
| Dependency-graph filters | `/graph` | `kind` (multi-select) + `teamId` (multi-select) | **built** | E-04.F-02.S-05 | Canvas-overlay (React Flow `<Panel>`, ADR-0040), **client-side dim/fade** of non-matches (focus never dims; edge dims iff either endpoint dims), **live-apply**, state in `sessionStorage` keyed by focus (only `?focus` in URL). Deferred: **status** (Lifecycle/Health — needs `GraphNodeDto` enrichment + combined-status story), **origin** (only `Manual` exists → Phase 2 scan/agent / E-06), **domain** + **criticality** (no backing field → new-field epic). |
```

- [ ] **Step 2: Mark the story complete** in `docs/product/CHECKLIST.md` — set E-04.F-02.S-05 to `[x]` with a one-line note:

```markdown
- [x] E-04.F-02.S-05 — Graph filters (Kind + Team) (catalog-graph-filters, 2026-06-29; client-side dim/fade Kind + Team filters on the standalone `/graph` explorer — canvas-overlay `<Panel>`, sessionStorage per focus, live-apply; focus never dims, edge dims iff either endpoint dims. Frontend-only, zero backend. Status/origin/domain/criticality deferred — see list-filter-registry. ADR-0040.)
```

- [ ] **Step 3: Commit**

```bash
git add docs/design/list-filter-registry.md docs/product/CHECKLIST.md
git commit -m "docs(catalog): record S-05 graph filters in registry + checklist (E-04.F-02.S-05)"
```

---

### Task 10: Verify, DoD gates, manual pass

**Files:**
- Modify: `docs/superpowers/verification/2026-06-29-catalog-graph-filters/dod.md` + `gate-findings.yaml` (update as each gate runs)

**Interfaces:** none (verification).

- [ ] **Step 1: Pre-push CI mirror (web subset)** — run the web slice of the local CI mirror:

Run: `bash scripts/ci-local.sh web` (or the documented web subset)
Expected: Release-config web build + full vitest suite green. Record the command + result in `dod.md` (Gates 1/3/4 rows).

- [ ] **Step 2: `/simplify` against the branch diff** (Gate 5) — run `/simplify`; address should-fix items (reuse/quality/efficiency) or note skips with a reason in `dod.md`.

- [ ] **Step 3: Per-task reviews + slice review** (Gates 2/7) — `/superpowers:requesting-code-review` against the full branch diff (spec + this plan as context); `/pr-review-toolkit:review-pr`; `/deep-review`. Log blocking/should-fix into `dod.md` + `gate-findings.yaml`; address blocking + should-fix, triage nits.

- [ ] **Step 4: Manual verification (ADR-0084)** — cold-start the dev server, then drive Playwright MCP per spec §7.5:
  - `/graph?focus=…` renders; open the overlay; **Kind = Application** fades service nodes + edges touching them; focus stays full.
  - **Team** selection fades off-team nodes; combine with Kind (AND); **Clear** restores all.
  - A faded node is still clickable (select → sidebar) and expandable.
  - Filters persist across an expand and across **F5**; re-rooting via "Set as focus" starts fresh filters.
  - Console clean. Capture a before/after screenshot into the verification folder.
  - **DevSeed has no relationships** — seed a small multi-node graph first (note the seeding step). If the dev stack is unavailable in-session, mark this **pending user verification** in `dod.md`.

- [ ] **Step 5: Gate 6 (mutation) — record N/A** — diff is frontend-only (no C# Domain/Application). Note N/A + rationale in `dod.md` and `gate-findings.yaml`.

- [ ] **Step 6: Terminal re-verify** — after any fixes from Steps 2–4, re-run `cd web && npm run build` + `cd web && npm test` and confirm green. Update `dod.md` summary table.

- [ ] **Step 7: Push + open PR**

```bash
git push -u origin feat/catalog-graph-filters
```

Open the PR (title `feat(web): graph explorer Kind + Team filters (E-04.F-02.S-05)`), linking the spec + this plan. Let CI run as the authoritative full-suite/container gate.

---

## Self-Review

**1. Spec coverage:**
- Kind + Team facets, client-side, zero backend (spec §1/§3 #1/#2) → Tasks 2, 3, 7, 8.
- Dim/fade, focus never dims, edge dims iff either endpoint (spec §3 #5/#6, §4.2) → Task 3 (logic + tests) + Task 6 (render).
- sessionStorage per focus, separate hook, only `?focus` in URL (spec §3 #3, §4.1) → Task 4.
- Canvas-overlay control, live-apply, reuse multi-select vocabulary (spec §3 #4/#8, §5.3) → Tasks 5 + 7.
- Team options = all org teams (spec §3 #11) → Task 8 (`useTeamsList`) + Task 7 (props).
- Position stability / data-only annotation (spec §3 #9) → Task 6 (dagre runs on the unfiltered graph; `dimmed` is data only).
- ADR-0107 registry + field-addition trigger (spec §8) → Task 9.
- DoD gates incl. real-seam N/A, mutation N/A, ADR-0084 (spec §9) → Tasks 1 + 10.
- **Gap closed vs spec:** spec §4.4 omitted `graphMerge.ts` (teamId threading) and the `MultiSelect` change — both are present here (Tasks 2 + 5) because the live code dropped `teamId` in merge and `MultiSelect` had no controlled mode. No spec requirement is left without a task.

**2. Placeholder scan:** every code step contains complete code; every command has an expected result. The only intentional "adapt to the file's existing mocks" note is in Task 8 Step 1 (the existing `GraphExplorerPage.test.tsx` already owns the `useGraph`/ReactFlow mock harness; the new case must reuse those identifiers rather than re-declaring a parallel harness) — concrete mock returns and assertions are given.

**3. Type consistency:** `GraphFilters` defined once (Task 3, `graphFilter.ts`), imported by Task 4. `RelationshipKind` reused everywhere (no parallel `EntityKind`). `applyGraphFilters` returns `{ dimmedNodeIds, dimmedEdgeIds }` (Task 3) consumed verbatim by Task 8 and shaped into `layoutGraph`'s `{ nodeIds, edgeIds }` param (Task 6) at the call site. `MultiSelect` gains `selectedKeys` + `onChange` (Task 5) used by `GraphFilterControls` (Task 7). `GraphNodeData.dimmed?` (Task 6) read by `EntityGraphNode` (Task 6) and asserted in Task 8. Consistent throughout.
