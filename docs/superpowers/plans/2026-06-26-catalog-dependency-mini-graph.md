# Catalog Dependency Mini-Graph Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Render a read-only, 1-hop React Flow dependency mini-graph above the existing Dependencies/Dependents tables on the Application and Service detail pages.

**Architecture:** Frontend-only. A pure mapper (`toGraphModel`) projects the relationship list (`GET /relationships?direction=all`, reused verbatim from Slice 1b) into a React Flow node/edge model; a `<DependencyMiniGraph>` component fetches, renders `<ReactFlow>` with a custom node, and navigates on node click. Lazy-loaded so the canvas library is a detail-route-only chunk. No backend, no API change, no codegen.

**Tech Stack:** React 19 + TypeScript (strict), Vite, Vitest, React Router v7, TanStack Query, `@xyflow/react` (new), Untitled UI / Tailwind v4.

**Spec:** `docs/superpowers/specs/2026-06-26-catalog-dependency-mini-graph-design.md`
**Branch:** `feat/catalog-dependency-mini-graph` (already created; spec committed).

## Global Constraints

- **Enum wire format is camelCase (ADR-0109):** `RelationshipResponse.type` is `"dependsOn"` / `"partOf"`; entity kinds are `"application"` / `"service"` (lowercase). Never hand-author PascalCase. Reuse `relationshipTypeLabel` from `relationshipTypeRules.ts` for edge labels.
- **Entity detail routes:** `/catalog/applications/:id` and `/catalog/services/:id`. The kind→path mapping is `kind === "application" ? "applications" : "services"` (the 1b `entityLink` convention).
- **`useRelationshipsList(params)` returns** `{ items, isLoading, isError, hasNext, hasPrev, goNext, goPrev }` (a `useCursorList` result); `items` is the current page (≤ `limit`).
- **Binding type gate is `tsc -b`** (`npm run build`), not per-file `tsc --noEmit` (ADR-0109).
- **Serena-guard hook:** NEW files are created with `Write`; EDITS to EXISTING `.ts`/`.tsx` (the two detail pages) MUST go through Serena symbolic tools (`replace_content` / `insert_*`), not `Edit`/`Write`. Markdown (`CHECKLIST.md`) edits use `Edit`.
- **All `web/` commands** run from the `web/` directory (Windows PowerShell). Single-file tests: `npx vitest run <path>`.
- **No backend / no codegen / no new endpoint** in this slice. If you find yourself editing C# or `openapi.ts`, stop — it's out of scope.

---

### Task 1: Add the `@xyflow/react` dependency

**Files:**
- Modify: `web/package.json` (+ `web/package-lock.json`)

**Interfaces:**
- Produces: the `@xyflow/react` module (exports `ReactFlow`, `Background`, `Handle`, `Position`, and types `Node`, `Edge`, `NodeProps`) available to later tasks.

- [ ] **Step 1: Install the dependency**

Run (from `web/`):
```
npm install @xyflow/react
```
Expected: `package.json` gains `"@xyflow/react": "^12.x"` under `dependencies`; `package-lock.json` updated. (v12+ supports React 19.)

- [ ] **Step 2: Verify the build still type-checks and compiles**

Run (from `web/`):
```
npm run build
```
Expected: PASS (`tsc -b` clean, vite build succeeds). Nothing imports the package yet — this confirms the install didn't break the toolchain.

- [ ] **Step 3: Commit**

```
git add web/package.json web/package-lock.json
git commit -m "build(web): add @xyflow/react for the dependency mini-graph"
```

---

### Task 2: `toGraphModel` pure mapper (TDD)

Projects the relationship list into a React Flow node/edge model. Pure function, no React — this is where all the logic lives, so it carries the strong test coverage.

**Files:**
- Create: `web/src/features/catalog/relationships/graphModel.ts`
- Test: `web/src/features/catalog/relationships/__tests__/graphModel.test.ts`

