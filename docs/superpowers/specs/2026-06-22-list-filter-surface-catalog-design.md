# Slice — List-filter surface on the Catalog lists (Services + Applications), + FilterBar boolean control & collapsible panel

**Date:** 2026-06-22
**Stories:** E-02.F-02 (Service management — list filtering) + E-02.F-01 (Application management — list filtering); second consumer of **ADR-0107** (list-filter consideration mandate + standard filter UI). Resolves the `Services` and `Applications` rows in `docs/design/list-filter-registry.md`.
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/list-filter-surface-catalog`

---

## 1. Goal

Apply the ADR-0107 filter surface shipped in #38 (the `<FilterBar>` + `useListFilters` + `useListUrlState` stack, proven on Teams) to the two **Catalog** lists:

- **Services** (`/catalog/services`) — add a **`displayName` text search** (mirror of Teams).
- **Applications** (`/catalog`) — add the same **`displayName` text search**, and **fold the existing standalone `includeDecommissioned` checkbox into `<FilterBar>`** — which requires **building the FilterBar `boolean` control** (reserved-but-unbuilt in #38).

Also **standardize the default sort to `displayName asc`** across both lists (screen + endpoint defaults), matching Teams — the single live precedent (user decision 2026-06-22).

Additionally, **upgrade the shared `<FilterBar>` into a collapsible disclosure panel** — a labeled "Filters" header with an active-filter count and a chevron toggle, **expanded by default**, collapsible on **all** viewports — and make it the standard shell for **every** FilterBar consumer (Teams included). This **extends ADR-0107 clause 6** (which scoped collapse to small viewports only) and ships with an ADR-0107 amendment (§3 #11). Open/closed state is **ephemeral** (component-local, resets to expanded on mount); persistence is a deferred nicety (§8).

Per ADR-0107 clause 1, only the **text** and (now) **boolean** controls are built. `single-select` / `multi-select` / `date-range` stay typed-but-unbuilt; richer Catalog facets (team / lifecycle / health / created-by) are deferred to **E-05 faceted search** (they need select controls).

---

## 2. Pre-requisites (already on master, from #38)

- **Filter surface:** `useListFilters(specs, urlState)` (text draft → commit on `submit()`); `<FilterBar specs filters>` (form + Search button + Enter `onKeyDown`; renders the `text` control, throws on every other `FilterSpec` type); `FilterSpec` discriminated union in `web/src/lib/list/filters/types.ts`.
- **URL state:** `useListUrlState` with `allowedSortFields` + `booleanFilters` + `textFilters` axes; `setTextFilter(name: string, …)` already widened to a `string` key for generic consumers; `setBooleanFilter(name: TBoolFilter, …)` still narrowed.
- **Cursor + filter wire contract (ADR-0095):** `CursorPage<T>`; opaque cursor `{s,i,d,f?}`; `ToCursorPagedAsync(…, expectedFilters:)` replays the `f`-map → `CursorFilterMismatchException` → 400 `cursor-filter-mismatch` on mid-pagination change.
- **Teams reference:** `ListTeamsHandler` (ILIKE filter before paging + `f`-map + private `EscapeLike`), `TeamEndpointDelegates.ListTeamsAsync` (blank→null trim; default `displayName asc`), `ListTeamsTests` real-seam coverage.
- **Catalog lists (current):**
  - Services: `GET /api/v1/catalog/services` → `CursorPage<ServiceResponse>`; sort allowlist `createdAt|displayName` (`ServiceSortField`); **screen default `displayName desc`**, **endpoint default `createdAt desc`** (mismatch — §3 #5); zero filters. `ListServicesQuery` / `ListServicesHandler` / `CatalogEndpointDelegates.ListServicesAsync`.
  - Applications: `GET /api/v1/catalog/applications` → `CursorPage<ApplicationResponse>`; sort allowlist `createdAt|displayName`; **screen + endpoint default `createdAt desc`**; one filter `includeDecommissioned` (boolean, `f`-map always-present) + optional `createdByUserId`. Standalone `<Checkbox>` on `CatalogListPage`, outside any FilterBar.

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **Mirror the Teams `displayNameContains` end-to-end** onto Services + Applications: `string? DisplayNameContains` on both query records; ILIKE-contains filter before paging; `f`-map key `displayNameContains` (present **only when applied**); endpoint binds `[FromQuery] string? displayNameContains`, trim→null when blank. | Identical contract to Teams; `key == URL param == API param == f-map key` end-to-end (ADR-0107). Zero new wire concepts. |
| 2 | **Build the FilterBar `boolean` control.** `FilterSpec` boolean variant renders a `<Checkbox>` (label = `spec.label`); fold Applications `includeDecommissioned` into the page's `FILTER_SPECS`; **delete the standalone checkbox block**. | The chosen way to bring the toggle into the unified surface (user decision). The control is reserved in #38; this is its first build. |
| 3 | **Boolean filters are submit-driven** — a checkbox edits a *draft*; the URL + query commit only on **Enter / Search**, exactly like text. | Uniform with text (user decision). **Intentional behavior change:** today the checkbox refetches immediately; after this slice it waits for Search. Captured in tests + §8. |
| 4 | **Extract `EscapeLike` to a shared helper** in `Kartova.SharedKernel.Postgres` (e.g. `LikePattern.EscapeContains` / `LikeEscaping`); repoint `ListTeamsHandler` + both new Catalog handlers at it. | Three call sites would otherwise duplicate a security-relevant 3-line method; one tested implementation. The `\`→`\\`, `%`→`\%`, `_`→`\_` order matters and shouldn't be re-derived per module. |
| 5 | **Default sort → `displayName asc`** on Services **and** Applications, on **both** the screen (`useListUrlState`) **and** the endpoint default (`?? ServiceSortField.DisplayName` / `?? ApplicationSortField.DisplayName`, `?? SortOrder.Asc`). Teams is already `displayName asc` — all three lists then agree. | Name-as-default across lists (user decision); A→Z is the natural reading of a name column; aligns the Services screen/endpoint mismatch so raw-API callers and the SPA agree. |
| 6 | **`displayNameContains` joins the existing `f`-map** on Applications **conditionally** (alongside always-present `includeDecommissioned` + conditional `createdByUserId`). On Services it's the only optional key (present only when applied), matching Teams. | A new optional filter dimension must be part of the cursor's filter identity, or paging across a name search could skip/duplicate rows. Reuses the proven ADR-0095 mechanism. |
| 7 | **Search scope = `displayName` only**, case-insensitive `ILIKE` contains, wildcards escaped, input trimmed, blank/whitespace ⇒ filter **absent**. | Same UX + cursor-identity invariant as Teams; description/tag/owner search is E-05. |
| 8 | **No default-sort change to Teams**, no sort-allowlist change anywhere, **no new sort fields**. | Teams already `displayName asc`; this slice is filters + a name-default alignment, not a sort-surface expansion. |
| 9 | **`single-select` / `multi-select` / `date-range` still throw** in `<FilterBar>`. Catalog facets (team / lifecycle / health / created-by) deferred to **E-05**. | ADR-0107 clause 1 — build controls when a screen needs them; E-05 owns multi-attribute faceting. |
| 10 | **No new `ProblemDetails`.** Any committed filter value sits in the React-Query `queryKey`, so `useCursorList` resets the cursor stack on change; `cursor-filter-mismatch` 400 is server-side defense-in-depth. | Mechanism already exists (ADR-0095). |
| 11 | **`<FilterBar>` becomes a collapsible disclosure panel** for **all** consumers: a "Filters" header (label + `activeCount` + chevron toggle) over a collapsible region holding the controls; **expanded by default**; collapse reclaims vertical space and the collapsed header keeps `Filters (N active)` so active filters are never hidden. Open/closed = **ephemeral** component state (default expanded). Built with an accessible disclosure (`aria-expanded` + `aria-controls`, react-aria `Disclosure` if the primitive is available). **Amends ADR-0107 clause 6** (text in §7). | User decision 2026-06-22 — a panel that holds the search controls and can be folded. Expanded-default preserves discoverability; ephemeral state keeps scope lean (no storage/URL coupling). One shell ⇒ uniform across Teams + Catalog + every future list. The small-viewport drawer/sheet is the responsive form of the same affordance. |

---

## 4. Architecture

### 4.1 Data flow (Applications — the richer case)

```
CatalogListPage
 ├ FILTER_SPECS = [
 │    { key:"displayNameContains", type:"text",   label:"Search applications", placeholder:"Search by name…" },
 │    { key:"includeDecommissioned", type:"boolean", label:"Show decommissioned" } ]
 ├ urlState = useListUrlState({ defaultSortBy:"displayName", defaultSortOrder:"asc",
 │              allowedSortFields:["createdAt","displayName"],
 │              booleanFilters:["includeDecommissioned"], textFilters:["displayNameContains"] })
 ├ filters  = useListFilters(FILTER_SPECS, urlState)   // text + boolean drafts, both commit on submit()
 ├ list     = useApplicationsList({ sortBy, sortOrder,
 │              displayNameContains: filters.queryFilters.displayNameContains,      // string | undefined
 │              includeDecommissioned: filters.queryFilters.includeDecommissioned }) // boolean (always present)
 ├ <FilterBar specs={FILTER_SPECS} filters={filters} />   // search box + checkbox + Search + N active / Clear all
 └ <ApplicationsTable …>   empty → filters.isActive ? "No applications match your filters" : "No applications yet"

