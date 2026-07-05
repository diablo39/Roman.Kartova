# API Graph UI (FU-A + FU-A1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Teach the web UI the `api` entity kind and the three API edge types (`instanceOf`, `providesApiFor`, `consumesApiFrom`) so API connectivity edges render in the graph explorer + detail tables, navigate to `/catalog/apis/:id`, are authorable via the relationship dialog, and are listed read-only on the Api detail page.

**Architecture:** Frontend-only. The backend already ships `EntityKind.Api`, the three edge types, per-type `RelationshipTypeRules.IsAllowedPair`, `CatalogEntityLookup` Api enrichment, and unfiltered list/graph handlers. Work removes the two `isRenderableKind` guards, widens the FE kind union, populates the creatable-type pair matrix, adds an Api branch to the entity-search picker, reframes the detail-page relationship section as generic Outgoing/Incoming, and mounts a read-only incoming-only variant on the Api detail page.

**Tech Stack:** React + TypeScript, react-aria-components (Untitled UI, ADR-0094), @xyflow/react (graph), TanStack Query, Vitest + React Testing Library, Playwright (ADR-0084 browser verification).

## Global Constraints

- **No new C# / backend / migration / contract change** — backend already supports everything (verified on master). This is a pure `web/` slice.
- **No new permission** — `CatalogRelationshipsWrite` already authorizes all creatable types (ADR-0108 either-endpoint authz). No 5-sync.
- **FE pair matrix is intentionally stricter than backend** — `dependsOn` never offers `api` as a target in the UI (API links use provides/consumes); backend's `DependsOn ⇒ true` still permits it at the API layer.
- **`dependsOn` MUST stay first in `CREATABLE_TYPES`** — it is the dialog's default type; existing dialog tests rely on the default.
- **react-aria `<Table>` needs exactly one `isRowHeader` column** (ADR-0084) — the relationship table already marks `entity`; do not remove it. Tests assert `getAllByRole("rowheader").length > 0`.
- **Enum wire values are camelCase** (ADR-0109) — `providesApiFor`, `consumesApiFrom`, `instanceOf`, `api`.
- **Run `web` checks via** `npm run test -- <path>` (vitest) and `npm run build` (tsc -b binding gate). Stop the dev server before `ci-local.sh frontend` (npm ci vs 5173 lock).
- **Line endings LF** (repo `.gitattributes eol=lf`).

## Impact Analysis (codelens/LSP)

**N/A — frontend-only.** No existing C# symbol signature or behavior changes; every backend enum/rule/handler this slice consumes pre-exists on master (`EntityKind.Api`, the three `RelationshipType` members, `RelationshipTypeRules.IsAllowedPair`, `CatalogEntityLookup` Api branch, unfiltered `ListRelationshipsForEntityHandler` + `GraphTraversalHandler`). TypeScript has no codelens/LSP MCP in this repo; the FE blast radius (the `application|service` hardcoding across the graph subsystem, the two `isRenderableKind` guards, the `isRenderableKind` removal) was enumerated by grep in the spec §5.1 and each hit maps to a task below. Confirm before Task 6 with `grep -rn "isRenderableKind" web/src` (only the definition should remain).

---

### Task 1: Relationship kind + type foundation (`relationshipTypeRules.ts`)

Widen the kind union, add the three creatable types + labels, replace the placeholder pair rule with the real per-type matrix, and add a shared `isRelationshipKind` predicate. Keep `isRenderableKind` for now (removed in Task 6 once its last consumer is migrated).

**Files:**
- Modify: `web/src/features/catalog/relationships/relationshipTypeRules.ts`
- Test: `web/src/features/catalog/relationships/__tests__/relationshipTypeRules.test.ts`

**Interfaces:**
- Produces: `type RelationshipKind = "application" | "service" | "api"`; `type CreatableRelationshipType = "dependsOn" | "instanceOf" | "providesApiFor" | "consumesApiFrom"`; `isRelationshipKind(kind: string): kind is RelationshipKind`; `isAllowedPair`, `allowedOtherKinds`, `offerableTypes`, `relationshipTypeLabel` (unchanged signatures, new behavior/keys).

- [ ] **Step 1: Rewrite the rules test to assert the per-type matrix**

Replace the entire body of `relationshipTypeRules.test.ts` with:

