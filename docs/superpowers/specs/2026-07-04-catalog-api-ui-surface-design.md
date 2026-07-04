# Slice — Catalog: API UI surface + list filters

**Date:** 2026-07-04
**Story / work item:** E-02.F-03 · **FU-9** (registered in `2026-07-03-catalog-api-entity-design.md` §11)
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-api-ui-surface`
**Governing decisions:** [ADR-0111](../../architecture/decisions/ADR-0111-api-first-class-entity-provider-instance-fields.md) (API first-class entity), [ADR-0094](../../architecture/decisions/ADR-0094-frontend-ui-stack.md) (Untitled UI), [ADR-0095](../../architecture/decisions/ADR-0095-cursor-pagination-contract.md) (cursor list), [ADR-0107](../../architecture/decisions/ADR-0107-list-filter-mandate.md) (filter mandate), [ADR-0084](../../architecture/decisions/ADR-0084-frontend-verification.md) (browser verification), [ADR-0109](../../architecture/decisions/ADR-0109-enum-camelcase-wire.md) (enum camelCase wire)

---

## 1. Goal

Make the sync `Api` entity — shipped backend-only in **E-02.F-03.S-01** (PR #55, 2026-07-04) — **visible and usable** in the web UI, and **build the three deferred list filters**. A developer can browse `/catalog/apis` (cursor-paginated, sortable, filterable), register an API through a dialog, and open a read-only detail page.

Two cohesive halves in **one slice**:

1. **Backend filter tail** — add the three ADR-0107 filters (name substring, style multi-select, team multi-select) to the existing `ListApis` list endpoint, which shipped sort/cursor only (S-01 design §3 #14 deferred filters to FU-9).
2. **Frontend surface** — list / detail / register React screens mirroring the shipped Service UI surface (S-02, `catalog-service-ui-surface`).

This closes **FU-9**. It creates **no new backend entity, aggregate, or structural link** — the API node's provider/instance/consumer/exposure wiring remains FU-1..FU-8/FU-10/FU-11.

### 1.1 Precedent (mirror, do not invent)

No canonical Stitch mockup exists for API screens (`docs/ui-screens/` has none). Per CLAUDE.md frontend agreement, this slice **mirrors the Service UI surface 1:1** — exactly as Service S-02 "mirrors the Application UI surface". Source files to mirror:

| Frontend concern | Mirror source (Service) |
|---|---|
| API client hooks | `web/src/features/catalog/api/services.ts` |
| Register zod schema | `web/src/features/catalog/schemas/registerService.ts` |
| List table | `web/src/features/catalog/components/ServicesTable.tsx` |
| Register dialog | `web/src/features/catalog/components/RegisterServiceDialog.tsx` |
| List page (filters) | `web/src/features/catalog/pages/ServicesListPage.tsx` |
| Detail page | `web/src/features/catalog/pages/ServiceDetailPage.tsx` |
| Routes | `web/src/app/router.tsx` |
| Nav item | `web/src/components/layout/Sidebar.tsx` |

Backend filter mirror source: `ListServicesQuery` / `ListServicesHandler` / `ListServicesHandlerFilterTests` / `ListServicesPaginationTests`.

---

## 2. Pre-requisites (already on master)

- **API node backend** (S-01, PR #55): `Api` aggregate, `ApiStyle {Rest,Grpc,GraphQL}`, `POST/GET-by-id/GET-list /api/v1/catalog/apis`, RLS `catalog_apis`, `catalog.apis.register` permission (5-sync done), `api.registered` audit, `ApiSortField {DisplayName,Style,Version,CreatedAt}`, `ApiResponse` DTO (with `CreatedBy` enrichment).
- **List infra:** `ToCursorPagedAsync`, f-map cursor codec, `CursorFilterMismatchException`, `LikeEscaping` (ADR-0095).
- **Filter infra (backend):** `ListServicesHandler` reference impl for `Array.Contains → = ANY` + f-map encoding of multi-select + ILIKE substring.
- **Filter infra (frontend):** `useListUrlState`, `useListFilters`, `<FilterBar>`, `<MultiSelect>`, `multiFilters` repeated-param URL axis, `useCursorList`, `<DataTable>` / react-aria `<Table>`.
- **Read permission:** `catalog.read` already grants GET on apis; no new permission this slice.
- **Codegen:** `openapi-fetch` generated client + committed `openapi-snapshot.json` fallback; `predev`/`prebuild` regenerate from live API (project memory: web-codegen + openapi-snapshot).

---

## 3. Design decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **One slice**: backend filter tail + full frontend surface. | Matches how Service S-02 shipped the whole surface at once; backend delta is a near-verbatim `ListServices` mirror. Fallback (only if past the ~800 ceiling): split backend-filters PR from UI PR. |
| 2 | **All three filters built now** (name typeahead · style multi-select · team multi-select). No deferral. | User decision. Fulfils the FU-9 pre-registered filter proposal; matches Applications/Services filter parity. |
| 3 | **Backend `ListApis` gains filter params**: `Guid[] TeamId`, `ApiStyle[] Style`, `string? DisplayNameContains`. | The list endpoint currently has none (S-01 §3 #14). Filters must be applied server-side before pagination (ADR-0095/0107). |
| 4 | Filters applied **before** pagination; encoded into the cursor **f-map** (sorted → canonical) so a mid-pagination filter change trips `CursorFilterMismatchException`. | Same invariant as `ListServicesHandler` — a hidden row must never become a cursor boundary. |
| 5 | `Style` multi-select is an **enum-in-set** filter (`ApiStyle[]`), implemented exactly like Service's `Health[]` (`Array.Contains(column) → = ANY(@p)`; f-map = sorted enum names). | Direct mirror. `ApiStyle` is a small closed enum — no join needed (unlike team). |
| 6 | Style filter **wire values reuse existing `ApiStyle` camelCase serialization** (ADR-0109) — same values `ApiResponse.style` already emits. No new casing. | Wire consistency; the client already knows these values from `ApiResponse`. |
| 7 | Team multi-select fetches `useTeamsList` (200 cap, same known limit as Services) for options + id→name lookup. | Direct mirror of `ServicesListPage`. |
| 8 | **Detail page = header + metadata only**: displayName, style badge, version, spec URL (clickable external link when present, `rel="noopener noreferrer"`), owning team, created-by chip, created-at. **No relationships/provider/consumer/exposure section.** | Those links don't exist yet (FU-1/FU-3/FU-5). Exactly how Service detail shipped (health badge only, consumers deferred). YAGNI — no dead scaffolding. |
| 9 | **Register dialog fields**: displayName, description, style `<select>` (Rest/Grpc/GraphQL), version, spec URL (optional), team `<select>` + read-only created-by chip. RHF + zod; server `ProblemDetails` → field errors via `applyProblemDetailsToForm`. | Mirrors `RegisterServiceDialog` (swap the endpoints editor for style/version/specUrl scalar inputs). |
| 10 | List table columns: **displayName (`isRowHeader`)** · style badge · version · team (name via lookup) · created-by · created-at. Sortable per S-01 allowlist `{displayName(default asc), style, version, createdAt}`. | react-aria `<Table>` needs exactly one `isRowHeader` col (ADR-0084 footgun). Sort allowlist fixed by S-01 — unchanged. |
| 11 | **Nav**: promote an "APIs" item in `Sidebar.tsx` (as Services was promoted from disabled). Route `/catalog/apis` (list) + `/catalog/apis/:id` (detail) in `router.tsx`. | Discoverability; mirrors Services nav/routing. |
| 12 | **Codegen sequencing (hard dependency)**: land backend filter change → rebuild API image → regenerate `openapi-snapshot.json` + generated client → build frontend against the new `operations["ListApis"]` query params. | New filter params are new OpenAPI. Building the frontend against a stale client fails `tsc -b` (the binding type gate). Project memory: rebuild API image to expose new endpoints. |
| 13 | Zod `registerApiSchema`: displayName (1..128), description (1..4096), style (enum), version (1..64), specUrl (optional; when present, absolute URL — mirror backend `Api.Create` `ValidateSpecUrl`), teamId (uuid). | Client-side mirror of the domain invariants (S-01 §5.1); server remains source of truth (422/400 still surface). |

---

## 4. Backend architecture

### 4.1 Query + handler delta (mirror `ListServices`)

```csharp
// ListApisQuery.cs — add filter dimensions
public sealed record ListApisQuery(
    ApiSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    Guid[] TeamId,
    ApiStyle[] Style,
    string? DisplayNameContains = null);
