# Slice — Catalog: Service UI surface (list · register · detail)

**Date:** 2026-06-20
**Stories:** E-02.F-02.S-02 (service detail page — health + consumers)
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-service-ui-surface`

---

## 1. Goal

Give the `Service` entity a usable web surface, mirroring how `Application` shipped its UI in slice 4: a developer can **list** services, **register** one (display name, description, owning team, 0..50 endpoints), and open a **read-only detail page**. The S-01 backend (`POST`/`GET`-by-id/`GET`-list at `/api/v1/catalog/services`, `catalog.services.register` permission, `service.registered` audit, RLS) is already on master with its own real-seam integration tests; this slice adds **no backend code** — it is a frontend-only slice that wires the typed HTTP client to the existing endpoints.

The story title reads "detail page with health and consumers". Health renders as a badge showing the real `HealthStatus` enum — which is **always `Unknown` today** (no write path until E-15/E-16). **Consumers** require entity relationships (E-04, not started) and are deferred. This mirrors how Application S-02 shipped "header + metadata only, tabs deferred".

---

## 2. Pre-requisites (already on master)

- **Service backend (S-01, commit 4eab9ff):** `ServiceResponse` (`Id, TenantId, DisplayName, Description, TeamId, CreatedByUserId, CreatedAt, Health, Endpoints[{url,protocol}], Version`, + server-enriched `CreatedBy: UserDisplayInfo?`); `POST /catalog/services` (`RequireAuthorization(CatalogServicesRegister)`), `GET /catalog/services/{id}` + `GET /catalog/services` (`RequireAuthorization(CatalogRead)`, `CursorPage<ServiceResponse>`, sort allowlist `createdAt|displayName`). Real-seam integration tests already cover all three (`RegisterServiceTests`, `GetServiceByIdTests`, `ListServicesPaginationTests`, `CatalogPermissionMatrixTests`).
- **Permissions:** `KartovaPermissions.CatalogServicesRegister = "catalog.services.register"` exists in both C# and `web/src/shared/auth/permissions.ts` (parity snapshot already green).
- **Frontend list stack:** `useCursorList` (`@/lib/list/useCursorList`), `useListUrlState` (`@/lib/list/useListUrlState`), DataTable primitives (`SortableHead`, `TablePager`, `TableSkeleton`, `fromSort`, `toSort` from `@/components/application/data-table/data-table`), `Table` (`@/components/application/table/table`).
- **Patterns to mirror 1:1:** `features/catalog/api/applications.ts`, `pages/CatalogListPage.tsx`, `pages/ApplicationDetailPage.tsx`, `components/ApplicationsTable.tsx`, `components/RegisterApplicationDialog.tsx`, `components/LifecycleBadge.tsx` + `catalog/lifecycle.ts`, `schemas/registerApplication.ts`.
- **Shared helpers:** `apiClient` (`./client`), `unwrapData`/`throwWithStatus` (`@/shared/api/openapi-fetch-helpers`), `applyProblemDetailsToForm` + `ProblemDetails` (`@/shared/forms/problemDetails`), `useTeamsList` (`@/features/teams/api/teams`), `CreatedByLink` + `UserDisplayInfo` (`@/features/users/components/CreatedByLink`), `useCurrentUser`, `usePermissions`, `Badge` (`@/components/base/badges/badges`).
- **Nav placeholder:** `Sidebar.tsx` already renders a disabled `Services` item (`<DisabledItem label="Services" />`) — this slice promotes it to a live link.

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **Frontend-only slice.** No backend changes; reuse S-01 endpoints verbatim. | The endpoints + their real-seam tests already exist. Adding backend code would be redundant scope. |
| 2 | Full Services surface in one slice: **list + register + detail** (user-confirmed scope). | Self-contained create→list→view in the UI; mirrors how Application shipped in slice 4 (no half-state where a detail page is unreachable). |
| 3 | Detail page is **read-only**. No edit / lifecycle / team-reassign. | Service has no domain support for any of these (not built in S-01). Adding mutations is out of scope for "detail page". |
| 4 | Health renders via a **`HealthBadge`** showing the real enum value (`Unknown` today). No scorecard, no write path. | AC asks for health on the page; the value exists. The probe/agent write path is E-15/E-16; the scorecard is E-10. |
| 5 | **Consumers / dependencies deferred to E-04.** Not rendered (not even an empty section). | Relationships are unbuilt; an empty "Consumers" panel would imply a feature that does not exist. Noted as explicit deferral. |
| 6 | Entry point = **promote the existing disabled `Services` nav item** to `NavItemLink to="/catalog/services"`. Ungated (mirrors the `Catalog` item). | Least-friction; matches Teams/Members as sibling top-level pages. API still enforces `CatalogRead`, exactly as Applications does behind the ungated Catalog link. |
| 7 | Routes: `/catalog/services` (list) and `/catalog/services/:id` (detail), siblings to the existing `/catalog/applications/:id`. | Consistent URL shape; `/catalog` stays the Applications list (renaming/segmenting it is a separate concern). |
| 8 | **Endpoints editor uses local `useState`**, not RHF `useFieldArray`. Validated in the submit handler. | The existing `RegisterApplicationDialog` deliberately keeps `<select>` out of RHF to avoid a known react-aria `Form` + controlled-`useController` interaction bug. The endpoints rows include a protocol `<select>`, so the same constraint applies. |
| 9 | **Codegen must be re-run** and the regenerated `src/generated/openapi.ts` + `openapi-snapshot.json` committed. | Service types are not yet in the generated client; the web image build compiles TS, so missing types break gate-4. |
| 10 | Client validation is **advisory**; the backend stays the authority. Field-level `ProblemDetails` → form via `applyProblemDetailsToForm`; everything else (incl. per-endpoint URL rejections) → toast. | Nested-array field mapping (`endpoints[2].url`) is out of scope; the backend's stricter cross-platform URL rule (absolute + non-empty authority) is surfaced as a toast, not a per-row error. |
| 11 | List sort allowlist = `createdAt`, `displayName` (derived from the `ListServices` OpenAPI operation, single source of truth). **Default `displayName desc`.** | General list convention (user preference, 2026-06-20): default sort by name, descending. Allowlist matches the backend `ServiceSortField` (ADR-0095). NB: the existing Applications list still defaults `createdAt desc` — realigning it is a separate, out-of-scope change. |
| 12 | Register button gated on `KartovaPermissions.CatalogServicesRegister`; list/detail require `CatalogRead` (enforced server-side; the nav link is not separately gated). | Mirrors the Applications register-button gate and the catalog read model. |

---

## 4. Architecture

### 4.1 Routes & navigation added by this slice

```
/catalog/services          → ServicesListPage   (NEW route)
/catalog/services/:id      → ServiceDetailPage   (NEW route)
Sidebar: Services          → NavItemLink to="/catalog/services"  (was DisabledItem)
```

### 4.2 Data flow

```
ServicesListPage
  ├ useListUrlState({ defaultSortBy:"displayName", defaultSortOrder:"desc",
  │                   allowedSortFields:["createdAt","displayName"] })
  ├ useServicesList({ sortBy, sortOrder })  → GET /catalog/services  → CursorPage<ServiceResponse>
  ├ useTeamsList({ limit:200 })             → teamNameById map for the Team column
  └ <ServicesTable> rows → Link to /catalog/services/:id
                         → "Register Service" button (perm-gated) → <RegisterServiceDialog>

