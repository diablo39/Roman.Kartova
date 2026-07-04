# List-Filter Registry

**Status:** Living document
**Owner:** Roman Głogowski (solo developer)
**Governing decision:** [ADR-0107](../architecture/decisions/ADR-0107-list-filtering-consideration-and-filterbar-ui.md) (consideration mandate + `<FilterBar>` UI), [ADR-0095](../architecture/decisions/ADR-0095-cursor-pagination-contract.md) (`f` filter-map wire format).

## Purpose

This is the **canonical, per-list record of the filter decision** required by ADR-0107 clause 1. Every list screen appears here exactly once. Each list slice's design carries a "Filter Proposal" section; its outcome is mirrored into the row below, so anyone scanning this file can confirm filtering was considered for every list — and what was decided.

**Status legend:**

- **built** — filters shipped via `<FilterBar>`/`useListFilters`.
- **built (pre-standard)** — filters shipped before ADR-0107; refactor to `<FilterBar>`/`useListFilters` is a tracked candidate, not necessarily same-slice.
- **deferred** — considered, consciously not built yet; the deferral target is named.
- **none-needed** — bounded/short list where filtering adds no value.
- **pending** — list exists but its filter decision has not been recorded; resolve at its next slice.

**Control availability:** text search + boolean toggle + **single-select** + **multi-select** controls are **built** (available for new filter specs). Date-range remains reserved (ADR-0107 clause 1), to be built when a screen first needs it.

## Registry

| List screen | Route | Filter fields | Status | Owning story | Notes |
|---|---|---|---|---|---|
| Applications | `/catalog` | `displayNameContains` (text) + `lifecycle` (multi-select) + `teamId` (multi-select) (FilterBar) | **built** | E-02.F-01 | Text search + lifecycle + team multi-select via `<FilterBar>`/`useListFilters`; submit-driven + URL-backed (repeated params `?lifecycle=&teamId=`). **Lifecycle multi-select replaces the `includeDecommissioned` boolean** (none-selected ⇒ ADR-0073 default-hide of Decommissioned; selecting Decommissioned reveals them). Team multi-select sources the first 200 teams from `useTeamsList`. Lifecycle + team **sort: opt-out** (explicit — enum-ordinal/needs-join, low value). Pulled the lifecycle/team facets forward from E-05 (faceted search). created-by facet still deferred → E-05. Field `successorApplicationId`: column=no / sort=no / filter=no — deprecation migration guidance surfaced on detail page only; not a list dimension (ADR-0107 field-addition trigger; ADR-0110 App→App successor). |
| Services | `/catalog/services` | `displayNameContains` | **built** | E-02.F-02 | Text search via `<FilterBar>`/`useListFilters`; team/health/createdBy facets deferred → E-05. |
| APIs | `/catalog/apis` | `displayNameContains` (text) + `style` (multi-select) + `teamId` (multi-select) (FilterBar) | **built** | E-02.F-03 (FU-9) | Text search + style + team multi-select via `<FilterBar>`/`useListFilters`. Sort allowlist `{displayName, style, version, createdAt}`, default `displayName asc`. Backend `ListApis` filter params mirror `ListServices`. Shipped 2026-07-04, promoted from the FU-9 deferral recorded at the API-entity slice (S-01, 2026-07-03). |
| Teams | `/teams` | `displayName` text search | **built** | E-03.F-02 | Renders via the shared `<FilterBar>` + `useListFilters`; default sort **`displayName asc`**. `<FilterBar>` shell is a collapsible disclosure panel (expanded by default), standard across all consumers. First consumer of the ADR-0107 surface (slice 2026-06-21). |
| Members / Users | `/members` (`GET /users`) | `role` (single-select) + `q` name/email search (FilterBar) | **built** | E-03.F-01.S-05 | Role single-select + name/email text search via `<FilterBar>`/`useListFilters`; submit-driven + URL-backed (`?role=&q=`). |
| Entity Relationships | App/Service detail → Relationships section (`GET /catalog/relationships?entityKind=&entityId=`) | none (no `<FilterBar>` facets) | **deferred** | E-04.F-01/F-02.S-02 | Per-entity embedded list (backend Slice 1a 2026-06-24; UI Slice 1b). `direction` (outgoing/incoming/all) is a **core query param**, not a facet. Facets considered + all deferred: `origin` (only `manual` exists → E-04.F-01.S-03/04 when scan/agent land), `type` (only 2 creatable now), related-entity-`kind` (only 2 kinds) → all → E-05 faceted search. **Sort:** `{ CreatedAt (default desc), Type }`; sort-by-related-name opt-out (cross-table keyset, deferred). Default deviates from `displayName asc` — relationship rows have no own displayName. |
| Dependency-graph filters | `/graph` | `kind` (multi-select) + `teamId` (multi-select) | **built** | E-04.F-02.S-05 | Canvas-overlay surface (React Flow `<Panel>`, ADR-0040) — **client-side dim/fade** of non-matching nodes (focus never dims; an edge dims iff either endpoint dims), **live-apply** (no submit), state in `sessionStorage` keyed by focus (only `?focus` in URL). Reuses the controlled `MultiSelect` + `useTeamsList`, **not** the `<FilterBar>` chrome (ADR-0040). Filter Proposal outcomes — `kind`: built · `teamId`: built · **status** (Lifecycle/Health): deferred (needs `GraphNodeDto` enrichment + combined-status story) · **origin**: deferred (only `Manual` exists → Phase 2 scan/agent / E-06) · **domain** + **criticality**: deferred (no backing field → new-field epic). Slice 2026-06-29. |

## Planned filtering surfaces (not yet built)

These backlog stories define multi-attribute filtering that, when built, MUST use `<FilterBar>` (ADR-0107):

- **Tag filtering across catalog** — multi-tag, AND/OR, URL-shareable, live (E-03.F-04.S-03).
- **Faceted search** — multi-select by entity type / team / tags / owner, live counts (E-05.F-01.S-02).
- **Repo-import filters** — name / language / activity (E-08.F-02.S-02).
- **Dashboards** — status board, environment map, maturity/risk heatmaps (E-06, E-17, E-18, E-20).

## How to update

When a list slice is designed: add or update its row from the slice's Filter Proposal outcome. When filters are refactored onto `<FilterBar>`, drop the "(pre-standard)" qualifier.

**New-field check (field-addition trigger, ADR-0107 clause 1).** When a slice adds a new queryable / user-facing field to an entity that already has a list here, revisit that list across all three surface axes — **column? · sort? · filter?** — and update its row (e.g. note "field `criticality`: column=yes / sort=no / filter=deferred→E-05"). New fields default to "reconsider"; opting an axis out is the explicit, recorded decision. This is a design/review gate, not an automated check.
