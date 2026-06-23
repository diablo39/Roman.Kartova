# Applications List — Team & Lifecycle Multi-Select Filters

**Date:** 2026-06-23
**Status:** Design — approved, pending spec review
**Branch:** `feat/applications-filter-team-lifecycle`
**Owning feature:** E-02.F-01 (Catalog list surface) — pulls the team/lifecycle facets forward from E-05 (faceted search)
**ADRs:** ADR-0107 (filter mandate + FilterBar UI), ADR-0095 (cursor + opaque `f` filter-map), ADR-0094 (react-aria UI stack), ADR-0073 (enforced entity lifecycle / decommissioned default-view)

---

## 1. Goal

Replace the Applications list's `displayNameContains` + `includeDecommissioned` filter
set with `displayNameContains` (unchanged) + **lifecycle** (multi-select) + **team**
(multi-select). End-to-end: backend query params + frontend controls, URL-backed,
submit-driven — consistent with the FilterBar shipped in PR #39/#40.

This slice **builds the previously-deferred `multi-select` FilterBar control** (ADR-0107
clause 1, reserved type) plus the multi-value URL-state axis it needs, as reusable infra,
and is the first consumer of both.

## 2. Scope

**In scope (one slice, ~440 lines production code, under the ~800 ceiling):**

- Base `MultiSelect` control (react-aria) — reusable.
- New multi-value URL axis (`multiFilters`) in `useListUrlState` + `setFilters` widening.
- `FilterSpec` `multi-select` variant + `FilterBar` render/commit/clear branch + `useListFilters` derivation.
- Backend `ListApplications`: add `lifecycle[]` + `teamId[]` query params, predicates,
  cursor `f`-map entries; **remove** `includeDecommissioned`.
- `CatalogListPage` (Applications, `/catalog`) wiring onto the two new filters.
- Filter registry + ADR-0107 as-built note updates.

**Explicitly out of scope (deferred, recorded — never silent):**

- **Services list** (`/catalog/services`): keeps its current `includeDecommissioned`
  boolean. Bringing Services onto a lifecycle multi-select for consistency is a future
  follow-up. The shared generic boolean infra is **retained** for it.
- **date-range control** (ADR-0107 reserved type, "sub-slice 3"): not needed — team and
  lifecycle are both categorical. Stays reserved/deferred. Could later apply to
  `createdAt` if a date filter is requested.
- **Lifecycle / team as sort keys**: explicit opt-out (see §3). Sort allowlist unchanged.
- **Created-by filter**: backend `createdByUserId` param already exists; no UI added here.
- **>200 teams in the team dropdown**: the page already caps the teams lookup at a
  200-item fetch; the team filter inherits that cap. Documented limitation, not addressed
  here (a future typeahead/async-options enhancement).

## 3. Surface decisions (ADR-0107 field-revisit — existing list)

The Applications list already exists; per the field-revisit trigger each affected field
records a decision on all three axes:

| Field | Column | Sort | Filter |
|-------|--------|------|--------|
| Lifecycle | ✓ already shown | ✗ **opt-out** — enum ordinal sort has low user value; YAGNI | ✓ **NEW** `multi-select` (replaces `includeDecommissioned`) |
| Team | ✓ already shown | ✗ **opt-out** — requires a Teams join; YAGNI | ✓ **NEW** `multi-select` |
| `includeDecommissioned` (boolean) | — | — | **REMOVED** — subsumed by the lifecycle filter |
| `displayNameContains` (text) | — | — | unchanged |

## 4. Lifecycle filter semantics (replaces `includeDecommissioned`)

Options = `Active` · `Deprecated` · `Decommissioned` (all three selectable; none
pre-selected).

- **None selected (default):** apply the ADR-0073 default view — exclude
  `Decommissioned` (show Active + Deprecated). This keeps the **unfiltered cursor
  byte-identical** to today (blank ⇒ no `f` key, no extra predicate beyond the existing
  default-view `WHERE`).
