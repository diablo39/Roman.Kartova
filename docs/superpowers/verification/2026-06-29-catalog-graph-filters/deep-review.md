# Deep PR Review — S-05 Graph filters (Kind + Team)

**Target:** branch `feat/catalog-graph-filters` vs `master` (merge-base `e7b6f9e`)
**Date:** 2026-06-29
**Spec:** `docs/superpowers/specs/2026-06-29-catalog-graph-filters-design.md`
**Plan:** `docs/superpowers/plans/2026-06-29-catalog-graph-filters.md`
**ADRs cross-referenced:** ADR-0040 (two-view dependency graph), ADR-0094/ADR-0088 (React Flow + Untitled UI), ADR-0107/ADR-0095 (list-filter consideration + `f` wire format — N/A here, recorded), ADR-0109 (camelCase wire enums).
**Evidence base:** full suite 672/672, `tsc -b` clean, ADR-0084 manual PASS (`./evidence/`), gate-7 final whole-branch review = merge-ready, gate-5 `/simplify` applied.

**Counts:** Blocking 0 · Should-fix 0 · Nits 4 · Missing-test 0 · Good 5

---

## Overview

Client-side Kind + Team dimming on the standalone `/graph` explorer, implemented exactly to spec: a pure `applyGraphFilters` computes dimmed node/edge id sets over the bounded in-memory merged graph; `useGraphFilters` holds the selection in `sessionStorage` keyed by focus (mirroring `useExplorerState`); `GraphFilterControls` is a React Flow `<Panel>` overlay using a newly controlled-capable `MultiSelect`; `layoutGraph` threads a data-only `dimmed` flag so positions never move; `EntityGraphNode` renders the muted variant. Frontend-only, zero backend/codegen. The slice is correct end-to-end and integration-clean; all findings below are nit-class.

## Blocking

None.

## Should-fix

None.

## Nits

1. **Escape clears the live filter** — `multi-select.tsx` (shared). Pressing Escape while a filter dropdown is open clears that facet (react-aria `ListBox` `escapeKeyBehavior="clearSelection"` default). For a live-apply overlay this undoes an *applied* filter, unlike FilterBar's submit-driven dropdowns where it cancels *pending* choices. `escapeKeyBehavior="none"` was trialed (tsc-valid) but does not suppress the clear in the Dialog→Popover→ListBox composition; a real fix means fighting react-aria's keyboard layer in a shared a11y primitive. **Accepted/deferred:** consistent app-wide, click-outside (the common dismissal) preserves the selection, and a view filter is non-destructive. Recorded in `gate-findings.yaml`.
2. **`KIND_OPTIONS` parallel-declares the kind labels** — `GraphFilterControls.tsx:23-26`. The two values duplicate the `RelationshipKind` union (`relationshipTypeRules.ts`). A third kind would silently desync. Low risk (kinds are a closed, rarely-changing set); deriving from a canonical label map is a small follow-up.
3. **Dim opacity defined in two layers** — `EntityGraphNode.tsx` (`opacity-30`, Tailwind) and `graphLayout.ts` (`opacity: 0.2`, inline edge style). Different values, different mechanisms, no shared constant. Intentional (nodes recede less than edges) but a latent consistency point if restyled.
4. **Page-test hygiene** — `GraphExplorerPage.test.tsx`: `import React` sits after the `vi.mock` block (harmless — Vitest hoists `vi.mock`), and two `as string[]` casts carry a stale "GraphFilters not exported" rationale (it *is* exported, `graphFilter.ts:4`). Cosmetic, test-only.

## Missing tests

None. (An earlier draft flagged "focus-never-dims missing at page level" — **retracted on verification**: `GraphExplorerPage.test.tsx:175` already asserts `application:focus` `data.dimmed === false` alongside the neighbour-dims assertion. Self-caught delusion; recorded in `gate-findings.yaml`.)

## What looks good

1. **`applyGraphFilters` is a faithful, pure encoding of the spec predicate** (`graphFilter.ts`): focus-exempt, AND-across/OR-within/empty-facet semantics, null-team handling, edge-dims-iff-either-endpoint — all in one readable function with no React coupling, fully unit-tested (5 cases).
2. **`dimmed` is a data-only annotation** — `layoutGraph` runs dagre over the unfiltered graph and only stamps `data.dimmed`/edge `style`; filtering provably never moves nodes (spec Decision 9), confirmed by `graphLayout.test.ts`.
3. **`useGraphFilters` mirrors `useExplorerState`** (sessionStorage key, prev-key reconcile, safe-default on corrupt/absent/throwing storage) — consistent with the established explorer-state pattern rather than a bespoke mechanism (`useGraphFilters.ts`).
4. **The `MultiSelect` controlled-mode addition is strictly additive** (`multi-select.tsx`): `selectedKeys`+`onChange` with the uncontrolled/FormData path byte-for-byte unchanged — verified by the 16/16 FilterBar regression.
5. **The page test exercises the real pipeline** (`GraphExplorerPage.test.tsx`): `applyGraphFilters` + `layoutGraph` run live (only `useGraphFilters`/`useTeamsList`/`GraphFilterControls` are mocked), so the dimming assertion reflects real computed output, not a hollow mock.

---

## ADR / spec cross-check

- **ADR-0040** (two-view graph; explorer "can evolve … without cluttering every page"): honored — filters are a canvas-overlay surface, not bolted onto entity pages; the `<FilterBar>` list chrome was correctly *not* reused.
- **ADR-0107/0095** (list-filter consideration + `f` wire format): `/graph` is a bounded aggregate, not a cursor list — correctly N/A for the `f` map; the per-facet Filter Proposal (Kind/Team built; Status/Origin/Domain/Criticality deferred) is recorded in `list-filter-registry.md`. Field-addition trigger does not fire (no new entity field). ✔
- **ADR-0109** (camelCase wire enums): node `kind` and filter option values are camelCase (`application`/`service`); team ids are guids. ✔
- **No new ADR required** — within ADR-0040. ✔
- **Deferrals correctly unimplemented:** no Status/Origin/Domain/Criticality controls anywhere in the diff. ✔
