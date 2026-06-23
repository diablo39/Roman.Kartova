# ADR-0107: List Filtering — Consideration Mandate and Standard `<FilterBar>` UI

**Status:** Accepted
**Date:** 2026-06-21
**Deciders:** Roman Głogowski (solo developer)
**Category:** Frontend Architecture
**Related:** ADR-0095 (cursor pagination + sort contract + the opaque `f` filter-map **wire format** — that ADR owns only the transport; this ADR owns the consideration mandate and the filter UI), ADR-0094 (Untitled UI primitive layer), ADR-0039 (React SPA), ADR-0040 (dependency-graph filters — a sibling surface), ADR-0092 (URL convention). DESIGN.md carries the visual tokens. Faceted *search* filtering rides Elasticsearch (ADR-0002, ADR-0013).

## Context

ADR-0095 froze the list *contract* — cursor pagination, sort syntax, and an opaque caller-owned `f` filter map carried in the cursor — but said nothing about how filters are *presented* or whether every list must offer them. Filters have so far been built ad hoc: the Applications `includeDecommissioned` default-view toggle (slice 6) and the Members `role` dropdown + name/email search (slice 10) each invented their own chrome and URL handling.

Two gaps follow from that:

1. **No consideration discipline.** Nothing forces a list slice to even *decide* whether it needs filters. Filtering quietly falls off the first cut and resurfaces later as a wire + UI rework — the same retroactive-cost trap ADR-0095 closed for pagination.
2. **No presentation standard.** Each filter screen reinvents its controls, URL state, and empty-state handling, so filters look and behave differently across the app. Upcoming multi-filter screens — tag filtering (E-03.F-04.S-03), faceted search (E-05.F-01.S-02) — will diverge without a standard.

**Discoverability note:** filtering was previously recorded only as an amendment buried inside ADR-0095 ("Cursor Pagination Contract"), so neither a filename nor the index pointed to it. This ADR is the dedicated, filtering-named home for the *mandate* and the *UI*; ADR-0095 retains only the `f`-map wire format and a cross-reference back here.

## Decision

1. **Filter consideration is a first-cut design requirement** (extends the ADR-0095 clause-8 first-cut philosophy from sort+pagination to filtering). Every list slice's design MUST contain a **"Filter Proposal"** section listing the candidate filter fields for that list, each with a recommendation — **implement-now / defer / none-needed** — and the human signs off.
   - Unlike ADR-0095 clause 8, *implementation* is not mandated first-cut: a list MAY ship with zero filters, but the design must show filtering was considered and consciously deferred. **Deferral is explicit and recorded — never silent omission.**
   - This is a design/spec gate, enforced by the `brainstorming` + `writing-plans` self-review, **not** a code arch test ("did you consider filters?" is a spec artifact, not a compile-time property).
   - The proposal is the per-list "we considered it" indicator and is mirrored into the list-filter registry (`docs/design/list-filter-registry.md`), the canonical record of each list's filter decision.
   - **Ask at creation.** When a list is first designed, the Filter Proposal is produced from a **per-field surface table** (each field: column? / sortable? / filterable + control) presented to and confirmed by the human *before the design is finalized* — the human decides the allowlist, the author does not silently pick it. The `sortBy` allowlist derives from the same table (ADR-0095); columns are chosen there too. Columns carry no separate *creation* mandate (a table must have columns, so their selection is structural and cannot be silently skipped — unlike filters).
   - **Field-addition trigger.** The mandate also fires on **field addition**: any slice adding a new queryable / user-facing field to an entity that already has a list screen MUST revisit each such list across all three surface axes — **column? / sort? / filter?** — and record the decision in the registry. New fields default to "reconsider"; opting an axis out is explicit. This closes the gap where a field added after a list already exists would otherwise never be reconsidered for that list.

2. **One component.** When filters ARE built, they render through a shared `<FilterBar>` (react-aria-components + Tailwind v4 per ADR-0094), positioned above the `<DataTable>`. Standard control vocabulary only: **text search, single-select, multi-select, boolean toggle, date-range**. No bespoke per-screen filter chrome.

