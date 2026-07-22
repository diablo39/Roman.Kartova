# Deep PR Review — feat/catalog-system-ui-surface (E-03.F-03 System UI surface)

**Reviewed:** branch `feat/catalog-system-ui-surface` vs `master` · Status: OPEN · Reviewer: deep-review (opus) · Date: 2026-07-22

## Overview
Frontend slice adding the System catalog entity UI (list page + tabbed read-only detail + register dialog + read-only Members section), plus one 3-line backend OpenAPI doc-transformer registration for `ListSystems`. Reviewed against spec, plan, ADR index (0084/0094/0095/0107/0111/0114), ADR-0097 test taxonomy, and the DoD ledger.

**Verdict: implementation is sound and spec-faithful; no blocking-class findings.** Mirrors the Service UI surface as designed, honors every cited ADR, generated-type contract matches the code.

Cross-checks confirmed: ADR-0095 (useListUrlState + useCursorList + DataTable/SortableHead/TablePager; default `displayName asc`; allowlist = backend `SystemSortField`); ADR-0107 (displayNameContains + teamId filters; registry row flipped to built); ADR-0114 (DetailTabs Overview·Members); ADR-0084 (isRowHeader on populated + loading branches of both tables; tests assert rowheader incl. loading + before modal); ADR-0111 read path (incoming edges, client partOf-filter, isRelationshipKind nav guard); backend transformer symmetric in enum map + limit set, OpenApiTests 3/3.

## Blocking-class
None.

## Should-fix
1. **DoD ledger stale/contradictory** — recorded `HEAD: ad991d31` (branch was 2 commits ahead at `6eecca18`); Gate 1 & 3 summary rows said PASS while their detail sections said PENDING. Fix: bump HEAD + reconcile gate 1/3 detail to PASS against the final commit. **RESOLVED 2026-07-22** — ledger reconciled to `6eecca18`.

## Nits
1. `SystemMembersSection.tsx` — `ENTITY_KIND_LABEL[m.kind] ?? m.kind` falls back to raw lowercase kind for a non-app/service source. Harmless today (incoming PartOf sources are Application/Service only). Rolled up as a nit.
2. Spec §2 called the member kind "a 4-member enum incl `system`"; the regenerated `entityKind` param is a loose `string`, not an enum — the `isRelationshipKind` guard is what narrows. **Fixed** (spec wording corrected).
3. `SystemsTable.tsx`/`SystemDetailPage.tsx` — `createdAt ? … : ""/"—"` guards a required non-null field. Dead-but-defensive, consistent with the Service mirror. Left.

## Missing tests
- `useSystem`'s `enabled: id !== ""` disabled branch — low value (route always supplies `:id`).
- Dialog-level blank-description handling covered at the hook tier only; dialog reset-on-close untested. **Both addressed** by the gate-8 test additions (reset-on-close) — blank-desc stays hook-tier per ADR-0097.
- (Gate-8 pr-test-analyzer also flagged the list-error card + dialog mutation-failure path — addressed in the gate-8 test commit.)

## What looks good
1. `SystemMembersSection` read-path drift-tolerance rationale documented with an e2e cross-reference — correct and defensively sound.
2. `useRegisterSystem` maps blank/whitespace description → `null` to match the required-nullable contract, with an explanatory comment.
3. `Sidebar` nav-highlight doc comment updated to explain why plain descendant matching is correct across the 4 `/catalog/*` prefixes.
4. `CursorListQueryParameterTransformer` latent S-01 defect fixed symmetrically in enum map + limit set — keeps the typed TS client honest.
5. `graphMerge` widened-kind cast documented as behavior-preserving with FU-A deferral, backed by a system-node pass-through test.
