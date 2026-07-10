# Graph Node Expand Affordance — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On the standalone `/graph` explorer, each node box shows whether it has unloaded in/out dependencies and lets the user expand/collapse — and set-focus / open — directly from the box.

**Architecture:** Add per-node in/out degree to the graph node contract (one batched RLS-scoped count query in the traversal handler); the frontend compares loaded-edge counts against those totals to drive active edge chevrons + a ⋯ dropdown menu on the custom React Flow node. Sidebar untouched (additive).

**Tech Stack:** .NET 10 / EF Core (Catalog module) · React + TS · @xyflow/react · react-aria-components (`Dropdown` wrapper) · @untitledui/icons · vitest.

**Spec:** `docs/superpowers/specs/2026-07-10-catalog-graph-node-expand-affordance-design.md`

## Global Constraints

- .NET 10; full solution builds with `TreatWarningsAsErrors=true` — **0 warnings, 0 errors**.
- Contract records carry `[ExcludeFromCodeCoverage]` — `GraphNodeDto` already has it; keep it when adding fields.
- Wire JSON is **camelCase** (ADR-0109) — new fields serialize as `outDegree` / `inDegree`.
- `cd web && npm run build` (`tsc -b && vite build`) is the binding type gate — must pass after codegen.
- Solution file: `Kartova.slnx`. Windows: use `cmd //c` or PowerShell wrappers for `dotnet`; commit multi-line messages via PowerShell with repeated `-m`.
- Line endings **LF** (`.gitattributes eol=lf` normalizes on commit).
- **No new permission** — `GET /catalog/graph` is read-only. No DB migration (query-only). No new ADR.
- Degree counts **explicit relationship edges only**; derived depends-on edges are skipped on both sides (backend count and FE loaded-count) so `loaded ≤ degree` always holds.

## Impact Analysis (codelens)

**Method:** roslyn-codelens (`find_references`) + `Grep` (index returned `project:""` = stale, so confirmed with grep per CLAUDE.md const/stale carve-out).

| Changed symbol | Change | Tool run | Callers / refs | Notable call sites | Covered by task |
|----------------|--------|----------|----------------|--------------------|-----------------|
| `Kartova.Catalog.Contracts.GraphNodeDto` | signature (+2 positional params) | `find_references` + `Grep "new GraphNodeDto"` | 1 construction site | `GraphTraversalHandler.cs:83` (Catalog.Infrastructure) | Task 1 |
| `Kartova.Catalog.Infrastructure.GraphTraversalHandler.Handle` | behavior (add degree enrichment) | read | signature unchanged; invoked only from `CatalogEndpointDelegates`/`CatalogModule` graph route | `CatalogEndpointDelegates.cs`, `CatalogModule.cs` | Task 1 (behavior additive, no caller edit) |

**Blast-radius notes:** `GraphNodeDto` is a positional record used at exactly one construction site; no test constructs it directly (integration tests assert on the deserialized `GraphResponse`). `Handle`'s signature is unchanged — the two graph-route wiring sites need no edit. No interface/base-type change, no cross-module fan-out. FE consumers are non-C# (regenerated codegen types) — scoped by Grep in Tasks 3–6.

**Coverage check:** the single `GraphNodeDto` construction site is edited in Task 1; no other caller/reference exists. Yes — fully covered.

---

### Task 1: Backend — per-node in/out degree on the graph contract

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Contracts/GraphResponse.cs` (add 2 fields to `GraphNodeDto`)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs` (degree query + enrichment; construction site line ~83)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs` (extend + new boundary/isolation degree tests)

**Interfaces:**
- Produces: `GraphNodeDto(EntityKind Kind, Guid Id, string DisplayName, int Depth, Guid? TeamId, int OutDegree, int InDegree)` — wire fields `outDegree` / `inDegree`. Semantics: total count of **explicit** `relationships` rows where the node is Source (`OutDegree`) / Target (`InDegree`), RLS-scoped to the caller's tenant, counted even for edges to nodes absent from `Nodes` (boundary).

- [ ] **Step 1: Write the failing degree assertions** — extend the existing two-hop test and add a boundary test in `GetCatalogGraphTests.cs`.

Append to `GET_graph_returns_two_hop_neighbourhood_with_depths` (after the existing asserts):
```csharp
        // Degree: F→A→B. F out=1/in=0, A out=1/in=1, B out=0/in=1.
        Assert.AreEqual(1, graph.Nodes.Single(n => n.Id == f).OutDegree);
        Assert.AreEqual(0, graph.Nodes.Single(n => n.Id == f).InDegree);
        Assert.AreEqual(1, graph.Nodes.Single(n => n.Id == a).OutDegree);
        Assert.AreEqual(1, graph.Nodes.Single(n => n.Id == a).InDegree);
        Assert.AreEqual(0, graph.Nodes.Single(n => n.Id == b).OutDegree);
        Assert.AreEqual(1, graph.Nodes.Single(n => n.Id == b).InDegree);
