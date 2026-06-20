# Service UI Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the catalog `Service` entity a web surface — list, register (with a 0..50 endpoints editor), and a read-only detail page — by wiring the SPA to the existing S-01 backend endpoints.

**Architecture:** Frontend-only React/TypeScript feature under `web/src/features/catalog`, mirroring the existing Application UI 1:1 (`applications.ts`, `CatalogListPage`, `ApplicationDetailPage`, `ApplicationsTable`, `RegisterApplicationDialog`). Data flows through the generated openapi-fetch client + TanStack Query; list paging via `useCursorList`/`useListUrlState`; forms via react-hook-form + zod. No backend code changes — the `/api/v1/catalog/services` endpoints and their real-seam integration tests already exist on master from E-02.F-02.S-01.

**Tech Stack:** React 19, TypeScript, TanStack Query v5, react-hook-form + zod, react-aria-components (Untitled UI), react-router-dom, openapi-fetch + openapi-typescript codegen, Vitest + Testing Library + user-event, sonner (toasts).

## Global Constraints

- **Frontend-only.** No `.cs` / backend changes. The three `/catalog/services` endpoints, the `catalog.services.register` permission, and `service.registered` audit are already on master (commit `4eab9ff`).
- **Branch:** `feat/catalog-service-ui-surface` (already created and checked out; the spec + sort-default commits are on it).
- **Build gate:** `npm run build` (`tsc -b && vite build`) MUST pass with 0 errors; `npm run lint` clean. The web image compiles TS, so the regenerated `src/generated/openapi.ts` MUST be committed.
- **Wire enum casing** (`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`): protocols = `rest`, `grpc`, `graphQL`, `webSocket`, `tcp`, `other`; health = `unknown`, `healthy`, `degraded`, `unhealthy`. Always derive the union *type* from `@/generated/openapi` and type label maps as `Record<Union, …>` so `tsc` catches any casing drift.
- **Default list sort:** `displayName` / `desc` (general convention, user pref 2026-06-20). Sort allowlist: `createdAt`, `displayName`.
- **Mirror precedent exactly** — when in doubt, copy the Application equivalent's structure, imports, and prop shapes.
- **Tests:** Vitest (`npm test`). Each new unit ships ≥1 happy + ≥1 negative case. Test harness pattern: mock `apiClient` via `vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET, POST })`, render inside `QueryClientProvider` + `MemoryRouter`.
- **Commits:** frequent, one per task minimum. Every commit message ends with the trailer:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
  (On Windows, author multi-line messages via multiple `-m` flags in PowerShell.)
- **Reference spec:** `docs/superpowers/specs/2026-06-20-catalog-service-ui-surface-design.md`.

---

### Task 1: Regenerate the OpenAPI client with Service types

**Files:**
- Modify (generated): `web/src/generated/openapi.ts`
- Modify (generated): `web/openapi-snapshot.json`

**Interfaces:**
- Produces: `components["schemas"]["ServiceResponse"]`, `["RegisterServiceRequest"]`, `["ServiceEndpointDto"]`, and `operations["ListServices"]` / `["GetServiceById"]` in the generated client — consumed by every later task.

**Context:** `npm run codegen` (in `web/`) fetches the live OpenAPI doc from `${VITE_API_BASE_URL:-http://localhost:8080}/openapi/v1.json`, writes `openapi-snapshot.json`, and regenerates `src/generated/openapi.ts`. The API must be running. If Docker / the dev stack is unavailable in-session, STOP and flag this task *pending user verification* — do not hand-edit the generated file.

- [ ] **Step 1: Start the backend API**

From the repo root, bring up the stack (Postgres + API):
```bash
docker compose up -d postgres api
```
Wait until `curl -s http://localhost:8080/openapi/v1.json | head -c 80` returns JSON (not a connection error).

- [ ] **Step 2: Run codegen**

```bash
cd web && npm run codegen
```
Expected stdout: `codegen: fetched live OpenAPI from http://localhost:8080/openapi/v1.json` then `codegen: wrote …/src/generated/openapi.ts`.

- [ ] **Step 3: Verify Service types are present**

```bash
grep -c "ServiceResponse\|RegisterServiceRequest\|ListServices\|GetServiceById" web/src/generated/openapi.ts
```
Expected: a non-zero count (≥ 4). Also confirm the enum casing matches the Global Constraints:
```bash
grep -oE '"(rest|grpc|graphQL|webSocket|tcp|other)"' web/src/generated/openapi.ts | sort -u
grep -oE '"(unknown|healthy|degraded|unhealthy)"' web/src/generated/openapi.ts | sort -u
```
Expected: all six protocol literals and all four health literals appear. **If any differ in casing, update the Global Constraints + the literal arrays in Tasks 2–3 to match the generated output (the generated client is the source of truth).**

- [ ] **Step 4: Confirm the client still typechecks**

```bash
cd web && npm run typecheck
```
Expected: exit 0, no errors (no consumers reference the new types yet).

- [ ] **Step 5: Commit**

```bash
git add web/src/generated/openapi.ts web/openapi-snapshot.json
git commit -m "chore(web): regenerate OpenAPI client with Service types (E-02.F-02.S-02)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: `registerService` zod schema

**Files:**
- Create: `web/src/features/catalog/schemas/registerService.ts`
- Test: `web/src/features/catalog/schemas/__tests__/registerService.test.ts`

**Interfaces:**
- Consumes: `components["schemas"]["ServiceEndpointDto"]["protocol"]` (Task 1).
- Produces:
  - `PROTOCOLS: readonly ["rest","grpc","graphQL","webSocket","tcp","other"]`
  - `PROTOCOL_LABEL: Record<ProtocolValue, string>`
  - `endpointSchema`, `registerServiceSchema` (zod)
  - types `RegisterServiceInput` (`{ displayName: string; description: string; teamId: string; endpoints: EndpointInput[] }`), `EndpointInput` (`{ url: string; protocol: ProtocolValue }`), `ProtocolValue`.

- [ ] **Step 1: Write the failing test**

`web/src/features/catalog/schemas/__tests__/registerService.test.ts`:
```ts
import { describe, it, expect } from "vitest";
import { registerServiceSchema, endpointSchema, PROTOCOLS, PROTOCOL_LABEL } from "../registerService";

const validTeamId = "00000000-0000-0000-0000-000000000010";

describe("registerServiceSchema", () => {
  it("accepts a valid service with zero endpoints", () => {
    const r = registerServiceSchema.safeParse({
      displayName: "Orders", description: "Order service", teamId: validTeamId, endpoints: [],
    });
    expect(r.success).toBe(true);
  });

  it("accepts a valid service with endpoints", () => {
    const r = registerServiceSchema.safeParse({
      displayName: "Orders", description: "Order service", teamId: validTeamId,
      endpoints: [{ url: "https://api.example.com/v1", protocol: "rest" }],
    });
    expect(r.success).toBe(true);
  });

  it("rejects an empty display name", () => {
    const r = registerServiceSchema.safeParse({ displayName: "", description: "d", teamId: validTeamId, endpoints: [] });
    expect(r.success).toBe(false);
  });

  it("rejects a non-uuid team id", () => {
    const r = registerServiceSchema.safeParse({ displayName: "Orders", description: "d", teamId: "not-a-uuid", endpoints: [] });
    expect(r.success).toBe(false);
  });

  it("rejects more than 50 endpoints", () => {
    const endpoints = Array.from({ length: 51 }, () => ({ url: "https://x.example.com", protocol: "rest" as const }));
    const r = registerServiceSchema.safeParse({ displayName: "Orders", description: "d", teamId: validTeamId, endpoints });
    expect(r.success).toBe(false);
  });
});

describe("endpointSchema", () => {
  it("accepts an absolute https URL", () => {
    expect(endpointSchema.safeParse({ url: "https://api.example.com/v1", protocol: "rest" }).success).toBe(true);
  });
  it("accepts a grpc/tcp/ws scheme", () => {
    expect(endpointSchema.safeParse({ url: "grpc://svc.internal:50051", protocol: "grpc" }).success).toBe(true);
  });
  it("rejects an empty url", () => {
    expect(endpointSchema.safeParse({ url: "", protocol: "rest" }).success).toBe(false);
  });
  it("rejects a relative url", () => {
    expect(endpointSchema.safeParse({ url: "/v1/orders", protocol: "rest" }).success).toBe(false);
  });
  it("rejects an unknown protocol", () => {
    expect(endpointSchema.safeParse({ url: "https://x.example.com", protocol: "soap" }).success).toBe(false);
  });
});