GET /api/v1/catalog/applications?sortBy=displayName&sortOrder=asc&limit=50&displayNameContains=foo&includeDecommissioned=true
 └ ListApplicationsAsync binds displayNameContains (trim→null), includeDecommissioned, createdByUserId
    └ ListApplicationsHandler:
        WHERE (!includeDecommissioned ⇒ Lifecycle != Decommissioned)
          AND (createdByUserId ⇒ a.CreatedByUserId == id)
          AND (displayNameContains ⇒ ILike(DisplayName, "%foo%", "\\"))     // before paging
        f = { includeDecommissioned, createdByUserId?, displayNameContains? }
        ToCursorPagedAsync(…, expectedFilters: f)
```

Services is the same, minus the boolean: one text spec, `f = { displayNameContains? }`.

### 4.2 File map

**Shared backend — created:**

| File | Purpose | ~LOC |
|---|---|---|
| `src/SharedKernel/Kartova.SharedKernel.Postgres/Pagination/LikeEscaping.cs` (final namespace/name confirmed against existing layout) | `EscapeContains(string raw)` → `%`-wrapped, metachar-escaped ILIKE pattern (or `EscapeLike` returning the escaped core). One implementation for all handlers. | 12 |
| `…/LikeEscapingTests.cs` (`Kartova.SharedKernel.*Tests`) | escapes `\` `%` `_`; passthrough for plain text; ordering (`\` first). | — |

**Backend — modified:**

| File | Change |
|---|---|
| `src/Modules/Catalog/Kartova.Catalog.Application/ListServicesQuery.cs` | `+ string? DisplayNameContains`. |
| `src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs` | `+ string? DisplayNameContains` (after existing params; keep `CreatedByUserId` defaulted). |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListServicesHandler.cs` | apply ILIKE filter before paging via shared helper; build conditional `f`-map `{displayNameContains}`; pass `expectedFilters:`. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApplicationsHandler.cs` | add ILIKE predicate; add `displayNameContains` to the existing `f`-map (conditional); use shared `EscapeLike`. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | `ListServicesAsync` + `ListApplicationsAsync`: bind `[FromQuery] string? displayNameContains`, trim→null; pass into query. **Defaults flip to `…SortField.DisplayName` / `SortOrder.Asc`** in both. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/ListTeamsHandler.cs` | repoint its inline `EscapeLike` at the shared helper (delete the private copy). |