**Interfaces:**
- Consumes: `RelationshipResponse` (from `@/features/catalog/api/relationships`), `RelationshipKind` + `CreatableRelationshipType` + `relationshipTypeLabel` (from `@/features/catalog/relationships/relationshipTypeRules`).
- Produces:
  - `type GraphSide = "focused" | "dependency" | "dependent"`
  - `type GraphNodeData = { kind: RelationshipKind; entityId: string; displayName: string; side: GraphSide }`
  - `type GraphNode = { id: string; type: "entity"; position: { x: number; y: number }; data: GraphNodeData }`
  - `type GraphEdge = { id: string; source: string; target: string; label: string }`
  - `type GraphModel = { nodes: GraphNode[]; edges: GraphEdge[] }`
  - `type FocusedEntity = { kind: RelationshipKind; id: string; displayName: string }`
  - `function toGraphModel(focused: FocusedEntity, relationships: RelationshipResponse[]): GraphModel`

- [ ] **Step 1: Write the failing test**

Create `web/src/features/catalog/relationships/__tests__/graphModel.test.ts`:
```ts
import { describe, it, expect } from "vitest";
import { toGraphModel, type FocusedEntity } from "@/features/catalog/relationships/graphModel";
import type { RelationshipResponse } from "@/features/catalog/api/relationships";

const focused: FocusedEntity = { kind: "service", id: "s1", displayName: "Me" };
const focusedNodeId = "service:s1";

function rel(over: Partial<RelationshipResponse>): RelationshipResponse {
  return {
    id: "r",
    type: "dependsOn",
    origin: "manual",
    source: { kind: "service", id: "s1", displayName: "Me" },
    target: { kind: "service", id: "s2", displayName: "AuthService" },
    createdByUserId: "u1",
    createdAt: "2026-06-25T00:00:00Z",
    ...over,
  } as RelationshipResponse;
}

describe("toGraphModel", () => {
  it("returns only the focused node when there are no relationships", () => {
    const m = toGraphModel(focused, []);
    expect(m.nodes).toHaveLength(1);
    expect(m.nodes[0]!.id).toBe(focusedNodeId);
    expect(m.nodes[0]!.data.side).toBe("focused");
    expect(m.edges).toHaveLength(0);
  });

  it("places an outgoing edge's other endpoint on the dependency side", () => {
    const m = toGraphModel(focused, [rel({ id: "r1" })]); // focused is source
    const other = m.nodes.find((n) => n.id === "service:s2")!;
    expect(other.data.side).toBe("dependency");
    expect(m.edges).toEqual([{ id: "r1", source: "service:s1", target: "service:s2", label: "Depends on" }]);
  });

  it("places an incoming edge's other endpoint on the dependent side", () => {
    const m = toGraphModel(focused, [
      rel({ id: "r2", source: { kind: "application", id: "a1", displayName: "Checkout" }, target: { kind: "service", id: "s1", displayName: "Me" } }),
    ]);
    const other = m.nodes.find((n) => n.id === "application:a1")!;
    expect(other.data.side).toBe("dependent");
    expect(m.edges).toEqual([{ id: "r2", source: "application:a1", target: "service:s1", label: "Depends on" }]);
  });

  it("labels a partOf edge 'Part of'", () => {
    const m = toGraphModel(focused, [
      rel({ id: "r3", type: "partOf", source: { kind: "service", id: "s1", displayName: "Me" }, target: { kind: "application", id: "a9", displayName: "Billing" } }),
    ]);
    expect(m.edges[0]!.label).toBe("Part of");
  });

  it("dedupes a neighbour seen in both directions to one node (dependency side) with both edges", () => {
    const m = toGraphModel(focused, [
      rel({ id: "out", source: { kind: "service", id: "s1", displayName: "Me" }, target: { kind: "service", id: "s2", displayName: "AuthService" } }),
      rel({ id: "in", source: { kind: "service", id: "s2", displayName: "AuthService" }, target: { kind: "service", id: "s1", displayName: "Me" } }),
    ]);
    expect(m.nodes.filter((n) => n.id === "service:s2")).toHaveLength(1);
    expect(m.nodes.find((n) => n.id === "service:s2")!.data.side).toBe("dependency");
    expect(m.edges).toHaveLength(2);
  });

  it("ignores a relationship that does not reference the focused entity", () => {
    const m = toGraphModel(focused, [
      rel({ id: "x", source: { kind: "service", id: "zzz", displayName: "Other" }, target: { kind: "service", id: "yyy", displayName: "Another" } }),
    ]);
    expect(m.nodes).toHaveLength(1); // focused only
    expect(m.edges).toHaveLength(0);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`):
