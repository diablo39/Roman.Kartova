# Slice — Standard list-filter surface (`<FilterBar>` + `useListFilters`), Teams first consumer

**Date:** 2026-06-21
**Stories:** E-03.F-02 (Team management — list filtering); first implementation of **ADR-0107** (list-filter consideration mandate + standard filter UI).
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/list-filter-surface-teams`

> **Amendment (2026-06-22):** text filters changed from **debounced-live** to **explicit submit** (Enter / Search button) — see ADR-0107 clause 3. `useListFilters` now exposes `submit()` (typing updates a draft; commit happens on submit), and `<FilterBar>` wraps inputs in a form + Search button + Enter `onKeyDown`. The debounce wording in §3 #5, the §4.2 file-map notes, §5.2, §6 and §10 is superseded by this submit model. (`activeCount` was also added to the hook during the /simplify pass.)

---

## 1. Goal

Build the **standard list-filter surface** defined by ADR-0107 — a shared `<FilterBar>` component plus a `useListFilters` hook — and make the **Teams list** its first consumer with a single **`displayName` text-search** filter. Also flip the Teams list default sort to **`displayName asc`** (resolved Filter Proposal, `docs/design/list-filter-registry.md`).

This is deliberately the *foundational* slice for filtering: every later filter screen (catalog tag filtering E-03.F-04.S-03, faceted search E-05.F-01.S-02, plus refactoring the pre-standard Applications/Members filters) becomes **config-only** against this surface. Per ADR-0107 clause 1, only the **text** control is built now; the other `FilterSpec` control types are typed but unbuilt (YAGNI).

---

## 2. Pre-requisites (already on master)

- **Cursor + filter wire contract (ADR-0095):** `CursorPage<T>` envelope; opaque cursor `{s,i,d,f?}` where `f` is the caller-owned filter map; `ToCursorPagedAsync(…, expectedFilters:)` replays the map and throws `CursorFilterMismatchException` → 400 `cursor-filter-mismatch` on mid-pagination change. Proven by the Applications list (`ListApplicationsHandler` builds `{includeDecommissioned, createdByUserId?}`).
- **Frontend list stack:** `useListUrlState` (sort + `booleanFilters`), `useCursorList` (resets the cursor stack when `queryKey` identity changes — so any filter value that lands in the query key auto-resets pagination), DataTable primitives, `Table`.
- **Teams list (current):** `GET /api/v1/organizations/teams` → `CursorPage<TeamResponse>`; sort allowlist `createdAt|displayName` (`TeamSortField`), default `createdAt desc`; **zero filters**. `ListTeamsHandler` / `ListTeamsQuery` / `TeamEndpointDelegates.ListTeamsAsync` / `TeamRoutes` (`.WithName("ListTeams")`). Real-seam coverage in `ListTeamsTests` (`KartovaApiFixtureBase`, real Postgres/RLS + real JWT).
- **OpenAPI:** `CursorListQueryParameterTransformer` emits the sort/limit enum schemas; a plain `string?` query param needs no transformer change. Codegen → `web/src/generated/openapi.ts` + `web/openapi-snapshot.json`; snapshot asserted by `OpenApiTests`.

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **Approach A (hybrid):** extend `useListUrlState` with text-filter URL support; new `useListFilters(specs, urlState)` adds debounce + spec binding + `clearAll` + the `queryFilters` object; `<FilterBar specs>` renders. | Smallest diff (text support mirrors `booleanFilters`), each hook stays single-purpose, matches ADR-0107's named shapes (`useListFilters` *composes* `useListUrlState`). |
| 2 | **A `FilterSpec`'s `key` is its name everywhere** — browser URL param, API query param, and cursor `f`-map key are all `displayNameContains`. | One identifier end-to-end (`?displayNameContains=foo` in URL → same query param → `f.displayNameContains`); the generic mapping is "key ⇒ wire name", no per-screen translation. |
| 3 | **Search scope = `displayName` only** (not description). | YAGNI; matches the recorded Filter Proposal. Adding fields later is a new `FilterSpec`. |
| 4 | **Match = case-insensitive contains** via Postgres `EF.Functions.ILike(DisplayName, "%"+esc+"%", "\\")`; LIKE wildcards (`%` `_` `\`) escaped; input trimmed; empty/whitespace ⇒ filter **absent** (no `f` key, no `WHERE`). | Forgiving substring search is the expected UX; escaping prevents user input widening the match; blank-as-absent keeps the unfiltered cursor identical to today's. |
| 5 | **Explicit submit (no debounce)** — amended 2026-06-22 (was 300ms debounce). Typing updates a *draft*; the URL+query commit fires only on **Enter or a Search button**. | User controls when the search runs; no query per keystroke. `useListFilters` exposes `submit()`; `<FilterBar>` = form + Search button + Enter `onKeyDown` → `submit()`. |
| 6 | **Default sort flips to `displayName asc`** on **both** the screen and the endpoint default. **Ascending** (A→Z), refining the "displayName desc" convention for **name columns** (user decision 2026-06-21). | Direct-API callers and the UI agree; A→Z is the natural reading of a name column. |
| 7 | **`<FilterBar>` builds only the `text` control**; `single-select`/`multi-select`/`boolean`/`date-range` are typed in the `FilterSpec` union but throw "not implemented yet" if used. | ADR-0107 clause 1 — text now, rest deferred until a screen needs them. |
| 8 | **No new `ProblemDetails`.** Filter change resets the cursor client-side (key in `queryKey`); `cursor-filter-mismatch` 400 is the server defense-in-depth. | The mechanism already exists (ADR-0095); nothing to add. |
| 9 | **Responsive:** for a single text field the bar is full-width on mobile; the **collapse-to-drawer** behavior (ADR-0107 clause 6) is deferred until a screen has multiple controls. | Don't build drawer chrome for one input. Noted as an explicit deferral, not an omission. |

---

## 4. Architecture

### 4.1 Data flow

```
TeamsListPage
 ├ specs = [{ key:"displayNameContains", type:"text",
 │            label:"Search teams", placeholder:"Search by name…" }]
 ├ urlState = useListUrlState({ defaultSortBy:"displayName", defaultSortOrder:"asc",
 │              allowedSortFields:["createdAt","displayName"],
 │              textFilters:["displayNameContains"] })            // NEW textFilters config
 ├ filters  = useListFilters(specs, urlState)
 │              → { values, bind, clearAll, isActive, queryFilters }
 ├ list     = useTeamsList({ sortBy, sortOrder, ...queryFilters }) // queryFilters in queryKey ⇒ cursor resets
 ├ <FilterBar specs={specs} filters={filters} />                   // above the table
 └ <Table> … empty → isActive ? "No teams match your search" : "No teams yet"