```

Add a new test (boundary node reports degree for an edge to an unloaded neighbour):
```csharp
    [TestMethod]
    public async Task GET_graph_degree_counts_edges_to_unloaded_boundary_neighbours()
    {
        // F→A→B→C, query depth=2 from F: nodes are F(0),A(1),B(2); C is unloaded (depth 3).
        // B's out-edge B→C must still be counted → B.OutDegree == 1 even though C is absent.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Degree Boundary");
        var f = await SeedServiceAsync(client, teamId, "deg-f");
        var a = await SeedServiceAsync(client, teamId, "deg-a");
        var b = await SeedServiceAsync(client, teamId, "deg-b");
        var c = await SeedServiceAsync(client, teamId, "deg-c");
        await DependsOnAsync(client, f, a);
        await DependsOnAsync(client, a, b);
        await DependsOnAsync(client, b, c);

        var graph = await (await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={f}&depth=2&direction=all"))
            .Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        Assert.IsFalse(graph!.Nodes.Any(n => n.Id == c), "C is beyond depth 2 and must be absent");
        Assert.AreEqual(1, graph.Nodes.Single(n => n.Id == b).OutDegree,
            "boundary node B reports its B→C degree even though C is unloaded");
        Assert.AreEqual(1, graph.Nodes.Single(n => n.Id == b).InDegree, "A→B");
    }
```

Add a tenant-isolation degree test (RLS scopes the count):
```csharp
    [TestMethod]
    public async Task GET_graph_degree_is_tenant_isolated()
    {
        // Org B: b1→b2. Org A focuses b1 (invisible under RLS) → focus node degree must be 0.
        var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "B Deg Iso");
        var b1 = await SeedServiceAsync(orgB, teamB, "bdeg-1");
        var b2 = await SeedServiceAsync(orgB, teamB, "bdeg-2");
        await DependsOnAsync(orgB, b1, b2);

        var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var graph = await (await orgA.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={b1}&depth=2&direction=all"))
            .Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsTrue(graph!.Nodes.All(n => n.OutDegree == 0 && n.InDegree == 0),
            "org-B relationships must not contribute to an org-A caller's node degrees");
    }
```

- [ ] **Step 2: Run the tests to confirm they fail** — degree assertions fail because `GraphNodeDto` has no `OutDegree`/`InDegree` (won't compile).

Run: `cmd //c "dotnet test src\Modules\Catalog\Kartova.Catalog.IntegrationTests --filter GetCatalogGraphTests"`
Expected: FAIL — compile error `GraphNodeDto does not contain a definition for 'OutDegree'`.

- [ ] **Step 3: Add the contract fields** — edit `GraphResponse.cs`:
```csharp
[ExcludeFromCodeCoverage]
public sealed record GraphNodeDto(
    EntityKind Kind, Guid Id, string DisplayName, int Depth, Guid? TeamId,
    int OutDegree, int InDegree);
```

- [ ] **Step 4: Compute degrees in the handler** — in `GraphTraversalHandler.Handle`, after `result` is built and the `info` displayName/teamId dictionary is populated, before projecting `nodes`, insert the batched count query:
```csharp
        // Per-node explicit-relationship degree (RLS-scoped). Boundary nodes have no fetched
        // neighbours, so degree cannot come from result.Edges — one batched count over the
        // returned node ids (≤ node cap). Ids are globally unique, so counting by Id is exact.
        var nodeIds = result.Nodes.Select(n => n.Ref.Id).Distinct().ToList();
        var degreeRows = await db.Relationships
            .Where(r => nodeIds.Contains(r.Source.Id) || nodeIds.Contains(r.Target.Id))
            .Select(r => new { SourceId = r.Source.Id, TargetId = r.Target.Id })
            .ToListAsync(ct);
        var outDeg = degreeRows.GroupBy(x => x.SourceId).ToDictionary(g => g.Key, g => g.Count());
        var inDeg = degreeRows.GroupBy(x => x.TargetId).ToDictionary(g => g.Key, g => g.Count());
```
Then update the node projection (construction site ~line 83):
```csharp
        var nodes = result.Nodes.Select(n =>
        {
            var found = info[(n.Ref.Kind, n.Ref.Id)];
            return new GraphNodeDto(
                n.Ref.Kind, n.Ref.Id, found?.DisplayName ?? string.Empty, n.Depth, found?.TeamId,
                outDeg.GetValueOrDefault(n.Ref.Id), inDeg.GetValueOrDefault(n.Ref.Id));
        }).ToList();
```

- [ ] **Step 5: Run the tests to confirm they pass**

Run: `cmd //c "dotnet test src\Modules\Catalog\Kartova.Catalog.IntegrationTests --filter GetCatalogGraphTests"`
Expected: PASS (all `GetCatalogGraphTests`, incl. the 2 new tests). Requires Docker (Testcontainers) — if unavailable, flag *pending user verification*.

- [ ] **Step 6: Build the solution (warnings-as-errors)**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```powershell
git add src/Modules/Catalog/Kartova.Catalog.Contracts/GraphResponse.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs
git commit -m "feat(catalog): per-node in/out degree on graph node contract" -m "Adds OutDegree/InDegree to GraphNodeDto via one batched RLS-scoped count query in GraphTraversalHandler; degree counts explicit relationship edges incl. edges to unloaded boundary nodes." -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Regenerate the codegen client + snapshot

**Files:**
- Modify (generated, gitignored): `web/src/generated/openapi.*`
- Modify (committed): `web/openapi-snapshot.json`

**Interfaces:**
- Produces: TS type `components["schemas"]["GraphNodeDto"]` gains `outDegree: number` and `inDegree: number`.

> Codegen reads the **live** API's OpenAPI document, so the API must run with the Task 1 contract before regenerating (per project codegen note). If the dev API image is used, rebuild it first.

- [ ] **Step 1: Start the API with the new contract** — either `cmd //c "dotnet run --project src/Kartova.Api"` (or the compose API service rebuilt: `docker compose build api && docker compose up -d api`). Confirm `/openapi/v1.json` (or the configured doc route) serves and includes `GraphNodeDto` with the two new fields.

- [ ] **Step 2: Regenerate**

Run: `cd web && npm run codegen`
Expected: `web/openapi-snapshot.json` and `web/src/generated/openapi.*` updated; diff shows `outDegree` / `inDegree` added under `GraphNodeDto`.

- [ ] **Step 3: Type-check the binding**

Run: `cd web && npx tsc -b`
Expected: 0 errors (no consumer references the new fields yet — this just confirms the regenerated types compile).

- [ ] **Step 4: Commit**

```powershell
git add web/openapi-snapshot.json
git commit -m "chore(web): regenerate OpenAPI snapshot for graph node degree fields" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
(Generated `web/src/generated/openapi.*` is gitignored — snapshot is the committed fallback.)

---

### Task 3: Frontend — carry degrees through merge + `loadedDegrees` helper

**Files:**
- Modify: `web/src/features/catalog/relationships/graphMerge.ts` (`ExplorerNode` + node mapping + new `loadedDegrees`)
- Test: `web/src/features/catalog/relationships/__tests__/graphMerge.test.ts` (add `loadedDegrees` cases)

**Interfaces:**
- Consumes: `GraphResponse` node fields `outDegree` / `inDegree` (Task 2).
- Produces:
  - `ExplorerNode` gains `outDegree: number; inDegree: number`.
  - `export function loadedDegrees(graph: ExplorerGraph): Map<string, { out: number; in: number }>` — counts **explicit** (non-derived) merged edges per node (`out` = node is source, `in` = node is target).

- [ ] **Step 1: Write the failing test** — add to `graphMerge.test.ts`:
```ts
import { loadedDegrees } from "@/features/catalog/relationships/graphMerge";

it("loadedDegrees counts explicit edges per direction and ignores derived edges", () => {
  const graph = {
    nodes: [],
    edges: [
      { id: "e1", source: "service:a", target: "service:b", label: "depends on" },
      { id: "e2", source: "service:a", target: "service:c", label: "depends on" },
      { id: "d1", source: "service:a", target: "service:d", label: "depends on · via X", derived: true },
    ],
    truncated: false,
  };
  const d = loadedDegrees(graph as never);
  expect(d.get("service:a")).toEqual({ out: 2, in: 0 }); // derived edge to d excluded
  expect(d.get("service:b")).toEqual({ out: 0, in: 1 });
  expect(d.get("service:c")).toEqual({ out: 0, in: 1 });
  expect(d.get("service:d")).toBeUndefined(); // only derived edge touched it
});
```

- [ ] **Step 2: Run to confirm it fails**

Run: `cd web && npm run test -- graphMerge`
Expected: FAIL — `loadedDegrees` is not exported.

- [ ] **Step 3: Implement** — in `graphMerge.ts`, extend `ExplorerNode` and the node mapping, and add the helper.

Add to `ExplorerNode`:
```ts
  outDegree: number;
  inDegree: number;
```
In `mergeGraphs`, set them when creating a node (`nodes.set(id, { … })`):
```ts
          outDegree: Number(n.outDegree ?? 0),
          inDegree: Number(n.inDegree ?? 0),
```
Append the helper at the end of the file:
```ts
export function loadedDegrees(graph: ExplorerGraph): Map<string, { out: number; in: number }> {
  const m = new Map<string, { out: number; in: number }>();
  const bump = (id: string, dir: "out" | "in") => {
    const e = m.get(id) ?? { out: 0, in: 0 };
    e[dir] += 1;
    m.set(id, e);
  };
  for (const e of graph.edges) {
    if (e.derived) continue; // degree from backend counts explicit edges only
    bump(e.source, "out");
    bump(e.target, "in");
  }
  return m;
}
```

- [ ] **Step 4: Run to confirm it passes**

Run: `cd web && npm run test -- graphMerge`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add web/src/features/catalog/relationships/graphMerge.ts web/src/features/catalog/relationships/__tests__/graphMerge.test.ts
git commit -m "feat(web): carry node degree through graph merge + loadedDegrees helper" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Frontend — `GraphActionsContext` + `GraphNodeData` fields

**Files:**
- Create: `web/src/features/catalog/relationships/GraphActionsContext.tsx`
- Modify: `web/src/features/catalog/relationships/graphModel.ts` (`GraphNodeData` fields)

**Interfaces:**
- Produces:
  - `GraphActions = { toggleExpand(node, dir): void; setFocus(kind, id): void; openPage(kind, id): void; atCap: boolean }`
  - `GraphActionsProvider` (context Provider), `useGraphActions(): GraphActions` (default = no-ops, `atCap: false`).
  - `GraphNodeData` gains optional: `expandableOut?, expandableIn?, expandedOut?, expandedIn?: boolean; unloadedOut?, unloadedIn?: number`.

- [ ] **Step 1: Create the context** — `GraphActionsContext.tsx`:
```tsx
import { createContext, useContext } from "react";
import type { ExpandDir } from "@/features/catalog/relationships/useExplorerState";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

export type GraphActions = {
  toggleExpand: (node: string, dir: ExpandDir) => void;
  setFocus: (kind: RelationshipKind, id: string) => void;
  openPage: (kind: RelationshipKind, id: string) => void;
  atCap: boolean;
};

const noop = () => {};
const GraphActionsContext = createContext<GraphActions>({
  toggleExpand: noop,
  setFocus: noop,
  openPage: noop,
  atCap: false,
});

export const GraphActionsProvider = GraphActionsContext.Provider;
export const useGraphActions = () => useContext(GraphActionsContext);
```

- [ ] **Step 2: Extend `GraphNodeData`** — in `graphModel.ts`, add to the `GraphNodeData` type:
```ts
  // explorer: node-level expand affordance (undefined on mini-graph / non-explorer models)
  expandableOut?: boolean;
  expandableIn?: boolean;
  expandedOut?: boolean;
  expandedIn?: boolean;
  unloadedOut?: number;
  unloadedIn?: number;
```

- [ ] **Step 3: Type-check**

Run: `cd web && npx tsc -b`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add web/src/features/catalog/relationships/GraphActionsContext.tsx web/src/features/catalog/relationships/graphModel.ts
git commit -m "feat(web): GraphActionsContext + expand-affordance fields on GraphNodeData" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Frontend — `EntityGraphNode` chevrons + ⋯ menu

**Files:**
- Modify: `web/src/features/catalog/components/EntityGraphNode.tsx`
- Test: `web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx`

**Interfaces:**
- Consumes: `GraphNodeData` affordance fields (Task 4), `useGraphActions` (Task 4), `Dropdown` (`@/components/base/dropdown/dropdown`), `entityDetailPath` (unused here — routing via `openPage`).
- Behavior: node key = `${data.kind}:${data.entityId}`.
  - **Left chevron** shown when `expandableIn || expandedIn`; expand icon (`ChevronLeft`) when `!expandedIn`, collapse icon (`Minus`) when `expandedIn`; click → `toggleExpand(key, "in")`; `aria-label` = `Expand/Collapse dependents`.
  - **Right chevron** mirror for out / `ChevronRight` / `dependencies`.
  - Chevron disabled when `atCap && !expanded(dir)`.
  - **⋯ menu** (`Dropdown`): items Expand/Collapse dependencies (`unloadedOut`), Expand/Collapse dependents (`unloadedIn`), Set as focus, Open page ↗. Expand items disabled when the direction is not expandable and not expanded, or `atCap && !expanded`.
  - All interactive controls carry `nodrag nopan` classes and `stopPropagation` on press so they don't trigger node-select.

- [ ] **Step 1: Write failing tests** — replace the file's tests with ones that wrap the node in a provider. Add:
```tsx
import { it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

vi.mock("@xyflow/react", () => ({
  Handle: () => null,
  Position: { Left: "left", Right: "right" },
}));

import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import { GraphActionsProvider, type GraphActions } from "@/features/catalog/relationships/GraphActionsContext";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";

function renderNode(data: GraphNodeData, actions?: Partial<GraphActions>) {
  const value: GraphActions = {
    toggleExpand: vi.fn(), setFocus: vi.fn(), openPage: vi.fn(), atCap: false, ...actions,
  };
  render(
    <GraphActionsProvider value={value}>
      <EntityGraphNode {...({ data } as unknown as Parameters<typeof EntityGraphNode>[0])} />
    </GraphActionsProvider>,
  );
  return value;
}

it("shows an expand-dependencies chevron only when out is expandable", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true, unloadedOut: 3 });
  expect(screen.getByRole("button", { name: /expand dependencies/i })).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /expand dependents/i })).toBeNull();
});