```
npx vitest run src/features/catalog/relationships/__tests__/graphModel.test.ts
```
Expected: FAIL — cannot resolve `@/features/catalog/relationships/graphModel`.

- [ ] **Step 3: Write the implementation**

Create `web/src/features/catalog/relationships/graphModel.ts`:
```ts
import type { RelationshipResponse } from "@/features/catalog/api/relationships";
import {
  relationshipTypeLabel,
  type RelationshipKind,
  type CreatableRelationshipType,
} from "@/features/catalog/relationships/relationshipTypeRules";

export type GraphSide = "focused" | "dependency" | "dependent";

export type GraphNodeData = {
  kind: RelationshipKind;
  entityId: string;
  displayName: string;
  side: GraphSide;
};

export type GraphNode = {
  id: string;
  type: "entity";
  position: { x: number; y: number };
  data: GraphNodeData;
};

export type GraphEdge = { id: string; source: string; target: string; label: string };

export type GraphModel = { nodes: GraphNode[]; edges: GraphEdge[] };

export type FocusedEntity = { kind: RelationshipKind; id: string; displayName: string };

const nodeId = (kind: string, id: string) => `${kind}:${id}`;

// Layout: dependents column (left) → focused (centre) → dependencies (right).
const COL_X: Record<GraphSide, number> = { dependent: 0, focused: 320, dependency: 640 };
const ROW_GAP = 90;

export function toGraphModel(focused: FocusedEntity, relationships: RelationshipResponse[]): GraphModel {
  const focusedId = nodeId(focused.kind, focused.id);
  const neighbours = new Map<string, GraphNodeData>();
  const edges: GraphEdge[] = [];

  for (const r of relationships) {
    const focusedIsSource = r.source.kind === focused.kind && r.source.id === focused.id;
    const focusedIsTarget = r.target.kind === focused.kind && r.target.id === focused.id;
    if (!focusedIsSource && !focusedIsTarget) continue;

    const other = focusedIsSource ? r.target : r.source;
    const otherId = nodeId(other.kind, other.id);
    const side: GraphSide = focusedIsSource ? "dependency" : "dependent";

    const existing = neighbours.get(otherId);
    if (!existing) {
      neighbours.set(otherId, {
        kind: other.kind as RelationshipKind,
        entityId: other.id,
        displayName: other.displayName,
        side,
      });
    } else if (existing.side === "dependent" && side === "dependency") {
      existing.side = "dependency"; // a node that is both → prefer the dependency side
    }

    edges.push({
      id: r.id,
      source: nodeId(r.source.kind, r.source.id),
      target: nodeId(r.target.kind, r.target.id),
      label: relationshipTypeLabel[r.type as CreatableRelationshipType] ?? r.type,
    });
  }

  const nodes: GraphNode[] = [
    { id: focusedId, type: "entity", position: { x: COL_X.focused, y: 0 }, data: { ...focused, entityId: focused.id, side: "focused" } },
  ];

  let depRow = 0;
  let dentRow = 0;
  for (const [id, data] of neighbours) {
    const row = data.side === "dependency" ? depRow++ : dentRow++;
    nodes.push({ id, type: "entity", position: { x: COL_X[data.side], y: row * ROW_GAP }, data });
  }

  return { nodes, edges };
}
```
Note: `{ ...focused, entityId: focused.id, side: "focused" }` spreads `{ kind, id, displayName }` then adds `entityId`; the stray `id` field on `data` is harmless (GraphNodeData has no `id`), but to keep `data` exactly typed, write the focused node's `data` explicitly: `{ kind: focused.kind, entityId: focused.id, displayName: focused.displayName, side: "focused" }`. Use the explicit form.

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`):
```
npx vitest run src/features/catalog/relationships/__tests__/graphModel.test.ts
```
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```
git add web/src/features/catalog/relationships/graphModel.ts web/src/features/catalog/relationships/__tests__/graphModel.test.ts
git commit -m "feat(web): toGraphModel mapper for the dependency mini-graph"
```

---

### Task 3: `EntityGraphNode` custom node (TDD)

The React Flow custom node: displayName + kind label, focused variant emphasised.

**Files:**
- Create: `web/src/features/catalog/components/EntityGraphNode.tsx`
- Test: `web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx`

**Interfaces:**
- Consumes: `GraphNodeData` (Task 2); `Handle`, `Position`, `Node`, `NodeProps` (from `@xyflow/react`, Task 1).
- Produces: `export function EntityGraphNode(props: NodeProps<Node<GraphNodeData>>)` — registered later as `nodeTypes.entity`.

- [ ] **Step 1: Write the failing test**

Create `web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx`:
```tsx
import { it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";

vi.mock("@xyflow/react", () => ({
  Handle: () => null,
  Position: { Left: "left", Right: "right" },
}));

import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";

function renderNode(data: GraphNodeData) {
  return render(<EntityGraphNode {...({ data } as never)} />);
}

it("renders the displayName and a human kind label", () => {
  renderNode({ kind: "service", entityId: "s2", displayName: "AuthService", side: "dependency" });
  expect(screen.getByText("AuthService")).toBeInTheDocument();
  expect(screen.getByText("Service")).toBeInTheDocument();
});

it("renders the application kind label", () => {
  renderNode({ kind: "application", entityId: "a1", displayName: "Checkout", side: "dependent" });
  expect(screen.getByText("Application")).toBeInTheDocument();
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`):
```
npx vitest run src/features/catalog/components/__tests__/EntityGraphNode.test.tsx
```
Expected: FAIL — cannot resolve `EntityGraphNode`.

- [ ] **Step 3: Write the implementation**

Create `web/src/features/catalog/components/EntityGraphNode.tsx`:
```tsx
import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";

const KIND_LABEL: Record<string, string> = { application: "Application", service: "Service" };

export function EntityGraphNode({ data }: NodeProps<Node<GraphNodeData>>) {
  const focused = data.side === "focused";
  return (
    <div
      className={
        focused
          ? "rounded-lg border-2 border-secondary bg-primary px-3 py-2 font-semibold shadow-sm"
          : "rounded-lg border border-secondary bg-primary px-3 py-2 shadow-xs"
      }
    >
      <Handle type="target" position={Position.Left} className="!border-0 !bg-transparent" />
      <div className="text-sm text-primary">{data.displayName}</div>
      <div className="text-xs text-tertiary">{KIND_LABEL[data.kind] ?? data.kind}</div>
      <Handle type="source" position={Position.Right} className="!border-0 !bg-transparent" />
    </div>
  );
}
```
Note: focused emphasis uses structural utilities (`border-2`, `font-semibold`) + the confirmed `border-secondary`/`text-primary`/`text-tertiary` tokens. Unknown Tailwind tokens no-op (they don't break the build); verify the focused node reads as emphasised in the Playwright pass (Task 5) and tweak if needed.

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`):
```
npx vitest run src/features/catalog/components/__tests__/EntityGraphNode.test.tsx
```
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```
git add web/src/features/catalog/components/EntityGraphNode.tsx web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx
git commit -m "feat(web): EntityGraphNode custom node for the dependency mini-graph"
```

---

### Task 4: `DependencyMiniGraph` component (TDD)

Fetches `direction=all`, builds the model, renders `<ReactFlow>`, handles states, navigates on neighbour click.

**Files:**
- Create: `web/src/features/catalog/components/DependencyMiniGraph.tsx`
- Test: `web/src/features/catalog/components/__tests__/DependencyMiniGraph.test.tsx`

**Interfaces:**
- Consumes: `useRelationshipsList` (`@/features/catalog/api/relationships`), `toGraphModel` + types (Task 2), `EntityGraphNode` (Task 3), `Skeleton` (`@/components/base/skeleton/skeleton`), `useNavigate` (react-router-dom), `ReactFlow`/`Background` (`@xyflow/react`).
- Produces: `export function DependencyMiniGraph(props: { entityKind: RelationshipKind; entityId: string; displayName: string })`.

- [ ] **Step 1: Write the failing test**

Create `web/src/features/catalog/components/__tests__/DependencyMiniGraph.test.tsx`:
```tsx
import { it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";

const navigate = vi.fn();
vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => navigate };
});

vi.mock("@xyflow/react", () => ({
  ReactFlow: (props: { nodes: { id: string; data: unknown }[]; edges: unknown[]; onNodeClick?: (e: unknown, n: unknown) => void }) => (
    <div data-testid="rf">
      <span data-testid="node-count">{props.nodes.length}</span>
      <span data-testid="edge-count">{props.edges.length}</span>
      {props.nodes.map((n) => (
        <button key={n.id} onClick={() => props.onNodeClick?.({}, n)}>
          {(n.data as { displayName: string }).displayName}
        </button>
      ))}
    </div>
  ),
  Background: () => null,
}));

import { DependencyMiniGraph } from "@/features/catalog/components/DependencyMiniGraph";
import * as api from "@/features/catalog/api/relationships";

function listResult(items: Partial<api.RelationshipResponse>[], extra: Record<string, unknown> = {}) {
  return { items, isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), ...extra } as never;
}

const outgoing: Partial<api.RelationshipResponse>[] = [
  { id: "r1", type: "dependsOn", origin: "manual", source: { kind: "service", id: "s1", displayName: "Me" }, target: { kind: "service", id: "s2", displayName: "AuthService" }, createdByUserId: "u1", createdAt: "2026-06-25T00:00:00Z" },
];

function renderGraph() {
  return render(<DependencyMiniGraph entityKind="service" entityId="s1" displayName="Me" />);
}

beforeEach(() => {
  vi.restoreAllMocks();
  navigate.mockReset();
});

it("renders nodes and edges from the relationship list", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult(outgoing));
  renderGraph();
  expect(screen.getByTestId("node-count")).toHaveTextContent("2"); // focused + 1 neighbour
  expect(screen.getByTestId("edge-count")).toHaveTextContent("1");
});

it("shows an empty placeholder when there are no relationships", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult([]));
  renderGraph();
  expect(screen.getByText(/no dependencies yet/i)).toBeInTheDocument();
  expect(screen.queryByTestId("rf")).not.toBeInTheDocument();
});

it("shows an error message when the list fails", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult([], { isError: true }));
  renderGraph();
  expect(screen.getByText(/couldn.t load the dependency graph/i)).toBeInTheDocument();
});

it("shows an overflow note when more relationships exist", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult(outgoing, { hasNext: true }));
  renderGraph();
  expect(screen.getByText(/see the tables below/i)).toBeInTheDocument();
});

it("navigates to a neighbour on node click but not for the focused node", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult(outgoing));
  renderGraph();
  fireEvent.click(screen.getByRole("button", { name: "AuthService" }));
  expect(navigate).toHaveBeenCalledWith("/catalog/services/s2");
  navigate.mockReset();
  fireEvent.click(screen.getByRole("button", { name: "Me" })); // focused node
  expect(navigate).not.toHaveBeenCalled();
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`):
```
npx vitest run src/features/catalog/components/__tests__/DependencyMiniGraph.test.tsx
```
Expected: FAIL — cannot resolve `DependencyMiniGraph`.

- [ ] **Step 3: Write the implementation**

Create `web/src/features/catalog/components/DependencyMiniGraph.tsx`:
```tsx
import { useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { ReactFlow, Background, type Node, type Edge } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useRelationshipsList } from "@/features/catalog/api/relationships";
import { toGraphModel, type FocusedEntity, type GraphNodeData } from "@/features/catalog/relationships/graphModel";
import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const NODE_TYPES = { entity: EntityGraphNode };
const GRAPH_LIMIT = 50;

interface Props {
  entityKind: RelationshipKind;
  entityId: string;
  displayName: string;
}

export function DependencyMiniGraph({ entityKind, entityId, displayName }: Props) {
  const navigate = useNavigate();
  const list = useRelationshipsList({ entityKind, entityId, direction: "all", limit: GRAPH_LIMIT });

  const model = useMemo(() => {
    const focused: FocusedEntity = { kind: entityKind, id: entityId, displayName };
    return toGraphModel(focused, list.items ?? []);
  }, [list.items, entityKind, entityId, displayName]);

  return (
    <section className="space-y-2" aria-label="Dependency graph">
      <h3 className="text-sm font-semibold text-primary">Dependency graph</h3>
      {list.isLoading ? (
        <Skeleton className="h-80 w-full" />
      ) : list.isError ? (
        <p className="text-sm text-error-primary">Couldn&apos;t load the dependency graph.</p>
      ) : model.edges.length === 0 ? (
        <p className="text-sm italic text-tertiary">No dependencies yet.</p>
      ) : (
        <>
          <div className="h-80 w-full overflow-hidden rounded-lg ring-1 ring-secondary">
            <ReactFlow
              nodes={model.nodes as Node[]}
              edges={model.edges as Edge[]}
              nodeTypes={NODE_TYPES}
              fitView
              nodesDraggable={false}
              nodesConnectable={false}
              elementsSelectable={false}
              proOptions={{ hideAttribution: true }}
              onNodeClick={(_, node) => {
                const data = node.data as GraphNodeData;
                if (data.side === "focused") return;
                navigate(`/catalog/${data.kind === "application" ? "applications" : "services"}/${data.entityId}`);
              }}
            >
              <Background />
            </ReactFlow>
          </div>
          {list.hasNext && (
            <p className="text-xs text-tertiary">
              Showing the first {GRAPH_LIMIT} relationships — see the tables below for the full list.
            </p>
          )}
        </>
      )}
    </section>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`):
