# System UI Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the frontend for the `System` catalog entity — list page, register dialog, and a tabbed read-only detail page whose Members tab lists components already assigned via `PartOf` edges.

**Architecture:** Pure frontend slice mirroring the existing Service/API UI surfaces (ADR-0094 Untitled UI + react-aria; ADR-0095 cursor lists; ADR-0107 filters; ADR-0114 tabbed detail). Backend `/systems` endpoints + `PartOf` edges already shipped in E-03.F-03.S-01. The Members list reuses the existing `GET /catalog/relationships` read layer (`entityKind=system`, `direction=incoming`, mapped over `r.source`).

**Tech Stack:** React 19 + TypeScript, TanStack Query, react-hook-form + zod, react-aria-components, Vitest + React Testing Library + `@testing-library/user-event`, openapi-typescript codegen. **No MSW in this repo** — data-layer tests stub `apiClient` via `vi.spyOn(clientModule,"apiClient","get")`; page/component tests `vi.mock` the feature hook and `react-oidc-context`.

## Global Constraints

- Untitled UI / react-aria-components only (ADR-0094). No shadcn/ui.
- Every list screen: `useListUrlState` + `useCursorList` + `<DataTable>`/`SortableHead` (ADR-0095). Default sort `displayName asc`.
- react-aria `<Table>` needs exactly one `isRowHeader` column (`id="displayName" isRowHeader`) or overlays blank-page the screen (ADR-0084). Tests must assert `getAllByRole("rowheader").length > 0` when a table precedes any dialog/overlay.
- Sort allowlist for Systems = `["createdAt","displayName"]` (backend `SystemSortField`). Filters = `displayNameContains` (text) + `teamId` (multi). **No health** (Systems have none).
- `System.description` is **optional** (backend `RegisterSystemRequest.Description` is nullable; `CatalogSystem` allows null); blank ⇒ send `undefined`.
- No new permission: register gated by `KartovaPermissions.CatalogSystemsRegister` (`catalog.systems.register`, already in `permissions.ts`); read by `catalog.read`.
- Enum/DTO wire shapes come from `@/generated/openapi` (single source of truth). Codegen (Task 1) MUST run before any typed data-layer code compiles.
- `npm run build` (tsc -b + vite) is the binding type gate. Frontend has no eslint CI job — run `components/application/**` hook-rule-sensitive code carefully.
- Commit after every green step.

## Impact Analysis (codelens)

`N/A — frontend-only slice; no C# symbol signatures or behavior change.` The backend `/systems` endpoints, `SystemResponse` contract, `catalog.systems.register` permission, and `PartOf` edge rules all shipped in E-03.F-03.S-01 and are consumed unchanged. FE reuse touchpoints (`useRelationshipsList`, `entityDetailPath`, `ENTITY_KIND_LABEL`, `useListUrlState`, `FilterBar`, `useListFilters`, `SortableHead`/`TablePager`, `DetailTabs`, `CreatedByLink`, `useTeamsList`, `useCurrentUser`) verified present during design.

## File Structure

**Create:**
- `web/src/features/catalog/schemas/registerSystem.ts` — zod input schema.
- `web/src/features/catalog/schemas/__tests__/registerSystem.test.ts`
- `web/src/features/catalog/api/systems.ts` — query/mutation hooks.
- `web/src/features/catalog/api/__tests__/systems.test.tsx`
- `web/src/features/catalog/components/SystemsTable.tsx`
- `web/src/features/catalog/components/__tests__/SystemsTable.test.tsx`
- `web/src/features/catalog/components/RegisterSystemDialog.tsx`
- `web/src/features/catalog/components/__tests__/RegisterSystemDialog.test.tsx`
- `web/src/features/catalog/components/SystemMembersSection.tsx`
- `web/src/features/catalog/components/__tests__/SystemMembersSection.test.tsx`
- `web/src/features/catalog/pages/SystemsListPage.tsx`
- `web/src/features/catalog/pages/__tests__/SystemsListPage.test.tsx`
- `web/src/features/catalog/pages/SystemDetailPage.tsx`
- `web/src/features/catalog/pages/__tests__/SystemDetailPage.test.tsx`

**Modify:**
- `web/openapi-snapshot.json` (regenerated)
- `web/src/components/layout/Sidebar.tsx` (+ nav item, doc comment)
- `web/src/app/router.tsx` (+ 2 routes)
- `docs/design/list-filter-registry.md` (+ Systems row)
- `docs/product/CHECKLIST.md` (mark S-01 UI shipped)

---

### Task 1: Register `ListSystems` in the OpenAPI transformer + regenerate the client

The S-01 `/systems` endpoints are not in the committed `web/openapi-snapshot.json`, so `components["schemas"]["SystemResponse"]` and `operations["ListSystems"]` don't exist yet. All later tasks depend on these types.

**Latent S-01 defect fixed here (backend, 3 lines):** S-01 added `ListSystems` but never registered it in `src/Kartova.Api/OpenApi/CursorListQueryParameterTransformer.cs` (whose doc says "Add a row when a new list endpoint is introduced"). Without it, `ListSystems` publishes loose `sortBy: string` / `sortOrder: string` / `limit: string` instead of typed enums + bounded int — which breaks the Services-mirror (`limit: params.limit ?? 50` is a `number`, unassignable to `limit?: string`). This is a **doc-shape-only** change (runtime binding already parses via `CursorListBinding`; S-01 `ListSystemsPaginationTests` already cover behavior) → **gate 5 stays N/A**. `OpenApiTests` may need a refresh (name-keyed/live).

**Files:**
- Modify: `src/Kartova.Api/OpenApi/CursorListQueryParameterTransformer.cs` — add `("ListSystems","sortBy")=typeof(SystemSortField)`, `("ListSystems","sortOrder")=typeof(SortOrder)` to `EnumByOperationParameter`; add `"ListSystems"` to `OperationsWithLimitParameter`.
- Modify: `web/openapi-snapshot.json`

- [ ] **Step 0: Register ListSystems in the transformer, rebuild the API image**

Apply the 3-line edit above, then rebuild so the live spec reflects it: `docker compose up -d --build api`.
Verify: `curl -s http://localhost:8080/openapi/v1.json | python -c "import sys,json; p=json.load(sys.stdin)['paths']['/api/v1/catalog/systems']['get']['parameters']; print({x['name']:x['schema'] for x in p if x['name'] in ('sortBy','sortOrder','limit')})"`
Expected: `sortBy`/`sortOrder` show `enum`, `limit` shows `type: integer` min 1 max 200.

- [ ] **Step 1: Start the API so the live spec exposes `/systems`**

The generated client is produced from the live API spec. Ensure the API image is rebuilt (new endpoints since last snapshot) and running:

Run: `docker compose up -d --build api` (from repo root)
Expected: `api` healthy; `GET http://localhost:8080/openapi/v1.json` includes `/api/v1/catalog/systems`.

Verify: `curl -s http://localhost:8080/openapi/v1.json | grep -c "catalog/systems"`
Expected: non-zero.

- [ ] **Step 2: Regenerate the snapshot + client**

Run (from `web/`): `npm run codegen` (the predev/prebuild openapi step; regenerates `src/generated/openapi.d.ts` from the live spec and refreshes `openapi-snapshot.json`).
Expected: `openapi-snapshot.json` diff adds the `/api/v1/catalog/systems` paths + `SystemResponse`, `RegisterSystemRequest`, `SystemSortField` schemas.

- [ ] **Step 3: Confirm the new types resolve**

Run (from `web/`): `npx tsc --noEmit -p tsconfig.json 2>&1 | head -20`
Expected: no errors referencing `SystemResponse`/`ListSystems` (types now present). Pre-existing unrelated output only.