it("hides both chevrons when nothing is expandable or expanded", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency" });
  expect(screen.queryByRole("button", { name: /dependencies|dependents/i })).toBeNull();
});

it("shows a collapse chevron when the direction is expanded", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandedOut: true });
  expect(screen.getByRole("button", { name: /collapse dependencies/i })).toBeInTheDocument();
});

it("clicking the out chevron toggles expand out", async () => {
  const a = renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true });
  await userEvent.click(screen.getByRole("button", { name: /expand dependencies/i }));
  expect(a.toggleExpand).toHaveBeenCalledWith("service:s", "out");
});

it("disables expand chevron at cap when not expanded", () => {
  renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true }, { atCap: true });
  expect(screen.getByRole("button", { name: /expand dependencies/i })).toBeDisabled();
});

it("menu opens and fires set focus / open page", async () => {
  const a = renderNode({ kind: "service", entityId: "s", displayName: "S", side: "dependency", expandableOut: true, unloadedOut: 2 });
  await userEvent.click(screen.getByRole("button", { name: /open menu/i }));
  await userEvent.click(screen.getByRole("menuitem", { name: /set as focus/i }));
  expect(a.setFocus).toHaveBeenCalledWith("service", "s");
});
```

- [ ] **Step 2: Run to confirm they fail**

Run: `cd web && npm run test -- EntityGraphNode`
Expected: FAIL — no expand buttons / menu rendered.

- [ ] **Step 3: Implement `EntityGraphNode`** — replace the component body:
```tsx
import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import { ChevronLeft, ChevronRight, Minus } from "@untitledui/icons";
import { Dropdown } from "@/components/base/dropdown/dropdown";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";
import { ENTITY_KIND_LABEL } from "@/features/catalog/relationships/graphModel";
import { useGraphActions } from "@/features/catalog/relationships/GraphActionsContext";
import type { ExpandDir } from "@/features/catalog/relationships/useExplorerState";