```
npx vitest run src/features/catalog/components/__tests__/DependencyMiniGraph.test.tsx
```
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```
git add web/src/features/catalog/components/DependencyMiniGraph.tsx web/src/features/catalog/components/__tests__/DependencyMiniGraph.test.tsx
git commit -m "feat(web): DependencyMiniGraph component (read-only 1-hop graph)"
```

---

### Task 5: Wire the mini-graph into both detail pages + close out

Insert the lazy-loaded graph above the existing `<RelationshipsSection>` on each detail page, update the checklist, and run the closing verification.

**Files:**
- Modify (via **Serena** — existing `.tsx`): `web/src/features/catalog/pages/ApplicationDetailPage.tsx`
- Modify (via **Serena** — existing `.tsx`): `web/src/features/catalog/pages/ServiceDetailPage.tsx`
- Modify (via Edit — markdown): `docs/product/CHECKLIST.md`

**Interfaces:**
- Consumes: `DependencyMiniGraph` (Task 4). Both pages already expose `app.displayName` / `svc.displayName`, `*.id`, and pass a lowercase `entityKind` to `<RelationshipsSection>`.

- [ ] **Step 1: Add the lazy import + Suspense-wrapped graph to `ApplicationDetailPage.tsx`**

Using Serena symbolic edits (the file is existing `.tsx`):