```ts
import { describe, it, expect } from "vitest";
import {
  isAllowedPair, offerableTypes, allowedOtherKinds, relationshipTypeLabel,
  isRelationshipKind,
} from "@/features/catalog/relationships/relationshipTypeRules";

describe("relationshipTypeRules", () => {
  it("dependsOn allows app/service pairs but never targets an api", () => {
    for (const s of ["application", "service"] as const)
      for (const t of ["application", "service"] as const)
        expect(isAllowedPair("dependsOn", s, t)).toBe(true);
    expect(isAllowedPair("dependsOn", "service", "api")).toBe(false);
    expect(isAllowedPair("dependsOn", "application", "api")).toBe(false);
  });

  it("instanceOf is service -> application only", () => {
    expect(isAllowedPair("instanceOf", "service", "application")).toBe(true);
    expect(isAllowedPair("instanceOf", "application", "service")).toBe(false);
    expect(isAllowedPair("instanceOf", "service", "api")).toBe(false);
  });

  it("providesApiFor / consumesApiFrom are {app,service} -> api only", () => {
    for (const type of ["providesApiFor", "consumesApiFrom"] as const) {
      expect(isAllowedPair(type, "application", "api")).toBe(true);
      expect(isAllowedPair(type, "service", "api")).toBe(true);
      expect(isAllowedPair(type, "api", "application")).toBe(false);
      expect(isAllowedPair(type, "application", "service")).toBe(false);
    }
  });

  it("offerableTypes differs by fixed kind and role", () => {
    expect(offerableTypes("source", "application")).toEqual(["dependsOn", "providesApiFor", "consumesApiFrom"]);
    expect(offerableTypes("source", "service")).toEqual(["dependsOn", "instanceOf", "providesApiFor", "consumesApiFrom"]);
    expect(offerableTypes("target", "application")).toEqual(["dependsOn", "instanceOf"]);
    expect(offerableTypes("target", "service")).toEqual(["dependsOn"]);
    expect(offerableTypes("source", "api")).toEqual([]);
    expect(offerableTypes("target", "api")).toEqual(["providesApiFor", "consumesApiFrom"]);
  });

  it("allowedOtherKinds constrains the other endpoint", () => {
    expect(allowedOtherKinds("dependsOn", "source", "application")).toEqual(["application", "service"]);
    expect(allowedOtherKinds("providesApiFor", "source", "service")).toEqual(["api"]);
    expect(allowedOtherKinds("instanceOf", "source", "service")).toEqual(["application"]);
  });

  it("labels every creatable type", () => {
    expect(relationshipTypeLabel.dependsOn).toBe("Depends on");
    expect(relationshipTypeLabel.instanceOf).toBe("Instance of");
    expect(relationshipTypeLabel.providesApiFor).toBe("Provides API for");
    expect(relationshipTypeLabel.consumesApiFrom).toBe("Consumes API from");
  });

  it("isRelationshipKind accepts the three kinds and rejects others", () => {
    expect(isRelationshipKind("api")).toBe(true);
    expect(isRelationshipKind("application")).toBe(true);
    expect(isRelationshipKind("broker")).toBe(false);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npm run test -- src/features/catalog/relationships/__tests__/relationshipTypeRules.test.ts`
Expected: FAIL — `isRelationshipKind` is not exported; `offerableTypes`/`isAllowedPair` return old values.

- [ ] **Step 3: Rewrite `relationshipTypeRules.ts`**

Replace the whole file with (note `isRenderableKind` retained, unchanged, for now):

```ts
export type RelationshipKind = "application" | "service" | "api";
export type CreatableRelationshipType =
  | "dependsOn"
  | "instanceOf"
  | "providesApiFor"
  | "consumesApiFrom";
export type FixedRole = "source" | "target";

export const relationshipTypeLabel: Record<CreatableRelationshipType, string> = {
  dependsOn: "Depends on",
  instanceOf: "Instance of",
  providesApiFor: "Provides API for",
  consumesApiFrom: "Consumes API from",
};

// dependsOn MUST stay first — it is the dialog's default type.
const CREATABLE_TYPES: CreatableRelationshipType[] = [
  "dependsOn",
  "instanceOf",
  "providesApiFor",
  "consumesApiFrom",
];
const ALL_KINDS: RelationshipKind[] = ["application", "service", "api"];

// Shared kind predicate for validating untrusted tokens (URL focus, persisted filters).
export function isRelationshipKind(kind: string): kind is RelationshipKind {
  return kind === "application" || kind === "service" || kind === "api";
}

// Transitional: still used by graph model/merge/section until Task 6 migrates them.
export function isRenderableKind(kind: string): kind is RelationshipKind {
  return kind === "application" || kind === "service";
}

// FE creatable subset of the backend RelationshipTypeRules.IsAllowedPair (ADR-0068/ADR-0111).
// Intentionally STRICTER than the backend for `dependsOn` (backend allows any->any incl. api;
// the UI steers API links to provides/consumes and never offers `api` as a dependsOn target).
export function isAllowedPair(
  type: CreatableRelationshipType,
  source: RelationshipKind,
  target: RelationshipKind,
): boolean {
  switch (type) {
    case "dependsOn":
      return (
        (source === "application" || source === "service") &&
        (target === "application" || target === "service")
      );
    case "instanceOf":
      return source === "service" && target === "application";
    case "providesApiFor":
    case "consumesApiFrom":
      return (source === "application" || source === "service") && target === "api";
  }
}

// Valid kinds for the OTHER endpoint given the chosen type and which side is fixed.
export function allowedOtherKinds(
  type: CreatableRelationshipType,
  fixedRole: FixedRole,
  fixedKind: RelationshipKind,
): RelationshipKind[] {
  return ALL_KINDS.filter((other) =>
    fixedRole === "source"
      ? isAllowedPair(type, fixedKind, other)
      : isAllowedPair(type, other, fixedKind),
  );
}

// Types creatable with `fixedKind` in the `fixedRole` slot (i.e. some other-kind is valid).
export function offerableTypes(
  fixedRole: FixedRole,
  fixedKind: RelationshipKind,
): CreatableRelationshipType[] {
  return CREATABLE_TYPES.filter((t) => allowedOtherKinds(t, fixedRole, fixedKind).length > 0);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd web && npm run test -- src/features/catalog/relationships/__tests__/relationshipTypeRules.test.ts`
Expected: PASS (all 7 tests).

- [ ] **Step 5: Verify the rest of `web` still builds**