RegisterServiceDialog
  ├ RHF (displayName, description)   + local useState (selectedTeamId, endpoints[])
  ├ <EndpointsEditor> add/remove rows {url, protocol}, 0..50
  └ submit → useRegisterService → POST /catalog/services
            → success: invalidate serviceKeys.all, toast, close
            → error:   applyProblemDetailsToForm (field) | toast (fallback / endpoint / 422 invalid-team / 403)

ServiceDetailPage
  └ useService(id) → GET /catalog/services/{id}
       loading → skeleton card; error/not-found → not-found card
       loaded  → header (DisplayName + <HealthBadge>), Description,
                 metadata grid (ID, Team, Created by <CreatedByLink>, Created, Version),
                 endpoints table (URL · Protocol) | "No endpoints" empty state
```

### 4.3 File map

**Created (frontend):**

| File | Purpose | ~LOC |
|---|---|---|
| `web/src/features/catalog/api/services.ts` | `serviceKeys`, `useServicesList` (`useCursorList`), `useService` (`useQuery`), `useRegisterService` (`useMutation`). Sort-param types derived from `operations["ListServices"]`. Exports `ServiceResponse`, `Health`. | 80 |
| `web/src/features/catalog/schemas/registerService.ts` | zod: `displayName` (1..128), `description` (1..4096), `teamId` (uuid), `endpoints` array of `{ url: non-empty + URL-parseable, protocol: enum }`, **max 50**. | 35 |
| `web/src/features/catalog/health.ts` | `healthColor(h)` + `healthLabel(h)` maps for the 4 enum values (mirrors `lifecycle.ts`). | 20 |
| `web/src/features/catalog/components/HealthBadge.tsx` | `Badge` pill from `health.ts`. | 25 |
| `web/src/features/catalog/components/EndpointsEditor.tsx` | Controlled list: rows of URL `Input` + Protocol `<select>` (`Rest/Grpc/GraphQL/WebSocket/Tcp/Other`), add/remove, disabled at 50, per-row inline error slot. | 90 |
| `web/src/features/catalog/components/RegisterServiceDialog.tsx` | Mirror `RegisterApplicationDialog`; embeds `EndpointsEditor`; team `<select>`; ProblemDetails→form + toast fallback. | 150 |
| `web/src/features/catalog/components/ServicesTable.tsx` | DataTable: Name (link→detail, sortable), Health (`HealthBadge`), Team (link), Created by (`CreatedByLink`), Endpoints (count), Created (sortable); loading + empty states. | 110 |
| `web/src/features/catalog/pages/ServicesListPage.tsx` | `useListUrlState` + `useServicesList` + `useTeamsList` + `<ServicesTable>` + Register button (perm-gated) + error card. | 70 |
| `web/src/features/catalog/pages/ServiceDetailPage.tsx` | Read-only detail; loading/not-found states; endpoints table; metadata grid. | 120 |

**Created (tests — gate-5 frontend artifacts):**

| File | Purpose |
|---|---|
| `web/src/features/catalog/api/__tests__/services.test.tsx` | Hook tests: list page fetch, detail fetch, register success invalidation/error. |
| `web/src/features/catalog/schemas/__tests__/registerService.test.ts` | Schema happy + negatives (empty name, >128, bad url, >50 endpoints, bad teamId). |
| `web/src/features/catalog/components/__tests__/EndpointsEditor.test.tsx` | Add/remove rows, 50-cap disables add, protocol change. |
| `web/src/features/catalog/components/__tests__/RegisterServiceDialog.test.tsx` | Submit happy, team-required, validation, ProblemDetails→field + toast fallback. |
| `web/src/features/catalog/components/__tests__/ServicesTable.test.tsx` | Rows + links, Health badge, endpoints count, empty, sort change. |
| `web/src/features/catalog/pages/__tests__/ServiceDetailPage.test.tsx` | Loading, loaded (endpoints + Unknown health), not-found. |

**Modified:**

| File | Change |
|---|---|
| `web/src/app/router.tsx` | Add `/catalog/services` + `/catalog/services/:id` routes (import the 2 pages). |
| `web/src/components/layout/Sidebar.tsx` | Replace `<DisabledItem label="Services" />` with `<NavItemLink to="/catalog/services" label="Services" />`. |
| `web/src/components/layout/__tests__/Sidebar.test.tsx` (if present) | Assert Services renders as an active link, not disabled. |
| `web/src/generated/openapi.ts` + `web/openapi-snapshot.json` | Regenerated via `npm run codegen` (generated — excluded from LOC). |

**Estimate ≈ 700 LOC hand-written production** (excludes tests, generated client). Under the ~800 ceiling, on the high end. **Fallback if it creeps over during implementation:** split the Register dialog + `EndpointsEditor` + `registerService.ts` schema (~275 LOC) into an immediate follow-up sub-slice, leaving list + read-only detail shippable on its own.

---

## 5. Components

### 5.1 `api/services.ts` (mirror `applications.ts`)

```ts
type ServiceResponse = components["schemas"]["ServiceResponse"];
type Health = ServiceResponse["health"];
type ListServicesQuery = NonNullable<operations["ListServices"]["parameters"]["query"]>;