GET /api/v1/organizations/teams?sortBy=displayName&sortOrder=asc&limit=50&displayNameContains=foo
 └ ListTeamsAsync binds displayNameContains (trim→null if blank)
    └ ListTeamsHandler:
        WHERE EF.Functions.ILike(DisplayName, "%foo%", "\\")     // applied BEFORE paging
        f = { displayNameContains:"foo" }                        // only when present
        ToCursorPagedAsync(spec, order, cursor, limit, …, expectedFilters: f)
        (cursor issued under a different f mid-paging ⇒ 400 cursor-filter-mismatch)
```

### 4.2 File map

**Frontend — created:**

| File | Purpose | ~LOC |
|---|---|---|
| `web/src/lib/list/filters/types.ts` | `FilterSpec` discriminated union. Live: `{ key; type:"text"; label; placeholder? }`. Typed-but-unbuilt: `single-select` / `multi-select` / `boolean` / `date-range`. `FilterValues = Record<string,string>`. | 20 |
| `web/src/lib/list/filters/useListFilters.ts` | `useListFilters(specs, urlState)`: local immediate input value per text spec, **300ms debounced** commit to `urlState.setTextFilter`, reconcile on external URL change; returns `values`, `bind(key)` ({value,onChange}), `clearAll()`, `isActive`, `queryFilters` (`{ [key]: committed ‖ undefined }`). | 75 |
| `web/src/components/application/filter-bar/FilterBar.tsx` | Renders `specs.map`: `type:"text"` → Untitled UI search `Input` (react-aria) wired to `filters.bind(spec.key)`, aria-label = spec.label; active-count + **"Clear all"** (shown when `isActive`); full-width on mobile. Non-text types → dev-time throw. | 80 |

**Frontend — created (tests, gate-5):**

| File | Purpose |
|---|---|
| `web/src/lib/list/filters/__tests__/useListFilters.test.tsx` | debounce commits after delay; `clearAll`; `queryFilters` shape; `isActive`; reconcile on URL change. |
| `web/src/components/application/filter-bar/__tests__/FilterBar.test.tsx` | renders text control from spec; typing calls onChange; Clear-all clears; aria-label present; unsupported type throws. |

**Frontend — modified:**

| File | Change |
|---|---|
| `web/src/lib/list/useListUrlState.ts` | add `textFilters?: readonly string[]` → `textFilters: Record<k,string>` (read from URL, default `""`) + `setTextFilter(k,v)` (set, or **delete param when blank**). Mirror of `booleanFilters`. |
| `web/src/lib/list/__tests__/use-list-url-state.test.tsx` | text-filter read/write; blank removes param. |
| `web/src/features/teams/api/teams.ts` | `ListTeamsQuery` type already derives params from codegen; add `displayNameContains?: string` to `TeamsListParams` + pass it in the `query`. |
| `web/src/features/teams/api/__tests__/teams.test.tsx` | assert `displayNameContains` flows into the request query. |
| `web/src/features/teams/pages/TeamsListPage.tsx` | default sort → `displayName`/`asc`; wire `<FilterBar>`; matches-vs-empty states. |
| `web/src/features/teams/pages/__tests__/TeamsListPage.test.tsx` | search box present; default sort displayName asc; typing filters (mock); "no matches" vs "no teams". |
| `web/src/generated/openapi.ts` + `web/openapi-snapshot.json` | regenerated via codegen (generated — excluded from LOC). |

**Backend — modified:**

| File | Change |
|---|---|
| `src/Modules/Organization/Kartova.Organization.Application/ListTeamsQuery.cs` | `+ string? DisplayNameContains`. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/TeamEndpointDelegates.cs` | bind `[FromQuery] string? displayNameContains`; trim→null when blank; pass into query; **default `SortBy: TeamSortField.DisplayName, SortOrder: SortOrder.Asc`**. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/ListTeamsHandler.cs` | apply `ILike` filter before paging; build `f`-map `{displayNameContains}` (present only when non-null); pass `expectedFilters:`. Small `EscapeLike` helper (escape `\` `%` `_`). |
| `src/Modules/Organization/Kartova.Organization.IntegrationTests/ListTeamsTests.cs` | extend (see §6); update default-sort expectation to `displayName asc`. |

**Estimate ≈ 255 production LOC** (frontend ≈ 220, backend ≈ 35; excludes tests + generated). Under the ~400 target → single slice, no decomposition.

---

## 5. Components

### 5.1 `useListUrlState` text-filter extension
Add a third config axis next to `allowedSortFields` / `booleanFilters`:
```ts
textFilters?: readonly TTextFilter[];        // e.g. ["displayNameContains"]
// returns:
textFilters: Readonly<Record<TTextFilter,string>>;   // "" when absent
setTextFilter: (name: TTextFilter, value: string) => void;  // blank ⇒ delete param
```
Read: `params.get(name) ?? ""`. Write: `value.trim() ? set : delete`. Same "no `=` clutter when default" rule as `booleanFilters`.

### 5.2 `useListFilters(specs, urlState)`
- Holds a local `Record<key,string>` seeded from `urlState.textFilters` (immediate input echo).
- On change: update local + schedule a **300ms** debounced `urlState.setTextFilter(key, value)`.
- `useEffect` reconciles local from `urlState.textFilters` when the URL changes externally (back/forward, shared link).
- `queryFilters` = `{ [key]: urlState.textFilters[key] || undefined }` (committed values only; `undefined` drops the param → omitted from `queryKey` so the unfiltered key matches today's).
- `isActive` = any committed value non-empty. `clearAll()` clears every spec's param.
- `bind(key)` → `{ value: local[key], onChange }` for the control.

### 5.3 `<FilterBar specs filters>`
Row above the table. For each spec: `type:"text"` → search `Input` (Untitled UI / react-aria), `aria-label`/placeholder from the spec, `maxLength={128}`, value/onChange from `filters.bind`. When `filters.isActive`: render a "Clear all" button (and an active-filter count — trivial at one filter, but the affordance is built per ADR-0107 clause 5). Unsupported `type` → `throw new Error("FilterBar: <type> control not implemented (ADR-0107 clause 1 — text only)")` so a future misuse fails loudly at dev time.

### 5.4 `ListTeamsHandler` filter
```csharp
IQueryable<Team> source = db.Teams;
Dictionary<string,string>? filters = null;
if (q.DisplayNameContains is { } name)   // already trimmed-non-empty at the edge
{
    var pattern = $"%{EscapeLike(name)}%";
    source = source.Where(t => EF.Functions.ILike(t.DisplayName, pattern, "\\"));
    filters = new(StringComparer.Ordinal) { ["displayNameContains"] = name };
}
var page = await source.ToCursorPagedAsync(
    spec, q.SortOrder, q.Cursor, q.Limit,
    TeamSortSpecs.IdSelector, IdExtractor, ct, expectedFilters: filters);