Run: `cd web && npm run build`
Expected: PASS — union widening is additive; `isRenderableKind` still exported for its current consumers.

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/relationships/relationshipTypeRules.ts web/src/features/catalog/relationships/__tests__/relationshipTypeRules.test.ts
git commit -m "feat(catalog-ui): api kind + API edge types in relationship rules (FU-A)"
```

---

### Task 2: Render API nodes in the graph model + merge (`graphModel.ts`, `graphMerge.ts`)

Remove the `isRenderableKind` skips so `api` neighbours/nodes/edges render; label + route them to `/catalog/apis/:id`.

**Files:**
- Modify: `web/src/features/catalog/relationships/graphModel.ts`
- Modify: `web/src/features/catalog/relationships/graphMerge.ts`
- Test: `web/src/features/catalog/relationships/__tests__/graphModel.test.ts`
- Test: `web/src/features/catalog/relationships/__tests__/graphMerge.test.ts`

**Interfaces:**
- Consumes: `RelationshipKind`, `isRelationshipKind`, `relationshipTypeLabel` (Task 1).
- Produces: `entityDetailPath("api", id)` → `/catalog/apis/:id`; `ENTITY_KIND_LABEL.api === "API"`; `parseEntityRef` resolves `api:<id>`; `toGraphModel`/`mergeGraphs` include api nodes/edges.

- [ ] **Step 1: Invert the api-exclusion test + add routing/label assertions in `graphModel.test.ts`**

Replace the last test (`"excludes a providesApiFor edge whose other endpoint is an api-kind node (FU-A deferred)"`, lines 64–76) with:

```ts
  it("renders a providesApiFor edge to an api-kind node on the dependency side", () => {
    const m = toGraphModel(focused, [
      rel({
        id: "api1",
        type: "providesApiFor",
        source: { kind: "service", id: "s1", displayName: "Me" },
        target: { kind: "api", id: "api-1", displayName: "Orders API" } as RelationshipResponse["target"],
      }),
    ]);
    const other = m.nodes.find((n) => n.id === "api:api-1")!;
    expect(other.data.kind).toBe("api");
    expect(other.data.side).toBe("dependency");
    expect(m.edges).toEqual([{ id: "api1", source: "service:s1", target: "api:api-1", label: "Provides API for" }]);
  });
```

Append two more tests inside the `describe("toGraphModel", …)` block:

```ts
  it("labels and routes the api kind", () => {
    expect(ENTITY_KIND_LABEL.api).toBe("API");
    expect(entityDetailPath("api", "x1")).toBe("/catalog/apis/x1");
  });

  it("parses an api entity ref", () => {
    expect(parseEntityRef("api:abc")).toEqual({ kind: "api", id: "abc" });
    expect(parseEntityRef("broker:abc")).toBeNull();
  });
```

Update the import at the top of the test to include the helpers:

```ts
import { toGraphModel, parseEntityRef, entityDetailPath, ENTITY_KIND_LABEL, type FocusedEntity } from "@/features/catalog/relationships/graphModel";
```

- [ ] **Step 2: Add an api-inclusion test in `graphMerge.test.ts`**

Append inside the existing `describe`/top-level test group (match the file's existing style):

```ts
import { mergeGraphs } from "@/features/catalog/relationships/graphMerge";
// ... existing imports ...

it("includes api nodes and their edges", () => {
  const merged = mergeGraphs([
    {
      nodes: [
        { kind: "service", id: "s1", displayName: "Me", depth: 0, teamId: "t1" },
        { kind: "api", id: "api-1", displayName: "Orders API", depth: 1, teamId: "t1" },
      ],
      edges: [
        { id: "e1", source: { kind: "service", id: "s1" }, target: { kind: "api", id: "api-1" }, type: "providesApiFor", origin: "manual" },
      ],
      truncated: false,
    } as never,
  ]);
  expect(merged.nodes.find((n) => n.id === "api:api-1")?.kind).toBe("api");
  expect(merged.edges).toEqual([{ id: "e1", source: "service:s1", target: "api:api-1", label: "Provides API for" }]);
});
```

- [ ] **Step 3: Run both tests to verify they fail**

Run: `cd web && npm run test -- src/features/catalog/relationships/__tests__/graphModel.test.ts src/features/catalog/relationships/__tests__/graphMerge.test.ts`
Expected: FAIL — api nodes are currently skipped; `entityDetailPath("api",…)` returns the service path.

- [ ] **Step 4: Edit `graphModel.ts`**

Change the import block (drop `isRenderableKind`, add `isRelationshipKind`):

```ts
import {
  relationshipTypeLabel,
  isRelationshipKind,
  type RelationshipKind,
  type CreatableRelationshipType,
} from "@/features/catalog/relationships/relationshipTypeRules";
```

In `toGraphModel`, delete the skip (lines 50–52):

```ts
    const other = focusedIsSource ? r.target : r.source;
    const otherId = nodeId(other.kind, other.id);
```

Replace the `neighbours.set` kind cast to trust the ref kind (it is a `RelationshipKind`):

```ts
      neighbours.set(otherId, {
        kind: other.kind as RelationshipKind,
        entityId: other.id,
        displayName: other.displayName,
        side,
      });
```
(unchanged shape — the cast already holds now that api is a `RelationshipKind`.)

Replace `ENTITY_KIND_LABEL`, `parseEntityRef`, `entityDetailPath`:

```ts
export const ENTITY_KIND_LABEL: Record<string, string> = { application: "Application", service: "Service", api: "API" };

export function parseEntityRef(token: string | null | undefined): { kind: RelationshipKind; id: string } | null {
  if (!token) return null;
  const [kind, id] = token.split(":");
  if (kind && id && isRelationshipKind(kind)) return { kind, id };
  return null;
}