type ServicesListParams = {
  sortBy: NonNullable<ListServicesQuery["sortBy"]>;     // "createdAt" | "displayName"
  sortOrder: NonNullable<ListServicesQuery["sortOrder"]>;
  limit?: number;
};

export const serviceKeys = {
  all: ["services"] as const,
  list: (p?: ServicesListParams) => p ? [...serviceKeys.all, "list", p] as const : [...serviceKeys.all, "list"] as const,
  detail: (id: string) => [...serviceKeys.all, "detail", id] as const,
};

export function useServicesList(params: ServicesListParams) { /* useCursorList → apiClient.GET("/api/v1/catalog/services", …) */ }
export function useService(id: string) { /* useQuery enabled:id!=="" → GET /catalog/services/{id} */ }
export function useRegisterService() { /* useMutation → POST /catalog/services; onSuccess invalidate serviceKeys.all */ }
```

### 5.2 `EndpointsEditor.tsx`

Controlled component: `value: EndpointDraft[]`, `onChange`, optional `disabled`. Each row = URL `Input` + Protocol native `<select>` (matching the team-select pattern already used in `RegisterApplicationDialog`). "Add endpoint" button appends `{ url: "", protocol: "Rest" }`; disabled when `value.length >= 50`. Per-row remove button. The protocol options are the 6 `Protocol` enum members. No RHF — pure props in/out so the parent owns submit-time validation.

### 5.3 `RegisterServiceDialog.tsx` (mirror `RegisterApplicationDialog`)

Same skeleton: `ModalOverlay`/`Modal`/`Dialog`, RHF for `displayName`+`description`, `selectedTeamId` + `endpoints` in `useState`, reset on close. Submit:
1. require `selectedTeamId` (else inline team error);
2. validate each non-empty endpoint row with `registerServiceSchema` (drop fully-empty trailing rows; surface row errors inline);
3. `mutateAsync({ displayName, description, teamId, endpoints })`;
4. success → toast + `onOpenChange(false)`; error → `applyProblemDetailsToForm` for `displayName`/`description`, else toast (`invalid-team`, `403`, endpoint URL rejections).

### 5.4 `ServiceDetailPage.tsx` (read-only; simpler than `ApplicationDetailPage`)

```
<Card>
  <CardHeader>  {displayName}  <HealthBadge health={svc.health} />  </CardHeader>
  <CardContent>
    Description (or italic "No description")
    <hr/>
    grid: ID(mono) · Team(link→/teams/:id) · Created by(<CreatedByLink>) · Created · Version
    <hr/>
    Endpoints: <Table> URL · Protocol  | empty → "No endpoints registered"
  </CardContent>