3. **One hook.** `useListFilters` owns filter state, composes with the existing `useListUrlState` so filters serialize to the URL query string (shareable/bookmarkable — satisfies E-03.F-04.S-03's "filter state shareable via URL" AC), and emits the ADR-0095 cursor `f` map. Changing the committed filter resets the cursor; the ADR-0095 `cursor-filter-mismatch` 400 guards stale-cursor reuse server-side.
   - **Text filters commit on explicit submit** (Enter or a Search button), not live-as-you-type (amended 2026-06-22 — superseded the original debounced-live design). A typed value is a **draft** held by `useListFilters`; only `submit()` commits it to the URL + query, so the cursor/query update once per search, not per keystroke. `<FilterBar>` wraps text inputs in a `<form>` with a Search button (`type="submit"`) and binds Enter via `onKeyDown` → `submit()`. Non-text controls (when built) MAY apply immediately.
   - **(as-built 2026-06-22, #39 — mechanism revised for performance; the commit-on-submit *decision* above is unchanged.)** The shipped implementation makes `<FilterBar>`'s text **and** boolean controls **uncontrolled** native DOM, so keystrokes trigger **zero** React renders (a per-keystroke draft re-rendered the whole page + table — ~709 ms/keystroke on a 50-row list; uncontrolled inputs cut it to ~4 ms). `useListFilters` therefore holds **no draft state** — it is pure-derived from the committed URL, returning only `{ queryFilters, isActive, activeCount }`; the `bind`/`bindBoolean`/`submit`/`clearAll`/reconcile API is gone. On Search/Enter `<FilterBar>` reads the controls via `FormData` and commits all filters in **one** navigation via `useListUrlState.setFilters({ text, booleans })` — a single atomic `setParams`, because looping `setTextFilter`/`setBooleanFilter` clobbered earlier writes under react-router's functional-updater (each call reads the stale committed location). The Search button is `type="button"` with `onClick` and Enter is bound via `onKeyDown` (react-aria's `Button`/`Input` do not emit a native form `submit`). `<FilterBar>` takes `urlState` (a `Pick` of `textFilters | booleanFilters | setFilters`), not a `filters` object. Layout: controls sit in the panel body; **Search is bottom-right and Clear all bottom-left** in a `border-top` action footer.

4. **Declarative config.** A screen passes a `FilterSpec[]` (field key, control type, label, options or async options loader). `<FilterBar>` renders from the spec — no imperative wiring per screen.

5. **Standard affordances.** Active-filter count, **"Clear all"**, and empty-state text that distinguishes "no data yet" from "no matches for these filters".

6. **Accessibility + responsive.** Keyboard/ARIA via react-aria-components; the bar collapses into a filter drawer/sheet on small viewports. *(amended 2026-06-22 — clause 6 collapse)* `<FilterBar>` renders its controls inside a collapsible disclosure panel on **all** viewports, not only small ones: a labeled "Filters" header (with the active-filter count and a chevron toggle) over a collapsible region holding the controls. The panel is **expanded by default**; collapsing reclaims vertical space while the header retains `Filters (N active)` so active filters are never hidden. This is the standard shell for **every** `<FilterBar>` consumer (Teams included). The small-viewport drawer/sheet remains the responsive form of the same collapse affordance. Open/closed state is ephemeral (component-local, resets to expanded on mount); persistence (localStorage/URL) is a deferred nicety.

7. **Server-side filtering only (MVP).** Filters map to backend query params + the `f` map; no client-only filtering of a partial cursor page (it would filter only the rows already fetched and produce wrong results). `[BoundedListResult]` lists MAY filter client-side.

8. **Enforcement of the UI standard is review-based** (like ADR-0104). A frontend lint rule flagging bespoke filter inputs adjacent to a `<DataTable>` is a possible future addition, not required now.

## Consequences

- Every list slice now produces an auditable filter decision; filtering can no longer fall silently off the first cut.
- Uniform filter UX across every list; new filter screens are *config*, not new components. Plugs into the existing `useListUrlState` / `useCursorList` / `<DataTable>` triad and the ADR-0095 `f` map with no contract change.
- The two existing ad-hoc filters (Applications `includeDecommissioned`, Members `role` + search) become refactor candidates to `<FilterBar>`. Tracked in the list-filter registry; not necessarily refactored in the slice that lands this ADR.
- Graph filters (ADR-0040) are a distinct canvas-overlay surface; they SHOULD borrow the control vocabulary but are not required to use `<FilterBar>`.
- Adds a small frontend building block to own and test; offset by deleting per-screen filter wiring over time.

## Alternatives considered

- **Amend ADR-0095 again** (keep the mandate there). Rejected — it's the third filtering concern buried in a pagination-named ADR; neither the filename nor the index would surface it. A dedicated filtering-named ADR is discoverable.
- **Rename ADR-0095** to include "filter". Rejected — ADRs are append-only records; renaming the file breaks every inbound link and the audit trail.
- **Per-screen filters (status quo).** Rejected — divergent UX, re-debated wiring every list, exactly what ADR-0095 avoided for pagination.
- **Client-side filtering.** Rejected for paged lists — filters a partial page, produces wrong results; only valid for bounded lists.
- **Defer the `<FilterBar>` component until the first faceted screen.** Rejected — the consideration mandate lands now, so the cheap-to-implement surface must exist now or "implement-now" recommendations have nowhere to land.

## References

- ADR-0095 — cursor pagination + sort contract + `f` filter-map wire format.
- ADR-0094 — Untitled UI free-tier (react-aria-components + Tailwind v4).
- `docs/design/list-filter-registry.md` — canonical per-list filter-decision record.
- E-03.F-04.S-03 (tag filtering, URL-shareable), E-05.F-01.S-02 (faceted search).