const stop = (e: { stopPropagation: () => void }) => e.stopPropagation();

export function EntityGraphNode({ data }: NodeProps<Node<GraphNodeData>>) {
  const { toggleExpand, setFocus, openPage, atCap } = useGraphActions();
  const key = `${data.kind}:${data.entityId}`;

  const base = "rounded-lg bg-primary px-3 py-2";
  const variant = data.selected
    ? "border-2 border-brand-solid shadow-md"
    : data.side === "focused"
      ? "border-2 border-secondary font-semibold shadow-sm"
      : "border border-secondary shadow-xs";
  const dim = data.dimmed ? "opacity-30" : "";

  const chevron = (dir: ExpandDir) => {
    const expandable = dir === "out" ? data.expandableOut : data.expandableIn;
    const expanded = dir === "out" ? data.expandedOut : data.expandedIn;
    if (!expandable && !expanded) return null;
    const noun = dir === "out" ? "dependencies" : "dependents";
    const label = `${expanded ? "Collapse" : "Expand"} ${noun}`;
    const disabled = atCap && !expanded;
    const Icon = expanded ? Minus : dir === "out" ? ChevronRight : ChevronLeft;
    const side = dir === "out" ? "-right-2.5" : "-left-2.5";
    return (
      <button
        type="button"
        aria-label={label}
        title={label}
        disabled={disabled}
        className={`nodrag nopan absolute top-1/2 ${side} flex size-5 -translate-y-1/2 items-center justify-center rounded-full bg-brand-solid text-white shadow-sm disabled:opacity-40`}
        onPointerDown={stop}
        onClick={(e) => { stop(e); toggleExpand(key, dir); }}
      >
        <Icon className="size-3" />
      </button>
    );
  };

  const expandItem = (dir: ExpandDir) => {
    const expandable = dir === "out" ? data.expandableOut : data.expandableIn;
    const expanded = dir === "out" ? data.expandedOut : data.expandedIn;
    const count = dir === "out" ? data.unloadedOut : data.unloadedIn;
    const noun = dir === "out" ? "dependencies" : "dependents";
    return (
      <Dropdown.Item
        label={`${expanded ? "Collapse" : "Expand"} ${noun}`}
        addon={!expanded && count ? String(count) : undefined}
        isDisabled={!expanded && (!expandable || atCap)}
        onAction={() => toggleExpand(key, dir)}
      />
    );
  };

  return (
    <div className={`${base} ${variant} ${dim} relative`}>
      <Handle type="target" position={Position.Left} className="!border-0 !bg-transparent" />
      {chevron("in")}
      {chevron("out")}
      <div className="flex items-start gap-2">
        <div className="min-w-0">
          <div className="text-sm text-primary">{data.displayName}</div>
          <div className="text-xs text-tertiary">{ENTITY_KIND_LABEL[data.kind] ?? data.kind}</div>
        </div>
        <div className="nodrag nopan ml-auto" onPointerDown={stop} onClick={stop}>
          <Dropdown.Root>
            <Dropdown.DotsButton className="size-5" />
            <Dropdown.Popover>
              <Dropdown.Menu>
                {expandItem("out")}
                {expandItem("in")}
                <Dropdown.Separator />
                <Dropdown.Item label="Set as focus" onAction={() => setFocus(data.kind, data.entityId)} />
                <Dropdown.Item label="Open page ↗" onAction={() => openPage(data.kind, data.entityId)} />
              </Dropdown.Menu>
            </Dropdown.Popover>
          </Dropdown.Root>
        </div>
      </div>
      <Handle type="source" position={Position.Right} className="!border-0 !bg-transparent" />
    </div>
  );
}
```

- [ ] **Step 4: Run to confirm they pass**

Run: `cd web && npm run test -- EntityGraphNode`
Expected: PASS. (If react-aria menu-item roles need the popover open first, tests already click `Open menu` before asserting `menuitem`.)

- [ ] **Step 5: Commit**

```powershell
git add web/src/features/catalog/components/EntityGraphNode.tsx web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx
git commit -m "feat(web): on-node expand chevrons + menu on graph EntityGraphNode" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: Frontend — wire the page (compute affordance data, provide actions, layout)

