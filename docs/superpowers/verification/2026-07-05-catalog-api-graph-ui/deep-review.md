# Deep Review — `feat/catalog-api-graph-ui` (FU-A + FU-A1)

**Reviewed against:** spec `docs/superpowers/specs/2026-07-05-catalog-api-graph-ui-design.md`, plan `docs/superpowers/plans/2026-07-05-catalog-api-graph-ui.md`, ADR-0111 (revised 2026-07-04, all-edge model), ADR-0084 (isRowHeader / Playwright verification), CLAUDE.md DoD.
**Diff:** 11 commits, 23 files, +431/-169.

### Overview

The branch delivers every §5 promise in the spec: API nodes render in graph model/merge/sidebar, a graph filter chip for `api`, an API entity picker in the relationship dialog, a generic Outgoing/Incoming `RelationshipsSection` with an `incoming-only` variant, and the CLAUDE.md ADR-0111 guardrail rewrite — each verified against near-verbatim matching plan code. The per-type FE pair matrix (`relationshipTypeRules.ts:36-60`) correctly implements the all-edge model from ADR-0111's 2026-07-04 revision (no FK fields reintroduced), and `isRenderableKind` was fully removed with zero dangling references (confirmed by repo-wide grep). One post-hoc fix (commit `1b8be1c`) landed outside the plan's task list and lacks direct test coverage for its own behavior.

### Blocking-class issues

None.

### Should-fix issues

**1. The "skip outgoing fetch on read-only page" fix has no test proving the fetch is skipped**
- Evidence: `web/src/features/catalog/components/RelationshipsSection.tsx:47` passes `{ enabled: variant === "full" }` to `useRelationshipsList`; the plumbing lives in `web/src/features/catalog/api/relationships.ts:30,34` and `web/src/lib/list/useCursorList.ts:11,29,50`. Every consumer test (`RelationshipsSection.test.tsx`, `ApiDetailPage.test.tsx`) mocks `useRelationshipsList` directly via `vi.spyOn(api, "useRelationshipsList").mockImplementation(...)`, so `opts.enabled` is never exercised. `useCursorList.ts` also has no dedicated test file at all.
- Impact: the exact defect this fix commit was written to close (wasted outgoing fetch on `ApiDetailPage`, per `dod.md` gate 7 entry) can silently regress — e.g. a future refactor could drop `enabled` from the passthrough and no test would catch it.
- Fix: add one assertion-level test — either a real (non-mocked) `useRelationshipsList` test asserting the query is `enabled: false`/not fired when `variant="incoming-only"`, or a `useCursorList` unit test asserting `enabled: false` short-circuits `useQuery`.

**2. `gate-findings.yaml` is missing**
- Evidence: `docs/superpowers/verification/2026-07-05-catalog-api-graph-ui/dod.md:7` declares `**Findings telemetry:** ./gate-findings.yaml`, but the directory contains only `dod.md` (confirmed via directory listing). CLAUDE.md's Definition of Done section requires it "alongside `dod.md`."
- Impact: gate-level findings (e.g. the gate-7 should-fix items already applied, the two `/simplify` items still queued) aren't captured in the machine-readable ledger the project convention relies on for cross-slice querying.
- Fix: create `gate-findings.yaml` from `docs/superpowers/templates/gate-findings-template.yaml` and backfill the findings already known from gates 5 and 7 before closing the DoD ledger.

### Nits

**1. `GraphExplorerSidebar.tsx` isn't listed in spec §5.1 or the plan's task file tables**
- Evidence: modified across commits `5015f40` and `2ef2fd7` (widened `Selected`/`entity` types, added `useApi` lookup) with matching test coverage in `GraphExplorerSidebar.test.tsx`; grep of both `docs/superpowers/specs/2026-07-05-catalog-api-graph-ui-design.md` and the plan for "GraphExplorerSidebar" returns no hits.
- Impact: documentation-only gap — the work is squarely in-scope (spec §1 implies sidebar API support) and correctly tested, just not enumerated.
- Fix: none required functionally; note in a spec amendment if the file table is meant to be exhaustive.

**2. `useCursorList.ts` / the `opts.enabled` addition to `relationships.ts` are undocumented in spec and plan**
- Evidence: only touched by review-fix commit `1b8be1c`; grep of spec and plan for "useCursorList" returns no hits.
- Impact: minor — small (5-line), backward-compatible (`enabled = true` default), low risk change, but introduced outside the plan without a plan/spec addendum.
- Fix: none required now; the missing-test item above (Should-fix #1) is the substantive follow-up.

**3. Two `/simplify` should-fix items are still queued, not yet applied**
- Evidence: `dod.md:20,44` — "2 should-fix (nested-ternary kind mapping in `graphModel.entityDetailPath` + `GraphExplorerSidebar` active-query selection) queued for the consolidated fix wave."
- Impact: gate 5 isn't closed as of this diff snapshot; terminal re-verify must happen after these land.
- Fix: apply, then re-run build+suite per the plan's terminal re-verify step (already tracked in `dod.md`).

### Missing tests

- `useCursorList({ enabled: false })` should not fire `queryFn` — assert via `useQuery`'s `enabled` flag not being invoked (e.g. spy on `fetchPage`, expect zero calls) in a new `useCursorList.test.ts`. Currently no test file exists for this module at all.
- `useRelationshipsList(params, { enabled: false })` should produce a disabled query — currently masked in every test by mocking the hook wholesale (`RelationshipsSection.test.tsx:15,62,74`).

### What looks good

- `relationshipTypeRules.ts:36-60` — `isAllowedPair`'s switch statement is a precise, ADR-0111-consistent implementation of the spec §4 FE pair matrix (dependsOn app/service↔app/service; instanceOf service→application; providesApiFor/consumesApiFrom {app,service}→api), correctly stricter than the backend's all-edge model per intentional design.
- `CLAUDE.md`'s ADR-0111 guardrail rewrite matches the ADR's 2026-07-04 revision text verbatim (all-edge, no FK columns, exposure/depends-on deferred to FU-B) — no stale FK-field language survives.
- `relationshipTypeRules.ts:16-18` — `dependsOn` correctly kept first in `CREATABLE_TYPES` per the plan's explicit global constraint (dialog default, existing tests depend on it).
- `RelationshipsSection.test.tsx:39-41` and `RelationshipsSection.tsx:99,115` — the `isRowHeader` regression test and both table columns are present, directly honoring the ADR-0084 guardrail (missing `isRowHeader` blank-pages the screen on a heavier re-render).
- `isRenderableKind` was cleanly deleted (relationshipTypeRules.ts, graphMerge.ts, graphModel.ts, useGraphFilters.ts, plus their tests) with zero dangling references confirmed by a repo-wide grep — no half-finished rename left behind.

---
**Counts:** Blocking 0 · Should-fix 2 · Nits 3 · Missing tests 2 · What looks good 5.