- [ ] **Step 4: Commit**

```bash
git add web/openapi-snapshot.json web/src/generated/
git commit -m "chore(web): regenerate OpenAPI client for /systems endpoints"
```

> If Docker is unavailable in this environment, flag **pending user verification** for Steps 1–2 and stop — later tasks cannot typecheck without the regenerated client.

---

### Task 2: `registerSystem` zod schema

**Files:**
- Create: `web/src/features/catalog/schemas/registerSystem.ts`
- Test: `web/src/features/catalog/schemas/__tests__/registerSystem.test.ts`

**Interfaces:**
- Produces: `registerSystemSchema`, `type RegisterSystemInput = { displayName: string; description?: string; teamId: string }`.

- [ ] **Step 1: Write the failing test**

```ts
// web/src/features/catalog/schemas/__tests__/registerSystem.test.ts
import { describe, it, expect } from "vitest";
import { registerSystemSchema } from "../registerSystem";

describe("registerSystemSchema", () => {
  const base = { displayName: "Payments Platform", description: "Core", teamId: "11111111-1111-1111-1111-111111111111" };

  it("accepts a valid input", () => {
    expect(registerSystemSchema.safeParse(base).success).toBe(true);
  });

  it("accepts a missing/empty description (optional)", () => {
    expect(registerSystemSchema.safeParse({ ...base, description: "" }).success).toBe(true);
    const { description, ...noDesc } = base;
    expect(registerSystemSchema.safeParse(noDesc).success).toBe(true);
  });

  it("rejects an empty display name", () => {
    expect(registerSystemSchema.safeParse({ ...base, displayName: "" }).success).toBe(false);
  });

  it("rejects a display name over 128 chars", () => {
    expect(registerSystemSchema.safeParse({ ...base, displayName: "x".repeat(129) }).success).toBe(false);
  });

  it("rejects a description over 4096 chars", () => {
    expect(registerSystemSchema.safeParse({ ...base, description: "x".repeat(4097) }).success).toBe(false);
  });

  it("rejects a non-uuid teamId", () => {
    expect(registerSystemSchema.safeParse({ ...base, teamId: "nope" }).success).toBe(false);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/schemas/__tests__/registerSystem.test.ts`
Expected: FAIL — cannot resolve `../registerSystem`.

- [ ] **Step 3: Write the schema**

```ts
// web/src/features/catalog/schemas/registerSystem.ts
import { z } from "zod";

export const registerSystemSchema = z.object({
  displayName: z
    .string()
    .min(1, "Display Name must not be empty")
    .max(128, "Display Name must be at most 128 characters"),
  description: z.string().max(4096, "Description must be at most 4096 characters").optional(),
  teamId: z.string().uuid("Team is required"),
});

export type RegisterSystemInput = z.infer<typeof registerSystemSchema>;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd web && npx vitest run src/features/catalog/schemas/__tests__/registerSystem.test.ts`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/schemas/registerSystem.ts web/src/features/catalog/schemas/__tests__/registerSystem.test.ts
git commit -m "feat(web): registerSystem zod schema"
```

---

### Task 3: `api/systems.ts` data layer

**Files:**
- Create: `web/src/features/catalog/api/systems.ts`
- Test: `web/src/features/catalog/api/__tests__/systems.test.tsx`

**Interfaces:**
- Consumes: `RegisterSystemInput` (Task 2); generated `SystemResponse`, `operations["ListSystems"]` (Task 1); `useCursorList`, `unwrapData`, `throwWithStatus`, `apiClient`.
- Produces: `useSystemsList(params)`, `useSystem(id)`, `useRegisterSystem()`, `systemKeys`, `type SystemResponse`. `SystemsListParams = { sortBy; sortOrder; limit?; teamId?: string[]; displayNameContains? }`.

- [ ] **Step 1: Write the failing test** (mirrors `api/__tests__/services.test.tsx` — `vi.spyOn(apiClient)`, NO msw)

> **Harness note (review B1):** this repo has **no MSW**. Data-layer tests stub the client with `vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET, POST })` and a local `wrapper()` (see `api/__tests__/services.test.tsx:1-33`). Query params are asserted by inspecting the GET mock's call args (`get.mock.calls[0][1].params.query`), not a captured URL.

```tsx
// web/src/features/catalog/api/__tests__/systems.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

import * as clientModule from "@/features/catalog/api/client";
import { useSystem, useSystemsList, useRegisterSystem } from "../systems";

function wrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

const sys = { id: "s1", tenantId: "t1", displayName: "Alpha", description: null, teamId: "team1", createdByUserId: "u1", createdAt: "2026-07-22T00:00:00Z", createdBy: null };
const page = { items: [sys], nextCursor: null, prevCursor: null };

describe("api/systems", () => {
  beforeEach(() => vi.restoreAllMocks());

  function stubGet(returnValue: unknown = page) {
    const get = vi.fn().mockResolvedValue({ data: returnValue, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    return get;
  }

  it("omits teamId/displayNameContains from the query when empty", async () => {
    const get = stubGet();
    const { result } = renderHook(() => useSystemsList({ sortBy: "displayName", sortOrder: "asc" }), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.items.length).toBe(1));
    const query = get.mock.calls[0][1].params.query;
    expect(query.sortBy).toBe("displayName");
    expect(query).not.toHaveProperty("teamId");
    expect(query).not.toHaveProperty("displayNameContains");
  });

  it("passes teamId (array) + displayNameContains when set", async () => {
    const get = stubGet();
    const { result } = renderHook(
      () => useSystemsList({ sortBy: "displayName", sortOrder: "asc", teamId: ["a", "b"], displayNameContains: "pay" }),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(result.current.items.length).toBe(1));
    const query = get.mock.calls[0][1].params.query;
    expect(query.teamId).toEqual(["a", "b"]);
    expect(query.displayNameContains).toBe("pay");
  });

  it("fetches a single system by id", async () => {
    stubGet(sys);
    const { result } = renderHook(() => useSystem("s1"), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.data?.displayName).toBe("Alpha"));
  });

  it("POSTs the payload on register and invalidates the systems cache", async () => {
    const post = vi.fn().mockResolvedValue({ data: sys, error: undefined, response: new Response() });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: vi.fn(), POST: post } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidate = vi.spyOn(qc, "invalidateQueries");
    const w = ({ children }: { children: ReactNode }) => <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
    const { result } = renderHook(() => useRegisterSystem(), { wrapper: w });
    await result.current.mutateAsync({ displayName: "Alpha", teamId: "team1", description: "core" });
    expect(post.mock.calls[0][1].body).toMatchObject({ displayName: "Alpha", teamId: "team1", description: "core" });
    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: ["systems"] }));
  });

  it("sends a blank description as null in the POST body", async () => {
    const post = vi.fn().mockResolvedValue({ data: sys, error: undefined, response: new Response() });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: vi.fn(), POST: post } as never);
    const { result } = renderHook(() => useRegisterSystem(), { wrapper: wrapper() });
    await result.current.mutateAsync({ displayName: "Alpha", teamId: "team1", description: "   " });
    expect(post.mock.calls[0]![1].body.description).toBeNull();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/api/__tests__/systems.test.tsx`
Expected: FAIL — cannot resolve `../systems`.

- [ ] **Step 3: Write the data layer**

```ts
// web/src/features/catalog/api/systems.ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { RegisterSystemInput } from "../schemas/registerSystem";
import type { components, operations } from "@/generated/openapi";