```

`ListApisHandler.Handle`:
1. `IQueryable<Api> source = db.Apis;`
2. `if (q.TeamId.Length > 0) source = source.Where(a => q.TeamId.Contains(a.TeamId));`
3. `if (q.Style.Length > 0) source = source.Where(a => q.Style.Contains(a.Style));`
4. `if (q.DisplayNameContains is { } name) source = source.Where(a => EF.Functions.ILike(a.DisplayName, $"%{LikeEscaping.EscapeLike(name)}%", "\\"));`
5. Build f-map dict only for non-empty dimensions: `teamId` = sorted `Guid.ToString("D")`, `style` = sorted enum names, `displayNameContains` = trimmed value.
6. `ToCursorPagedAsync(..., expectedFilters: filters)` then existing creator enrichment.

### 4.2 Endpoint delegate binding

`CatalogEndpointDelegates.ListApisAsync` — bind repeated query params `style` (→ `ApiStyle[]`), `teamId` (→ `Guid[]`), and `displayNameContains` (→ `string?`), mapping into the new `ListApisQuery` fields. Mirror `ListServicesAsync` binding (repeated-param arrays + trim/normalize blank → null for the text filter).

### 4.3 Error semantics (reuse existing handlers)

| Case | Status | Type |
|---|---|---|
| Invalid `sortBy` (outside allowlist) | 400 | `InvalidSortFieldException` → `PagingExceptionHandler` |
| Bad limit | 400 | `invalid-limit` |
| Cursor/filter mismatch mid-pagination | 400 | `CursorFilterMismatchException` |
| Unparseable style value in `style=` | 400 | model-binding / `malformed-request` |

No new ProblemDetails types.

---

## 5. Frontend architecture

```
web/src/features/catalog/
  api/apis.ts                       useApisList(params) · useApi(id) · useRegisterApi()   [mirror api/services.ts]
  schemas/registerApi.ts            registerApiSchema + RegisterApiInput                   [mirror registerService.ts]
  components/ApisTable.tsx          sortable columns; displayName isRowHeader; style badge  [mirror ServicesTable.tsx]
  components/RegisterApiDialog.tsx  RHF+zod; style/version/specUrl + team select + chip     [mirror RegisterServiceDialog.tsx]
  pages/ApisListPage.tsx            useListUrlState + useListFilters + FilterBar + table     [mirror ServicesListPage.tsx]
  pages/ApiDetailPage.tsx           header + metadata (no relationships section)             [mirror ServiceDetailPage.tsx]
  + __tests__/ for each of the above