- **Some selected:** `lifecycle IN (selected)` exactly — including `Decommissioned` when
  the user picks it. Selecting `Decommissioned` is the new way to reveal decommissioned
  apps (the checkbox's old job).

```
if (lifecycle.Count > 0)  source = source.Where(a => lifecycle.Contains(a.Lifecycle));
else                      source = source.Where(a => a.Lifecycle != Lifecycle.Decommissioned); // ADR-0073
if (teamId.Count > 0)     source = source.Where(a => teamId.Contains(a.TeamId));
```

## 5. Architecture & components

Each unit has one responsibility and a well-defined interface.

### 5.1 `useListUrlState` — multi-value axis
**File:** `web/src/lib/list/useListUrlState.ts` (modify)

- New config field `multiFilters?: readonly string[]` (param names that hold arrays).
- New derived `multiFilters: Record<string, string[]>` read via `params.getAll(key)`.
- `setFilters` signature widens from `{ text, booleans }` to
  `{ text, booleans, multi?: Record<string, string[]> }`. On commit, multi values
  serialize as **repeated params** (`append`); an empty array deletes the key
  (blank ⇒ absent). All three maps commit in **one** navigation (existing single-`setParams`
  rule — looped calls clobber).

**Produces:** `multiFilters` map + widened `setFilters`.

### 5.2 Base `MultiSelect` control
**File:** `web/src/components/base/multi-select/multi-select.tsx` (create)

- Props: `{ name, "aria-label"?, label?, options: { label; value }[], defaultSelectedKeys?: string[], placeholder?, size?, className?, ref? }`.
- Render: react-aria `Button` trigger whose text summarizes selection
  (`placeholder` when empty · the single option label when one · `"N selected"` when many)
  + `Popover` + `ListBox selectionMode="multiple"` with a checkmark per selected row.
- Holds internal `selected: Set<string>` seeded from `defaultSelectedKeys`.
- **FormData bridge:** renders a hidden `<input type="hidden" name={name} value={v}>` for
  each selected value, so `FormData.getAll(name)` returns the selected array. This keeps
  FilterBar's commit logic uniform (everything read from FormData; multi-select uses
  `getAll`).

**Produces:** `MultiSelect` (and reuses the existing `SelectOption` shape).

### 5.3 `FilterSpec` + `FilterBar` + `useListFilters`
**Files:** `web/src/lib/list/filters/types.ts`, `web/src/components/application/filter-bar/FilterBar.tsx`, `web/src/lib/list/filters/useListFilters.ts` (modify)

- `types.ts`: promote `multi-select` to its own variant
  `{ key; type: "multi-select"; label; options: { label; value }[] }`; `date-range`
  stays the reserved bare-union member.
- `FilterBar`: new render branch for `multi-select` — keyed by the committed values
  (joined) so external changes (back/forward, Clear all) re-seed via `defaultSelectedKeys`.
  `commit()` folds `multi[s.key] = data.getAll(s.key).map(String)`; `clearAll()` sets
  `multi[s.key] = []`; both call `setFilters({ text, booleans, multi })`.
- `useListFilters`: derive `multi-select` keys into `queryFilters[key]: string[]`;
  `isActive`/`activeCount` count a multi-select as active when its array is non-empty.

### 5.4 Backend `ListApplications`
**Files:** `ListApplicationsQuery.cs`, `CatalogEndpointDelegates.cs`, `ListApplicationsHandler.cs` (modify)

- **Query/record:** add `IReadOnlyList<Lifecycle> Lifecycle` + `IReadOnlyList<Guid> TeamId`;
  **remove** `bool IncludeDecommissioned`.
- **Endpoint binding:** bind `lifecycle` (repeated query string → parse case-insensitively
  to `Lifecycle`, reject unknown tokens with 400) and `teamId` (repeated query string →
  `Guid.TryParse`, reject malformed with 400). Drop the `includeDecommissioned` bind.
- **Handler predicates:** per §4. Filter-key consistency — URL param, API query param, and
  `f`-map key are identical: `lifecycle`, `teamId`.
- **Cursor `f`-map (ADR-0095):** when a list is non-empty, record
  `f["lifecycle"] = string.Join(",", selected sorted)` and
  `f["teamId"] = string.Join(",", ids sorted)` — sorted so identity is order-independent;
  key **absent** when the selection is empty. Remove the `includeDecommissioned` `f` entry.

### 5.5 `CatalogListPage` wiring
**Files:** `web/src/features/catalog/pages/CatalogListPage.tsx`, `web/src/features/catalog/api/applications.ts` (modify)

- `FILTER_SPECS` = displayNameContains (text) + lifecycle (multi-select; options
  `Active`/`Deprecated`/`Decommissioned` with wire values `active`/`deprecated`/`decommissioned`)
  + team (multi-select; options mapped from the existing `useTeamsList` 200-fetch →
  `{ label: displayName, value: id }`). Remove the `includeDecommissioned` boolean spec.