type SystemResponse = components["schemas"]["SystemResponse"];
type ListSystemsQuery = NonNullable<operations["ListSystems"]["parameters"]["query"]>;

type SystemsListParams = {
  sortBy: NonNullable<ListSystemsQuery["sortBy"]>;      // "createdAt" | "displayName"
  sortOrder: NonNullable<ListSystemsQuery["sortOrder"]>;
  limit?: number;
  /** ADR-0107 steward-team multi-select. Empty/undefined ⇒ omitted ⇒ show all. */
  teamId?: string[];
  displayNameContains?: string;
};

export const systemKeys = {
  all: ["systems"] as const,
  list: (params?: SystemsListParams) =>
    params ? ([...systemKeys.all, "list", params] as const) : ([...systemKeys.all, "list"] as const),
  detail: (id: string) => [...systemKeys.all, "detail", id] as const,
};

export function useSystemsList(params: SystemsListParams) {
  return useCursorList<SystemResponse>({
    queryKey: systemKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/systems", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
            ...(params.teamId?.length ? { teamId: params.teamId } : {}),
            ...(params.displayNameContains ? { displayNameContains: params.displayNameContains } : {}),
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useSystem(id: string) {
  return useQuery({
    queryKey: systemKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/systems/{id}", {
        params: { path: { id } },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useRegisterSystem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterSystemInput) => {
      const body = { ...input, description: input.description?.trim() ? input.description : null }; // string|null contract
      const { data, error, response } = await apiClient.POST("/api/v1/catalog/systems", { body });
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: systemKeys.all });
    },
  });
}

export type { SystemResponse };
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd web && npx vitest run src/features/catalog/api/__tests__/systems.test.tsx`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/api/systems.ts web/src/features/catalog/api/__tests__/systems.test.tsx
git commit -m "feat(web): systems data layer (list/detail/register hooks)"
```

---

### Task 4: `SystemsTable` component

**Files:**
- Create: `web/src/features/catalog/components/SystemsTable.tsx`
- Test: `web/src/features/catalog/components/__tests__/SystemsTable.test.tsx`

**Interfaces:**
- Consumes: `SystemResponse` (Task 3); `SortableHead`/`TablePager`/`TableSkeleton`/`fromSort`/`toSort`; `CreatedByLink`; `CursorListResult`/`SortDirection`.
- Produces: `SystemsTable({ list, sortBy, sortOrder, onSortChange, teamNameById })`. `SortField = "createdAt" | "displayName"`.

- [ ] **Step 1: Write the failing test**

```tsx
// web/src/features/catalog/components/__tests__/SystemsTable.test.tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { SystemsTable } from "../SystemsTable";
import type { CursorListResult } from "@/lib/list/types";
import type { SystemResponse } from "@/features/catalog/api/systems";

// Full CursorListResult default (matches ServicesTable.test.tsx) — no unsafe cast; includes
// the isFetching/refetch fields the type requires so future drift is type-checked.
function makeList(over: Partial<CursorListResult<SystemResponse>> = {}): CursorListResult<SystemResponse> {
  return {
    items: [], isLoading: false, isFetching: false, isError: false, error: null,
    hasPrev: false, hasNext: false, goPrev: vi.fn(), goNext: vi.fn(), reset: vi.fn(), refetch: vi.fn(),
    ...over,
  };
}
const sys = (over: Partial<SystemResponse> = {}): SystemResponse => ({ id: "s1", tenantId: "t1", displayName: "Alpha", description: null, teamId: "team1", createdByUserId: "u1", createdAt: "2026-07-22T00:00:00Z", createdBy: null, ...over } as SystemResponse);

const render1 = (ui: React.ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>);

describe("SystemsTable", () => {
  const teamNames = new Map([["team1", "Platform Team"]]);

  it("renders a row with a name link and steward team", () => {
    render1(<SystemsTable list={makeList({ items: [sys()] })} sortBy="displayName" sortOrder="asc" onSortChange={vi.fn()} teamNameById={teamNames} />);
    expect(screen.getByRole("link", { name: "Alpha" })).toHaveAttribute("href", "/catalog/systems/s1");
    expect(screen.getByText("Platform Team")).toBeInTheDocument();
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0); // ADR-0084
  });

  it("renders a loading skeleton (still has a row header) when loading", () => {
    render1(<SystemsTable list={makeList({ isLoading: true })} sortBy="displayName" sortOrder="asc" onSortChange={vi.fn()} teamNameById={teamNames} />);
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0); // ADR-0084 (loading branch)
  });

  it("shows an empty state when there are no systems", () => {
    render1(<SystemsTable list={makeList({ items: [] })} sortBy="displayName" sortOrder="asc" onSortChange={vi.fn()} teamNameById={teamNames} />);
    expect(screen.getByText("No systems yet")).toBeInTheDocument();
  });

  it("invokes onSortChange when the Name header is activated", async () => {
    const onSortChange = vi.fn();
    render1(<SystemsTable list={makeList({ items: [sys()] })} sortBy="displayName" sortOrder="asc" onSortChange={onSortChange} teamNameById={teamNames} />);
    await userEvent.click(screen.getByRole("columnheader", { name: /name/i }));
    expect(onSortChange).toHaveBeenCalledWith("displayName", "desc");
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/SystemsTable.test.tsx`
Expected: FAIL — cannot resolve `../SystemsTable`.

- [ ] **Step 3: Write the component**

```tsx
// web/src/features/catalog/components/SystemsTable.tsx
import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Card, CardContent } from "@/components/base/card/card";
import { SortableHead, TablePager, TableSkeleton, fromSort, toSort } from "@/components/application/data-table/data-table";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import type { CursorListResult, SortDirection } from "@/lib/list/types";
import type { SystemResponse } from "@/features/catalog/api/systems";

type SortField = "createdAt" | "displayName";

interface Props {
  list: CursorListResult<SystemResponse>;
  sortBy: SortField;
  sortOrder: SortDirection;
  onSortChange: (field: SortField, order: SortDirection) => void;
  /** Resolves teamId → displayName (parent fetches all teams once). */
  teamNameById: Map<string, string>;
}

export function SystemsTable({ list, sortBy, sortOrder, onSortChange, teamNameById }: Props) {
  if (list.isLoading) {
    return (
      <Table aria-label="Systems">
        <Table.Header>
          <Table.Head id="displayName" isRowHeader>Name</Table.Head>
          <Table.Head id="team">Steward team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
          <Table.Head id="createdAt">Created</Table.Head>
        </Table.Header>
        <TableSkeleton rows={5} cells={4} />
      </Table>
    );
  }

  if (list.items.length === 0) {
    return (
      <Card className="mx-auto max-w-md text-center">
        <CardContent className="space-y-2 p-8">
          <p className="text-base font-medium text-primary">No systems yet</p>
          <p className="text-sm text-tertiary">
            Use the &quot;+ Register System&quot; button in the header to add your first one.
          </p>
        </CardContent>
      </Card>
    );
  }

  const handleSortChange = (descriptor: Parameters<typeof toSort>[0]) => {
    const { field, order } = toSort(descriptor);
    if (field === "createdAt" || field === "displayName") {
      onSortChange(field, order);
    }
  };

  return (
    <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
      <Table aria-label="Systems" sortDescriptor={fromSort(sortBy, sortOrder)} onSortChange={handleSortChange}>
        <Table.Header>
          <SortableHead id="displayName" isRowHeader>Name</SortableHead>
          <Table.Head id="team">Steward team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
          <SortableHead id="createdAt">Created</SortableHead>
        </Table.Header>
        <Table.Body>
          {list.items.map((sys) => (
            <Table.Row key={sys.id} id={sys.id}>
              <Table.Cell>
                <Link to={`/catalog/systems/${sys.id}`} className="block font-medium text-primary hover:underline">
                  {sys.displayName}
                </Link>
              </Table.Cell>
              <Table.Cell className="text-sm">
                <Link to={`/teams/${sys.teamId}`} className="text-primary hover:underline">
                  {teamNameById.get(sys.teamId) ?? "Unknown team"}
                </Link>
              </Table.Cell>
              <Table.Cell className="text-sm">
                <CreatedByLink user={sys.createdBy} />
              </Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {sys.createdAt ? new Date(sys.createdAt).toLocaleDateString() : ""}
              </Table.Cell>
            </Table.Row>
          ))}
        </Table.Body>
      </Table>
      <TablePager hasPrev={list.hasPrev} hasNext={list.hasNext} onPrev={list.goPrev} onNext={list.goNext} pageSize={list.items.length} />
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/SystemsTable.test.tsx`
Expected: PASS (4 tests). If the sort-click assertion order differs, mirror the exact interaction used in `ServicesTable.test.tsx`.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/SystemsTable.tsx web/src/features/catalog/components/__tests__/SystemsTable.test.tsx
git commit -m "feat(web): SystemsTable list component"
```

---

### Task 5: `RegisterSystemDialog` component

**Files:**
- Create: `web/src/features/catalog/components/RegisterSystemDialog.tsx`
- Test: `web/src/features/catalog/components/__tests__/RegisterSystemDialog.test.tsx`

**Interfaces:**
- Consumes: `registerSystemSchema`/`RegisterSystemInput` (Task 2); `useRegisterSystem` (Task 3); `useTeamsList`; `useCurrentUser`; modal/form primitives.
- Produces: `RegisterSystemDialog({ open, onOpenChange })`. Team `<select>` carries `data-testid="register-system-team-select"`.

- [ ] **Step 1: Write the failing test**

> **Harness note (review B1/B2/B3):** no MSW. Mock the collaborator hooks and `react-oidc-context` (the dialog reads `useCurrentUser` → `useAuth`, which throws if unmocked). `useTeamsList`/`useRegisterSystem` are mocked directly; the blank-description→undefined transform lives in the hook and is covered in Task 3, so here we only assert the dialog passes the raw payload through.

```tsx
// web/src/features/catalog/components/__tests__/RegisterSystemDialog.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

