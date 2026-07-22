# Design — System UI surface (list + detail + read-only members)

**Story:** E-03.F-03 — System UI surface (frontend for E-03.F-03.S-01, which shipped backend-only)
**Date:** 2026-07-22 · **Author:** Roman Głogowski (AI-assisted)
**ADRs touched:** none changed. Applies ADR-0094 (Untitled UI / react-aria), ADR-0095 (cursor list surface), ADR-0107 (list columns/sort/filters), ADR-0114 (tabbed entity-detail layout), ADR-0084 (browser verification), ADR-0111 (`PartOf` = `{Application,Service}→System` — read-only consumption of existing edges).

---

## 1. Goal

Ship the frontend for the `System` catalog entity. The backend (register/get/list `/systems`, RLS, `catalog.systems.register`, `PartOf` edges) landed in S-01 backend-only. This slice mirrors the existing **Service/API UI surfaces**: a Systems list page, a Register-System dialog, and a tabbed read-only detail page whose second tab lists the components already assigned to the System via incoming `PartOf` edges.

Frontend, plus **one 3-line backend OpenAPI-doc fix** (discovered at execution): S-01 never registered `ListSystems` in `CursorListQueryParameterTransformer`, so its `sortBy`/`sortOrder`/`limit` publish as loose `string` instead of typed enums + bounded int, breaking the typed client mirror. Registering it is a doc-shape-only change (runtime binding unchanged; behavior already covered by S-01 `ListSystemsPaginationTests`) — **gate 5 stays N/A**. No contract or permission change.

## 2. Locked decisions