(a) Add `lazy, Suspense` to the existing `import { useState } from "react";` → `import { lazy, Suspense, useState } from "react";`

(b) After the existing imports, add the lazy declaration (preserves the repo's named-export convention):
```tsx
const DependencyMiniGraph = lazy(() =>
  import("@/features/catalog/components/DependencyMiniGraph").then((m) => ({ default: m.DependencyMiniGraph })),
);
```

(c) In the returned JSX, immediately **before** the existing:
```tsx
          <RelationshipsSection
            entityKind="application"
```
insert:
```tsx
          <Suspense fallback={<Skeleton className="h-80 w-full" />}>
            <DependencyMiniGraph entityKind="application" entityId={app.id} displayName={app.displayName} />
          </Suspense>
          <hr className="border-secondary" />
```
(`Skeleton` is already imported in this file.)

- [ ] **Step 2: Add the same to `ServiceDetailPage.tsx`**

(a) The file's first line is `import { useMemo } from "react";` → change to `import { lazy, Suspense, useMemo } from "react";`

(b) After the imports, add:
```tsx
const DependencyMiniGraph = lazy(() =>
  import("@/features/catalog/components/DependencyMiniGraph").then((m) => ({ default: m.DependencyMiniGraph })),
);
```

(c) Immediately **before** the existing:
```tsx
          <RelationshipsSection
            entityKind="service"
```
insert:
```tsx
          <Suspense fallback={<Skeleton className="h-80 w-full" />}>
            <DependencyMiniGraph entityKind="service" entityId={svc.id} displayName={svc.displayName} />
          </Suspense>
          <hr className="border-secondary" />
```
(`Skeleton` is already imported in this file.)

- [ ] **Step 3: Type-check and build**

Run (from `web/`):
```
npm run build
```
Expected: PASS. Confirms the lazy import, props, and React Flow types all resolve under `tsc -b`.

- [ ] **Step 4: Run the full web test suite**

Run (from `web/`):
```
npm test
```
Expected: PASS — all existing tests plus the three new files (graphModel, EntityGraphNode, DependencyMiniGraph) green. The detail-page tests (`ApplicationDetailPage.test.tsx` / `ServiceDetailPage.test.tsx`) still pass because the graph is lazy + Suspense and `useRelationshipsList` is already query-backed; if a detail-page test now renders the graph and needs the query mocked, mock `useRelationshipsList` there to return an empty `listResult` (empty → placeholder, no React Flow).

- [ ] **Step 5: Update the checklist**

Edit `docs/product/CHECKLIST.md`: mark `E-04.F-02.S-01` done and note the S-02 "table below graph" arrangement, with date `2026-06-26` and the slice name, mirroring the format of the existing E-04 rows. Example:
```
- [x] E-04.F-02.S-01 — Embedded mini dependency graph (catalog-dependency-mini-graph, 2026-06-26; read-only 1-hop React Flow graph above the Dependencies/Dependents tables on Application + Service detail pages; reuses the 1b relationship endpoint; standalone /graph explorer + S-03–S-06 deferred)
```
Also bump the Phase 1 progress counter on the summary line if present.

- [ ] **Step 6: Manual verification (ADR-0084) — Playwright MCP, cold-start dev server**

Cold-start the dev server first (HMR can mask config/dep errors), then with Playwright MCP:
- Open an Application detail page that has dependencies → the graph renders, focused node centred, dependencies on the right, dependents on the left, edges labelled.
- Click a neighbour node → URL navigates to that entity's detail page.
- Open a Service detail page → same.
- Open an entity with no relationships → "No dependencies yet" placeholder, no canvas.
- Confirm the browser console is clean.

If the dev stack is unavailable in-session, mark this step **pending user verification** and say so explicitly (do not claim the UI works).

- [ ] **Step 7: Commit**

```
git add web/src/features/catalog/pages/ApplicationDetailPage.tsx web/src/features/catalog/pages/ServiceDetailPage.tsx docs/product/CHECKLIST.md
git commit -m "feat(catalog): embedded dependency mini-graph on entity detail pages (E-04.F-02.S-01)"
```

- [ ] **Step 8: Pre-push CI mirror + DoD gates**

Run the frontend CI mirror:
```
scripts/ci-local.sh frontend
```
Expected: green. Then work the Definition-of-Done gates per CLAUDE.md (build · per-task reviews · full suite · container build · /simplify · requesting-code-review · review-pr · deep-review). Slice-specific: **gate 3 real-seam addition = N/A** (no HTTP/auth/DB wiring), **gate 6 mutation = N/A** (no C# Domain/Application change), **gate 4 container build applies** (the web image must restore `@xyflow/react`). Open the PR once gates are green.

---

## Self-Review

**1. Spec coverage:**
- §3 #1 React Flow renderer → Task 1 (dep) + Task 4 (`<ReactFlow>`). ✓
- §3 #2 shared component on both pages, no routes → Task 4 + Task 5. ✓
- §3 #3 embedded only, explorer/button deferred → no `/graph` task; out-of-scope honored. ✓
- §3 #4 deterministic layout, no engine → `COL_X`/`ROW_GAP` in Task 2; no dagre/elk. ✓
- §3 #5 one combined graph → `toGraphModel` builds a single model both directions. ✓
- §3 #6 read-only + node-click navigate → `nodesDraggable/Connectable/elementsSelectable={false}` + `onNodeClick` in Task 4. ✓
- §3 #7 reuse endpoint, no backend/codegen → only `useRelationshipsList` consumed; Global Constraints forbid C#/openapi edits. ✓
- §3 #8 cap 50 + overflow note → `GRAPH_LIMIT=50` + `list.hasNext` note in Task 4. ✓
- §3 #9 kind+displayName only → `EntityGraphNode` renders exactly those; no lifecycle/health. ✓
- §3 #10 tables remain accessible source → graph inserted *above* the untouched `<RelationshipsSection>` (Task 5). ✓
- §3 #11 lazy-load → `lazy()` + `Suspense` in Task 5. ✓
- §3 #12 React Flow mocked in tests; Playwright real check → Tasks 3/4 mock `@xyflow/react`; Task 5 step 6 Playwright. ✓
- §7 unit + component test files → Tasks 2/3/4. ✓ §7.3 container build → Task 5 step 8. ✓ §7.4 mutation N/A; §7.5 manual → step 6. ✓

**2. Placeholder scan:** No TBD/TODO; every code step shows complete code; every command has an expected result. The CHECKLIST edit (Task 5 step 5) gives an exact example line. ✓

**3. Type consistency:**
- `toGraphModel(focused: FocusedEntity, relationships: RelationshipResponse[]): GraphModel` — identical signature in Task 2 interface, impl, and Task 4 call site. ✓
- `GraphNodeData` (`kind/entityId/displayName/side`) consumed identically by `EntityGraphNode` (Task 3) and `onNodeClick` (Task 4). ✓
- Node `id` format `${kind}:${id}` produced in Task 2, never parsed elsewhere (navigation reads `data.kind`/`data.entityId`, not the id). ✓
- `useRelationshipsList` result fields used (`items/isLoading/isError/hasNext`) match the real hook shape. ✓
- Edge label uses `relationshipTypeLabel[r.type as CreatableRelationshipType]` — same expression the existing `RelationshipsSection` uses. ✓

No issues found.