vi.mock("react-oidc-context", () => ({
  useAuth: () => ({ isAuthenticated: true, user: { access_token: "t", profile: { sub: "u", name: "Alice", email: "a@x", tenant_id: "t" } } }),
}));

const mutateAsync = vi.fn().mockResolvedValue({ id: "s1" });
vi.mock("@/features/catalog/api/systems", () => ({ useRegisterSystem: () => ({ mutateAsync, isPending: false }) }));

const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => useTeamsListMock() }));

import { RegisterSystemDialog } from "../RegisterSystemDialog";

const TEAM_ID = "11111111-1111-1111-1111-111111111111";
function teamsResult(items: { id: string; displayName: string }[]) {
  return { items, isLoading: false, isFetching: false, isError: false, error: null, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn() };
}

describe("RegisterSystemDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useTeamsListMock.mockReturnValue(teamsResult([{ id: TEAM_ID, displayName: "Platform Team" }]));
  });

  it("submits the payload and calls onOpenChange(false) on success", async () => {
    const onOpenChange = vi.fn();
    render(<RegisterSystemDialog open onOpenChange={onOpenChange} />);

    await userEvent.type(screen.getByLabelText(/Display Name/i), "Payments");
    await userEvent.selectOptions(screen.getByTestId("register-system-team-select"), TEAM_ID);
    await userEvent.click(screen.getByRole("button", { name: /Register System/i }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(mutateAsync).toHaveBeenCalledWith(expect.objectContaining({ displayName: "Payments", teamId: TEAM_ID }));
  });

  it("blocks submit and shows an error when no team is selected", async () => {
    render(<RegisterSystemDialog open onOpenChange={vi.fn()} />);
    await userEvent.type(screen.getByLabelText(/Display Name/i), "Payments");
    await userEvent.click(screen.getByRole("button", { name: /Register System/i }));
    expect(await screen.findByText("Team is required")).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("disables submit and hints when no teams exist", () => {
    useTeamsListMock.mockReturnValue(teamsResult([]));
    render(<RegisterSystemDialog open onOpenChange={vi.fn()} />);
    expect(screen.getByRole("button", { name: /Register System/i })).toBeDisabled();
    expect(screen.getByText(/create a team first/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/RegisterSystemDialog.test.tsx`
Expected: FAIL — cannot resolve `../RegisterSystemDialog`.

- [ ] **Step 3: Write the component** (mirrors `RegisterServiceDialog`, no endpoints, optional description)

```tsx
// web/src/features/catalog/components/RegisterSystemDialog.tsx
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";
import { Avatar } from "@/components/base/avatar/avatar";

import { registerSystemSchema, type RegisterSystemInput } from "@/features/catalog/schemas/registerSystem";
import { useRegisterSystem } from "@/features/catalog/api/systems";
import { useTeamsList } from "@/features/teams/api/teams";
import { applyProblemDetailsToForm, type ProblemDetails } from "@/shared/forms/problemDetails";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { initialsOf } from "@/shared/auth/initials";

type TextFieldsInput = { displayName: string; description?: string };

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RegisterSystemDialog({ open, onOpenChange }: Props) {
  const user = useCurrentUser();
  const mutation = useRegisterSystem();
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const [selectedTeamId, setSelectedTeamId] = useState<string>("");
  const [teamError, setTeamError] = useState<string>("");

  const form = useForm<TextFieldsInput>({
    resolver: zodResolver(registerSystemSchema.pick({ displayName: true, description: true })),
    defaultValues: { displayName: "", description: "" },
  });

  useEffect(() => {
    if (!open) {
      form.reset({ displayName: "", description: "" });
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setSelectedTeamId("");
      setTeamError("");
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    if (!selectedTeamId) {
      setTeamError("Team is required");
      return;
    }
    setTeamError("");
    const payload: RegisterSystemInput = { ...values, teamId: selectedTeamId };
    try {
      await mutation.mutateAsync(payload);
      toast.success("System registered");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
      );
      if (!handled) {
        toast.error(problem.detail ?? problem.title ?? "Failed to register system");
      }
    }
  });

  const initials = initialsOf(user?.displayName);
  const teams = teamsList.items ?? [];
  const noTeams = !teamsList.isLoading && teams.length === 0;

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[640px]">
        <Dialog aria-label="Register System" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Register System</h2>
              <p className="text-sm text-tertiary">Group related components under a stewarded system</p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <FormField name="displayName" control={form.control}>
                {({ field, fieldState }) => (
                  <Input
                    label="Display Name"
                    placeholder="Payments Platform"
                    hint={fieldState.error?.message ?? "Human-friendly name shown in UI."}
                    isInvalid={!!fieldState.error}
                    isRequired
                    {...field}
                  />
                )}
              </FormField>
              <FormField name="description" control={form.control}>
                {({ field, fieldState }) => (
                  <TextArea
                    label="Description"
                    rows={3}
                    placeholder="Short summary (optional)…"
                    hint={fieldState.error?.message}
                    isInvalid={!!fieldState.error}
                    {...field}
                  />
                )}
              </FormField>

              <div className="flex flex-col gap-1">
                <label htmlFor="register-system-team" className="text-sm font-medium text-secondary">
                  Steward team <span className="text-error-primary">*</span>
                </label>
                <select
                  id="register-system-team"
                  data-testid="register-system-team-select"
                  className="rounded-md border border-secondary px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-brand-500 disabled:opacity-60 bg-primary text-primary"
                  value={selectedTeamId}
                  onChange={(e) => {
                    setSelectedTeamId(e.target.value);
                    if (e.target.value) setTeamError("");
                  }}
                  disabled={teamsList.isLoading || mutation.isPending}
                  aria-invalid={!!teamError}
                >
                  <option value="">Select a team…</option>
                  {teams.map((t) => (
                    <option key={t.id} value={t.id}>{t.displayName}</option>
                  ))}
                </select>
                {teamError && <p className="text-xs text-error-primary">{teamError}</p>}
                {noTeams && (
                  <p className="text-xs text-tertiary">
                    No teams available — create a team first before registering a system.
                  </p>
                )}
              </div>

              <div>
                <p className="text-xs uppercase tracking-wide text-tertiary">Created by</p>
                <div className="mt-1 inline-flex items-center gap-2 rounded-md border border-secondary bg-secondary/40 px-2 py-1.5">
                  <Avatar size="xs" initials={initials} />
                  <div className="min-w-0">
                    <div className="text-sm font-medium text-primary truncate">{user?.displayName ?? "—"}</div>
                    <div className="text-xs text-tertiary truncate">{user?.email ?? ""}</div>
                  </div>
                </div>
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <Button type="button" color="secondary" size="sm" onClick={() => onOpenChange(false)}>
                  Cancel
                </Button>
                <Button type="submit" color="primary" size="sm" isLoading={mutation.isPending} isDisabled={noTeams}>
                  Register System
                </Button>
              </div>
            </HookForm>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/RegisterSystemDialog.test.tsx`
Expected: PASS (3 tests). If `useCurrentUser` reads more of the `useAuth` shape, align the `react-oidc-context` mock with `RegisterServiceDialog.test.tsx`.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/RegisterSystemDialog.tsx web/src/features/catalog/components/__tests__/RegisterSystemDialog.test.tsx
git commit -m "feat(web): RegisterSystemDialog"
```

---

### Task 6: `SystemMembersSection` (read-only members)

**Files:**
- Create: `web/src/features/catalog/components/SystemMembersSection.tsx`
- Test: `web/src/features/catalog/components/__tests__/SystemMembersSection.test.tsx`

**Interfaces:**
- Consumes: `useRelationshipsList` (`@/features/catalog/api/relationships`); `entityDetailPath`, `ENTITY_KIND_LABEL` (`@/features/catalog/relationships/graphModel`); `Table`, `TableSkeleton`, `TablePager`, `Badge`.
- Produces: `SystemMembersSection({ systemId })`.

- [ ] **Step 1: Write the failing test**

> **Harness note (review B1):** no MSW — mock `useRelationshipsList` directly (mirrors `ServiceDetailPage.test.tsx:23-26`), returning a controllable result object. `useDeleteRelationship` is not imported by this read-only component, so it need not be mocked.

```tsx
// web/src/features/catalog/components/__tests__/SystemMembersSection.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

const useRelationshipsListMock = vi.fn();
vi.mock("@/features/catalog/api/relationships", () => ({
  useRelationshipsList: (...a: unknown[]) => useRelationshipsListMock(...a),
}));

import { SystemMembersSection } from "../SystemMembersSection";

const edge = (kind: string, id: string, displayName: string, type = "partOf") => ({
  id: `rel-${id}`, type, origin: "manual", createdByUserId: "u1", createdAt: "2026-07-22T00:00:00Z", createdBy: null,
  source: { kind, id, displayName },
  target: { kind: "system", id: "sys1", displayName: "Payments" },
});
function result(over: Record<string, unknown> = {}) {
  return { items: [], isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), ...over };
}
const render1 = (ui: React.ReactElement) => render(<MemoryRouter>{ui}</MemoryRouter>);

describe("SystemMembersSection", () => {
  beforeEach(() => vi.clearAllMocks());

  it("queries the incoming System relationships", () => {
    useRelationshipsListMock.mockReturnValue(result());
    render1(<SystemMembersSection systemId="sys1" />);
    expect(useRelationshipsListMock).toHaveBeenCalledWith(
      expect.objectContaining({ entityKind: "system", entityId: "sys1", direction: "incoming" }),
    );
  });

  it("lists member components with a kind badge + link (row header present)", () => {
    useRelationshipsListMock.mockReturnValue(result({ items: [edge("application", "a1", "Billing App"), edge("service", "s1", "Ledger Svc")] }));
    render1(<SystemMembersSection systemId="sys1" />);
    expect(screen.getByRole("link", { name: "Billing App" })).toHaveAttribute("href", "/catalog/applications/a1");
    expect(screen.getByRole("link", { name: "Ledger Svc" })).toHaveAttribute("href", "/catalog/services/s1");
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0); // ADR-0084
  });

  it("filters out non-PartOf edges (read-path drift tolerance)", () => {
    useRelationshipsListMock.mockReturnValue(result({ items: [edge("application", "a1", "Billing App"), edge("service", "s2", "Rogue", "dependsOn")] }));
    render1(<SystemMembersSection systemId="sys1" />);
    expect(screen.getByText("Billing App")).toBeInTheDocument();
    expect(screen.queryByText("Rogue")).not.toBeInTheDocument();
  });

  it("shows an empty state when nothing is assigned", () => {
    useRelationshipsListMock.mockReturnValue(result({ items: [] }));
    render1(<SystemMembersSection systemId="sys1" />);
    expect(screen.getByText("No components assigned yet.")).toBeInTheDocument();
  });

  it("shows a loading skeleton (with a row header)", () => {
    useRelationshipsListMock.mockReturnValue(result({ isLoading: true }));
    render1(<SystemMembersSection systemId="sys1" />);
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0); // ADR-0084 loading branch
  });

  it("shows an error line on failure", () => {
    useRelationshipsListMock.mockReturnValue(result({ isError: true }));
    render1(<SystemMembersSection systemId="sys1" />);
    expect(screen.getByText(/Couldn.t load members/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/SystemMembersSection.test.tsx`
Expected: FAIL — cannot resolve `../SystemMembersSection`.

- [ ] **Step 3: Write the component**

```tsx
// web/src/features/catalog/components/SystemMembersSection.tsx
import { Link } from "react-router-dom";
import { Badge } from "@/components/base/badges/badges";
import { Table } from "@/components/application/table/table";
import { TableSkeleton, TablePager } from "@/components/application/data-table/data-table";
import { useRelationshipsList } from "@/features/catalog/api/relationships";
import { entityDetailPath, ENTITY_KIND_LABEL } from "@/features/catalog/relationships/graphModel";
import { isRelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

interface Props {
  systemId: string;
}

// Members are the components PARTOF this System — the SOURCE side of every incoming
// edge. Write-time rules (RelationshipTypeRules.IsAllowedPair) restrict incoming-to-System
// edges to PartOf, but the READ path (ListRelationshipsForEntityHandler) applies no type
// filter — so we filter to `partOf` client-side to stay drift-tolerant against backfills /
// direct DB writes (ADR-0111 amendment hedge; cf. e2e/tests/relationship-drift.spec.ts).
export function SystemMembersSection({ systemId }: Props) {
  const members = useRelationshipsList({ entityKind: "system", entityId: systemId, direction: "incoming" });
  const rows = members.items.filter((r) => r.type === "partOf");

  return (
    <section className="space-y-2" aria-label="Members">
      <h3 className="text-sm font-semibold text-primary">Members</h3>
      {members.isLoading ? (
        <Table aria-label="Members">
          <Table.Header>
            <Table.Head id="entity" isRowHeader>Component</Table.Head>
            <Table.Head id="kind">Kind</Table.Head>
          </Table.Header>
          <TableSkeleton rows={3} cells={2} />
        </Table>
      ) : members.isError ? (
        <p className="text-sm text-error-primary">Couldn&apos;t load members.</p>
      ) : rows.length === 0 ? (
        <p className="text-sm italic text-tertiary">No components assigned yet.</p>
      ) : (
        <>
          <Table aria-label="Members">
            <Table.Header>
              <Table.Head id="entity" isRowHeader>Component</Table.Head>
              <Table.Head id="kind">Kind</Table.Head>
            </Table.Header>
            <Table.Body>
              {rows.map((r) => {
                const m = r.source;
                return (
                  <Table.Row key={r.id} id={r.id}>
                    <Table.Cell>
                      {isRelationshipKind(m.kind) ? (
                        <Link to={entityDetailPath(m.kind, m.id)} className="text-primary hover:underline">
                          {m.displayName}
                        </Link>
                      ) : (
                        <span className="text-primary">{m.displayName}</span>
                      )}
                    </Table.Cell>
                    <Table.Cell>
                      <Badge type="pill-color" size="sm" color="gray">
                        {ENTITY_KIND_LABEL[m.kind] ?? m.kind}
                      </Badge>
                    </Table.Cell>
                  </Table.Row>
                );
              })}
            </Table.Body>
          </Table>
          <TablePager hasPrev={members.hasPrev} hasNext={members.hasNext} onPrev={members.goPrev} onNext={members.goNext} pageSize={rows.length} />
        </>
      )}
    </section>
  );
}
```

> **Defensive-filter note (review S1+S2):** `isRelationshipKind` guards the down-cast (member `kind` is a 4-member generated enum incl. `"system"`; `RelationshipKind` is 3-member) — no blind `as` cast, no broken `/catalog/undefined/{id}` link if drift ever surfaces a non-app/service source. The `r.type === "partOf"` filter tolerates read-path edges the write rules wouldn't have allowed.

- [ ] **Step 4: Run test to verify it passes**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/SystemMembersSection.test.tsx`
Expected: PASS (6 tests). If `entityDetailPath`/`isRelationshipKind` signatures differ, confirm against `RelationshipsSection.tsx` + `relationshipTypeRules.ts`.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/SystemMembersSection.tsx web/src/features/catalog/components/__tests__/SystemMembersSection.test.tsx
git commit -m "feat(web): read-only SystemMembersSection"
```

---

### Task 7: `SystemsListPage`

**Files:**
- Create: `web/src/features/catalog/pages/SystemsListPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/SystemsListPage.test.tsx`

**Interfaces:**
- Consumes: `useSystemsList` (Task 3), `SystemsTable` (Task 4), `RegisterSystemDialog` (Task 5), `useListUrlState`, `useListFilters`, `FilterBar`, `useTeamsList`, `usePermissions`, `KartovaPermissions`.
- Produces: `SystemsListPage()`.

- [ ] **Step 1: Write the failing test**

> **Harness note (review B1/B3):** no MSW, no ambient OrgAdmin. Mirror `ServicesListPage.test.tsx:1-58` exactly — `vi.mock` `react-oidc-context`, `usePermissions` (per-test `setPerms`), `useSystemsList`, and `useTeamsList`; assert URL→query threading by inspecting the `useSystemsList` mock's call args.

```tsx
// web/src/features/catalog/pages/__tests__/SystemsListPage.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

vi.mock("react-oidc-context", () => ({
  useAuth: () => ({ isAuthenticated: true, user: { access_token: "t", profile: { sub: "u", name: "Alice", email: "a@x", tenant_id: "t" } } }),
}));

const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({ usePermissions: () => usePermissionsMock() }));

const useSystemsListMock = vi.fn();
vi.mock("@/features/catalog/api/systems", () => ({
  useSystemsList: (...a: unknown[]) => useSystemsListMock(...a),
  useRegisterSystem: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => useTeamsListMock() }));

import { SystemsListPage } from "../SystemsListPage";
import { KartovaPermissions } from "@/shared/auth/permissions";

const TEAM_ID = "00000000-0000-0000-0000-000000000099";
function stubList(over: Record<string, unknown> = {}) {
  return { items: [], isLoading: false, isFetching: false, isError: false, error: null, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn(), ...over };
}
function setPerms(perms: string[]) {
  usePermissionsMock.mockReturnValue({ role: "t", hasPermission: (p: string) => perms.includes(p), isLoading: false });
}
function renderPage(path = "/catalog/systems") {
  return render(<MemoryRouter initialEntries={[path]}><SystemsListPage /></MemoryRouter>);
}

describe("SystemsListPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useSystemsListMock.mockReturnValue(stubList());
    useTeamsListMock.mockReturnValue(stubList({ items: [{ id: TEAM_ID, displayName: "Platform" }] }));
  });

  it("renders the Systems heading", () => {
    setPerms([]);
    renderPage();
    expect(screen.getByRole("heading", { name: /systems/i })).toBeInTheDocument();
  });

  it("shows Register System for a user with the register permission", () => {
    setPerms([KartovaPermissions.CatalogSystemsRegister]);
    renderPage();
    expect(screen.getByRole("button", { name: /register system/i })).toBeInTheDocument();
  });

  it("hides Register System for a user without the permission", () => {
    setPerms([]);
    renderPage();
    expect(screen.queryByRole("button", { name: /register system/i })).toBeNull();
  });

  it("defaults sort to displayName asc (sends it to useSystemsList)", () => {
    setPerms([]);
    renderPage();
    expect(useSystemsListMock).toHaveBeenCalledWith(expect.objectContaining({ sortBy: "displayName", sortOrder: "asc" }));
  });

  it("threads displayNameContains from the URL to useSystemsList", () => {
    setPerms([]);
    renderPage("/catalog/systems?displayNameContains=pay");
    expect(useSystemsListMock).toHaveBeenCalledWith(expect.objectContaining({ displayNameContains: "pay" }));
  });

  it("threads teamId from the URL to useSystemsList", () => {
    setPerms([]);
    renderPage(`/catalog/systems?teamId=${TEAM_ID}`);
    expect(useSystemsListMock).toHaveBeenCalledWith(expect.objectContaining({ teamId: [TEAM_ID] }));
  });

  it("shows a filtered empty-state when a search yields no rows", async () => {
    setPerms([]);
    renderPage("/catalog/systems?displayNameContains=zzz");
    expect(await screen.findByText(/no systems match your filters/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/SystemsListPage.test.tsx`
Expected: FAIL — cannot resolve `../SystemsListPage`.

- [ ] **Step 3: Write the page** (mirrors `ServicesListPage`, no health filter)

```tsx
// web/src/features/catalog/pages/SystemsListPage.tsx
import { useMemo, useState, useEffect } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { FilterBar } from "@/components/application/filter-bar/FilterBar";
import { useListFilters } from "@/lib/list/filters/useListFilters";
import type { FilterSpec } from "@/lib/list/filters/types";
import { useSystemsList } from "@/features/catalog/api/systems";
import { useTeamsList } from "@/features/teams/api/teams";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { SystemsTable } from "@/features/catalog/components/SystemsTable";
import { RegisterSystemDialog } from "@/features/catalog/components/RegisterSystemDialog";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

const ALLOWED_SORT_FIELDS = ["createdAt", "displayName"] as const;
const TEXT_FILTERS = ["displayNameContains"] as const;
const MULTI_FILTERS = ["teamId"] as const;

export function SystemsListPage() {
  const urlState = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
    textFilters: TEXT_FILTERS,
    multiFilters: MULTI_FILTERS,
  });

  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

  const filterSpecs: FilterSpec[] = useMemo(
    () => [
      { key: "displayNameContains", type: "text", label: "Search systems", placeholder: "Search by name…" },
      {
        key: "teamId",
        type: "multi-select",
        label: "Steward team",
        placeholder: "All teams",
        options: (teamsList.items ?? []).map((t) => ({ label: t.displayName, value: t.id })),
      },
    ],
    [teamsList.items],
  );
  const filters = useListFilters(filterSpecs, urlState);

  const list = useSystemsList({
    sortBy: urlState.sortBy,
    sortOrder: urlState.sortOrder,
    displayNameContains: filters.textValues.displayNameContains,
    teamId: filters.multiValues.teamId,
  });

  const [dialogOpen, setDialogOpen] = useState(false);
  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canRegister = !permissionsLoading && hasPermission(KartovaPermissions.CatalogSystemsRegister);

  useEffect(() => {
    if (list.isError) console.error("SystemsListPage list error", list.error);
  }, [list.isError, list.error]);
  useEffect(() => {
    if (teamsList.isError) console.error("SystemsListPage teams error", teamsList.error);
  }, [teamsList.isError, teamsList.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Systems</h2>
        {canRegister && (
          <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
            Register System
          </Button>
        )}
      </div>

      <FilterBar specs={filterSpecs} urlState={urlState} />

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load systems</p>
            <p className="text-sm text-tertiary">Try refreshing or resetting the list.</p>
            <Button size="sm" onClick={() => list.reset()}>Reset</Button>
          </CardContent>
        </Card>
      ) : !list.isLoading && list.items.length === 0 && filters.isActive ? (
        <Card className="mx-auto max-w-md text-center">
          <CardContent className="space-y-2 p-8">
            <p className="text-base font-medium text-primary">No systems match your filters</p>
            <p className="text-sm text-tertiary">Try a different name or clear the filters.</p>
          </CardContent>
        </Card>
      ) : (
        <SystemsTable
          list={list}
          sortBy={urlState.sortBy}
          sortOrder={urlState.sortOrder}
          onSortChange={urlState.setSort}
          teamNameById={teamNameById}
        />
      )}

      {canRegister && <RegisterSystemDialog open={dialogOpen} onOpenChange={setDialogOpen} />}
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/SystemsListPage.test.tsx`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/pages/SystemsListPage.tsx web/src/features/catalog/pages/__tests__/SystemsListPage.test.tsx
git commit -m "feat(web): SystemsListPage"
```

---

### Task 8: `SystemDetailPage` (tabbed: Overview · Members)

**Files:**
- Create: `web/src/features/catalog/pages/SystemDetailPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/SystemDetailPage.test.tsx`

**Interfaces:**
- Consumes: `useSystem` (Task 3), `SystemMembersSection` (Task 6), `DetailTabs`, `useTeamsList`, `CreatedByLink`, `Card`/`Skeleton`.
- Produces: `SystemDetailPage()` at route `/catalog/systems/:id`.

- [ ] **Step 1: Write the failing test**

> **Harness note (review B1):** mirror `ServiceDetailPage.test.tsx` — `vi.mock` `useTeamsList` + `@/features/catalog/api/relationships` (the Members section's data source); use a real `QueryClient` with `setQueryData(systemKeys.detail(id), …)` for the synchronous tab tests, and `vi.spyOn(clientModule,"apiClient","get")` for the async header / not-found / loading tests.

```tsx
// web/src/features/catalog/pages/__tests__/SystemDetailPage.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { systemKeys } from "@/features/catalog/api/systems";
import { SystemDetailPage } from "../SystemDetailPage";

vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: () => ({ items: [{ id: "team1", displayName: "Platform Team" }], isLoading: false }),
}));
vi.mock("@/features/catalog/api/relationships", () => ({
  useRelationshipsList: () => ({ items: [], isLoading: false, isError: false, hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn() }),
}));

const sys = { id: "sys1", tenantId: "t1", displayName: "Payments Platform", description: "Money movement", teamId: "team1", createdByUserId: "u1", createdAt: "2026-07-22T00:00:00Z", createdBy: null };

function harness(qc: QueryClient, path: string) {
  return (
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes><Route path="/catalog/systems/:id" element={<SystemDetailPage />} /></Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}
function renderCached(search = "") {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } });
  qc.setQueryData(systemKeys.detail(sys.id), sys);
  return render(harness(qc, `/catalog/systems/${sys.id}${search}`));
}

describe("SystemDetailPage", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("renders Overview fields (default tab) with the description", () => {
    renderCached();
    expect(screen.getByRole("heading", { name: "Payments Platform" })).toBeInTheDocument();
    expect(screen.getByText("Money movement")).toBeInTheDocument();
    expect(screen.getByText("Platform Team")).toBeInTheDocument();
  });

  it("switches to the Members tab and shows the empty state (with a row header once loaded)", async () => {
    renderCached();
    await userEvent.click(screen.getByRole("tab", { name: /Members/i }));
    expect(screen.getByRole("tab", { name: /Members/i })).toHaveAttribute("aria-selected", "true");
    expect(await screen.findByText("No components assigned yet.")).toBeInTheDocument();
  });

  it("renders a skeleton while loading", () => {
    const get = vi.fn().mockReturnValue(new Promise(() => {}));
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { container } = render(harness(qc, "/catalog/systems/sys1"));
    expect(container.querySelectorAll('[data-testid="system-detail-skeleton"]').length).toBeGreaterThan(0);
  });

  it("shows a not-found card on 404", async () => {
    const get = vi.fn().mockResolvedValue({ data: undefined, error: { status: 404, title: "Not Found" } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, "/catalog/systems/missing"));
    await waitFor(() => expect(screen.getByText("System not found")).toBeInTheDocument());
  });
});
```

> The Members table's own `isRowHeader` (asserted directly in Task 6) covers the ADR-0084 rule for the tab-switched surface; this suite confirms the tab actually mounts it.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/SystemDetailPage.test.tsx`
Expected: FAIL — cannot resolve `../SystemDetailPage`.

- [ ] **Step 3: Write the page** (mirrors `ServiceDetailPage` structure)

```tsx
// web/src/features/catalog/pages/SystemDetailPage.tsx
import { useMemo } from "react";
import { Link, useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { DetailTabs } from "@/components/application/tabs/detail-tabs";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import { useSystem } from "@/features/catalog/api/systems";
import { useTeamsList } from "@/features/teams/api/teams";
import { SystemMembersSection } from "@/features/catalog/components/SystemMembersSection";

export function SystemDetailPage() {
  const { id } = useParams<{ id: string }>();
  const query = useSystem(id ?? "");
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

  if (query.isLoading) {
    return (
      <Card data-testid="system-detail-skeleton">
        <CardHeader>
          <Skeleton className="h-7 w-64" />
          <Skeleton className="mt-2 h-4 w-32" />
        </CardHeader>
        <CardContent className="space-y-4">
          <Skeleton className="h-20 w-full" />
          <Skeleton className="h-12 w-2/3" />
        </CardContent>
      </Card>
    );
  }

  if (query.isError || !query.data) {
    return (
      <Card className="mx-auto max-w-md">
        <CardContent className="space-y-2 p-6 text-center">
          <p className="text-base font-medium text-error-primary">System not found</p>
          <p className="text-sm text-tertiary">It may have been deleted, or you may not have access in this tenant.</p>
        </CardContent>
      </Card>
    );
  }

  const sys = query.data;

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-2xl font-semibold text-primary">{sys.displayName}</h2>
      </div>
      <Card>
        <DetailTabs aria-label={sys.displayName}>
          <DetailTabs.Tab id="overview" label="Overview">
            <div className="space-y-6">
              <section>
                <h3 className="text-sm font-medium text-tertiary">Description</h3>
                <p className="mt-1 text-sm text-secondary">
                  {sys.description ? sys.description : <span className="italic">No description</span>}
                </p>
              </section>
              <hr className="border-secondary" />
              <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                <Field label="ID" value={sys.id} mono />
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Steward team</div>
                  <div className="mt-1 text-sm">
                    <Link to={`/teams/${sys.teamId}`} className="text-primary hover:underline">
                      {teamNameById.get(sys.teamId) ?? "View team"}
                    </Link>
                  </div>
                </div>
                <div>
                  <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
                  <div className="mt-1 text-sm"><CreatedByLink user={sys.createdBy} /></div>
                </div>
                <Field label="Created" value={sys.createdAt ? new Date(sys.createdAt).toLocaleString() : "—"} />
              </section>
            </div>
          </DetailTabs.Tab>

          <DetailTabs.Tab id="members" label="Members">
            <SystemMembersSection systemId={sys.id} />
          </DetailTabs.Tab>
        </DetailTabs>
      </Card>
    </div>
  );
}

function Field({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-tertiary">{label}</div>
      <div className={mono ? "mt-1 font-mono text-sm text-primary" : "mt-1 text-sm text-primary"}>{value}</div>
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/SystemDetailPage.test.tsx`
Expected: PASS (4 tests). Confirm the `DetailTabs.Tab` label→`role="tab"` mapping matches `ServiceDetailPage.test.tsx`.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/pages/SystemDetailPage.tsx web/src/features/catalog/pages/__tests__/SystemDetailPage.test.tsx
git commit -m "feat(web): SystemDetailPage (Overview + Members tabs)"
```

---

### Task 9: Wire nav + routes

**Files:**
- Modify: `web/src/app/router.tsx` (after the `/catalog/apis/:id` route, ~line 59)
- Modify: `web/src/components/layout/Sidebar.tsx` (after the APIs nav item, ~line 88; + doc comment ~line 42)
- Test: `web/src/components/layout/__tests__/Sidebar.test.tsx` (extend)

- [ ] **Step 1: Add a failing Sidebar assertion**

In `web/src/components/layout/__tests__/Sidebar.test.tsx`, add to the nav-links test:

```tsx
expect(screen.getByRole("link", { name: "Systems" })).toHaveAttribute("href", "/catalog/systems");
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd web && npx vitest run src/components/layout/__tests__/Sidebar.test.tsx`
Expected: FAIL — no "Systems" link.

- [ ] **Step 3: Add the nav item + routes + imports**

In `web/src/components/layout/Sidebar.tsx`, after the APIs `<li>`:

```tsx
<li>
  <NavItemLink to="/catalog/systems" label="Systems" />
</li>
```

Extend the nav-highlight doc comment (~line 42) to note Systems owns `/catalog/systems` and does not cross-highlight.

In `web/src/app/router.tsx`, add the import and routes:

```tsx
import { SystemsListPage } from "@/features/catalog/pages/SystemsListPage";
import { SystemDetailPage } from "@/features/catalog/pages/SystemDetailPage";
```

```tsx
<Route path="/catalog/systems" element={<SystemsListPage />} />
<Route path="/catalog/systems/:id" element={<SystemDetailPage />} />
```

- [ ] **Step 4: Run to verify it passes + full typecheck**

Run: `cd web && npx vitest run src/components/layout/__tests__/Sidebar.test.tsx`
Expected: PASS.
Run: `cd web && npm run build`
Expected: `tsc -b` + vite build succeed (binding type gate; catches any generated-type or import drift across all tasks).

- [ ] **Step 5: Commit**

```bash
git add web/src/app/router.tsx web/src/components/layout/Sidebar.tsx web/src/components/layout/__tests__/Sidebar.test.tsx
git commit -m "feat(web): wire Systems nav item + routes"
```

---

### Task 10: Registry + checklist docs

**Files:**
- Modify: `docs/design/list-filter-registry.md`
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: UPDATE the existing Systems row in the filter registry**

`docs/design/list-filter-registry.md:34` **already has** a Systems row (added at the S-01 backend slice, status `built (API-only, no <FilterBar> yet)`, note ends "revisit this row when the Systems list screen is built"). Do **not** add a second row — edit the existing one:
- Status: `built (API-only, no <FilterBar> yet)` → **`built`**.
- Column 1 (surface): drop "backend only — **no UI this slice**"; set to `/catalog/systems` (list screen + `<FilterBar>`).
- Note: replace the trailing "No `<FilterBar>`/`useListFilters` wiring this slice … revisit this row when the Systems list screen is built." with "List screen wires `useListUrlState`/`useListFilters`/`<FilterBar>` + `<DataTable>` (slice 2026-07-22). Renders `displayNameContains` text + `teamId` multi-select."
- Keep the existing `createdAt`-range and `memberCount` deferral notes unchanged.

- [ ] **Step 2: Update the checklist**

In `docs/product/CHECKLIST.md`, annotate `E-03.F-03.S-01` that the UI surface shipped (list `/catalog/systems` + tabbed detail with read-only Members; assignment UI deferred), referencing this slice's verification folder.

- [ ] **Step 3: Commit**

```bash
git add docs/design/list-filter-registry.md docs/product/CHECKLIST.md
git commit -m "docs(catalog): Systems list-filter registry row + checklist update"
```

---

## DoD

The eleven CLAUDE.md gates apply as written. Frontend-only ⇒ **gate 5 (real-seam) and gate 6 (mutation) are N/A** with the reasons in the design §6 (backend seams already covered by S-01; no Domain/Application logic changed). Gate 4 (`images`/web container build) still runs. After gate 9 fixes, re-run `npm run build` + full Vitest suite on the final commit, then gate 10 (browser, ADR-0084 — cold-start, register a System, open detail, switch to Members; screenshot + 0 console errors + a manual keyboard/screen-reader pass over the new nav item, dialog, and tab switch) and gate 11 (CI green). Maintain the DoD ledger + `gate-findings.yaml` under `docs/superpowers/verification/2026-07-22-catalog-system-ui-surface/`.

**E2E-impact trigger (CLAUDE.md) — N/A (stated, not silent):** the new `/catalog/systems` nav item + routes are purely additive; no existing `e2e/` spec asserts the Sidebar link set or traverses `/catalog/systems`, and `detail-tabs.spec.ts` / `relationship-drift.spec.ts` are App/Service/API-scoped (verified during review). No `e2e/` spec update required this slice.

## Self-Review

- **Spec coverage:** list page (T7) ✓ · register dialog (T5) ✓ · detail Overview+Members tabs (T8) ✓ · read-only members (T6) ✓ · data layer (T3) ✓ · schema (T2) ✓ · nav+routes (T9) ✓ · codegen (T1) ✓ · surface table → registry (T10) ✓ · no new permission (reused, T7) ✓ · deferrals (assignment, graph node, hierarchy) left untouched ✓.
- **Placeholder scan:** none — every code step carries full source.
- **Type consistency:** `SystemResponse`/`SystemsListParams`/`RegisterSystemInput` defined in T2/T3 and consumed unchanged in T4–T8; `SortField = "createdAt"|"displayName"` consistent T4/T7; `SystemMembersSection({ systemId })` prop name consistent T6/T8; `entityDetailPath(kind as RelationshipKind, id)` cast matches `RelationshipsSection`.