</Card>
```
Loading → skeleton card (reuse the `ApplicationDetailPage` skeleton shape). Error/`!data` → "Service not found" card.

### 5.5 `health.ts`

```ts
export const healthLabel = (h: Health) => ({ unknown:"Unknown", healthy:"Healthy", degraded:"Degraded", unhealthy:"Unhealthy" }[h]);
export const healthColor = (h: Health) => ({ unknown:"gray", healthy:"success", degraded:"warning", unhealthy:"error" }[h]); // Badge color tokens
```
(Exact `Badge` color token names verified against `badges.tsx` during implementation.)

---

## 6. Error handling (frontend surfacing)

No new `ProblemDetails` types — the backend mapping from S-01 is reused. Frontend behavior:

| Backend response | UI behavior |
|---|---|
| 400 validation-failed (name/desc) | `applyProblemDetailsToForm` → field error under the input. |
| 400 bad endpoint url / malformed | toast (no per-endpoint field mapping). |
| 403 (lacks `catalog.services.register`, or non-member of team) | toast; Register button hidden when the caller lacks the register permission. |
| 422 invalid-team | toast "team does not exist in this tenant". |
| 404 on detail GET | "Service not found" card (deleted / not visible in tenant). |
| list fetch error | error card with Reset (mirror `CatalogListPage`). |

---

## 7. Testing strategy (gate-5 artifacts)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md).

**Backend real seam — already satisfied by S-01, no new artifacts.** This slice adds **no** backend HTTP/auth/DB/middleware code; the three `/catalog/services` endpoints and their authz/RLS are covered by the existing `RegisterServiceTests`, `GetServiceByIdTests`, `ListServicesPaginationTests`, and `CatalogPermissionMatrixTests` (real `JwtBearer` + real Postgres/RLS via `KartovaApiFixtureBase`). The Testing-Strategy real-seam rule therefore applies to S-01's artifacts, which are green on master; it imposes no new backend test here.

**Frontend (Vitest)** — mirrors the Application UI precedent, ≥1 happy + ≥1 negative per unit:
- `services.test.tsx` — list fetch maps rows; detail fetch returns service; register success invalidates `serviceKeys.all`; register error surfaces.
- `registerService.test.ts` — valid input passes; rejects empty/over-long name, bad url, >50 endpoints, non-uuid team.
- `EndpointsEditor.test.tsx` — add appends a row; remove drops it; `add` disabled at 50; protocol change propagates.
- `RegisterServiceDialog.test.tsx` — happy submit calls mutation with mapped payload; missing team blocks submit with inline error; `ProblemDetails` 400 maps to the field; non-field error toasts.
- `ServicesTable.test.tsx` — renders rows + detail links + Health badge + endpoint count; empty state; sort-change callback fires for `displayName`/`createdAt`.
- `ServiceDetailPage.test.tsx` — skeleton while loading; loaded shows endpoints + `Unknown` health + metadata; not-found card on error.

**Gate-4 container build:** no Dockerfile/`COPY` change, but the web image compiles TS — so the regenerated `openapi.ts` **must be committed** or the build fails. Verify `npm run build` (tsc + vite) green locally before push.

**Manual verification (ADR-0084):** Playwright MCP cold-start dev server → navigate `/catalog/services` → register a service (with and without endpoints) → open detail → check console clean. The checked-in Playwright E2E suite (E-01.F-02.S-03) is not yet started, so no new automated E2E is introduced here.

---

## 8. Definition of Done

The eight always-blocking gates + conditional mutation gate as defined in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; this slice does not restate them.

**Mutation gate (6) does NOT apply** — the diff touches only frontend TypeScript; no C# Domain/Application logic changes. (Stryker.NET targets the .NET modules.) Note this as the documented skip reason.

Run `scripts/ci-local.sh frontend` (and the web image build) green before push. Steps requiring a running API (codegen, Playwright MCP) are flagged *pending user verification* if Docker/dev-stack is unavailable in-session.

---

## 9. Out of scope (explicit deferrals)

- **Consumers / dependency list / mini dependency-graph** on the detail page → E-04.
- **Health write path** (probe/agent ingestion) → E-15 / E-16; health stays `Unknown` and read-only.
- **Health scorecard / completeness** → E-10.
- **Edit service metadata, endpoint add/remove after create, lifecycle transitions, team reassign** → later E-02.F-02 stories (no Service domain support exists yet).
- **APIs tab** (sync/async API entities) → E-02.F-03; **Documentation tab** → E-11; **Deployments** → E-02.F-05.
- **Tags** → E-03.F-04; **search/filter by type/team/tags** → E-05.
- Tabbed detail layout, "Open full graph", quick links — the rich mockup chrome is deferred with its backing epics.
- Renaming/segmenting `/catalog` into an Applications-vs-Services tabbed shell.

---

## 10. Implementation order (rough — finalised by writing-plans)

1. **Codegen:** run the API, `npm run codegen`, commit regenerated `openapi.ts` + `openapi-snapshot.json`; confirm `ServiceResponse`/`ListServices`/`GetServiceById` types present.
2. `schemas/registerService.ts` + schema tests (RED→GREEN).
3. `health.ts` + `HealthBadge.tsx`.
4. `api/services.ts` + hook tests.
5. `components/EndpointsEditor.tsx` + tests.
6. `components/RegisterServiceDialog.tsx` + tests.
7. `components/ServicesTable.tsx` + tests.
8. `pages/ServicesListPage.tsx`.
9. `pages/ServiceDetailPage.tsx` + tests.
10. Wire `router.tsx` routes + promote `Sidebar.tsx` Services nav (+ sidebar test).
11. `npm run build` + `scripts/ci-local.sh frontend` green; Playwright MCP manual pass; push, open PR, run DoD gates.

---

## 11. Self-review

**Spec coverage:** every §3 decision traces to §4–§7; every gate-5 frontend artifact in §7 is a named test file in §4.3 that writing-plans will turn into a task.

**Placeholder scan:** no TBD/TODO. §5 code is illustrative; final code lands in executing-plans. Two details marked for in-implementation verification (Badge color tokens in §5.5; sidebar test existence in §4.3) — both are mechanical confirmations, not open design questions.

**Internal consistency:**
- `ServiceResponse` field set (`health`, `endpoints[{url,protocol}]`, `createdBy`, `version`) consistent across §2, §5.1, §5.4.
- Sort allowlist `createdAt|displayName` and **default `displayName desc`** consistent across §3 (#11), §4.2, §5.1, §7.
- Endpoints cardinality `0..50` consistent across §3 (#8), §5.2, §5.3, §7.
- "frontend-only, no backend change" consistent across §1, §3 (#1), §7, §8.

**Scope check:** single PR; 9 created + 4 modified production files + 6 test files; ~700 LOC hand-written production, under the 800 ceiling with a named split fallback (§4.3). No decomposition required.

**Ambiguity check:** "health and consumers" resolved — health = read-only badge of the live enum (`Unknown`), consumers = deferred to E-04 (§3 #4–#5, §9). Endpoints editor state ownership resolved to local `useState` (§3 #8) with a cited rationale.

**No blocking issues found.**
