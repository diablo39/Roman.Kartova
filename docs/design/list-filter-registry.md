# List-Filter Registry

**Status:** Living document
**Owner:** Roman Głogowski (solo developer)
**Governing decision:** [ADR-0107](../architecture/decisions/ADR-0107-list-filtering-consideration-and-filterbar-ui.md) (consideration mandate + `<FilterBar>` UI), [ADR-0095](../architecture/decisions/ADR-0095-cursor-pagination-contract.md) (`f` filter-map wire format).

## Purpose

This is the **canonical, per-list record of the filter decision** required by ADR-0107 clause 1. Every list screen appears here exactly once. Each list slice's design carries a "Filter Proposal" section; its outcome is mirrored into the row below, so anyone scanning this file can confirm filtering was considered for every list — and what was decided.

**Status legend:**

- **built** — filters shipped.
- **built (pre-standard)** — filters shipped before ADR-0107; refactor to `<FilterBar>`/`useListFilters` is a tracked candidate, not necessarily same-slice.
- **deferred** — considered, consciously not built yet; the deferral target is named.
- **none-needed** — bounded/short list where filtering adds no value.
- **pending** — list exists but its filter decision has not been recorded; resolve at its next slice.

## Registry

| List screen | Route | Filter fields | Status | Owning story | Notes |
|---|---|---|---|---|---|
| Applications | `/catalog` | `includeDecommissioned` (default-view toggle) | built (pre-standard) | E-02.F-01 | Refactor the toggle into `<FilterBar>`; broader type/team/tag filtering tracked under E-05 faceted search. |
| Services | `/catalog/services` | — | deferred → E-05 (faceted) | E-02.F-02 | Frontend-only S-02 slice deferred search/filter to E-05 (its §9). Reassess if a Services-specific filter is requested sooner. |
| Teams | `/teams` | `displayName` text search | **built** | E-03.F-02 | Renders via the shared `<FilterBar>` + `useListFilters`; default sort **`displayName asc`**. First consumer of the ADR-0107 surface (slice 2026-06-21). |
| Members / Users | `/members` (`GET /users`) | `role` (viewer/member/orgAdmin/all) + name/email search | built (pre-standard) | E-03.F-01.S-05 | Refactor `role` dropdown + search into `<FilterBar>`/`useListFilters`. |

## Planned filtering surfaces (not yet built)

These backlog stories define multi-attribute filtering that, when built, MUST use `<FilterBar>` (ADR-0107):

- **Tag filtering across catalog** — multi-tag, AND/OR, URL-shareable, live (E-03.F-04.S-03).
- **Faceted search** — multi-select by entity type / team / tags / owner, live counts (E-05.F-01.S-02).
- **Dependency-graph filters** — team / domain / criticality / entity-type / origin (E-04.F-02.S-05). Distinct canvas-overlay surface; borrows the control vocabulary, not required to use `<FilterBar>` (ADR-0040).
- **Repo-import filters** — name / language / activity (E-08.F-02.S-02).
- **Dashboards** — status board, environment map, maturity/risk heatmaps (E-06, E-17, E-18, E-20).

## How to update

When a list slice is designed: add or update its row from the slice's Filter Proposal outcome. When filters are refactored onto `<FilterBar>`, drop the "(pre-standard)" qualifier.

**New-field check (field-addition trigger, ADR-0107 clause 1).** When a slice adds a new queryable / user-facing field to an entity that already has a list here, revisit that list across all three surface axes — **column? · sort? · filter?** — and update its row (e.g. note "field `criticality`: column=yes / sort=no / filter=deferred→E-05"). New fields default to "reconsider"; opting an axis out is the explicit, recorded decision. This is a design/review gate, not an automated check.