describe("PROTOCOLS / PROTOCOL_LABEL", () => {
  it("exposes all six protocols with labels", () => {
    expect(PROTOCOLS).toHaveLength(6);
    for (const p of PROTOCOLS) expect(typeof PROTOCOL_LABEL[p]).toBe("string");
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd web && npm test -- src/features/catalog/schemas/__tests__/registerService.test.ts
```
Expected: FAIL — `Cannot find module '../registerService'`.

- [ ] **Step 3: Write the schema**

`web/src/features/catalog/schemas/registerService.ts`:
```ts
import { z } from "zod";
import type { components } from "@/generated/openapi";

/** Wire-shape protocol union, sourced from the OpenAPI codegen (single source of truth). */
type ProtocolValue = components["schemas"]["ServiceEndpointDto"]["protocol"];

/**
 * Protocol values in wire form (camelCase per JsonStringEnumConverter +
 * JsonNamingPolicy.CamelCase). The `satisfies readonly ProtocolValue[]` clause
 * fails the build if any literal's casing drifts from the generated client.
 */
export const PROTOCOLS = ["rest", "grpc", "graphQL", "webSocket", "tcp", "other"] as const satisfies readonly ProtocolValue[];

/** Human-friendly labels for the protocol <select> and the detail table.
 *  Typed as a total Record so a missing/extra key fails `tsc`. */
export const PROTOCOL_LABEL: Record<ProtocolValue, string> = {
  rest: "REST",
  grpc: "gRPC",
  graphQL: "GraphQL",
  webSocket: "WebSocket",
  tcp: "TCP",
  other: "Other",
};

function isAbsoluteUrl(value: string): boolean {
  try {
    const u = new URL(value);
    return !!u.protocol && !!u.host;
  } catch {
    return false;
  }
}

export const endpointSchema = z.object({
  url: z
    .string()
    .min(1, "Endpoint URL must not be empty")
    .max(2048, "Endpoint URL must be at most 2048 characters")
    .refine(isAbsoluteUrl, "Endpoint URL must be an absolute URL (include a scheme and host)"),
  protocol: z.enum(PROTOCOLS),
});

export const registerServiceSchema = z.object({
  displayName: z.string().min(1, "Display Name must not be empty").max(128, "Display Name must be at most 128 characters"),
  description: z.string().min(1, "Description is required").max(4096, "Description must be at most 4096 characters"),
  teamId: z.string().uuid("Team is required"),
  endpoints: z.array(endpointSchema).max(50, "A service may have at most 50 endpoints"),
});

export type RegisterServiceInput = z.infer<typeof registerServiceSchema>;
export type EndpointInput = z.infer<typeof endpointSchema>;
export type { ProtocolValue };
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd web && npm test -- src/features/catalog/schemas/__tests__/registerService.test.ts && npm run typecheck
```
Expected: all tests PASS; typecheck exit 0. (If `z.enum(PROTOCOLS)` complains about a readonly tuple, change to `z.enum([...PROTOCOLS] as [ProtocolValue, ...ProtocolValue[]])`.)

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/schemas/registerService.ts web/src/features/catalog/schemas/__tests__/registerService.test.ts
git commit -m "feat(web): registerService zod schema + endpoints validation (E-02.F-02.S-02)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: `health.ts` map + `HealthBadge` component

**Files:**
- Create: `web/src/features/catalog/health.ts`
- Create: `web/src/features/catalog/components/HealthBadge.tsx`
- Test: `web/src/features/catalog/components/__tests__/HealthBadge.test.tsx`

**Interfaces:**
- Consumes: `components["schemas"]["ServiceResponse"]["health"]` (Task 1).
- Produces: type `Health`; `healthLabel(h: Health): string`; `healthColor(h: Health): "gray"|"success"|"warning"|"error"`; component `HealthBadge({ health: Health; size?: "sm"|"md" })`.

- [ ] **Step 1: Write the failing test**

`web/src/features/catalog/components/__tests__/HealthBadge.test.tsx`:
```tsx
import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { HealthBadge } from "../HealthBadge";
import { healthLabel } from "@/features/catalog/health";

describe("HealthBadge", () => {
  it("renders the Unknown label for the default health", () => {
    render(<HealthBadge health="unknown" />);
    expect(screen.getByText("Unknown")).toBeInTheDocument();
  });

  it("renders the Healthy label", () => {
    render(<HealthBadge health="healthy" />);
    expect(screen.getByText("Healthy")).toBeInTheDocument();
  });
});

describe("healthLabel", () => {
  it("maps each enum value to a human label", () => {
    expect(healthLabel("unknown")).toBe("Unknown");
    expect(healthLabel("degraded")).toBe("Degraded");
    expect(healthLabel("unhealthy")).toBe("Unhealthy");
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd web && npm test -- src/features/catalog/components/__tests__/HealthBadge.test.tsx
```
Expected: FAIL — `Cannot find module '../HealthBadge'`.

- [ ] **Step 3: Write `health.ts` then `HealthBadge.tsx`**

`web/src/features/catalog/health.ts`:
```ts
import type { components } from "@/generated/openapi";

/** Wire-shape health string, sourced from the OpenAPI codegen. */
export type Health = components["schemas"]["ServiceResponse"]["health"];

type HealthBadgeColor = "gray" | "success" | "warning" | "error";

// Typed as total Records so a missing/extra key (e.g. a casing drift) fails `tsc`.
const LABEL: Record<Health, string> = {
  unknown: "Unknown",
  healthy: "Healthy",
  degraded: "Degraded",
  unhealthy: "Unhealthy",
};

const COLOR: Record<Health, HealthBadgeColor> = {
  unknown: "gray",
  healthy: "success",
  degraded: "warning",
  unhealthy: "error",
};

export function healthLabel(health: Health): string {
  return LABEL[health];
}

export function healthColor(health: Health): HealthBadgeColor {
  return COLOR[health];
}
```

`web/src/features/catalog/components/HealthBadge.tsx`:
```tsx
import { Badge } from "@/components/base/badges/badges";
import { healthColor, healthLabel, type Health } from "@/features/catalog/health";

export interface HealthBadgeProps {
  health: Health;
  size?: "sm" | "md";
}

export function HealthBadge({ health, size = "sm" }: HealthBadgeProps) {
  return (
    <Badge color={healthColor(health)} type="pill-color" size={size}>
      {healthLabel(health)}
    </Badge>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd web && npm test -- src/features/catalog/components/__tests__/HealthBadge.test.tsx && npm run typecheck
```
Expected: PASS; typecheck exit 0. (A `tsc` error on the `Record<Health, …>` literals means the wire casing differs from the constants — fix the keys to match the generated `Health` union.)

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/health.ts web/src/features/catalog/components/HealthBadge.tsx web/src/features/catalog/components/__tests__/HealthBadge.test.tsx
git commit -m "feat(web): HealthBadge + health label/color map (E-02.F-02.S-02)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: `api/services.ts` query + mutation hooks

**Files:**
- Create: `web/src/features/catalog/api/services.ts`
- Test: `web/src/features/catalog/api/__tests__/services.test.tsx`

**Interfaces:**
- Consumes: `apiClient` (`./client`), `useCursorList`, `unwrapData`/`throwWithStatus`, `RegisterServiceInput` (Task 2), generated `components`/`operations` (Task 1).
- Produces:
  - `serviceKeys` (`.all`, `.list(params?)`, `.detail(id)`)
  - `useServicesList(params: { sortBy; sortOrder; limit? })` → `CursorListResult<ServiceResponse>`
  - `useService(id: string)` → TanStack `useQuery` result of `ServiceResponse`
  - `useRegisterService()` → mutation; `mutateAsync(input: RegisterServiceInput)` → `ServiceResponse`
  - type `ServiceResponse`

- [ ] **Step 1: Write the failing test**

`web/src/features/catalog/api/__tests__/services.test.tsx`:
```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

import * as clientModule from "@/features/catalog/api/client";
import { useService, useServicesList, useRegisterService } from "../services";

function wrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

describe("services api", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("useService GETs by path id", async () => {
    const get = vi.fn().mockResolvedValue({
      data: { id: "svc-1", tenantId: "t", displayName: "Orders", description: "d", teamId: "tm",
        createdByUserId: "u", createdAt: "2026-01-01T00:00:00Z", health: "unknown", endpoints: [], version: "v1" },
      error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);

    const { result } = renderHook(() => useService("svc-1"), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.data?.displayName).toBe("Orders"));
    expect(get).toHaveBeenCalledWith("/api/v1/catalog/services/{id}", { params: { path: { id: "svc-1" } } });
  });

  it("useServicesList GETs the list with sort params", async () => {
    const get = vi.fn().mockResolvedValue({
      data: { items: [], nextCursor: null, prevCursor: null }, error: undefined,
    });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);

    const { result } = renderHook(
      () => useServicesList({ sortBy: "displayName", sortOrder: "desc" }),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(result.current.isLoading).toBe(false));
    expect(get).toHaveBeenCalledWith("/api/v1/catalog/services", expect.objectContaining({
      params: { query: expect.objectContaining({ sortBy: "displayName", sortOrder: "desc" }) },
    }));
  });

  it("useRegisterService POSTs the body", async () => {
    const post = vi.fn().mockResolvedValue({ data: { id: "svc-1" }, error: undefined, response: { status: 201 } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: vi.fn(), POST: post } as never);

    const { result } = renderHook(() => useRegisterService(), { wrapper: wrapper() });
    await result.current.mutateAsync({ displayName: "Orders", description: "d", teamId: "tm", endpoints: [] });
    expect(post).toHaveBeenCalledWith("/api/v1/catalog/services", {
      body: { displayName: "Orders", description: "d", teamId: "tm", endpoints: [] },
    });
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd web && npm test -- src/features/catalog/api/__tests__/services.test.tsx
```
Expected: FAIL — `Cannot find module '../services'`.

- [ ] **Step 3: Write the hooks**

`web/src/features/catalog/api/services.ts`:
```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { RegisterServiceInput } from "../schemas/registerService";
import type { components, operations } from "@/generated/openapi";

type ServiceResponse = components["schemas"]["ServiceResponse"];
type ListServicesQuery = NonNullable<operations["ListServices"]["parameters"]["query"]>;

type ServicesListParams = {
  sortBy: NonNullable<ListServicesQuery["sortBy"]>;     // "createdAt" | "displayName"
  sortOrder: NonNullable<ListServicesQuery["sortOrder"]>;
  limit?: number;
};

export const serviceKeys = {
  all: ["services"] as const,
  list: (params?: ServicesListParams) =>
    params
      ? ([...serviceKeys.all, "list", params] as const)
      : ([...serviceKeys.all, "list"] as const),
  detail: (id: string) => [...serviceKeys.all, "detail", id] as const,
};

export function useServicesList(params: ServicesListParams) {
  return useCursorList<ServiceResponse>({
    queryKey: serviceKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/services", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            limit: params.limit ?? 50,
            cursor,
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useService(id: string) {
  return useQuery({
    queryKey: serviceKeys.detail(id),
    enabled: id !== "",
    queryFn: async () => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/services/{id}", {
        params: { path: { id } },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useRegisterService() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: RegisterServiceInput) => {
      const { data, error, response } = await apiClient.POST("/api/v1/catalog/services", { body: input });
      if (error) throwWithStatus(error, response);
      return unwrapData(data);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: serviceKeys.all });
    },
  });
}

export type { ServiceResponse };
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd web && npm test -- src/features/catalog/api/__tests__/services.test.tsx && npm run typecheck
```
Expected: PASS; typecheck exit 0.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/api/services.ts web/src/features/catalog/api/__tests__/services.test.tsx
git commit -m "feat(web): services API hooks (list/get/register) (E-02.F-02.S-02)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: `EndpointsEditor` component

**Files:**
- Create: `web/src/features/catalog/components/EndpointsEditor.tsx`
- Test: `web/src/features/catalog/components/__tests__/EndpointsEditor.test.tsx`

**Interfaces:**
- Consumes: `PROTOCOLS`, `PROTOCOL_LABEL`, `EndpointInput` (Task 2); `Button`, `Input`.
- Produces: `EndpointsEditor({ value: EndpointInput[]; onChange: (next: EndpointInput[]) => void; disabled?: boolean; errors?: (string|undefined)[] })`. Controlled — owns no internal endpoint state. "Add endpoint" appends `{ url:"", protocol:"rest" }`; disabled at 50; per-row Remove; per-row URL error slot via `errors[index]`.

- [ ] **Step 1: Write the failing test**

`web/src/features/catalog/components/__tests__/EndpointsEditor.test.tsx`:
```tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { EndpointsEditor } from "../EndpointsEditor";
import type { EndpointInput } from "@/features/catalog/schemas/registerService";

// Stateful wrapper so add/remove mutate real state in the test.
function Harness({ initial = [] as EndpointInput[] }) {
  const [value, setValue] = useState<EndpointInput[]>(initial);
  return <EndpointsEditor value={value} onChange={setValue} />;
}

describe("EndpointsEditor", () => {
  it("adds an endpoint row when 'Add endpoint' is clicked", async () => {
    render(<Harness />);
    expect(screen.queryByLabelText(/endpoint 1 url/i)).toBeNull();
    await userEvent.click(screen.getByRole("button", { name: /add endpoint/i }));
    expect(screen.getByLabelText(/endpoint 1 url/i)).toBeInTheDocument();
  });

  it("removes an endpoint row", async () => {
    render(<Harness initial={[{ url: "https://a.example.com", protocol: "rest" }]} />);
    expect(screen.getByLabelText(/endpoint 1 url/i)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /remove endpoint 1/i }));
    expect(screen.queryByLabelText(/endpoint 1 url/i)).toBeNull();
  });

  it("disables 'Add endpoint' at 50 rows", () => {
    const fifty = Array.from({ length: 50 }, (): EndpointInput => ({ url: "https://x.example.com", protocol: "rest" }));
    render(<EndpointsEditor value={fifty} onChange={vi.fn()} />);
    expect(screen.getByRole("button", { name: /add endpoint/i })).toBeDisabled();
  });

  it("renders a per-row URL error from the errors prop", () => {
    render(
      <EndpointsEditor
        value={[{ url: "bad", protocol: "rest" }]}
        onChange={vi.fn()}
        errors={["Endpoint URL must be an absolute URL (include a scheme and host)"]}
      />,
    );
    expect(screen.getByText(/must be an absolute url/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd web && npm test -- src/features/catalog/components/__tests__/EndpointsEditor.test.tsx
```
Expected: FAIL — `Cannot find module '../EndpointsEditor'`.

- [ ] **Step 3: Write the component**

`web/src/features/catalog/components/EndpointsEditor.tsx`:
```tsx
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Input } from "@/components/base/input/input";
import { PROTOCOLS, PROTOCOL_LABEL, type EndpointInput } from "@/features/catalog/schemas/registerService";

const MAX_ENDPOINTS = 50;

interface Props {
  value: EndpointInput[];
  onChange: (next: EndpointInput[]) => void;
  disabled?: boolean;
  errors?: (string | undefined)[];
}

export function EndpointsEditor({ value, onChange, disabled = false, errors = [] }: Props) {
  const atMax = value.length >= MAX_ENDPOINTS;

  const updateRow = (index: number, patch: Partial<EndpointInput>) =>
    onChange(value.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  const addRow = () => {
    if (!atMax) onChange([...value, { url: "", protocol: "rest" }]);
  };
  const removeRow = (index: number) => onChange(value.filter((_, i) => i !== index));

  return (
    <div className="flex flex-col gap-3" data-testid="endpoints-editor">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-secondary">Endpoints</span>
        <span className="text-xs text-tertiary">{value.length}/{MAX_ENDPOINTS}</span>
      </div>

      {value.length === 0 && (
        <p className="text-xs text-tertiary">No endpoints yet — a service can be registered without any.</p>
      )}

      {value.map((row, index) => (
        <div key={index} className="flex items-start gap-2">
          <div className="flex-1">
            <Input
              aria-label={`Endpoint ${index + 1} URL`}
              placeholder="https://api.example.com/v1"
              value={row.url}
              onChange={(v: string) => updateRow(index, { url: v })}
              isInvalid={!!errors[index]}
              hint={errors[index]}
              isDisabled={disabled}
            />
          </div>
          <select
            aria-label={`Endpoint ${index + 1} protocol`}
            className="rounded-md border border-secondary px-3 py-2 text-sm bg-primary text-primary disabled:opacity-60"
            value={row.protocol}
            onChange={(e) => updateRow(index, { protocol: e.target.value as EndpointInput["protocol"] })}
            disabled={disabled}
          >
            {PROTOCOLS.map((p) => (
              <option key={p} value={p}>{PROTOCOL_LABEL[p]}</option>
            ))}
          </select>
          <Button
            type="button"
            color="tertiary"
            size="sm"
            aria-label={`Remove endpoint ${index + 1}`}
            onClick={() => removeRow(index)}
            isDisabled={disabled}
          >
            Remove
          </Button>
        </div>
      ))}

      <div>
        <Button type="button" color="secondary" size="sm" iconLeading={Plus} onClick={addRow} isDisabled={disabled || atMax}>
          Add endpoint
        </Button>
        {atMax && <p className="mt-1 text-xs text-tertiary">Maximum of {MAX_ENDPOINTS} endpoints reached.</p>}
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd web && npm test -- src/features/catalog/components/__tests__/EndpointsEditor.test.tsx && npm run typecheck
```
Expected: PASS; typecheck exit 0.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/EndpointsEditor.tsx web/src/features/catalog/components/__tests__/EndpointsEditor.test.tsx
git commit -m "feat(web): EndpointsEditor (add/remove rows, 0..50, protocol select) (E-02.F-02.S-02)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 6: `RegisterServiceDialog` component

**Files:**
- Create: `web/src/features/catalog/components/RegisterServiceDialog.tsx`
- Test: `web/src/features/catalog/components/__tests__/RegisterServiceDialog.test.tsx`

**Interfaces:**
- Consumes: `registerServiceSchema`/`endpointSchema`/`EndpointInput`/`RegisterServiceInput` (Task 2); `useRegisterService` (Task 4); `EndpointsEditor` (Task 5); `useTeamsList`, `useCurrentUser`, `initialsOf`, `applyProblemDetailsToForm`, modal/form/Input/TextArea/Button/Avatar primitives.
- Produces: `RegisterServiceDialog({ open: boolean; onOpenChange: (open: boolean) => void })`. Team `<select>` carries `data-testid="register-service-team-select"`; submit button label `Register Service`.

**Behavior:** Mirror `RegisterApplicationDialog`. `displayName`+`description` via RHF/zod (`textFieldsSchema = registerServiceSchema.pick({displayName,description})`). `teamId` + `endpoints` in local `useState` (avoids the known react-aria `Form` + controlled-`<select>` bug). On submit: require team; drop empty-URL rows; validate remaining rows with `endpointSchema` keeping per-row error indices aligned to the displayed array; on success toast + close; on error map `ProblemDetails.errors` to fields else toast.

- [ ] **Step 1: Write the failing test**

`web/src/features/catalog/components/__tests__/RegisterServiceDialog.test.tsx`:
```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { Toaster } from "sonner";

vi.mock("sonner", async (importOriginal) => {
  const mod = await importOriginal<typeof import("sonner")>();
  return { ...mod, toast: { ...mod.toast, success: vi.fn(), error: vi.fn() } };
});
import { toast } from "sonner";

const useAuthMock = vi.fn();
vi.mock("react-oidc-context", () => ({ useAuth: () => useAuthMock() }));

const TEAMS = [
  { id: "00000000-0000-0000-0000-000000000010", displayName: "Platform", description: null },
  { id: "00000000-0000-0000-0000-000000000011", displayName: "Frontend", description: null },
];
const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: (...args: unknown[]) => useTeamsListMock(...args),
}));
function makeTeamsResult(items: typeof TEAMS) {
  return { items, isLoading: false, isError: false, hasNext: false, hasPrev: false,
    goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn(), isFetching: false, error: null };
}

const mutateAsync = vi.fn();
vi.mock("@/features/catalog/api/services", () => ({
  useRegisterService: () => ({ mutateAsync, isPending: false }),
}));

import { RegisterServiceDialog } from "../RegisterServiceDialog";

function setup({ onOpenChange = vi.fn() } = {}) {
  useAuthMock.mockReturnValue({
    isAuthenticated: true,
    user: { access_token: "tok", profile: { sub: "u-1", name: "Alice Admin", email: "alice@orga.kartova.local", tenant_id: "t" } },
  });
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={qc}>
      <Toaster />
      <RegisterServiceDialog open onOpenChange={onOpenChange} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("RegisterServiceDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useTeamsListMock.mockReturnValue(makeTeamsResult(TEAMS));
  });

  it("renders the core fields and team options", () => {
    setup();
    expect(screen.getByLabelText(/display name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    expect(screen.getByTestId("register-service-team-select")).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Platform" })).toBeInTheDocument();
  });

  it("blocks submit with 'Team is required' when no team chosen", async () => {
    setup();
    await userEvent.type(screen.getByLabelText(/display name/i), "Orders");
    await userEvent.type(screen.getByLabelText(/description/i), "Order service");
    await userEvent.click(screen.getByRole("button", { name: /register service/i }));
    expect(await screen.findByText("Team is required")).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("submits valid input (incl. one endpoint), toasts success, and closes", async () => {
    mutateAsync.mockResolvedValue({ id: "svc-1" });
    const onOpenChange = vi.fn();
    setup({ onOpenChange });

    await userEvent.type(screen.getByLabelText(/display name/i), "Orders");
    await userEvent.type(screen.getByLabelText(/description/i), "Order service");
    await userEvent.selectOptions(screen.getByTestId("register-service-team-select"), "Platform");
    await userEvent.click(screen.getByRole("button", { name: /add endpoint/i }));
    await userEvent.type(screen.getByLabelText(/endpoint 1 url/i), "https://api.example.com/v1");

    await userEvent.click(screen.getByRole("button", { name: /register service/i }));

    await waitFor(() =>
      expect(mutateAsync).toHaveBeenCalledWith(expect.objectContaining({
        displayName: "Orders", description: "Order service", teamId: TEAMS[0]!.id,
        endpoints: [{ url: "https://api.example.com/v1", protocol: "rest" }],
      })),
    );
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Service registered"));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("shows a per-row error and does not submit when an endpoint URL is invalid", async () => {
    setup();
    await userEvent.type(screen.getByLabelText(/display name/i), "Orders");
    await userEvent.type(screen.getByLabelText(/description/i), "Order service");
    await userEvent.selectOptions(screen.getByTestId("register-service-team-select"), "Platform");
    await userEvent.click(screen.getByRole("button", { name: /add endpoint/i }));
    await userEvent.type(screen.getByLabelText(/endpoint 1 url/i), "not-a-url");
    await userEvent.click(screen.getByRole("button", { name: /register service/i }));

    expect(await screen.findByText(/must be an absolute url/i)).toBeInTheDocument();
    expect(mutateAsync).not.toHaveBeenCalled();
  });

  it("maps ProblemDetails 400 field errors to the form", async () => {
    mutateAsync.mockRejectedValue({ status: 400, errors: { displayName: ["Name already taken"] } });
    setup();
    await userEvent.type(screen.getByLabelText(/display name/i), "Orders");
    await userEvent.type(screen.getByLabelText(/description/i), "Order service");
    await userEvent.selectOptions(screen.getByTestId("register-service-team-select"), "Platform");
    await userEvent.click(screen.getByRole("button", { name: /register service/i }));
    expect(await screen.findByText(/name already taken/i)).toBeInTheDocument();
  });

  it("falls back to a toast on a flat ProblemDetails error", async () => {
    mutateAsync.mockRejectedValue({ status: 422, title: "Invalid team", detail: "The supplied teamId does not resolve to a team in the current tenant." });
    const onOpenChange = vi.fn();
    setup({ onOpenChange });
    await userEvent.type(screen.getByLabelText(/display name/i), "Orders");
    await userEvent.type(screen.getByLabelText(/description/i), "Order service");
    await userEvent.selectOptions(screen.getByTestId("register-service-team-select"), "Platform");
    await userEvent.click(screen.getByRole("button", { name: /register service/i }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("The supplied teamId does not resolve to a team in the current tenant."));
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd web && npm test -- src/features/catalog/components/__tests__/RegisterServiceDialog.test.tsx
```
Expected: FAIL — `Cannot find module '../RegisterServiceDialog'`.

- [ ] **Step 3: Write the component**

`web/src/features/catalog/components/RegisterServiceDialog.tsx`:
```tsx
import { useEffect, useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { toast } from "sonner";
import { z } from "zod";

import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { HookForm, FormField } from "@/components/base/form/hook-form";
import { Input } from "@/components/base/input/input";
import { TextArea } from "@/components/base/textarea/textarea";
import { Button } from "@/components/base/buttons/button";
import { Avatar } from "@/components/base/avatar/avatar";

import {
  registerServiceSchema,
  endpointSchema,
  type RegisterServiceInput,
  type EndpointInput,
} from "@/features/catalog/schemas/registerService";
import { useRegisterService } from "@/features/catalog/api/services";
import { useTeamsList } from "@/features/teams/api/teams";
import { EndpointsEditor } from "./EndpointsEditor";
import { applyProblemDetailsToForm, type ProblemDetails } from "@/shared/forms/problemDetails";
import { useCurrentUser } from "@/shared/auth/useCurrentUser";
import { initialsOf } from "@/shared/auth/initials";

const textFieldsSchema = registerServiceSchema.pick({ displayName: true, description: true });
type TextFieldsInput = z.infer<typeof textFieldsSchema>;

interface Props {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function RegisterServiceDialog({ open, onOpenChange }: Props) {
  const user = useCurrentUser();
  const mutation = useRegisterService();
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const [selectedTeamId, setSelectedTeamId] = useState<string>("");
  const [teamError, setTeamError] = useState<string>("");
  const [endpoints, setEndpoints] = useState<EndpointInput[]>([]);
  const [endpointErrors, setEndpointErrors] = useState<(string | undefined)[]>([]);

  const form = useForm<TextFieldsInput>({
    resolver: zodResolver(textFieldsSchema),
    defaultValues: { displayName: "", description: "" },
  });

  useEffect(() => {
    if (!open) {
      form.reset({ displayName: "", description: "" });
      setSelectedTeamId("");
      setTeamError("");
      setEndpoints([]);
      setEndpointErrors([]);
    }
  }, [open, form]);

  const onSubmit = form.handleSubmit(async (values) => {
    if (!selectedTeamId) {
      setTeamError("Team is required");
      return;
    }
    setTeamError("");

    // Validate endpoints: skip empty-URL rows (they are dropped), keep error
    // indices aligned to the displayed `endpoints` array.
    const rowErrors: (string | undefined)[] = new Array(endpoints.length).fill(undefined);
    const filled: EndpointInput[] = [];
    let endpointsValid = true;
    endpoints.forEach((e, i) => {
      if (e.url.trim() === "") return;
      const result = endpointSchema.safeParse(e);
      if (result.success) {
        filled.push(result.data);
      } else {
        endpointsValid = false;
        rowErrors[i] = result.error.issues[0]?.message ?? "Invalid endpoint";
      }
    });
    setEndpointErrors(rowErrors);
    if (!endpointsValid) return;

    const payload: RegisterServiceInput = { ...values, teamId: selectedTeamId, endpoints: filled };

    try {
      await mutation.mutateAsync(payload);
      toast.success("Service registered");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      const handled = applyProblemDetailsToForm(problem, (name, error) =>
        form.setError(name as Parameters<typeof form.setError>[0], error),
      );
      if (!handled) {
        toast.error(problem.detail ?? problem.title ?? "Failed to register service");
      }
    }
  });

  const initials = initialsOf(user?.displayName);
  const teams = teamsList.items ?? [];
  const noTeams = !teamsList.isLoading && teams.length === 0;

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[640px]">
        <Dialog aria-label="Register Service" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <div className="w-full">
            <div className="space-y-1 mb-4">
              <h2 className="text-lg font-semibold text-primary">Register Service</h2>
              <p className="text-sm text-tertiary">Add a new service to your catalog</p>
            </div>

            <HookForm form={form} onSubmit={onSubmit} className="space-y-5">
              <FormField name="displayName" control={form.control}>
                {({ field, fieldState }) => (
                  <Input
                    label="Display Name"
                    placeholder="Orders Service"
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
                    placeholder="Short summary..."
                    hint={fieldState.error?.message}
                    isInvalid={!!fieldState.error}
                    isRequired
                    {...field}
                  />
                )}
              </FormField>

              <div className="flex flex-col gap-1">
                <label htmlFor="register-service-team" className="text-sm font-medium text-secondary">
                  Team <span className="text-error-primary">*</span>
                </label>
                <select
                  id="register-service-team"
                  data-testid="register-service-team-select"
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
                    No teams available — create a team first before registering a service.
                  </p>
                )}
              </div>

              <EndpointsEditor
                value={endpoints}
                onChange={setEndpoints}
                errors={endpointErrors}
                disabled={mutation.isPending}
              />

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
                  Register Service
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

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd web && npm test -- src/features/catalog/components/__tests__/RegisterServiceDialog.test.tsx && npm run typecheck
```
Expected: PASS; typecheck exit 0.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/RegisterServiceDialog.tsx web/src/features/catalog/components/__tests__/RegisterServiceDialog.test.tsx
git commit -m "feat(web): RegisterServiceDialog with endpoints editor (E-02.F-02.S-02)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 7: `ServicesTable` component

**Files:**
- Create: `web/src/features/catalog/components/ServicesTable.tsx`
- Test: `web/src/features/catalog/components/__tests__/ServicesTable.test.tsx`

**Interfaces:**
- Consumes: `ServiceResponse` (Task 4); `HealthBadge` (Task 3); `CreatedByLink`; DataTable primitives; `CursorListResult`/`SortDirection`.
- Produces: `ServicesTable({ list: CursorListResult<ServiceResponse>; sortBy: "createdAt"|"displayName"; sortOrder: SortDirection; onSortChange: (field, order) => void; teamNameById: Map<string,string> })`. Columns: Name (link → `/catalog/services/{id}`, sortable), Health, Team (link → `/teams/{id}`), Created by, Endpoints (count), Created (sortable). Loading skeleton (6 cells) + empty state "No services yet".

- [ ] **Step 1: Write the failing test**

`web/src/features/catalog/components/__tests__/ServicesTable.test.tsx`:
```tsx
import React from "react";
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { ServicesTable } from "../ServicesTable";
import type { CursorListResult } from "@/lib/list/types";
import type { ServiceResponse } from "@/features/catalog/api/services";

function withRouter(ui: React.ReactNode) {
  return <MemoryRouter>{ui}</MemoryRouter>;
}

const s1: ServiceResponse = {
  id: "00000000-0000-0000-0000-000000000001",
  tenantId: "t",
  displayName: "Orders",
  description: "Order service",
  teamId: "00000000-0000-0000-0000-000000000010",
  createdByUserId: "00000000-0000-0000-0000-0000000000aa",
  createdBy: { id: "00000000-0000-0000-0000-0000000000aa", displayName: "Alice Admin", email: "alice@example.com" },
  createdAt: "2026-04-30T00:00:00Z",
  health: "unknown",
  endpoints: [{ url: "https://api.example.com/v1", protocol: "rest" }],
  version: "v1",
};

function makeList(overrides: Partial<CursorListResult<ServiceResponse>>): CursorListResult<ServiceResponse> {
  return {
    items: [], isLoading: false, isFetching: false, isError: false, error: null,
    hasNext: false, hasPrev: false, goNext: () => {}, goPrev: () => {}, reset: () => {}, refetch: () => {},
    ...overrides,
  };
}

const noop = () => {};
const teamNames = new Map<string, string>([["00000000-0000-0000-0000-000000000010", "Platform"]]);

describe("ServicesTable", () => {
  it("renders a row linking to the service detail page", () => {
    render(withRouter(<ServicesTable list={makeList({ items: [s1] })} sortBy="displayName" sortOrder="desc" onSortChange={noop} teamNameById={teamNames} />));
    const link = screen.getByRole("link", { name: /orders/i });
    expect(link).toHaveAttribute("href", `/catalog/services/${s1.id}`);
  });

  it("renders the health badge and endpoint count", () => {
    render(withRouter(<ServicesTable list={makeList({ items: [s1] })} sortBy="displayName" sortOrder="desc" onSortChange={noop} teamNameById={teamNames} />));
    expect(screen.getByText("Unknown")).toBeInTheDocument();
    expect(screen.getByText("1")).toBeInTheDocument();
  });

  it("links the team name", () => {
    render(withRouter(<ServicesTable list={makeList({ items: [s1] })} sortBy="displayName" sortOrder="desc" onSortChange={noop} teamNameById={teamNames} />));
    expect(screen.getByRole("link", { name: /^platform$/i })).toHaveAttribute("href", "/teams/00000000-0000-0000-0000-000000000010");
  });

  it("shows the empty state when there are no services", () => {
    render(withRouter(<ServicesTable list={makeList({ items: [] })} sortBy="displayName" sortOrder="desc" onSortChange={noop} teamNameById={teamNames} />));
    expect(screen.getByText(/no services yet/i)).toBeInTheDocument();
  });

  it("renders skeleton rows while loading", () => {
    const { container } = render(withRouter(<ServicesTable list={makeList({ isLoading: true })} sortBy="displayName" sortOrder="desc" onSortChange={noop} teamNameById={teamNames} />));
    expect(container.querySelectorAll('[data-testid="row-skeleton"]').length).toBeGreaterThan(0);
  });

  it("invokes onSortChange when the Name header is activated", async () => {
    const onSortChange = vi.fn();
    render(withRouter(<ServicesTable list={makeList({ items: [s1] })} sortBy="createdAt" sortOrder="desc" onSortChange={onSortChange} teamNameById={teamNames} />));
    await userEvent.click(screen.getByRole("columnheader", { name: /name/i }));
    expect(onSortChange).toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd web && npm test -- src/features/catalog/components/__tests__/ServicesTable.test.tsx
```
Expected: FAIL — `Cannot find module '../ServicesTable'`.

- [ ] **Step 3: Write the component**

`web/src/features/catalog/components/ServicesTable.tsx`:
```tsx
import { Link } from "react-router-dom";
import { Table } from "@/components/application/table/table";
import { Card, CardContent } from "@/components/base/card/card";
import { SortableHead, TablePager, TableSkeleton, fromSort, toSort } from "@/components/application/data-table/data-table";
import { HealthBadge } from "./HealthBadge";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import type { CursorListResult, SortDirection } from "@/lib/list/types";
import type { ServiceResponse } from "@/features/catalog/api/services";

type SortField = "createdAt" | "displayName";

interface Props {
  list: CursorListResult<ServiceResponse>;
  sortBy: SortField;
  sortOrder: SortDirection;
  onSortChange: (field: SortField, order: SortDirection) => void;
  /** Resolves teamId → displayName (parent fetches all teams once). */
  teamNameById: Map<string, string>;
}

export function ServicesTable({ list, sortBy, sortOrder, onSortChange, teamNameById }: Props) {
  if (list.isLoading) {
    return (
      <Table aria-label="Services">
        <Table.Header>
          <Table.Head id="displayName" isRowHeader>Name</Table.Head>
          <Table.Head id="health">Health</Table.Head>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
          <Table.Head id="endpoints">Endpoints</Table.Head>
          <Table.Head id="createdAt">Created</Table.Head>
        </Table.Header>
        <TableSkeleton rows={5} cells={6} />
      </Table>
    );
  }

  if (list.items.length === 0) {
    return (
      <Card className="mx-auto max-w-md text-center">
        <CardContent className="space-y-2 p-8">
          <p className="text-base font-medium text-primary">No services yet</p>
          <p className="text-sm text-tertiary">
            Use the &quot;+ Register Service&quot; button in the header to add your first one.
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
      <Table aria-label="Services" sortDescriptor={fromSort(sortBy, sortOrder)} onSortChange={handleSortChange}>
        <Table.Header>
          <SortableHead id="displayName" isRowHeader>Name</SortableHead>
          <Table.Head id="health">Health</Table.Head>
          <Table.Head id="team">Team</Table.Head>
          <Table.Head id="createdBy">Created by</Table.Head>
          <Table.Head id="endpoints">Endpoints</Table.Head>
          <SortableHead id="createdAt">Created</SortableHead>
        </Table.Header>
        <Table.Body>
          {list.items.map((svc) => (
            <Table.Row key={svc.id} id={svc.id}>
              <Table.Cell>
                <Link to={`/catalog/services/${svc.id}`} className="block font-medium text-primary hover:underline">
                  {svc.displayName}
                </Link>
              </Table.Cell>
              <Table.Cell>
                <HealthBadge health={svc.health} />
              </Table.Cell>
              <Table.Cell className="text-sm">
                <Link to={`/teams/${svc.teamId}`} className="text-primary hover:underline">
                  {teamNameById.get(svc.teamId) ?? "Unknown team"}
                </Link>
              </Table.Cell>
              <Table.Cell className="text-sm">
                <CreatedByLink user={svc.createdBy} />
              </Table.Cell>
              <Table.Cell className="text-sm text-tertiary">{svc.endpoints.length}</Table.Cell>
              <Table.Cell className="text-sm text-tertiary">
                {svc.createdAt ? new Date(svc.createdAt).toLocaleDateString() : ""}
              </Table.Cell>
            </Table.Row>
          ))}
        </Table.Body>
      </Table>
      <TablePager
        hasPrev={list.hasPrev}
        hasNext={list.hasNext}
        onPrev={list.goPrev}
        onNext={list.goNext}
        pageSize={list.items.length}
      />
    </div>
  );
}
```

**Note:** `ServiceResponse.createdBy` is `UserDisplayInfo | undefined` (no `null` in the generated type unless the backend nulls it); `CreatedByLink` accepts `null | undefined` so this is safe. If `tsc` flags the `health`/`createdBy` prop types, align them to the generated `ServiceResponse` shape — do not loosen `CreatedByLink`.

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd web && npm test -- src/features/catalog/components/__tests__/ServicesTable.test.tsx && npm run typecheck
```
Expected: PASS; typecheck exit 0.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/ServicesTable.tsx web/src/features/catalog/components/__tests__/ServicesTable.test.tsx
git commit -m "feat(web): ServicesTable (name/health/team/created-by/endpoints/created) (E-02.F-02.S-02)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: `ServicesListPage`

**Files:**
- Create: `web/src/features/catalog/pages/ServicesListPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/ServicesListPage.test.tsx`

**Interfaces:**
- Consumes: `useServicesList` (Task 4), `ServicesTable` (Task 7), `RegisterServiceDialog` (Task 6), `useTeamsList`, `useListUrlState`, `usePermissions`, `KartovaPermissions.CatalogServicesRegister`.
- Produces: default-exportable page `ServicesListPage()` mounted at `/catalog/services`. Heading "Services"; Register button gated on `CatalogServicesRegister`; default sort `displayName`/`desc`.

- [ ] **Step 1: Write the failing test**

`web/src/features/catalog/pages/__tests__/ServicesListPage.test.tsx`:
```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

const usePermissionsMock = vi.fn();
vi.mock("@/shared/auth/usePermissions", () => ({ usePermissions: () => usePermissionsMock() }));

const useServicesListMock = vi.fn();
vi.mock("@/features/catalog/api/services", () => ({
  useServicesList: (...a: unknown[]) => useServicesListMock(...a),
}));

const useTeamsListMock = vi.fn();
vi.mock("@/features/teams/api/teams", () => ({ useTeamsList: () => useTeamsListMock() }));

import { ServicesListPage } from "../ServicesListPage";
import { KartovaPermissions } from "@/shared/auth/permissions";

function emptyList() {
  return { items: [], isLoading: false, isFetching: false, isError: false, error: null,
    hasNext: false, hasPrev: false, goNext: vi.fn(), goPrev: vi.fn(), reset: vi.fn(), refetch: vi.fn() };
}
function setPerms(perms: string[]) {
  usePermissionsMock.mockReturnValue({ role: "t", hasPermission: (p: string) => perms.includes(p), isLoading: false });
}
function renderPage() {
  return render(<MemoryRouter initialEntries={["/catalog/services"]}><ServicesListPage /></MemoryRouter>);
}

describe("ServicesListPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    useServicesListMock.mockReturnValue(emptyList());
    useTeamsListMock.mockReturnValue(emptyList());
  });

  it("renders the Services heading", () => {
    setPerms([]);
    renderPage();
    expect(screen.getByRole("heading", { name: /services/i })).toBeInTheDocument();
  });

  it("shows Register Service for a user with the register permission", () => {
    setPerms([KartovaPermissions.CatalogServicesRegister]);
    renderPage();
    expect(screen.getByRole("button", { name: /register service/i })).toBeInTheDocument();
  });

  it("hides Register Service for a user without the permission", () => {
    setPerms([]);
    renderPage();
    expect(screen.queryByRole("button", { name: /register service/i })).toBeNull();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd web && npm test -- src/features/catalog/pages/__tests__/ServicesListPage.test.tsx
```
Expected: FAIL — `Cannot find module '../ServicesListPage'`.

- [ ] **Step 3: Write the page**

`web/src/features/catalog/pages/ServicesListPage.tsx`:
```tsx
import { useMemo, useState, useEffect } from "react";
import { Plus } from "@untitledui/icons";
import { Button } from "@/components/base/buttons/button";
import { Card, CardContent } from "@/components/base/card/card";
import { useServicesList } from "@/features/catalog/api/services";
import { useTeamsList } from "@/features/teams/api/teams";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { ServicesTable } from "@/features/catalog/components/ServicesTable";
import { RegisterServiceDialog } from "@/features/catalog/components/RegisterServiceDialog";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";

const ALLOWED_SORT_FIELDS = ["createdAt", "displayName"] as const;

export function ServicesListPage() {
  const { sortBy, sortOrder, setSort } = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "desc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
  });

  const list = useServicesList({ sortBy, sortOrder });
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );
  const [dialogOpen, setDialogOpen] = useState(false);

  const { hasPermission, isLoading: permissionsLoading } = usePermissions();
  const canRegister = !permissionsLoading && hasPermission(KartovaPermissions.CatalogServicesRegister);

  useEffect(() => {
    if (list.isError) console.error("ServicesListPage list error", list.error);
  }, [list.isError, list.error]);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h2 className="text-2xl font-semibold text-primary">Services</h2>
        {canRegister && (
          <Button onClick={() => setDialogOpen(true)} size="sm" color="primary" iconLeading={Plus}>
            Register Service
          </Button>
        )}
      </div>

      {list.isError ? (
        <Card className="mx-auto max-w-md">
          <CardContent className="space-y-3 p-6 text-center">
            <p className="text-base font-medium text-error-primary">Failed to load services</p>
            <p className="text-sm text-tertiary">Try refreshing or resetting the list.</p>
            <Button size="sm" onClick={() => list.reset()}>Reset</Button>
          </CardContent>
        </Card>
      ) : (
        <ServicesTable
          list={list}
          sortBy={sortBy}
          sortOrder={sortOrder}
          onSortChange={setSort}
          teamNameById={teamNameById}
        />
      )}

      {canRegister && <RegisterServiceDialog open={dialogOpen} onOpenChange={setDialogOpen} />}
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd web && npm test -- src/features/catalog/pages/__tests__/ServicesListPage.test.tsx && npm run typecheck
```
Expected: PASS; typecheck exit 0.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/pages/ServicesListPage.tsx web/src/features/catalog/pages/__tests__/ServicesListPage.test.tsx
git commit -m "feat(web): ServicesListPage (default displayName desc, gated register) (E-02.F-02.S-02)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 9: `ServiceDetailPage` (read-only)

**Files:**
- Create: `web/src/features/catalog/pages/ServiceDetailPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx`

**Interfaces:**
- Consumes: `useService` (Task 4), `HealthBadge` (Task 3), `PROTOCOL_LABEL` (Task 2), `CreatedByLink`, `useTeamsList`, `Card`/`CardHeader`/`CardContent`, `Skeleton`, `Table`.
- Produces: page `ServiceDetailPage()` mounted at `/catalog/services/:id`. Loading skeleton carries `data-testid="service-detail-skeleton"`; not-found card text "Service not found".

- [ ] **Step 1: Write the failing test**

`web/src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx`:
```tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";

import * as clientModule from "@/features/catalog/api/client";
import { ServiceDetailPage } from "../ServiceDetailPage";

// useTeamsList is only used to resolve the team name link — stub it out.
vi.mock("@/features/teams/api/teams", () => ({
  useTeamsList: () => ({ items: [{ id: "00000000-0000-0000-0000-000000000010", displayName: "Platform" }], isLoading: false }),
}));

function harness(qc: QueryClient, path: string) {
  return (
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/catalog/services/:id" element={<ServiceDetailPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

const svc = {
  id: "00000000-0000-0000-0000-000000000001",
  tenantId: "t",
  displayName: "Orders",
  description: "Order service",
  teamId: "00000000-0000-0000-0000-000000000010",
  createdByUserId: "00000000-0000-0000-0000-0000000000aa",
  createdBy: { id: "00000000-0000-0000-0000-0000000000aa", displayName: "Alice Admin", email: "alice@example.com" },
  createdAt: "2026-01-01T12:34:56Z",
  health: "unknown",
  endpoints: [{ url: "https://api.example.com/v1", protocol: "rest" }],
  version: "v1",
};

describe("ServiceDetailPage", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("renders the service header, Unknown health, and an endpoint", async () => {
    const get = vi.fn().mockResolvedValue({ data: svc, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, `/catalog/services/${svc.id}`));

    await waitFor(() => expect(screen.getByText("Orders")).toBeInTheDocument());
    expect(screen.getByText("Unknown")).toBeInTheDocument();
    expect(screen.getByText("https://api.example.com/v1")).toBeInTheDocument();
    expect(screen.getByText("REST")).toBeInTheDocument();
  });

  it("renders the 'No endpoints' empty state", async () => {
    const get = vi.fn().mockResolvedValue({ data: { ...svc, endpoints: [] }, error: undefined });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, `/catalog/services/${svc.id}`));
    await waitFor(() => expect(screen.getByText(/no endpoints registered/i)).toBeInTheDocument());
  });

  it("renders a skeleton while loading", () => {
    const get = vi.fn().mockReturnValue(new Promise(() => {}));
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { container } = render(harness(qc, `/catalog/services/${svc.id}`));
    expect(container.querySelectorAll('[data-testid="service-detail-skeleton"]').length).toBeGreaterThan(0);
  });

  it("renders a not-found card on error", async () => {
    const get = vi.fn().mockResolvedValue({ data: undefined, error: { status: 404, title: "Not Found" } });
    vi.spyOn(clientModule, "apiClient", "get").mockReturnValue({ GET: get, POST: vi.fn() } as never);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(harness(qc, `/catalog/services/missing`));
    await waitFor(() => expect(screen.getByText(/service not found/i)).toBeInTheDocument());
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd web && npm test -- src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx
```
Expected: FAIL — `Cannot find module '../ServiceDetailPage'`.

- [ ] **Step 3: Write the page**

`web/src/features/catalog/pages/ServiceDetailPage.tsx`:
```tsx
import { useMemo } from "react";
import { Link, useParams } from "react-router-dom";
import { Card, CardContent, CardHeader } from "@/components/base/card/card";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { Table } from "@/components/application/table/table";
import { HealthBadge } from "@/features/catalog/components/HealthBadge";
import { CreatedByLink } from "@/features/users/components/CreatedByLink";
import { useService } from "@/features/catalog/api/services";
import { useTeamsList } from "@/features/teams/api/teams";
import { PROTOCOL_LABEL } from "@/features/catalog/schemas/registerService";

export function ServiceDetailPage() {
  const { id } = useParams<{ id: string }>();
  const query = useService(id ?? "");
  const teamsList = useTeamsList({ sortBy: "displayName", sortOrder: "asc", limit: 200 });
  const teamNameById = useMemo(
    () => new Map<string, string>((teamsList.items ?? []).map((t) => [t.id, t.displayName])),
    [teamsList.items],
  );

  if (query.isLoading) {
    return (
      <Card data-testid="service-detail-skeleton">
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
          <p className="text-base font-medium text-error-primary">Service not found</p>
          <p className="text-sm text-tertiary">
            It may have been deleted, or you may not have access in this tenant.
          </p>
        </CardContent>
      </Card>
    );
  }

  const svc = query.data;

  return (
    <Card>
      <CardHeader className="space-y-3">
        <div className="flex flex-wrap items-center gap-3">
          <h2 className="text-2xl font-semibold text-primary">{svc.displayName}</h2>
          <HealthBadge health={svc.health} size="md" />
        </div>
      </CardHeader>
      <CardContent className="space-y-6">
        <section>
          <h3 className="text-sm font-medium text-tertiary">Description</h3>
          <p className="mt-1 text-sm text-secondary">
            {svc.description ? svc.description : <span className="italic">No description</span>}
          </p>
        </section>

        <hr className="border-secondary" />

        <section className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <Field label="ID" value={svc.id} mono />
          <div>
            <div className="text-xs uppercase tracking-wide text-tertiary">Team</div>
            <div className="mt-1 text-sm">
              <Link to={`/teams/${svc.teamId}`} className="text-primary hover:underline">
                {teamNameById.get(svc.teamId) ?? "View team"}
              </Link>
            </div>
          </div>
          <div>
            <div className="text-xs uppercase tracking-wide text-tertiary">Created by</div>
            <div className="mt-1 text-sm">
              <CreatedByLink user={svc.createdBy} />
            </div>
          </div>
          <Field label="Created" value={svc.createdAt ? new Date(svc.createdAt).toLocaleString() : "—"} />
          <Field label="Version" value={svc.version} mono />
        </section>

        <hr className="border-secondary" />

        <section>
          <h3 className="text-sm font-medium text-tertiary">Endpoints</h3>
          {svc.endpoints.length === 0 ? (
            <p className="mt-1 text-sm text-tertiary italic">No endpoints registered</p>
          ) : (
            <div className="mt-2 overflow-hidden rounded-lg ring-1 ring-secondary">
              <Table aria-label="Service endpoints">
                <Table.Header>
                  <Table.Head id="url" isRowHeader>URL</Table.Head>
                  <Table.Head id="protocol">Protocol</Table.Head>
                </Table.Header>
                <Table.Body>
                  {svc.endpoints.map((e, i) => (
                    <Table.Row key={`${e.url}-${i}`} id={`${e.url}-${i}`}>
                      <Table.Cell className="font-mono text-sm text-primary">{e.url}</Table.Cell>
                      <Table.Cell className="text-sm">{PROTOCOL_LABEL[e.protocol]}</Table.Cell>
                    </Table.Row>
                  ))}
                </Table.Body>
              </Table>
            </div>
          )}
        </section>
      </CardContent>
    </Card>
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

- [ ] **Step 4: Run the test to verify it passes**

```bash
cd web && npm test -- src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx && npm run typecheck
```
Expected: PASS; typecheck exit 0.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/pages/ServiceDetailPage.tsx web/src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx
git commit -m "feat(web): read-only ServiceDetailPage with endpoints + health (E-02.F-02.S-02)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 10: Wire routes + promote the Services nav item

**Files:**
- Modify: `web/src/app/router.tsx`
- Modify: `web/src/components/layout/Sidebar.tsx:83-85` (the `<DisabledItem label="Services" />` line)
- Modify: `web/src/components/layout/__tests__/Sidebar.test.tsx:91-99` (the "disabled placeholders" test now expects 2, not 3)

**Interfaces:**
- Consumes: `ServicesListPage` (Task 8), `ServiceDetailPage` (Task 9).
- Produces: live routes `/catalog/services` and `/catalog/services/:id`; an active `Services` nav link.

- [ ] **Step 1: Update the Sidebar test to the new expectation (failing)**

In `web/src/components/layout/__tests__/Sidebar.test.tsx`, replace the `"renders disabled placeholders (Services / Infrastructure / Docs)"` test (lines 91-99) with:
```tsx
  it("renders the Services link (promoted from a disabled placeholder)", () => {
    setPermissions();
    renderSidebar();
    expect(screen.getByRole("link", { name: "Services" })).toBeInTheDocument();
  });

  it("renders disabled placeholders (Infrastructure / Docs) with data-disabled", () => {
    setPermissions();
    renderSidebar();
    const disabled = screen.getAllByText(/Infrastructure|Docs/);
    expect(disabled.length).toBe(2);
    for (const node of disabled) {
      expect(node.getAttribute("data-disabled")).toBe("true");
    }
  });
```

- [ ] **Step 2: Run the Sidebar test to verify it fails**

```bash
cd web && npm test -- src/components/layout/__tests__/Sidebar.test.tsx
```
Expected: FAIL — no `Services` link yet (still a disabled placeholder).

- [ ] **Step 3: Promote the Services nav item**

In `web/src/components/layout/Sidebar.tsx`, replace:
```tsx
          <li>
            <DisabledItem label="Services" />
          </li>
```
with:
```tsx
          <li>
            <NavItemLink to="/catalog/services" label="Services" />
          </li>
```
(Leave the `Infrastructure` and `Docs` `DisabledItem`s untouched.)

- [ ] **Step 4: Add the routes**

In `web/src/app/router.tsx`, add the imports near the other catalog imports:
```tsx
import { ServicesListPage } from "@/features/catalog/pages/ServicesListPage";
import { ServiceDetailPage } from "@/features/catalog/pages/ServiceDetailPage";
```
Then add inside `<Route element={<ProtectedShell />}>`, directly after the `/catalog/applications/:id` route:
```tsx
        <Route path="/catalog/services" element={<ServicesListPage />} />
        <Route path="/catalog/services/:id" element={<ServiceDetailPage />} />
```

- [ ] **Step 5: Run the Sidebar test + typecheck to verify green**

```bash
cd web && npm test -- src/components/layout/__tests__/Sidebar.test.tsx && npm run typecheck
```
Expected: PASS; typecheck exit 0.

- [ ] **Step 6: Commit**

```bash
git add web/src/app/router.tsx web/src/components/layout/Sidebar.tsx web/src/components/layout/__tests__/Sidebar.test.tsx
git commit -m "feat(web): wire Services routes + promote Services nav link (E-02.F-02.S-02)" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```

---

### Task 11: Full verification, manual smoke, and DoD

**Files:** none (verification only).

- [ ] **Step 1: Full frontend test suite**

```bash
cd web && npm test
```
Expected: all suites PASS (new service suites + all pre-existing).

- [ ] **Step 2: Lint + production build (gate 1 + gate 4 input)**

```bash
cd web && npm run lint && npm run build
```
Expected: lint clean; `tsc -b` 0 errors; `vite build` succeeds. (This is what the web container image compiles — a red build here is a gate-4 failure.)

- [ ] **Step 3: Manual smoke via Playwright MCP (ADR-0084)**

Cold-start the dev server, then drive the browser:
- Navigate to `/catalog/services` → expect the "Services" heading and an empty-state or table.
- Click **Register Service** → fill name/description, pick a team, **add one endpoint** (`https://api.example.com/v1`, REST) → submit → expect a success toast and the row appears.
- Register a second service **with zero endpoints** → expect success.
- Click a row → expect the read-only detail page with `Unknown` health, the endpoints table (or "No endpoints registered"), and a working Team link.
- Confirm the browser console is clean.

If Docker / the dev stack is unavailable in-session, mark Steps 1–3's runtime checks that need the API (Playwright only) *pending user verification*; the Vitest suite and build do not need a running API.

- [ ] **Step 4: Run the pre-push CI mirror (frontend subset)**

```bash
bash scripts/ci-local.sh frontend
```
Expected: green (Release-equivalent lint/typecheck/test/build for the web project).

- [ ] **Step 5: Update the progress checklist**

In `docs/product/CHECKLIST.md`, mark `E-02.F-02.S-02` complete with a one-line provenance note (date + "list + register dialog + read-only detail; health=Unknown badge; consumers deferred to E-04"), and bump the Phase 1 progress counter.

- [ ] **Step 6: Commit the checklist + run the DoD gates**

```bash
git add docs/product/CHECKLIST.md
git commit -m "docs(product): mark E-02.F-02.S-02 (Service UI surface) complete" -m "Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>"
```
Then run the remaining DoD gates from CLAUDE.md against the branch diff: `/simplify` (gate 5), `/superpowers:requesting-code-review` (gate 7), `/pr-review-toolkit:review-pr` (gate 8), `/deep-review` (gate 9). **Gate 6 (mutation) does not apply** — no C# Domain/Application change; note the skip reason. After gates 5/7/9 apply any fixes, re-run Steps 1–2 to confirm still-green before opening the PR.

---

## Self-Review

**1. Spec coverage** — every spec section maps to a task:
- §3 #1 frontend-only → Global Constraints + no backend tasks. ✓
- §3 #2 list+register+detail → Tasks 7/8 (list), 5/6 (register), 9 (detail). ✓
- §3 #3 read-only detail → Task 9 (no edit/lifecycle/assign). ✓
- §3 #4 HealthBadge of live enum → Task 3 + rendered in Tasks 7/9. ✓
- §3 #5 consumers deferred → not built (Out of scope §9); no task, intentional. ✓
- §3 #6 promote nav item → Task 10. ✓
- §3 #7 routes → Task 10. ✓
- §3 #8 endpoints editor local useState → Task 5 (controlled) + Task 6 (state owner). ✓
- §3 #9 codegen committed → Task 1. ✓
- §3 #10 client validation advisory + ProblemDetails→form/toast → Task 6 submit + tests. ✓
- §3 #11 sort allowlist + default displayName desc → Task 8. ✓
- §3 #12 register gated / read CatalogRead → Task 8 (button gate); read enforced server-side (no task). ✓
- §4.3 file map → all created/modified files have tasks. ✓
- §7 test artifacts → registerService (T2), services (T4), EndpointsEditor (T5), RegisterServiceDialog (T6), ServicesTable (T7), ServiceDetailPage (T9); plus HealthBadge (T3) and ServicesListPage (T8) added for coverage; Sidebar test updated (T10). ✓
- §7 build/gate-4 (commit generated client) → Task 1 + Task 11 Step 2. ✓
- §8 mutation gate skip → Task 11 Step 6 note. ✓

**2. Placeholder scan** — no "TBD"/"add error handling"/"similar to Task N". Every code step shows full file content; every command shows expected output. The only deferred decisions are explicit verification branches (enum casing in Task 1 Step 3; `z.enum` readonly fallback in Task 2 Step 4; prop-type alignment in Task 7) — each states the exact corrective action, not a vague "handle it".

**3. Type consistency** — names align across tasks: `serviceKeys`/`useServicesList`/`useService`/`useRegisterService`/`ServiceResponse` (T4) are imported with those exact names in T6/T7/T8/T9; `EndpointInput`/`registerServiceSchema`/`endpointSchema`/`PROTOCOLS`/`PROTOCOL_LABEL`/`ProtocolValue` (T2) used consistently in T5/T6/T9; `Health`/`HealthBadge`/`healthLabel`/`healthColor` (T3) used in T7/T9; `ServicesTable`/`ServicesListPage`/`ServiceDetailPage` route+import names match T7→T8, T8/T9→T10. Sort field union `"createdAt"|"displayName"` consistent across T4/T7/T8. Test harness (mock `apiClient` getter; `QueryClientProvider`+`MemoryRouter`) consistent with the repo's existing tests.

**No blocking issues found.**