**Frontend — modified:**

| File | Change |
|---|---|
| `web/src/lib/list/useListUrlState.ts` | widen `setBooleanFilter` `name` param to `string` (symmetry with `setTextFilter`; lets the string-keyed `useListFilters` drive booleans without a cast). Read-side `booleanFilters` map keeps its narrowed keys. |
| `web/src/lib/list/filters/useListFilters.ts` | extend to handle **boolean** specs alongside text: boolean draft seeded from `urlState.booleanFilters`; `bindBoolean(key)→{value:boolean,onChange}`; `submit()` commits text **and** boolean drafts; `clearAll()` resets both (booleans→false); `queryFilters` adds boolean keys as `boolean` (always present); `isActive`/`activeCount` count committed text (non-empty) **and** committed booleans (true); reconcile effect adopts external boolean changes. Widen the `urlState` `Pick` to include `booleanFilters` + `setBooleanFilter`. |
| `web/src/components/application/filter-bar/FilterBar.tsx` | (a) add the `type:"boolean"` branch → `<Checkbox isSelected onChange label={spec.label}>` via `filters.bindBoolean(spec.key)`; keep throwing for `single-select`/`multi-select`/`date-range`. (b) **wrap the controls in a collapsible disclosure panel** (§5.3): "Filters" header + `activeCount` + chevron toggle button (`aria-expanded`/`aria-controls`), expanded-by-default `useState`; the `role="search"` form + Search button + "N active"/"Clear all" move inside the collapsible region; collapsed header shows `Filters (N active)`. Applies to every consumer. |
| `web/src/features/teams/pages/__tests__/TeamsListPage.test.tsx` | regression: search input still reachable (panel expanded by default); add a collapse/expand assertion if structure-coupled queries break. No behavior change to Teams filtering. |
| `web/src/features/catalog/api/services.ts` | `+ displayNameContains?: string` on `ServicesListParams`; thread into `query` only when set. |
| `web/src/features/catalog/api/applications.ts` | `+ displayNameContains?: string` on `ApplicationsListParams`; thread only when set. |
| `web/src/features/catalog/pages/ServicesListPage.tsx` | default sort → `displayName asc`; `textFilters` + `FILTER_SPECS` (one text); `useListFilters`; render `<FilterBar>`; filtered-empty state. |
| `web/src/features/catalog/pages/CatalogListPage.tsx` | default sort → `displayName asc`; add `textFilters`; `FILTER_SPECS` = text + boolean; `useListFilters`; **delete the standalone `<Checkbox>` block**; render `<FilterBar>`; thread both query params; filtered-empty state. |
| `web/src/generated/openapi.ts` + `web/openapi-snapshot.json` | regenerated via codegen for the two new `displayNameContains` params (generated — excluded from LOC; snapshot committed). |