- `useListUrlState({ textFilters:["displayNameContains"], multiFilters:["lifecycle","teamId"] })`
  (drop `booleanFilters`).
- `useApplicationsList` params gain `lifecycle?: string[]` + `teamId?: string[]`, drop
  `includeDecommissioned`; thread from `filters.queryFilters`. Empty array ⇒ param omitted.
- Filtered empty-state copy: `filters.isActive ? "No applications match your filters" : <existing>`.

## 6. Data flow

1. User opens/closes the Filters panel, selects teams/lifecycles (local, zero React renders
   for text; multi-select holds its own draft state + hidden inputs), clicks **Search** /
   Enter.
2. `FilterBar.commit()` reads FormData (text/getAll for multi) → `setFilters` → one URL
   navigation with repeated params.
3. `useListUrlState` re-derives `multiFilters`; `useListFilters` exposes `queryFilters`;
   `CatalogListPage` threads arrays into `useApplicationsList` → `apiClient.GET` with
   repeated query params.
4. Backend parses/validates, applies predicates, records the `f`-map, returns
   `CursorPage<ApplicationResponse>`. Changing a filter mid-pagination ⇒ `f`-map mismatch
   ⇒ 400 `cursor-filter-mismatch` (ADR-0095).

## 7. Error handling

- Unknown `lifecycle` token or malformed `teamId` ⇒ 400 (validation), consistent with the
  existing `limit`/`sortBy`/`createdByUserId` validation in the endpoint.
- Empty arrays ⇒ params omitted (no error, default view).
- Frontend: existing list error path (error-logging `useEffect` + table error state)
  unchanged.

## 8. Testing strategy

Applies [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). This slice wires HTTP + DB,
so it MUST hit the **real seam** (real Postgres/RLS + real `JwtBearer` via
`KartovaApiFixtureBase` — never the fake auth handler or a mocked DbContext).

**Gate-3 real-seam integration tests (named deliverables):**
- `ListApplications` lifecycle filter — **happy:** select `Deprecated` ⇒ only deprecated
  rows; **edge:** empty selection ⇒ decommissioned hidden (ADR-0073 default preserved);
  **reveal:** select `Decommissioned` ⇒ decommissioned rows returned.
- `ListApplications` team filter — filter to a subset of `teamId`s ⇒ only those teams' apps
  (RLS-scoped).
- Combined lifecycle + team filter ⇒ intersection.
- **Cursor `f`-map mismatch:** page 1 with a filter, then alter the filter on the next
  cursor ⇒ 400 `cursor-filter-mismatch`.
- These extend / migrate the existing `ListApplicationsPaginationTests` and
  `ListApplicationsHandlerFilterTests` (which reference the removed `includeDecommissioned`).

**Frontend unit tests (named deliverables):**
- `MultiSelect` control — seeds from `defaultSelectedKeys`; selecting/deselecting updates
  `FormData.getAll`; summary text (empty/one/many).
- `FilterBar` multi-select — render, commit via `getAll`, Clear all empties it, seed from
  committed values; the existing "throws for multi-select" test is replaced.
- `useListFilters` — multi-select `queryFilters` array + `isActive`/count.
- `useListUrlState` — multi-value read (`getAll`), repeated-param serialize, blank ⇒ absent.
- `CatalogListPage` — lifecycle + team thread to `apiClient.GET` (repeated params); filtered
  empty-state; default (no selection) omits the params.

**Gate 6 (mutation): BLOCKING** for this slice — the diff touches `ListApplicationsHandler`
Application-layer logic (predicate + `f`-map). Run `/misc:mutation-sentinel` →
`/misc:test-generator` on the changed backend files; target ≥80%; document survivors.

**Gate 4 (container build): N/A** — no Dockerfile/`COPY` change.

**OpenAPI snapshot:** removing `includeDecommissioned` and adding `lifecycle[]`/`teamId[]`
changes the contract → regenerate `web/openapi-snapshot.json` via the predev/prebuild
codegen and commit it.

## 9. Definition of Done

This slice is "complete" only when CLAUDE.md's eight always-blocking gates (gate 6
**blocking** here) are green and citable by command + output, including the terminal
re-verify. This section links those gates — it does not restate them.

## 10. Decomposition note

Considered splitting into lifecycle-first + team-second sub-slices; the human chose a
single slice (one DoD cycle) given the total stays under the ~800-line ceiling. The
multi-select control + URL axis is built once and consumed by both filters in this slice.
