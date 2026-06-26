# Deep PR Review — `feat/list-filter-surface-catalog`

**Status:** OPEN (pre-merge gate) · **Date:** 2026-06-22 · **Range:** `9d82ebb..676419c` (18 commits)
**Read against:** spec `2026-06-22-list-filter-surface-catalog-design.md`, plan `2026-06-22-list-filter-surface-catalog-plan.md`, ADR-0107, ADR-0095, ADR-0094, ADR-0097 (test taxonomy), CLAUDE.md §Definition of Done. Mutation report: N/A (gate 6 skipped by user decision, recorded in spec §7).

## Overview

The slice adds a `displayName` substring filter to the Catalog Services (`/catalog/services`) and Applications (`/catalog`) lists, builds the `boolean` control in the shared `<FilterBar>` and folds the Applications `includeDecommissioned` toggle into it, turns `<FilterBar>` into an all-viewport collapsible disclosure panel, extracts a shared `LikeEscaping.EscapeLike`, and standardizes the default list sort to `displayName asc` across Teams/Services/Applications (screen + endpoint). It also clears the codebase-wide `react-hooks/set-state-in-effect` lint debt (user-requested).

## Blocking-class issues

**None.** Every spec §3 decision is implemented (verified below), and no DoD gate is *violated*. Two DoD gates have evidence still in-flight (see Should-fix #1) — they gate merge but are not defects.

Spec-coverage spot check (all honored):
- §3 #1 displayName mirror — `ListServicesHandler.cs` + `ListApplicationsHandler.cs` apply `EF.Functions.ILike(DisplayName, "%"+EscapeLike(name)+"%", "\\")` before paging.
- §3 #4 shared `EscapeLike` — `src/Kartova.SharedKernel.Postgres/Pagination/LikeEscaping.cs:12`, called by all three handlers; private Teams copy removed (`ListTeamsHandler.cs`).
- §3 #5 `displayName asc` default — screen (`ServicesListPage.tsx`, `CatalogListPage.tsx` `useListUrlState`) + endpoint (`CatalogEndpointDelegates.cs` `?? ServiceSortField.DisplayName / SortOrder.Asc` and `?? ApplicationSortField.DisplayName / SortOrder.Asc`).
- §3 #6/#7 conditional `f`-map + blank-as-absent — endpoint `string.IsNullOrWhiteSpace ? null : .Trim()`; `f`-map key added only when applied.
- §3 #11 collapsible panel + ADR-0107 clause-6 amendment present in `docs/architecture/decisions/ADR-0107-...md:38`.

## Should-fix issues

**1. DoD gates 3 (full suite) and 4 (container build) — evidence pending, must be green before merge.**
- **Evidence:** ledger `.superpowers/sdd/progress.md` Task 12 — gate 1 build green (0/0), frontend `vitest` 523/523 green, per-task backend integration green (Services 8/8, Applications 28/28 + 4/4); but a full `dotnet test Kartova.slnx` and the `images`/ci-local Release run were not green-confirmed at review time (`scripts/ci-local.sh` running; `OpenApiTests` failed earlier only on Docker-down container init, not assertions).
- **Impact:** CLAUDE.md DoD requires gates 3 + 4 green with citable evidence; merge is blocked until ci-local returns green.
- **Fix:** confirm `scripts/ci-local.sh` exits green (backend Release build+test, web image, helm) and `OpenApiTests` passes now that Docker is up; cite the output before merge.

**2. Plan §6 named backend *unit* handler filter tests that were not added — coverage rests on the integration tier only.**
- **Evidence:** plan §6 "Backend unit: `ListServicesHandler` + `ListApplicationsHandler` filter tests (mirror `ListApplicationsHandlerFilterTests`)". No unit test in `src/Modules/Catalog/Kartova.Catalog.Tests/` references the filter; the predicate/`f`-map behavior is covered only by `ListServicesPaginationTests` / `ListApplicationsPaginationTests` (integration) + `LikeEscapingTests` (unit, escaping only).
- **Impact:** ADR-0097 pyramid tier gap — the cheap unit tier for the handler predicate + `f`-map presence/combination is absent. Behavior IS covered end-to-end at the mandated real-seam integration tier, so this is tier-completeness, not a coverage hole.
- **Fix:** add `ListServicesHandlerFilterTests` and extend `ListApplicationsHandlerFilterTests` (mock `CatalogDbContext`/`IUserDirectory`) asserting: predicate narrows by name; `f`-map carries `displayNameContains` only when present; Applications combines it with `includeDecommissioned` + `createdByUserId`. Or amend the plan to record that the integration tier is the chosen home for this behavior.

## Nits

1. **`queryFilters` heterogeneous union forces `as string|undefined` / `as boolean` casts** at 3 call sites (`ServicesListPage.tsx`, `CatalogListPage.tsx`, `TeamsListPage.tsx`). Intended per plan; tracked follow-up to expose typed accessors when a 2nd consumer lands. (sources: /simplify altitude+simplification, opus review)
2. **`useListFilters` text/bool reconcile is duplicated 2×** (`useListFilters.ts`) — extract `useDerivedDraft` when a 3rd control type (date-range/select) arrives. YAGNI now.
3. **FilterBar active count shows in header and body when expanded** (`FilterBar.tsx`) — intended per spec §5.3 (header survives collapse, body sits with Clear-all); cosmetic.
4. **`ListApplicationsHandler.cs` applies the `displayNameContains` predicate (~:74) separately from its `f`-map entry (~:93)** whereas `ListServicesHandler` co-locates them — defensible for a multi-filter handler (predicates grouped, then `f`-map), but inconsistent across the two handlers.

## Missing tests

- **Backend unit (plan §6, ADR-0097):** `ListServicesHandlerFilterTests` (new) + `ListApplicationsHandlerFilterTests` (extend) — see Should-fix #2 for the exact assertions. *Not blocking* because the integration tier (`ListServicesPaginationTests` happy/blank/cursor-mismatch/default-sort; `ListApplicationsPaginationTests` happy/combination-with-includeDecommissioned/default-sort) covers the behavior on the real seam.
- No other acceptance criterion from spec §6 is uncovered: ILIKE escaping (`LikeEscapingTests`), submit-driven boolean + reconcile + backward-compat (`useListFilters.test.tsx`), collapse/boolean/throw (`FilterBar.test.tsx`), param threading (`services/applications.test.tsx`), page wiring + filtered-empty + default-asc (`ServicesListPage`/`CatalogListPage.test.tsx`), Teams regression (`TeamsListPage.test.tsx`) all present.

## What looks good

1. **Shared `EscapeLike` extraction at the right altitude** — `LikeEscaping.cs:12` replaces three would-be copies; backslash-first ordering preserved; all three handlers call it identically (ADR-0095 `f`-map intact). Net duplication reduced, not added.
2. **Cursor `f`-map identity preserved across all three lists** — filter applied *before* `ToCursorPagedAsync`, key present only when applied, so a hidden row never becomes a cursor boundary and blank-as-absent keeps the unfiltered cursor byte-identical (ADR-0095). The Applications handler correctly keeps `includeDecommissioned` always-present while adding `displayNameContains` conditionally.
3. **`useListFilters` backward compatibility** — render-time reconcile is identity-gated against the content-memoized committed maps (no draft clobbering, the prior reconcile-bug class), and boolean reads use optional chaining so text-only callers (Teams/Services) run zero boolean code (`useListFilters.ts`).
4. **Filtered empty-state distinguishes "no data yet" from "no matches"** (ADR-0107 clause 5) — both pages gate the filtered card on `filters.isActive`, leaving the table's own "none yet" card for the unfiltered case (`ServicesListPage.tsx`, `CatalogListPage.tsx`).
5. **Collapsible disclosure done accessibly** — `FilterBar.tsx` uses `aria-expanded`/`aria-controls` to a `useId()` panel, conditionally unmounts the region (collapsed controls aren't tab-reachable), and the ADR-0107 clause-6 amendment documents the all-viewport collapse decision.

---

**Verdict:** No blocking-class defects. Mergeable once Should-fix #1 (ci-local gates 3/4 green) is confirmed; Should-fix #2 (unit-tier handler tests) is a follow-up that does not block (real-seam tier covers the behavior).