export function entityDetailPath(kind: RelationshipKind, id: string): string {
  const seg = kind === "application" ? "applications" : kind === "service" ? "services" : "apis";
  return `/catalog/${seg}/${id}`;
}
```

- [ ] **Step 5: Edit `graphMerge.ts`**

Change the import block (drop `isRenderableKind`):

```ts
import {
  relationshipTypeLabel,
  type RelationshipKind,
  type CreatableRelationshipType,
} from "@/features/catalog/relationships/relationshipTypeRules";
```

Delete the node skip (line 30) and the edge skip (line 44):

```ts
    for (const n of r.nodes) {
      const id = nodeId(n.kind, n.id);
```
```ts
    for (const e of r.edges) {
      if (!edges.has(e.id)) {
```

(The `n.kind`/`e.source.kind` values are `RelationshipKind` from the DTO; no further change.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `cd web && npm run test -- src/features/catalog/relationships/__tests__/graphModel.test.ts src/features/catalog/relationships/__tests__/graphMerge.test.ts`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add web/src/features/catalog/relationships/graphModel.ts web/src/features/catalog/relationships/graphMerge.ts web/src/features/catalog/relationships/__tests__/graphModel.test.ts web/src/features/catalog/relationships/__tests__/graphMerge.test.ts
git commit -m "feat(catalog-ui): render api nodes/edges in graph model + merge (FU-A)"
```

---

### Task 3: Graph filter chip + persisted-filter kind (`GraphFilterControls.tsx`, `useGraphFilters.ts`)

Add the API filter option and let a persisted `api` kind survive rehydration.

**Files:**
- Modify: `web/src/features/catalog/components/GraphFilterControls.tsx`
- Modify: `web/src/features/catalog/relationships/useGraphFilters.ts`
- Test: `web/src/features/catalog/components/__tests__/GraphFilterControls.test.tsx`
- Test: `web/src/features/catalog/relationships/__tests__/useGraphFilters.test.ts`

**Interfaces:**
- Consumes: `isRelationshipKind` (Task 1).
- Produces: `KIND_OPTIONS` includes `{ label: "API", value: "api" }`; `useGraphFilters` retains `api` in persisted kinds.

- [ ] **Step 1: Add the persisted-api test in `useGraphFilters.test.ts`**

Append:

```ts
it("retains a persisted api kind on rehydrate", () => {
  const storage = makeStorage();
  const first = renderHook(() => useGraphFilters("application:focus", storage));
  act(() => first.result.current.setKinds(["api"]));
  const second = renderHook(() => useGraphFilters("application:focus", storage));
  expect(second.result.current.filters.kinds).toEqual(["api"]);
});
```
(If `makeStorage` is defined locally in the test, reuse it; otherwise mirror the existing storage helper used by the neighbouring tests.)

- [ ] **Step 2: Add the API-chip test in `GraphFilterControls.test.tsx`**

Append a test asserting the API option renders (match the file's existing render harness for `GraphFilterControls`):

```ts
it("offers an API kind option", () => {
  render(
    <GraphFilterControls
      kinds={[]} teamIds={[]} teams={[]} activeCount={0}
      onKindsChange={() => {}} onTeamIdsChange={() => {}} onClear={() => {}}
    />,
  );
  // MultiSelect renders options on open; assert the label is present in the option set.
  expect(screen.getByText("API")).toBeInTheDocument();
});
```
(If the existing `GraphFilterControls.test.tsx` opens the MultiSelect before asserting options, follow that same open interaction; keep the assertion on the `"API"` label.)

- [ ] **Step 3: Run both tests to verify they fail**

Run: `cd web && npm run test -- src/features/catalog/relationships/__tests__/useGraphFilters.test.ts src/features/catalog/components/__tests__/GraphFilterControls.test.tsx`
Expected: FAIL — `api` is stripped by the local `isKind`; no "API" option exists.

- [ ] **Step 4: Edit `useGraphFilters.ts`**

Replace the local predicate (line 8) with the shared one:

```ts
import type { GraphFilters } from "@/features/catalog/relationships/graphFilter";
import { isRelationshipKind, type RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";
```
and in `read`, use `isRelationshipKind`:

```ts
      kinds: Array.isArray(p.kinds) ? p.kinds.filter(isRelationshipKind) : [],
```
Delete the local `const isKind = …` line.

- [ ] **Step 5: Edit `GraphFilterControls.tsx`**

Add the API option:

```ts
const KIND_OPTIONS = [
  { label: "Application", value: "application" },
  { label: "Service", value: "service" },
  { label: "API", value: "api" },
];
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `cd web && npm run test -- src/features/catalog/relationships/__tests__/useGraphFilters.test.ts src/features/catalog/components/__tests__/GraphFilterControls.test.tsx`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add web/src/features/catalog/components/GraphFilterControls.tsx web/src/features/catalog/relationships/useGraphFilters.ts web/src/features/catalog/relationships/__tests__/useGraphFilters.test.ts web/src/features/catalog/components/__tests__/GraphFilterControls.test.tsx
git commit -m "feat(catalog-ui): API kind in graph filters (FU-A)"
```

---

### Task 4: Api entity picker + generic dialog copy (`relationships.ts`, `AddRelationshipDialog.tsx`)

Teach the entity-search hook to search APIs and neutralise the dependency-specific dialog copy.

**Files:**
- Modify: `web/src/features/catalog/api/relationships.ts` (`useEntitySearch`)
- Modify: `web/src/features/catalog/components/AddRelationshipDialog.tsx`
- Test: `web/src/features/catalog/components/__tests__/AddRelationshipDialog.test.tsx`

**Interfaces:**
- Consumes: `offerableTypes`/`allowedOtherKinds` matrix (Task 1); `GET /api/v1/catalog/apis` (`displayNameContains`, `sortBy=displayName`).
- Produces: `useEntitySearch("api", q)` returns `EntityOption[]` with `kind: "api"`; dialog offers API types and forces `otherKind=api`.

- [ ] **Step 1: Update the dialog test — mock echoes the picked kind; add API-type cases**

In `AddRelationshipDialog.test.tsx`, change the `EntitySearchCombobox` mock so the picked entity's kind mirrors the `kind` prop (needed to assert API-target payloads), and use a stable id `e9`:

```ts
vi.mock("@/features/catalog/components/EntitySearchCombobox", () => ({
  EntitySearchCombobox: ({ kind, onSelect }: { kind: string; onSelect: (e: unknown) => void }) => (
    <button type="button" onClick={() => onSelect({ kind, id: "e9", displayName: "Picked" })}>
      pick-entity
    </button>
  ),
}));
```

Update the two existing payload assertions to the new id/kind: in `"submits with correct payload when source role"` change `targetId: "app9"` → `targetId: "e9"` (targetKind stays `"application"` — dependsOn default, first allowed other kind); in `"submits with the fixed entity on the target side when target role"` change `sourceId: "app9"` → `sourceId: "e9"` (sourceKind stays `"application"`).

Append two new tests:

```ts
it("offers API edge types from an application source and posts an api target", async () => {
  const mutateAsync = vi.fn().mockResolvedValue({ id: "r1" });
  vi.spyOn(api, "useCreateRelationship").mockReturnValue({ mutateAsync, isPending: false } as never);
  harness(
    <AddRelationshipDialog open onOpenChange={vi.fn()} fixedRole="source"
      fixedEntity={{ kind: "application", id: "a1", displayName: "Checkout" }} />,
  );
  const typeSelect = screen.getByTestId("relationship-type-select") as HTMLSelectElement;
  expect(Array.from(typeSelect.options).map((o) => o.value)).toEqual(["dependsOn", "providesApiFor", "consumesApiFrom"]);
  fireEvent.change(typeSelect, { target: { value: "providesApiFor" } });
  fireEvent.click(screen.getByText("pick-entity"));
  fireEvent.click(screen.getByRole("button", { name: /add relationship/i }));
  await waitFor(() =>
    expect(mutateAsync).toHaveBeenCalledWith({
      sourceKind: "application", sourceId: "a1", type: "providesApiFor", targetKind: "api", targetId: "e9",
    }),
  );
});

it("offers instanceOf from a service source", () => {
  vi.spyOn(api, "useCreateRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
  harness(
    <AddRelationshipDialog open onOpenChange={vi.fn()} fixedRole="source" fixedEntity={svc} />,
  );
  const typeSelect = screen.getByTestId("relationship-type-select") as HTMLSelectElement;
  expect(Array.from(typeSelect.options).map((o) => o.value)).toEqual(["dependsOn", "instanceOf", "providesApiFor", "consumesApiFrom"]);
});
```

- [ ] **Step 2: Run the dialog test to verify the new cases fail**

Run: `cd web && npm run test -- src/features/catalog/components/__tests__/AddRelationshipDialog.test.tsx`
Expected: FAIL — the two new tests fail (type options / payload); existing ones pass with the updated ids.

- [ ] **Step 3: Add the `api` branch to `useEntitySearch`**

In `web/src/features/catalog/api/relationships.ts`, inside `useEntitySearch`'s `queryFn`, add an `api` branch before the services fallback (mirror the existing application branch):

```ts
      if (kind === "application") {
        const { data, error } = await apiClient.GET("/api/v1/catalog/applications", { params: { query: q } });
        if (error) throw error;
        return unwrapData(data).items.map((e) => ({ kind, id: e.id, displayName: e.displayName }));
      }
      if (kind === "api") {
        const { data, error } = await apiClient.GET("/api/v1/catalog/apis", { params: { query: q } });
        if (error) throw error;
        return unwrapData(data).items.map((e) => ({ kind, id: e.id, displayName: e.displayName }));
      }
      const { data, error } = await apiClient.GET("/api/v1/catalog/services", { params: { query: q } });
```

(The `q` object — `{ displayNameContains: query, sortBy: "displayName", sortOrder: "asc", limit: 10 }` — is already shared; the apis endpoint accepts all four.)

- [ ] **Step 4: Neutralise dialog copy in `AddRelationshipDialog.tsx`**

Replace the title `<h2>` and subtitle `<p>` (lines 107–114):

```tsx
          <h2 className="text-lg font-semibold text-primary">
            {fixedRole === "source" ? "Add outgoing relationship" : "Add incoming relationship"}
          </h2>
          <p className="text-sm text-tertiary">
            {fixedRole === "source"
              ? `Pick a type and the target for ${fixedEntity.displayName}.`
              : `Pick a type and the source for ${fixedEntity.displayName}.`}
          </p>
```

(No structural change — the Type dropdown + reactive kind-select/picker already drive everything via the matrix.)

- [ ] **Step 5: Run the dialog test to verify it passes**

Run: `cd web && npm run test -- src/features/catalog/components/__tests__/AddRelationshipDialog.test.tsx`
Expected: PASS (all tests, incl. the two new).

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/api/relationships.ts web/src/features/catalog/components/AddRelationshipDialog.tsx web/src/features/catalog/components/__tests__/AddRelationshipDialog.test.tsx
git commit -m "feat(catalog-ui): api entity picker + generic relationship dialog copy (FU-A)"
```

---

### Task 5: Reframe the detail-page section + read-only incoming-only variant (`RelationshipsSection.tsx`)

Remove the `isRenderableKind` filters, generalise the copy, route api rows, gate add-buttons on offerable types, and add the `variant="incoming-only"` read-only mode for API focus.

**Files:**
- Modify: `web/src/features/catalog/components/RelationshipsSection.tsx`
- Test: `web/src/features/catalog/components/__tests__/RelationshipsSection.test.tsx`

**Interfaces:**
- Consumes: `offerableTypes`, `relationshipTypeLabel`, `entityDetailPath`-equivalent link logic, `RelationshipKind` (Tasks 1–2).
- Produces: `<RelationshipsSection … variant?: "full" | "incoming-only">` (default `"full"`); `entityKind` accepts `"api"`.

- [ ] **Step 1: Update `RelationshipsSection.test.tsx` — generic render, api link, offerable gating, incoming-only**

Update the existing first test's expectations for the new titles/copy (titles are now "Relationships" with "Outgoing"/"Incoming" group headings; add buttons read "Add outgoing"/"Add incoming"). Then append:

```ts
it("renders an api-target row linking to the api detail page", () => {
  vi.spyOn(api, "useRelationshipsList").mockImplementation((p: api.RelationshipsListParams) =>
    listResult(p.direction === "outgoing"
      ? [{ id: "r3", type: "providesApiFor", origin: "manual",
          source: { kind: "service", id: "s1", displayName: "Me" },
          target: { kind: "api", id: "api-1", displayName: "Orders API" }, createdByUserId: "u1", createdAt: "2026-06-25T00:00:00Z" }]
      : []));
  vi.spyOn(api, "useDeleteRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
  mockPerms(true);
  render(
    <MemoryRouter>
      <RelationshipsSection entityKind="service" entityId="s1" entityTeamId="t1" entityDisplayName="Me" />
    </MemoryRouter>,
  );
  expect(screen.getByText("Orders API").closest("a")).toHaveAttribute("href", "/catalog/apis/api-1");
  expect(screen.getByText("Provides API for")).toBeInTheDocument();
});

it("incoming-only variant hides the Outgoing group and all add buttons", () => {
  vi.spyOn(api, "useRelationshipsList").mockImplementation((p: api.RelationshipsListParams) =>
    listResult(p.direction === "incoming"
      ? [{ id: "r4", type: "consumesApiFrom", origin: "manual",
          source: { kind: "service", id: "s2", displayName: "Billing" },
          target: { kind: "api", id: "api-1", displayName: "Orders API" }, createdByUserId: "u1", createdAt: "2026-06-25T00:00:00Z" }]
      : []));
  vi.spyOn(api, "useDeleteRelationship").mockReturnValue({ mutateAsync: vi.fn(), isPending: false } as never);
  mockPerms(true);
  render(
    <MemoryRouter>
      <RelationshipsSection entityKind="api" entityId="api-1" entityTeamId="t1" entityDisplayName="Orders API" variant="incoming-only" />
    </MemoryRouter>,
  );
  expect(screen.queryByText("Outgoing")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /add/i })).not.toBeInTheDocument();
  expect(screen.getByText("Billing").closest("a")).toHaveAttribute("href", "/catalog/services/s2");
});
```

- [ ] **Step 2: Run the section test to verify it fails**

Run: `cd web && npm run test -- src/features/catalog/components/__tests__/RelationshipsSection.test.tsx`
Expected: FAIL — no `variant` prop; api rows filtered out; titles/copy mismatch.

- [ ] **Step 3: Edit `RelationshipsSection.tsx`**

Change the import to drop `isRenderableKind`:

```ts
import { relationshipTypeLabel, offerableTypes, type RelationshipKind, type CreatableRelationshipType } from "@/features/catalog/relationships/relationshipTypeRules";
```

Widen props + add variant:

```ts
interface Props {
  entityKind: RelationshipKind;
  entityId: string;
  entityTeamId: string;
  entityDisplayName: string;
  variant?: "full" | "incoming-only";
}
```

Generalise `entityLink` to route all three kinds:

```ts
function entityLink(kind: string, id: string) {
  const seg = kind === "application" ? "applications" : kind === "service" ? "services" : "apis";
  return `/catalog/${seg}/${id}`;
}
```

In `renderGroup`, delete both `.filter((r) => isRenderableKind(related(r).kind))` calls — use `list.items` directly (empty check and map). Add per-group add-button gating: compute `const canAdd = canManage && offerableTypes(addRole, entityKind).length > 0;` and gate the header `<Button>` on `canAdd` instead of `canManage`.

Update the component signature and the two `renderGroup` calls: read `variant` from props (default `"full"`); title the section groups **"Outgoing"** / **"Incoming"** and pass generic help/empty copy; render the Incoming group always, and render the Outgoing group only when `variant === "full"`. Button labels: `"Add outgoing"` / `"Add incoming"`. Concretely, the JSX return becomes:

```tsx
  const variant = props.variant ?? "full";
  return (
    <section className="space-y-6" aria-label="Relationships">
      {variant === "full" && renderGroup(
        "Outgoing",
        { title: "Outgoing relationships", description: `Edges where ${entityDisplayName} is the source — what it depends on, the APIs it provides or consumes, or the application it is an instance of.` },
        "No outgoing relationships.",
        outgoing,
        (r) => r.target,
        "source",
        "Add outgoing",
      )}
      {renderGroup(
        "Incoming",
        { title: "Incoming relationships", description: `Edges where ${entityDisplayName} is the target — its dependents, providers, and consumers.` },
        `Nothing points to this ${entityKind === "api" ? "API" : entityKind}.`,
        incoming,
        (r) => r.source,
        "target",
        "Add incoming",
      )}
      {dialog && (
        <AddRelationshipDialog
          open
          onOpenChange={(o) => { if (!o) setDialog(null); }}
          fixedRole={dialog}
          fixedEntity={fixedEntity}
        />
      )}
    </section>
  );
```
(Destructure `variant` in the component params, or read via a `props` object — match the file's existing destructuring style; keep the `<h3>` group heading rendering the passed title.)

- [ ] **Step 4: Run the section test to verify it passes**

Run: `cd web && npm run test -- src/features/catalog/components/__tests__/RelationshipsSection.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/RelationshipsSection.tsx web/src/features/catalog/components/__tests__/RelationshipsSection.test.tsx
git commit -m "feat(catalog-ui): generic Outgoing/Incoming relationship section + incoming-only variant (FU-A)"
```

---

### Task 6: Mount on the Api detail page + remove dead `isRenderableKind` (`ApiDetailPage.tsx`, `relationshipTypeRules.ts`)

Add the read-only providers/consumers list to the Api detail page, then delete the now-unused `isRenderableKind`.

**Files:**
- Modify: `web/src/features/catalog/pages/ApiDetailPage.tsx`
- Modify: `web/src/features/catalog/relationships/relationshipTypeRules.ts`
- Test: `web/src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx`

**Interfaces:**
- Consumes: `RelationshipsSection` `variant="incoming-only"` (Task 5).

- [ ] **Step 1: Add an Api-detail relationships test**

In `ApiDetailPage.test.tsx`, add a test that the providers/consumers list renders with no add buttons (mock `useRelationshipsList`/`useDeleteRelationship` + `usePermissions` the same way `RelationshipsSection.test.tsx` does, and `useApi` to return an api). Assert:

```ts
it("shows the incoming providers/consumers list with no add buttons", async () => {
  // mock useApi -> { id: "api-1", displayName: "Orders API", teamId: "t1", style, version, createdAt, createdBy, ... }
  // mock useRelationshipsList (incoming) -> one consumesApiFrom edge from a service
  // mock useDeleteRelationship, usePermissions(OrgAdmin), useTeamsList
  // render <ApiDetailPage/> inside MemoryRouter at /catalog/apis/api-1
  expect(await screen.findByText("Incoming")).toBeInTheDocument();
  expect(screen.queryByRole("button", { name: /add/i })).not.toBeInTheDocument();
});
```
(Follow the existing `ApiDetailPage.test.tsx` render/route harness for the `:id` param and the `useApi` mock shape; reuse its api fixture.)

- [ ] **Step 2: Run to verify it fails**

Run: `cd web && npm run test -- src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx`
Expected: FAIL — no relationships section on the page.

- [ ] **Step 3: Mount the section in `ApiDetailPage.tsx`**

Add the import and render the section after the closing `</section>` of the metadata grid, still inside `<CardContent>`:

```tsx
import { RelationshipsSection } from "@/features/catalog/components/RelationshipsSection";
```
```tsx
        <hr className="border-secondary" />

        <RelationshipsSection
          entityKind="api"
          entityId={api.id}
          entityTeamId={api.teamId}
          entityDisplayName={api.displayName}
          variant="incoming-only"
        />
```

- [ ] **Step 4: Run to verify it passes**

Run: `cd web && npm run test -- src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx`
Expected: PASS.

- [ ] **Step 5: Remove the dead `isRenderableKind`**

Confirm no remaining consumers: `grep -rn "isRenderableKind" web/src` should show only the definition + (possibly) the test. Delete the `isRenderableKind` function from `relationshipTypeRules.ts` (the transitional block added in Task 1). If any grep hit remains outside the definition, migrate it to `isRelationshipKind` first.

- [ ] **Step 6: Full web build + test sweep**

Run: `cd web && npm run build && npm run test -- src/features/catalog`
Expected: PASS — no dangling `isRenderableKind` reference; all catalog tests green.

- [ ] **Step 7: Commit**

```bash
git add web/src/features/catalog/pages/ApiDetailPage.tsx web/src/features/catalog/pages/__tests__/ApiDetailPage.test.tsx web/src/features/catalog/relationships/relationshipTypeRules.ts
git commit -m "feat(catalog-ui): providers/consumers on Api detail page; drop dead isRenderableKind (FU-A1)"
```

---

### Task 7: Fix the stale ADR-0111 guardrail in CLAUDE.md

Doc-only hygiene surfaced during exploration — the guardrail still describes the pre-revision FK model.

**Files:**
- Modify: `CLAUDE.md` (Architectural guardrails, the "API entity model" bullet)

- [ ] **Step 1: Rewrite the bullet**

Replace the ADR-0111 guardrail bullet (the one beginning "**API entity model:** … provider (`Api.implementedByApplicationId`) + instance (`Service.applicationId`) are nullable **FK fields**…") with a line reflecting the 2026-07-04 all-edge revision:

```markdown
- **API entity model:** API is a first-class catalog entity (`EntityKind.Api`); provider, instance, and consumer links are all **relationship edges** — `provides-api-for` ({Application, Service} → Api), `instance-of` (Service → Application), `consumes-api-from` ({Application, Service} → Api). No provider/instance FK columns. Exposure (`exposes`) and service↔service `depends-on` **derive** over edges (deferred: FU-B). `ServiceEndpoint` = labeled address (protocol dropped → `Api.Style`) (ADR-0111 **revised 2026-07-04**, amends ADR-0068).
```

- [ ] **Step 2: Sanity-check no other CLAUDE.md line still asserts FK fields**

Run: `grep -n "implementedByApplicationId\|nullable .*FK field" CLAUDE.md`
Expected: no stale "not graph edges" / FK-field assertion remains for ADR-0111.

- [ ] **Step 3: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: fix stale ADR-0111 guardrail (FK fields -> all-edge model)"
```

---

### Task 8: DoD ledger + browser verification (ADR-0084)

The load-bearing gate for this frontend slice. Real-seam Postgres/JWT integration = **N/A** (backend unchanged); Playwright is mandatory.

**Files:**
- Create: `docs/superpowers/verification/2026-07-05-catalog-api-graph-ui/dod.md` (copy `docs/superpowers/templates/dod-ledger-template.md`)
- Create: `docs/superpowers/verification/2026-07-05-catalog-api-graph-ui/gate-findings.yaml` (copy template)
- Modify: `docs/product/CHECKLIST.md` (note FU-A + FU-A1 done under E-02.F-03)

- [ ] **Step 1: Initialise the DoD ledger + findings file** from the templates; mark gate 5 real-seam **N/A (frontend-only; seam covered by connectivity slice)** and gate 6 mutation **N/A (no Domain/Application C# change)** with reasons.

- [ ] **Step 2: Cold-start the dev server** (HMR cache can mask config errors — ADR-0084). Stop any running dev server, then `cd web && npm run dev`; log in `admin@orga` / `dev_password_12`, navigate in-SPA (deep-link cold-load bounces, bug #47).

- [ ] **Step 3: Playwright — graph render + navigate.** Focus a service in the graph explorer, expand to reach an `Api` node (DevSeed has apps but seed an API edge first if none exist), click the API node → assert URL is `/catalog/apis/:id`. Snapshot; check console clean.

- [ ] **Step 4: Playwright — author an API edge.** On an application/service detail page, click "Add outgoing", choose "Provides API for", pick an API in the picker, save → assert the row appears with the "Provides API for" badge and links to `/catalog/apis/:id`. Open the dialog in the real browser (react-aria table blank-page guard).

- [ ] **Step 5: Playwright — Api detail providers/consumers.** Open the API's detail page → assert the incoming "Providers & consumers" list renders the edge and shows no add buttons.

- [ ] **Step 6: Save evidence** (screenshots) as siblings under `verification/2026-07-05-catalog-api-graph-ui/`; record results in `dod.md`.

- [ ] **Step 7: Run the remaining DoD gates** per CLAUDE.md (build 0-warn; `/simplify`; `/superpowers:requesting-code-review`; `/pr-review-toolkit:review-pr`; `/deep-review`), then **terminal re-verify** `cd web && npm run build && npm run test -- src/features/catalog` green on the final commit. Run `scripts/ci-local.sh frontend` (Release mirror) green before push (stop dev server first — npm ci vs 5173 lock).

- [ ] **Step 8: Commit the ledger + evidence + checklist update**

```bash
git add docs/superpowers/verification/2026-07-05-catalog-api-graph-ui/ docs/product/CHECKLIST.md
git commit -m "docs(catalog): DoD ledger + FU-A/FU-A1 checklist (E-02.F-03)"
```

---

## Self-Review

**1. Spec coverage:**
- §4 #1 full FU-A → Tasks 1–6. #2 generic Outgoing/Incoming → Task 5. #3 pair matrix → Task 1. #4 no new visual (label only) → Task 2 (`ENTITY_KIND_LABEL.api`) + Task 3 (chip). #5 FU-A1 incoming-only variant → Tasks 5–6. #6 no new permission → honored (no permission task).
- §5.1 files: relationshipTypeRules (T1), graphModel (T2), graphMerge (T2), useGraphFilters (T3), graph.ts (**no change needed** — `GraphFocus.kind` widens transitively via `RelationshipKind`; noted in Impact Analysis; not a task), GraphFilterControls (T3), RelationshipsSection (T5), AddRelationshipDialog (T4), relationships.ts useEntitySearch (T4), EntitySearchCombobox (**no change** — already generic; noted in spec), ApiDetailPage (T6). graphFilter.ts (**no change** — kind-generic; covered by T3 tests). ✓
- §5.2 tests: rules matrix (T1), graphModel (T2), graphMerge (T2), graphFilter (kind-generic; add covered under T3 useGraphFilters/GraphFilterControls — graphFilter.ts itself unchanged), AddRelationshipDialog (T4), RelationshipsSection (T5), ApiDetailPage (T6), GraphFilterControls (T3). ✓
- §5.3 docs: CLAUDE.md guardrail (T7), CHECKLIST (T8), list-filter-registry (no change — correctly none). ✓
- §6 Impact Analysis N/A → mirrored in plan header. §7 DoD (gate 5/6 N/A, Playwright mandatory) → T8. §8 out-of-scope → not implemented (correct). ✓

**2. Placeholder scan:** No TBD/TODO/"add error handling"/"similar to". Test bodies where the existing harness is reused (ApiDetailPage T6 §Step1, GraphFilterControls open interaction) point at the concrete sibling test to mirror rather than inventing a divergent harness — the assertion code is shown; the mock wiring reuses an existing, named pattern. ✓

**3. Type consistency:** `RelationshipKind` (+api), `CreatableRelationshipType` (+3), `isRelationshipKind`, `isAllowedPair`/`allowedOtherKinds`/`offerableTypes`, `relationshipTypeLabel`, `entityDetailPath`, `variant: "full" | "incoming-only"` used consistently across Tasks 1→6. `dependsOn` kept first in `CREATABLE_TYPES` (matches unchanged dialog default tests). Picker mock echoes `kind` → payload assertions use `e9`/matrix-derived kinds. ✓