**Docs — modified:**

| File | Change |
|---|---|
| `docs/architecture/decisions/ADR-0107-list-filtering-consideration-and-filterbar-ui.md` | append the clause-6 amendment (text in §7) — collapse extended from small-viewport-only to an all-viewport collapsible disclosure panel, expanded by default, standard across consumers. |
| `docs/design/list-filter-registry.md` | Services + Applications rows (§7) + a note that the FilterBar shell is now a collapsible panel. |

**Tests (gate-5) — see §6.**

**Estimate ≈ 330 production LOC** (backend ≈ 70, frontend ≈ 160 incl. the disclosure shell, plus deletions; excludes tests + generated). Under the ~400 target → single slice, no decomposition.

---

## 5. Components

### 5.1 Shared `EscapeLike`
Lift the Teams implementation verbatim into `Kartova.SharedKernel.Postgres`: `\`→`\\` first, then `%`→`\%`, `_`→`\_`. Handlers call `$"%{Shared.EscapeLike(name)}%"` (or a `EscapeContains` that wraps). Teams + both Catalog handlers reference it; the private copy in `ListTeamsHandler` is removed.

### 5.2 `useListFilters` boolean extension
Booleans get the **same draft → submit lifecycle** as text:
- seed `boolDraft` from `urlState.booleanFilters`; `bindBoolean(key) → { value: boolDraft[key] ?? false, onChange }`.
- `submit()` writes every text draft via `setTextFilter` **and** every boolean draft via `setBooleanFilter`.
- `clearAll()` sets text `""` + booleans `false`, resets drafts.
- `queryFilters`: text → `string | undefined` (undefined when empty); boolean → `boolean` (always present, default `false` — so `includeDecommissioned` is always on the wire, matching today).
- `isActive` = any committed text non-empty **or** any committed boolean true; `activeCount` = sum.
- reconcile `useEffect` adopts committed text **and** boolean values on external URL change (back/forward, shared link, `clearAll`).
- `urlState` `Pick` widens to `"textFilters" | "setTextFilter" | "booleanFilters" | "setBooleanFilter"`.

### 5.3 `<FilterBar>` — collapsible panel + boolean branch

**Disclosure shell (all consumers).** The controls render inside a collapsible panel:
```
┌─ Filters                                   [▾] ┐   header: label + activeCount + toggle
│  <form role="search">                          │   ← collapsible region (expanded by default)
│    [ text inputs ] [ checkboxes ]               │
│    [Search]   {activeCount} active   Clear all  │
│  </form>                                        │
└─────────────────────────────────────────────────┘
```
- `const [open, setOpen] = useState(true)` (ephemeral, default expanded).
- Header is a toggle button: `aria-expanded={open}`, `aria-controls={panelId}`, chevron icon flips; label `Filters`, and when `filters.isActive` the header also shows the count so `Filters (N active)` survives collapse.
- The collapsible region (the existing `role="search"` form) is hidden when `!open` (unmounted or `hidden`); use react-aria-components `Disclosure`/`DisclosurePanel` if available, else a button + region with the ARIA wiring above.
- Active-count + **Clear all** stay in the panel body (visible when expanded); the header count is the at-a-glance signal when collapsed.

**Boolean branch (inside the form):**
```tsx
if (spec.type === "boolean") {
  const { value, onChange } = filters.bindBoolean(spec.key);
  return <Checkbox key={spec.key} isSelected={value} onChange={onChange} label={spec.label} />;
}
```
Rendered between the text input(s) and the Search button. Toggling edits the draft only; the form's `onSubmit`/Enter commits. `single-select`/`multi-select`/`date-range` keep throwing.

### 5.4 Catalog handlers
Same shape as `ListTeamsHandler` §5.4. Services adds the optional `f`-key; Applications **adds** `displayNameContains` to the dict it already builds:
```csharp
if (q.DisplayNameContains is { } name)
{
    source = source.Where(a => EF.Functions.ILike(a.DisplayName, $"%{EscapeLike(name)}%", "\\"));
    filters["displayNameContains"] = name;   // dict already created for includeDecommissioned
}
```
Filter applied **before** paging (hidden row never a cursor boundary).

---

## 6. Testing strategy (gate-5 artifacts)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). Wiring slice (HTTP/DB filter + cursor + default-sort flip) → **real seam mandatory**.

**Backend unit:**
- `LikeEscapingTests` — escapes `\` `%` `_`, plain passthrough, ordering.
- `ListServicesHandler` + `ListApplicationsHandler` filter tests (mirror `ListApplicationsHandlerFilterTests`): predicate applies; `f`-map carries `displayNameContains` only when present; Applications combines it with `includeDecommissioned` + `createdByUserId`.

**Backend real-seam — `ListServicesPaginationTests` + `ListApplicationsPaginationTests` (`KartovaApiFixtureBase`, real Postgres/RLS + real `JwtBearer`)** — ≥1 happy + ≥1 negative each:
- **happy:** `?displayNameContains=<frag>` returns only matching rows, case-insensitive substring.
- **paging consistency:** seed > limit matching rows; page through with the filter; **changing the filter against a live cursor → 400 `cursor-filter-mismatch`**.
- **RLS:** a same-named row in another tenant is **not** returned.
- **combination (Applications):** `displayNameContains` + `includeDecommissioned=true` returns matching decommissioned rows; default (`false`) still hides them.
- **negative — blank:** whitespace `displayNameContains` behaves as no filter.
- **escape:** literal `%` matches only rows containing `%`.
- **default sort:** no `sortBy` ⇒ `displayName asc` (both endpoints — updates existing expectations).

**Frontend (Vitest)** — ≥1 happy + ≥1 negative per unit:
- `useListFilters` boolean: `bindBoolean`; `submit` commits booleans; `clearAll` resets booleans; `queryFilters` includes the boolean (always); `isActive`/`activeCount` count booleans; external-URL reconcile.
- `FilterBar`: boolean spec renders a checkbox + toggling edits draft (no commit until submit); `single-select`/`date-range` still throw. **Collapsible panel:** expanded by default (controls present on mount); header toggle hides/shows the region and flips `aria-expanded`; collapsed header shows `Filters (N active)` when filters are active; `Clear all` reachable when expanded.
- `TeamsListPage` (regression): search input present on mount (panel expanded) and filtering still works through the panel — proves the shell change didn't regress the first consumer.
- `services.ts` / `applications.ts`: `displayNameContains` flows into the request query (and only when set).
- `ServicesListPage`: search present; default sort `displayName asc`; typing+submit filters (mock); matches-vs-empty.
- `CatalogListPage`: **checkbox now inside FilterBar**; toggling + Search applies `includeDecommissioned`; `displayNameContains` threads; default sort `displayName asc`; `clearAll` resets both; standalone-checkbox removal asserted (no duplicate control).

**Gate-4 container build:** web image compiles TS — the regenerated `openapi.ts` **must be committed** or the build breaks. `OpenApiTests` snapshot updated for the two new params. No Dockerfile/`COPY` change → the `images` job adds no new surface beyond the TS compile.

**Manual verification (ADR-0084):** Playwright MCP cold-start → `/catalog` and `/catalog/services` → type search + toggle "Show decommissioned" → Search applies both → "Clear all" restores → shareable URL carries `?displayNameContains=&includeDecommissioned=true` → default order A→Z → console clean. *Pending user verification* if the dev-stack/Docker is unavailable in-session.

---

## 7. Definition of Done

CLAUDE.md → Working agreements → **Definition of Done** (eight always-blocking gates + conditional mutation) applies verbatim; not restated here.

**Mutation gate (6) SKIPPED for this slice** — user decision 2026-06-22. The diff does touch Catalog Application/Infrastructure logic (filter predicates + `f`-map, query records, endpoint normalization, shared `EscapeLike`), which would normally make gate 6 blocking; the conditional gate permits a skip "with a noted reason," and this is that note. Coverage stands on the explicit unit + real-seam tests in §6 (predicate applies / `f`-map presence / ILIKE escaping incl. `%`/`_`/`\` / blank-as-absent / cursor-filter-mismatch / RLS). The other eight always-blocking gates are unaffected. `/misc:mutation-sentinel` → `/misc:test-generator` is NOT run for this slice.

Run `scripts/ci-local.sh` (or `backend`/`frontend` subsets) green before push. Steps needing the running stack (codegen, Playwright MCP) → flagged *pending user verification* if unavailable.

**ADR-0107 clause-6 amendment — text to append (preview before saving, CLAUDE.md):**

> **Amendment (2026-06-22) — clause 6 (collapse).** `<FilterBar>` renders its controls inside a **collapsible disclosure panel** on **all** viewports, not only small ones: a labeled "Filters" header (with the active-filter count and a chevron toggle) over a collapsible region holding the controls. The panel is **expanded by default**; collapsing reclaims vertical space while the header retains `Filters (N active)` so active filters are never hidden. This is the standard shell for **every** `<FilterBar>` consumer (Teams included). The small-viewport drawer/sheet remains the responsive form of the same collapse affordance. Open/closed state is ephemeral (component-local, resets to expanded on mount); persistence (localStorage/URL) is a deferred nicety.

**On completion — registry (`docs/design/list-filter-registry.md`):**
- Services row → **built**, filter `displayNameContains`; facets deferred → E-05.
- Applications row → `displayNameContains` (text, built) + `includeDecommissioned` refactored onto `<FilterBar>`; **drop the "(pre-standard)" qualifier**; lifecycle/team/created-by facets deferred → E-05.
- Note FilterBar control availability: **text + boolean built**; `single-select`/`multi-select`/`date-range` still reserved. **Shell is now a collapsible disclosure panel (expanded by default), standard across all consumers.**

**On completion — memory:** update the `default-list-sort` memory: standardized on **`displayName asc`** across Teams/Services/Applications (supersedes the earlier "displayName desc / Applications left as-is").

**On completion — CHECKLIST:** annotate E-02.F-01 / E-02.F-02 with the filter + name-default-sort addition.

---

## 8. Out of scope (explicit deferrals)

- **Catalog facets** — team / lifecycle / health / created-by filters → **E-05 faceted search** (need select / multi-select controls).
- **`single-select` / `multi-select` / `date-range` FilterBar controls** — typed only; built when a screen needs them.
- **Service health filter** — no write path until E-15/E-16 (all rows `Unknown`); filtering on a constant is meaningless today.
- **Dedicated mobile drawer/sheet chrome** — the collapsible disclosure panel (§5.3) is built and collapses on all viewports, which is acceptable on mobile; a distinct bottom-sheet/drawer treatment is deferred until a screen needs it.
- **Panel open/closed persistence** — state is ephemeral (resets to expanded on mount); remembering it via localStorage/URL is a deferred nicety, not built now.
- **Members/Users list refactor** onto `<FilterBar>` — separate registry candidate, not this slice.
- **Sort-surface changes** — no new sort fields, no allowlist change, Teams default untouched.
- **Server-side trigram / fuzzy search, relevance ranking** — plain `ILIKE` substring only.

---

## 9. Implementation order (rough — finalised by writing-plans)

1. Shared `EscapeLike` helper + test; repoint `ListTeamsHandler` (RED→GREEN, no behavior change).
2. Backend Services: `ListServicesQuery` field + handler filter/`f`-map + endpoint param + default-sort flip; extend `ListServicesPaginationTests` + handler filter test.
3. Backend Applications: `ListApplicationsQuery` field + handler predicate/`f`-map + endpoint param + default-sort flip; extend `ListApplicationsPaginationTests` + handler filter test.
4. Codegen: run API, `npm run codegen`, commit regenerated `openapi.ts` + snapshot; update `OpenApiTests`.
5. `useListUrlState` `setBooleanFilter` widening + test.
6. `useListFilters` boolean extension + test.
7. `FilterBar` collapsible disclosure shell + boolean branch + tests (incl. `TeamsListPage` regression). Append the ADR-0107 clause-6 amendment.
8. `services.ts` / `applications.ts` params + tests.
9. Wire `ServicesListPage` (default asc, FilterBar, empty states) + test.
10. Wire `CatalogListPage` (default asc, FilterBar text+boolean, delete checkbox, empty states) + test.
11. `scripts/ci-local.sh` green; Playwright MCP manual pass; mutation gate on changed C#; registry + memory + CHECKLIST updates; push / PR / DoD gates.

---

## 10. Self-review

**Spec coverage:** every §3 decision traces to §4–§6; every gate-5 artifact in §6 is a named file/area in §4.2 for writing-plans to turn into a task (shared helper, both handlers, both endpoints, four frontend units, both pages, the FilterBar collapsible shell + Teams regression, and the ADR-0107 amendment).

**Internal consistency:** `displayNameContains` is one identifier across URL / query param / `f`-map (§3 #1/#6, §4.1, §5.4). Default sort `displayName asc` consistent across §3 #5, §4.1, §4.2, §6. Boolean = submit-driven consistent across §3 #2/#3, §5.2, §5.3, §6. "text + boolean built, rest throw" consistent across §3 #2/#9, §4.2, §5.3, §8. Collapsible panel consistent across §1, §3 #11, §4.2, §5.3, §6, §7 (amendment), §8 (deferrals).

**Scope check:** ~330 production LOC, single PR, under the 400 target; no decomposition. The collapsible-panel + boolean control are shared-`<FilterBar>` changes affecting Teams — covered by a Teams regression test, no Teams behavior change. Mutation gate (6) would normally apply (Catalog Application/Infrastructure logic touched) but is **skipped this slice by user decision** with a recorded reason (§7); explicit unit + real-seam tests carry the predicate/`f`-map/escaping coverage. The FilterBar shell is TS, outside Stryker regardless.

**Ambiguity check:** blank/whitespace search ⇒ filter absent (§3 #7); boolean commit timing ⇒ submit-driven with an explicit behavior-change note (§3 #3); `EscapeLike` location ⇒ shared SharedKernel.Postgres (§3 #4); default-sort direction ⇒ asc, both screen + endpoint (§3 #5); panel default state ⇒ expanded, ephemeral (§3 #11); panel scope ⇒ all consumers (§3 #11). `includeDecommissioned` counts toward `isActive`/`activeCount` (a deliberate "deviation from default = active filter" choice; the empty-state-wording edge when it's the only active filter is benign — §5.2).

**No blocking issues found.**