```
`EscapeLike` replaces `\`→`\\`, `%`→`\%`, `_`→`\_`. Filter applied **before** paging so a hidden row never becomes a cursor boundary (same invariant as `ListApplicationsHandler`).

---

## 6. Testing strategy (gate-5 artifacts)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). Wiring slice (HTTP/DB filter + cursor) → **real seam mandatory**.

**Backend real-seam — `ListTeamsTests` (`KartovaApiFixtureBase`, real Postgres/RLS + real `JwtBearer`)** — ≥1 happy + ≥1 negative:
- **happy:** `?displayNameContains=ALP` returns only the matching team(s), case-insensitive substring.
- **paging consistency:** seed > limit matching teams; page through with the filter; cursor stays consistent; **changing the filter against a live cursor → 400 `cursor-filter-mismatch`**.
- **RLS:** a same-named team in another tenant is **not** returned (filter never widens past the tenant row set).
- **negative — blank:** `?displayNameContains=%20` (whitespace) behaves as no filter (all teams).
- **escape:** a literal `%` in the query matches only a team whose name contains `%`, not everything.
- **default sort:** no `sortBy` ⇒ `displayName asc`.

**Frontend (Vitest)** — ≥1 happy + ≥1 negative per unit (see §4.2 test files): `useListUrlState` text param; `useListFilters` debounce/clearAll/queryFilters/isActive/reconcile; `<FilterBar>` render/onChange/clear/aria/unsupported-throw; `TeamsListPage` search present + default asc + matches-vs-empty.

**Gate-4 container build:** the web image compiles TS — the regenerated `openapi.ts` **must be committed** or the build breaks. `OpenApiTests` snapshot updated for the new `displayNameContains` param.

**Manual verification (ADR-0084):** Playwright MCP cold-start → `/teams` → type in search → list narrows (debounced) → "Clear all" restores → shareable URL carries `?displayNameContains=` → console clean. *Pending user verification* if Docker/dev-stack is unavailable in-session.

---

## 7. Definition of Done

CLAUDE.md → Working agreements → **Definition of Done** (eight always-blocking gates + conditional mutation) applies verbatim; not restated here.

**Mutation gate (6) APPLIES** — the diff changes Organization **Application/Infrastructure** logic (`ListTeamsHandler` filter + `f`-map, `ListTeamsQuery`, the edge normalization). Run `/misc:mutation-sentinel` → `/misc:test-generator` on the changed C# files (target ≥80%; document survivors). Frontend TS is outside Stryker.

Run `scripts/ci-local.sh` (or `backend`/`frontend` subsets) green before push. Steps needing the running stack (codegen, Playwright MCP) → flagged *pending user verification* if unavailable.

**On completion:** flip the `docs/design/list-filter-registry.md` Teams row `decided — implement (slice pending)` → **`built`** and drop the pre-`<FilterBar>` qualifier; Applications + Members remain listed as `built (pre-standard)` refactor candidates.

---

## 8. Out of scope (explicit deferrals)

- **Other `FilterSpec` control types** (single-select, multi-select, boolean, date-range) — typed only; built when a screen needs them.
- **FilterBar collapse-to-drawer** on mobile — deferred until a multi-control screen (decision §3 #9).
- **Refactoring Applications (`includeDecommissioned`) and Members (`role` + search) onto `<FilterBar>`** — tracked in the registry; separate slices.
- **Searching team `description`**, member-count / "my teams" filters — not in the recorded Filter Proposal.
- **Server-side trigram / fuzzy search, relevance ranking** — plain `ILIKE` substring only.

---

## 9. Implementation order (rough — finalised by writing-plans)

1. Backend: `ListTeamsQuery` field + `ListTeamsHandler` filter/`f`-map/`EscapeLike` + endpoint param + default-sort flip; extend `ListTeamsTests` (RED→GREEN, real seam).
2. Codegen: run API, `npm run codegen`, commit regenerated `openapi.ts` + snapshot; update `OpenApiTests`.
3. `useListUrlState` text-filter support + test.
4. `filters/types.ts`; `useListFilters` + test.
5. `FilterBar` + test.
6. `teams.ts` param + test; wire `TeamsListPage` (default asc + `<FilterBar>` + empty states) + test.
7. `scripts/ci-local.sh` green; Playwright MCP manual pass; mutation gate on changed C#; registry flip; push / PR / DoD gates.

---

## 10. Self-review

**Spec coverage:** every §3 decision traces to §4–§6; every gate-5 artifact in §6 is a named file in §4.2 for writing-plans to turn into a task.

**Internal consistency:** `displayNameContains` is the single identifier across URL / query param / `f`-map / sort-allowlist-unrelated (§3 #2, §4.1, §5.2, §5.4). Default sort `displayName asc` consistent across §3 #6, §4.1, §4.2, §6. "text-only control" consistent across §3 #7, §4.2, §5.3, §8.

**Scope check:** ~255 production LOC, single PR, under the 400 target; no decomposition. Mutation gate correctly flagged as applying (C# logic touched).

**Ambiguity check:** blank/whitespace search resolved to "filter absent" (§3 #4); debounce-vs-cursor-reset resolved (commit debounced; queryKey holds committed value only, §3 #5 / §5.2); default-sort direction resolved to asc (§3 #6).

**No blocking issues found.**