| Decision | Choice | Rationale |
|---|---|---|
| Pattern source | Mirror the Service UI surface | Closest sibling; `SystemResponse` ≈ `ServiceResponse` minus health/endpoints/version |
| Detail layout | `DetailTabs`: **Overview · Members** | ADR-0114 tabbed convention, consistent with Service/API; only-active-panel mounts |
| Members section | **Read-only**, from `GET /relationships?entityKind=system&direction=incoming`, client-side filtered to `type==="partOf"`, mapped over `r.source` | Backend resolves `System` in `CatalogEntityLookup`; write rules restrict incoming-to-System edges to `PartOf` but the **read path applies no type filter**, so filter client-side for drift tolerance (backfill/direct-write), and guard the member-kind with `isRelationshipKind` before building a detail link (the relationship read contract can carry kinds — e.g. `system`/`api` — outside the FE's 3-member `RelationshipKind`; render plain text otherwise, never a blind cast) |
| Assignment (create/remove `PartOf`) | **Out of scope** | No FE path today; needs `relationshipTypeRules` + `AddRelationshipDialog` extended (touches ADR-0111) → future rel-UI slice / S-02 |
| Permission | Reuse `catalog.systems.register` (write) + `catalog.read` (read) | Both already live from S-01's 5-sync; **no new permission** |
| Default sort | `displayName asc` | Standard for name-bearing lists (CLAUDE.md list convention) |

## 3. Non-goals / out of scope (explicit)

- Assign/remove components to a System (create/delete `PartOf`) → deferred to a rel-UI slice / S-02.
- System node rendering in the graph explorer / mini-graph → follow-up **FU-A**. NB: there is **no** downstream kind-filter today, so a `system` (or `api`) node returned by `/graph` currently renders with a raw label + broken detail-nav (`/catalog/undefined/{id}`). This is **pre-existing** (live since S-01's `PartOf` edges; shared with `graphModel.ts:80`), not introduced here — this slice only made the generated kind union widen (forcing the `graphMerge.ts` cast). Proper filtering/rendering is FU-A's job.
- Browse catalog by Org/Team/System hierarchy → **E-03.F-03.S-02**.
- `memberCount` column on the list (derived aggregate; would need a backend count) → deferred, flagged not silent.
- Description column on the list (too long for a row) → not needed.
- Edit/rename/delete System → not scoped (no backend mutation beyond register exists).

## 4. Components / changes (all `web/`)

### 4.1 Data layer
- **New** `api/systems.ts` — `useSystemsList` / `useSystem` / `useRegisterSystem`, `systemKeys`. Direct mirror of `api/services.ts`; list params `{ sortBy, sortOrder, limit?, teamId?: string[], displayNameContains? }`.
- **New** `schemas/registerSystem.ts` — zod `RegisterSystemInput`: `displayName` (trim, 1–128), `description` (optional, ≤4096), `teamId` (uuid). Mirror `registerService.ts` minus endpoints.

### 4.2 Components
- **New** `components/SystemsTable.tsx` — `<DataTable>` columns: `displayName` (row header, link to detail, sortable), `team` (name via `teamNameById`), `createdBy` (`CreatedByLink`), `createdAt` (sortable). Mirror `ServicesTable.tsx` minus health.
- **New** `components/RegisterSystemDialog.tsx` — name + description + steward-team picker (raw HTML `<select>`, same as `RegisterServiceDialog` — not a shared `Select` component); submit → `useRegisterSystem`; toast + invalidate. Mirror `RegisterServiceDialog.tsx` minus endpoints editor.
- **New** `components/SystemMembersSection.tsx` — read-only. `useRelationshipsList({ entityKind: "system", entityId, direction: "incoming" })`, then `.filter(r => r.type === "partOf")` (drift tolerance); table rows over `r.source`: name (link via `entityDetailPath`, guarded by `isRelationshipKind` — plain text otherwise) + kind badge (`ENTITY_KIND_LABEL`). Loading skeleton / error line / empty "No components assigned yet." + `TablePager`. No add/delete.

### 4.3 Pages
- **New** `pages/SystemsListPage.tsx` — mirror `ServicesListPage.tsx`: `useListUrlState` (allowed sort `["createdAt","displayName"]`, text filter `displayNameContains`, multi filter `teamId`), `FilterBar`, `useSystemsList`, `SystemsTable`, gated `RegisterSystemDialog` (`CatalogSystemsRegister`). No health filter.
- **New** `pages/SystemDetailPage.tsx` — mirror `ServiceDetailPage.tsx` structure. `DetailTabs`:
  - **Overview**: Description · steward Team (link `/teams/:id`) · Created by (`CreatedByLink`) · Created · ID (mono).
  - **Members**: `<SystemMembersSection systemId={sys.id} />`.
  - Loading skeleton + "System not found" error card, same as Service.

### 4.4 App shell
- **Edit** `components/layout/Sidebar.tsx` — add `<NavItemLink to="/catalog/systems" label="Systems" />` after the APIs item. Update the nav-highlight doc comment (Systems has its own `/catalog/systems` prefix; no cross-highlight).
- **Edit** `app/router.tsx` — `<Route path="/catalog/systems" element={<SystemsListPage />} />` + `/catalog/systems/:id` → `<SystemDetailPage />`.

### 4.5 Codegen
- Regenerate `web/openapi-snapshot.json` + the generated client from the **live API** (predev/prebuild, per the OpenAPI snapshot codegen convention) — the S-01 `/systems` endpoints are not yet in the committed snapshot. Requires rebuilding the API image so the endpoints are exposed. Commit the regenerated snapshot.

## 5. List surface (ADR-0107) — confirmed

| Field | Column? | Sortable? | Filter? |
|-------|---------|-----------|---------|
| displayName | ✓ (row header, link) | ✓ (default `asc`) | ✓ text `displayNameContains` |
| steward team | ✓ (name) | ✗ | ✓ multi-select `teamId` |
| description | ✗ | ✗ | ✗ |
| createdBy | ✓ | ✗ | ✗ |
| createdAt | ✓ | ✓ | ✗ (defer) |

Identical to the Services list minus `health`. Backend `ListSystems` already supports this exact sort allowlist + filter set. **Update the existing Systems row** in `docs/design/list-filter-registry.md` (added at S-01 as `built (API-only…)`) — flip to `built` + `<FilterBar>`-wired; do not add a second row.

## 6. Testing strategy (per docs/TESTING-STRATEGY.md)

Frontend-only slice; the backend seam is already covered by S-01's real-seam integration tests.

**Gate 3 — unit (Vitest + RTL; NO MSW in this repo — stub `apiClient` via `vi.spyOn`, `vi.mock` collaborator hooks + `react-oidc-context`):**
- `schemas/__tests__/registerSystem.test.ts` — valid input, name too long/empty, description too long/blank-optional, bad uuid.
- `api/__tests__/systems.test.tsx` — list query param shaping (teamId/displayNameContains omitted when empty; array when set), detail fetch, register POST + cache invalidation, blank-description → sent as `null` (the generated `RegisterSystemRequest.description` is `string | null`).
- `components/__tests__/SystemsTable.test.tsx` — renders rows, row-header link, loading skeleton, empty state, sort callback wiring.
- `components/__tests__/RegisterSystemDialog.test.tsx` — submit success path, no-team validation error, no-teams disabled.
- `components/__tests__/SystemMembersSection.test.tsx` — query shape, rows (name link + kind badge, row-header present), non-`partOf` filtered out, empty state, loading skeleton, error line.
- `pages/__tests__/SystemsListPage.test.tsx` — heading, register-button gating (shown + hidden), default sort, displayNameContains/teamId → query threading, empty-with-filters state.
- `pages/__tests__/SystemDetailPage.test.tsx` — Overview fields, tab switch to Members, loading skeleton, not-found card. **ADR-0084 row-header rule** is asserted on the *populated* Members table in `SystemMembersSection.test.tsx` (the detail page's empty-members case renders no table); this suite confirms the tab mounts the section.

**Gate 5 — real-seam integration:** **N/A — frontend-only slice; no new HTTP/auth/DB/middleware seam** (register/get/list `/systems` seams already covered by `RegisterSystemTests` / `GetSystemSurfaceTests` / `ListSystemsPaginationTests` from S-01). Same rationale as the `catalog-service-ui-surface` slice.

**Gate 6 — mutation:** **N/A** — no Domain/Application logic changed.

**Gate 10 — browser (ADR-0084):** cold-start dev server, authenticate, navigate in-SPA to `/catalog/systems`, register a System, open its detail, switch to Members tab; screenshot + 0 console errors. (DevSeed has no Systems yet → verify empty-members state; optionally seed one `PartOf` edge to verify a populated member row.)

## 7. Definition of Done

The eleven CLAUDE.md gates apply as written (not restated). Frontend-only ⇒ gate 5 and gate 6 are **N/A with the reasons above**; gate 4 (container/`images` job) still runs (web image). DoD ledger + `gate-findings.yaml` under `docs/superpowers/verification/2026-07-22-catalog-system-ui-surface/`.

## 8. Impact analysis note

No existing C# symbol signature/behavior changes (frontend-only). The plan's `## Impact Analysis (codelens)` section is `N/A — frontend-only slice; no C# symbols changed`. FE reuse touchpoints (`useRelationshipsList`, `entityDetailPath`, `ENTITY_KIND_LABEL`, `useListUrlState`, `FilterBar`, `DataTable`, `DetailTabs`, `CreatedByLink`) are additive consumers, verified present.

## 9. Size estimate

~450–500 LOC production (2 pages + 3 components + 2 data/schema modules + 2 shell edits), well under the ~800 ceiling.