**Files:**
- Modify: `web/src/features/catalog/relationships/graphLayout.ts` (optional `decorate` param)
- Modify: `web/src/features/catalog/pages/GraphExplorerPage.tsx` (compute decorate map + provider)
- Test: `web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx` (affordance wiring)

**Interfaces:**
- Consumes: `loadedDegrees` (Task 3), `GraphActionsProvider`/`GraphActions` (Task 4), `useExplorerState.isExpanded/toggleExpand` (existing), `entityDetailPath` (existing).
- Produces: `layoutGraph(graph, focusId, selectedId, dimmed?, decorate?: Map<string, Partial<GraphNodeData>>)` — merges `decorate.get(id)` into each node's `data`.

- [ ] **Step 1: Write the failing page test** — add to `GraphExplorerPage.test.tsx` a case asserting an expandable node exposes the chevron (drives the compute→layout→node path). Match the file's existing render/mock harness (MSW or query mocks); assert:
```tsx
// after rendering the explorer focused on a node whose backend outDegree(1) > loaded(0):
expect(await screen.findByRole("button", { name: /expand dependencies/i })).toBeInTheDocument();
```
(Reuse the file's existing graph-response mock; add `outDegree`/`inDegree` to the mocked focus node so `expandableOut` computes true. If the harness lacks a ready mock, add one node with `outDegree: 1` and no loaded out-edge.)

- [ ] **Step 2: Run to confirm it fails**

Run: `cd web && npm run test -- GraphExplorerPage`
Expected: FAIL — chevron not rendered (page doesn't compute/provide affordance data yet).

- [ ] **Step 3: Extend `layoutGraph`** — add the optional param and merge:
```ts
export function layoutGraph(
  graph: ExplorerGraph,
  focusId: string,
  selectedId: string | null,
  dimmed: { nodeIds: Set<string>; edgeIds: Set<string> } = { nodeIds: new Set(), edgeIds: new Set() },
  decorate?: Map<string, Partial<GraphNodeData>>,
): { nodes: Node<GraphNodeData>[]; edges: Edge[] } {
```
In the node `data` object, append after `dimmed`:
```ts
        ...(decorate?.get(n.id) ?? {}),
```

- [ ] **Step 4: Compute + provide in `GraphExplorerPage`** — add imports:
```tsx
import { mergeGraphs, bfsDepth, loadedDegrees } from "@/features/catalog/relationships/graphMerge";
import { GraphActionsProvider, type GraphActions } from "@/features/catalog/relationships/GraphActionsContext";
import { entityDetailPath } from "@/features/catalog/relationships/graphModel";
```
After `dimmed` is computed, add the decorate map:
```tsx
  const loaded = useMemo(() => loadedDegrees(merged), [merged]);
  const decorate = useMemo(() => {
    const m = new Map<string, Partial<GraphNodeData>>();
    for (const n of merged.nodes) {
      const ld = loaded.get(n.id) ?? { out: 0, in: 0 };
      const outDeg = n.outDegree ?? 0;
      const inDeg = n.inDegree ?? 0;
      m.set(n.id, {
        expandableOut: ld.out < outDeg,
        expandableIn: ld.in < inDeg,
        expandedOut: isExpanded(n.id, "out"),
        expandedIn: isExpanded(n.id, "in"),
        unloadedOut: Math.max(0, outDeg - ld.out),
        unloadedIn: Math.max(0, inDeg - ld.in),
      });
    }
    return m;
  }, [merged, loaded, isExpanded]);
```
Pass `decorate` into `layoutGraph`:
```tsx
        ? layoutGraph(merged, focusId, selected, { nodeIds: dimmed.dimmedNodeIds, edgeIds: dimmed.dimmedEdgeIds }, decorate)
```
Add `decorate` to that `useMemo` dependency array. Build the actions value:
```tsx
  const actions = useMemo<GraphActions>(() => ({
    toggleExpand,
    setFocus: (kind, id) => navigate(`/graph?focus=${kind}:${id}`),
    openPage: (kind, id) => navigate(entityDetailPath(kind, id)),
    atCap,
  }), [toggleExpand, navigate, atCap]);
```
Wrap the `<ReactFlow>` element in the provider:
```tsx
              <GraphActionsProvider value={actions}>
                <ReactFlow …>
                  … existing children …
                </ReactFlow>
              </GraphActionsProvider>
```

- [ ] **Step 5: Run to confirm it passes + full FE suite + build**

Run: `cd web && npm run test -- GraphExplorerPage`
Expected: PASS.
Run: `cd web && npm run test`
Expected: all vitest green.
Run: `cd web && npm run build`
Expected: `tsc -b && vite build` → 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add web/src/features/catalog/relationships/graphLayout.ts web/src/features/catalog/pages/GraphExplorerPage.tsx web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx
git commit -m "feat(web): wire node expand affordance into graph explorer page" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: DoD ledger + verification sweep

**Files:**
- Create: `docs/superpowers/verification/2026-07-10-catalog-graph-node-expand-affordance/dod.md` (copy `docs/superpowers/templates/dod-ledger-template.md`)
- Create: `docs/superpowers/verification/2026-07-10-catalog-graph-node-expand-affordance/gate-findings.yaml` (copy `docs/superpowers/templates/gate-findings-template.yaml`)

- [ ] **Step 1: Create the ledger + findings files** from the templates; fill the header (branch, spec, plan paths).

- [ ] **Step 2: Run the always-blocking gates** (governed by CLAUDE.md — this section lists commands, the gates themselves are authoritative), recording each row in `dod.md`:
  - Gate 1 build: `cmd //c "dotnet build Kartova.slnx"` + `cd web && npm run build`.
  - Gate 3 full suite: backend `cmd //c "dotnet test Kartova.slnx"` (Docker/Testcontainers; run Catalog integ in isolation if the known named-pipe flake appears) + `cd web && npm run test`. **Real-seam** = the Task 1 degree tests (real Postgres/RLS + real JWT).
  - Gate 4 container build: the `images` CI job (`docker compose build`) — runs on PR.
  - Gate 5 `/simplify` against the branch diff.
  - **Gate 6 mutation (BLOCKING — touches Infrastructure logic):** `/misc:mutation-sentinel` → `/misc:test-generator` on `GraphTraversalHandler.cs` degree logic; target ≥80%, document survivors.
  - Gates 7–9: `/superpowers:requesting-code-review`, `/pr-review-toolkit:review-pr`, `/deep-review` against the full branch diff (spec + plan as context).
  - Gate 10 visual (ADR-0084): cold-start dev server, authenticate, navigate in-SPA to `/graph?focus=<service>:<id>`, verify chevrons appear on expandable nodes, click a chevron → neighbours load, open ⋯ menu → Set-as-focus / Open-page work, console clean; screenshot under the verification folder.
  - Gate 11 CI: run `scripts/ci-local.sh` (or `backend`/`frontend` subsets) pre-push; then CI green on the PR (authoritative).

- [ ] **Step 3: Terminal re-verify** — after gates 5–9 apply any fixes, re-run gate 1 build + gate 3 full suite on the final commit; confirm green before claiming completion. Cite the ledger path in the completion claim.

---

## Self-Review

**1. Spec coverage:** §4.1 backend degree → Task 1; codegen (§4.3 generated) → Task 2; §4.2 merge/compute → Tasks 3, 6; context → Task 4; node chevrons+menu → Task 5; page wiring → Task 6; §7 testing → tests in Tasks 1/3/5/6 + gate sweep Task 7; §6 edge cases (focus node, at-cap, hidden-edge) → covered by chevron logic (Task 5) + at-cap tests (Task 5) + boundary test (Task 1). No gaps.

**2. Placeholder scan:** none — every code/step is concrete.

**3. Type consistency:** `GraphNodeDto(… , int OutDegree, int InDegree)` (Task 1) ↔ wire `outDegree`/`inDegree` (Task 2) ↔ `ExplorerNode.outDegree/inDegree` (Task 3) ↔ `decorate` computed from `n.outDegree`/`n.inDegree` (Task 6) ↔ `GraphNodeData.expandable*/expanded*/unloaded*` (Task 4) consumed in `EntityGraphNode` (Task 5). `loadedDegrees` returns `{ out, in }` — used consistently in Tasks 3/6. `toggleExpand(node, dir)` matches `useExplorerState` (existing) and `GraphActions` (Task 4). Consistent.
