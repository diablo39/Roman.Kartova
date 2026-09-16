# Deep PR Review — Infrastructure in Catalog Hierarchy + System Members (TD-005/006)

**Target:** branch `chore/tech-debt-td-005-006` vs `master` (`git diff master...HEAD`)
**Reviewed:** 2026-09-16 · HEAD `451ff7b`
**Spec:** `docs/superpowers/specs/2026-09-16-hierarchy-infra-members-design.md`
**Plan:** `docs/superpowers/plans/2026-09-16-hierarchy-infra-members.md`
**ADRs cross-referenced:** ADR-0111 (Infrastructure is a PartOf source; `provides/instance/consumes` + `PartOf` edge model), ADR-0109 (enum wire = camelCase), ADR-0082 (hierarchy returns team IDs only), ADR-0095 (cursor lists — n/a, hierarchy is a bounded tree).

## Overview

Small, additive slice completing two deferred follow-ups: infrastructure (VMs) now appear in the `GET /catalog/hierarchy` read model + Hierarchy page (TD-005), and as linked/removable members in the System detail Members table (TD-006). A single FE `PART_OF_SOURCE_KINDS` constant replaces three inline kind-lists, mirroring backend `RelationshipTypeRules.IsPartOfSourceKind`. Backend change is a 3-line additive query + one `KindWire` arm; FE is a shared constant + consumers. Spec ↔ diff ↔ ADR-0111 all agree: Infrastructure is a legitimate PartOf source, so surfacing it in the hierarchy read model corrects a genuine read-model omission, not a new capability.

## Blocking

None.

## Should-fix

None outstanding — the two should-fix issues found by the earlier review gates (2, 7) are already fixed at `451ff7b`:
- `buildHierarchyView.ts` unguarded wire-kind cast → now filtered via `isPartOfSourceKind` at the trust boundary; `PART_OF_SOURCE_KINDS` pinned `satisfies readonly EntityKind[]`.
- `HierarchyMemberDto.Kind` contract doc comment → now lists `infrastructure` (spec-mandated).
- New `db.Infrastructure` read path → covered by a cross-tenant RLS integration assertion (Org-B VM absent from Org-A tree).

## Nits

- **Count-vs-rendered drift after the guard filter** — `buildHierarchyView.ts:31` drops a member whose `kind` is not a PartOf-source kind, but the team/system `componentCount` fields come straight from the DTO (`buildHierarchyView` maps counts independently). If the backend ever emitted an off-set kind, the count label could exceed the rendered member list. Purely theoretical today (`KindWire` only emits the three valid kinds and throws otherwise), cosmetic if it ever happened. Not worth code now; noted for awareness.
- **`infrastructure` as "a kind" now asserted in ~3 spots of `relationshipTypeRules.ts`** (`isEntityKind`, `PART_OF_SOURCE_KINDS`, and the `EntityKind` union) — acceptable (distinct sets), flagged by type-design as a watch-item as the module grows.

## Missing tests

None material. Coverage is layered and behavioral: assembler unit (assigned + ungrouped infra), real-seam integration (assigned-under-system, ungrouped, **and** cross-tenant isolation of the new infra read path), FE predicate (accept/reject), page infra-link, members infra link+Remove, node-meta exhaustiveness, and the new wire-drift drop test. The low-value gaps the test analyzer noted (structural Api-exclusion at the read model; the `KindWire` `_ => throw` defensive branch) are intentionally skipped — Api has no PartOf-source path today and both are guarded structurally.

## What looks good

- `GetCatalogHierarchyHandler.cs:29-31` — infra source mirrors the existing Apps/Services materialization shape exactly (in-memory `.Select` after `ToListAsync`, per the file's own EF-translatability docblock); `partOf` ingestion already handled infra edges, so the fix is minimal and correct.
- `SystemMembersSection.tsx:117` — the deliberate predicate split (link via `isEntityKind`, Remove via `isPartOfSourceKind`) is the right modelling: render anything routable, but only offer Remove for kinds the setter accepts. Drift-tolerant and intentional.
- `CatalogHierarchyPage.tsx:88` — replacing the `application ? … : "services"` ternary with `entityDetailPath` fixes a real latent mis-route (infra → `/services`) and removes a duplicated path-mapping.
- `buildHierarchyView.ts:29-34` — guarding the free wire string at the boundary (not casting) is the correct trust-boundary discipline; comment explains why.
- Integration test `GetCatalogHierarchyTests.cs:148-155` — cross-tenant assertion on the new read path uses id-absence with a diagnostic message; appropriate (a generic 404/absence, not a discriminative-status case, so status-blind absence is fine here per the project's isolation-test rule).

## Verdict

Merge-ready pending the remaining DoD gates (5 `/simplify`, 6 `requesting-code-review`, terminal re-verify, 9 visual, 10 CI). No blocking or should-fix findings survive review.