web/src/app/router.tsx              + /catalog/apis and /catalog/apis/:id routes
web/src/components/layout/Sidebar.tsx  + "APIs" nav item (promoted)
```

### 5.1 List filter wiring (ADR-0107)

```
ALLOWED_SORT_FIELDS = ["displayName", "style", "version", "createdAt"]  // S-01 allowlist
TEXT_FILTERS   = ["displayNameContains"]
MULTI_FILTERS  = ["style", "teamId"]
STYLE_OPTIONS  = [{label:"REST",value:"rest"},{label:"gRPC",value:"grpc"},{label:"GraphQL",value:<ApiStyle camelCase for GraphQL>}]  // reuse ApiResponse.style wire values
```
- `style` multi-select: static options (closed enum).
- `teamId` multi-select: dynamic from `useTeamsList` (200 cap).
- `displayNameContains`: text typeahead.
- List query passes `{sortBy, sortOrder, style, teamId, displayNameContains}`; empty arrays/blank omitted (no predicate).

### 5.2 Codegen

`api/apis.ts` types come from generated `components["schemas"]["ApiResponse"]` + `operations["ListApis"]["parameters"]["query"]` (now including `style`/`teamId`/`displayNameContains`). **Regenerate the client after the backend filter change lands** (§3 #12). `tsc -b` (`npm run build`) is the binding type gate.

---

## 6. Testing strategy (per docs/TESTING-STRATEGY.md)

**Backend (real seam — `KartovaApiFixtureBase`, real Postgres/RLS + real JWT):**
- `ListApisHandlerFilterTests` (unit-ish, real DbContext): each filter predicate; combined filters; empty ⇒ no predicate. [mirror `ListServicesHandlerFilterTests`]
- Extend `ListApisPaginationTests`: each `sortBy` still honored under filters; **each filter honored** (name/style/team); f-map mismatch mid-pagination ⇒ 400; **tenant isolation** (another tenant's APIs never appear); ≥1 happy + ≥1 negative.

**Frontend (mirror Service tests):**
- `api/apis.test.tsx`, `ApisTable.test.tsx`, `RegisterApiDialog.test.tsx`, `ApisListPage.test.tsx`, `ApiDetailPage.test.tsx`.
- **Assert `getAllByRole("rowheader").length > 0`** on the table (ADR-0084 blank-page guard).

**Real-browser (ADR-0084, cold-start dev server first):** navigate to `/catalog/apis` (in-SPA, per bug #47) → list renders → apply each filter → open Register dialog (verify no blank-page from rowHeader) → submit → open a detail page → check console clean. DevSeed has apps/teams but **no APIs** — register one (or seed) to exercise the surface.

---

## 7. Definition of Done

The eight always-blocking gates + conditional mutation gate in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; not restated here.

- **Gate 6 (mutation): blocking** for the **backend** diff (Application/Infrastructure filter-predicate + f-map logic in `ListApisQuery`/`ListApisHandler`/delegate). Frontend excluded from mutation. Target ≥80% on changed backend files; document survivors.
- **Gate 3/4** hit the real seam + container build (backend filter change + regenerated web image).
- Run `scripts/ci-local.sh` (Release mirror) green before push. Stop the vite dev server first (project memory: `ci-local frontend` npm ci vs dev server EPERM).
- DoD ledger + `gate-findings.yaml` under `docs/superpowers/verification/2026-07-04-catalog-api-ui-surface/`.

---

## 8. Registry + checklist updates (on completion)

- `docs/design/list-filter-registry.md` — the `/catalog/apis` row moves from "Planned filtering surfaces (all deferred → FU-9)" to **built**: name typeahead + style multi-select + team multi-select; sort allowlist `{displayName, style, version, createdAt}`, default `displayName asc`.
- `docs/product/CHECKLIST.md` — note under E-02.F-03: FU-9 shipped (API UI surface + 3 list filters). S-02/S-03 and FU-1..FU-8/FU-10/FU-11 remain open.

---

## 9. Out of scope (explicit deferrals)

- Provider/instance/consumer/exposure UI and edges → FU-1..FU-5, FU-10, FU-11.
- Async APIs, unified per-service view → S-02 (FU-7), S-03 (FU-8).
- Edit / lifecycle / delete API → later E-02.F-03 stories.
- Spec rendering (OpenAPI/proto/GraphQL) → E-11. Version history → E-21. Search indexing → E-05.
- New sort fields or additional filter facets beyond the three.

---

## 10. Self-review

**Spec coverage:** §3 decisions trace to §4 (backend), §5 (frontend), §6 (tests); every named test artifact in §6 is a deliverable `writing-plans` turns into a task. Codegen sequencing (§3 #12, §5.2) called out as a hard dependency. No new permission → no 5-sync this slice (read reuses `catalog.read`).

**Placeholder scan:** the only intentional deferred literal is the GraphQL style wire value (`<ApiStyle camelCase for GraphQL>` in §5.1) — resolve by reading the value `ApiResponse.style` emits for `GraphQL` (ADR-0109) during implementation; not a design gap.

**Internal consistency:** sort allowlist identical in §3 #10, §5.1, §8. Detail-page scope (metadata only) consistent §3 #8 / §5 / §9. Backend filter mechanics mirror `ListServices` throughout.

**Scope check:** one slice; backend delta small (mirror), frontend ≈ Service S-02 (one slice). Est. ~450–650 lines production business code (TSX + handler/query/delegate); under the ~800 ceiling — no decomposition. Fallback split noted (§3 #1).

**Ambiguity check:** slice boundary resolved (one slice, §3 #1); filter set resolved (all three, §3 #2); style filter as enum-in-set + wire reuse resolved (§3 #5/#6); detail page resolved (§3 #8).

**No blocking issues found.**

## Impact Analysis (codelens/LSP)

`writing-plans` will populate the enforceable `## Impact Analysis (codelens/LSP)` section. Preview of the one existing-symbol signature change:

- **`ListApisQuery` record** — constructor signature changes (adds `TeamId`, `Style`, `DisplayNameContains`). Callers: `ListApisHandler` (Infra) + the `ListApisAsync` endpoint delegate (both changed in this slice) + `ListApisPaginationTests`. Confirm the full caller set with `find_references` on `ListApisQuery` during planning (expected small, all in the Catalog module). `ListApisHandler.Handle` behavior changes (adds predicates) — its callers are the delegate + tests. No shared-const / cross-module symbol touched; `ApiStyle` (enum) already public and consumed by `ApiResponse`. Non-C# frontend couplings (generated client) covered by §3 #12 sequencing, not codelens.
