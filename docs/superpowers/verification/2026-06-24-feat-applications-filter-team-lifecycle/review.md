# Deep Review — feat/applications-filter-team-lifecycle

**Date:** 2026-06-24
**Status:** OPEN (pre-merge gate)
**Range:** `2e223c8..d176837`
**Reviewer:** deep-review (opus), in-session delegate
**Spec:** docs/superpowers/specs/2026-06-23-applications-list-team-lifecycle-filters-design.md
**Plan:** docs/superpowers/plans/2026-06-23-applications-list-team-lifecycle-filters-plan.md
**ADRs:** ADR-0107, ADR-0095, ADR-0073, ADR-0094 · **Tests:** ADR-0097 · **Mutation:** mutation-report-surviving.md

## Counts
- Blocking: 2 · Should-fix: 2 · Nits: 3 · Missing-tests: 3 · Good: 5

---

### Overview

This slice replaces the Applications list's `includeDecommissioned` boolean filter with two multi-select filters — `lifecycle` and `teamId` — end-to-end. It builds the reusable react-aria `MultiSelect` control (hidden-input FormData bridge), a new `multiFilters` repeated-param axis in `useListUrlState`, the `FilterBar`/`useListFilters` multi-select branch, and the backend `ListApplications` `lifecycle[]`/`teamId[]` query params with `IN` predicates and sorted-comma-joined cursor `f`-map entries. The deliberate decisions (empty f-map default, boolean infra retained as reserved, deletion of the two legacy filterless/legacy-cursor 400 tests) are all honored as documented in the spec.

### Blocking-class issues

**1. Mutation gate (gate 6, BLOCKING) evidence is stale relative to the final diff.**
- Evidence: `mutation-report-surviving.md` (generated `2026-06-24T05:54Z`) cites `CatalogEndpointDelegates.cs:186` `.Distinct().ToArray()`, but final source at `d176837` is `.ToHashSet().ToArray()` (gate-5 simplify). Report predates the final code-mutating change.
- Impact: gate-6 evidence not citable against the shipped diff (DoD: gates green+citable on final code; terminal re-verify exists because gates 5–9 invalidate earlier green).
- Fix: re-run mutation on HEAD; confirm ≥80%.
- **CONTROLLER RESOLUTION:** re-ran targeted Stryker on `Kartova.Catalog.Infrastructure` at HEAD (`d176837`) — see resolution note below. The `Distinct`→`ToHashSet` swap is in Linq-ignored code (no new/changed mutants); the re-run is the citable evidence on shipped code + proves the teamId fix.

**2. Logic-class surviving mutants on the lifecycle/team branch decisions (lines 92/97).**
- Evidence: `mutation-report-surviving.md` — `ListApplicationsHandler.cs:92` `>0`→`>=0`; `:97` `>0`→`<0`/`>=0`/`!(>0)` (Survived).
- Reviewer impact: framed as predicate-vs-default-view inversion left uncaught.
- **CONTROLLER ADJUDICATION:** lines 92/97 are the **f-map key-presence guards**, NOT the predicate guards (predicates at 51/81 were KILLED by the existing unit tests — `Handle_with_no_lifecycle_filter_excludes_Decommissioned_rows` kills line-51 `>=0`; `Handle_with_teamId_filters_to_that_team` kills line-81). Of the f-map guards: the two `>=0` (always-add empty key) are **equivalent** (symmetric issue/validate, opaque/ephemeral cursor — no observable diff); the two teamId `<0`/`!(>0)` are **real** (drop the teamId cursor-mismatch guard) and were **closed by the new integration test `GET_teamId_cursor_then_changed_teamId_returns_400_cursor_filter_mismatch` (commit `5b3bdba`)** — which post-dated the stale report. The HEAD re-run confirms they are now killed.

### Should-fix issues

**1. ADR-0073 implementation note still documents the removed `includeDecommissioned` contract.**
- Evidence: `ADR-0073:60` describes `?includeDecommissioned=true` + `CursorCodec.ic` + legacy-cursor-decodes-false — all false after this slice.
- **CONTROLLER RESOLUTION:** appended a dated supersession note to ADR-0073 pointing to the lifecycle multi-select replacement (ADR-0107 clause 2).

**2. No unit-tier coverage of the cursor f-map serialization (sorted comma-join).**
- Evidence: f-map build `ListApplicationsHandler.cs:91-109` exercised only at the integration tier (`GET_lifecycle_cursor_then_changed_lifecycle_…`).
- **CONTROLLER DISPOSITION:** the f-map identity is verified at the **real seam** (lifecycle + teamId cursor-mismatch tests, both green, controller-verified 30/30) — the project's real-seam rule prefers this seam for cursor behavior. Integration tests are green locally + are a CI gate. Left as-is; not adding a redundant lower-tier test.

### Nits
1. `multi-select.tsx` hidden-input sort comment understates that test determinism is the only reason (harmless; backend re-sorts the f-map anyway).
2. `FilterBar` multi-select width `sm:w-56` hardcoded (consistent with single-select; no action).
3. `CatalogListPage` `teamNameById` memo predates this slice (used by the table; not introduced here).

### Missing tests
- (Addressed) teamId f-map cursor-mismatch guard → `GET_teamId_cursor_then_changed_teamId_…` (commit `5b3bdba`).
- (Adjudicated) lifecycle/team predicate boundaries → already killed at unit tier (lines 51/81) by the existing filter tests.
- (Conditional) unit-tier f-map order-independence → covered at the real seam; not added.

### What looks good
1. **Empty f-map default is genuinely byte-identical** — `ListApplicationsHandler.cs:91` only adds keys when `Length > 0`; `CursorFilterComparer` walks the sorted key union, so filterless cursor vs default request both carry `{}` and match. Deleting the two legacy 400 tests is correct (their premise is gone).
2. **Single atomic `setFilters` correctly extended to the multi axis** — folds the `multi` map into the same single `setParams` navigation as text/booleans (clobber fix preserved); `delete`-before-`append` prevents stale repeated params.
3. **Validation parity** — `lifecycle` rejects numeric+unknown tokens via the same `int.TryParse`+`Enum.TryParse(ignoreCase)`+`Enum.IsDefined` triad as `sortBy`, with a dedicated `InvalidLifecycleFilter` problem type; integration-tested.
4. **Sort opt-out is real** — `ApplicationSortSpecs.AllowedFieldNames` + `CatalogListPage.ALLOWED_SORT_FIELDS` both exclude lifecycle/team, matching spec §3.
5. **Real-seam integration coverage is thorough** — lifecycle reveal/all/invalid-400, team subset/union, lifecycle + teamId cursor mismatch, combined default-view + team composition; all against real Postgres/RLS + JWT (ADR-0097 tier 3).

---

## Controller resolution (2026-06-24)
- Should-fix #1: ADR-0073 dated supersession note appended.
- Should-fix #2: dispositioned (real-seam coverage suffices).
- Blocking #1 + #2: re-ran targeted mutation on HEAD (`d176837`) — see ledger / mutation-report-surviving.md (regenerated). Result documented inline above.
