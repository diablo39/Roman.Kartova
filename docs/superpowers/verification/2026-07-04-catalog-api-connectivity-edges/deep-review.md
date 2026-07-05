# Deep Review — Catalog: API connectivity via edges

**Target:** branch `feat/catalog-api-connectivity-edges` vs `master` (bfc6e52..e15fa3a)
**Date:** 2026-07-04
**Spec:** `docs/superpowers/specs/2026-07-04-catalog-api-connectivity-edges-design.md`
**Plan:** `docs/superpowers/plans/2026-07-04-catalog-api-connectivity-edges.md`
**ADR:** ADR-0111 (Revised 2026-07-04 — provider/instance are edges), ADR-0068 (vocabulary), ADR-0108 (either-endpoint authority), ADR-0110 (FK-vs-edge cardinality test)

## Overview

All-edge realization of the ADR-0111 revision. Production surface is exactly the 4 backend files the spec promised (`EntityKind +Api`; `RelationshipType +InstanceOf −PartOf`; `RelationshipTypeRules` matrix; `CatalogEntityLookup` Api branch) + FE hygiene (drop `partOf`) + snapshot regen. Cross-checked against spec §4 decision table, ADR-0111 revision, ADR-0068 vocabulary, ADR-0108 authority, and CLAUDE.md DoD/guardrails. Implementation is faithful; the reuse of the existing relationship create/graph/authz/audit machinery means no new endpoint/permission/FK/migration/derivation, exactly as scoped.

## Blocking
None.

## Should-fix
1. **[RESOLVED e15fa3a] Relationships-list render path not guarded like the graph.** `web/src/features/catalog/components/RelationshipsSection.tsx` rendered every relationship via `relationshipTypeLabel[...] ?? r.type` (raw camelCase label) and `entityLink(kind,id)` (app/service ternary → `api` target mis-links to `/catalog/services/{apiId}`). The fix commit 06cd66b guarded the graph render paths (`graphModel`/`graphMerge`) but not this list path — an inconsistency. Same precondition as the graph finding (an `api`-target edge is only creatable out-of-band; no UI creates them this slice). Resolved by applying the shared `isRenderableKind` filter to the list (commit e15fa3a), so all FE render paths uniformly defer `api`-node rendering to FU-A.

## Nits
1. **[RESOLVED]** Stale FE test title (`"labels both creatable types"`) and `isAllowedPair` unused-param placeholder — both addressed in 06cd66b / 840cecd (renamed + commented; the always-true `dependsOn` pair test documented as a placeholder oracle until FU-A).
2. **[by-design]** `DependsOn` remains `any→any`, so it may now touch `Api` endpoints. Intentional per spec §4 #6 (max-flexibility); domain matrix covers it. No action.

## Missing tests
1. **[RESOLVED 06cd66b]** `InstanceOf` HTTP-layer negative (`POST_instanceOf_application_to_service_returns_400`) added — closes the symmetry gap vs `ProvidesApiFor`.
2. **[accepted]** No integration case for `DependsOn` touching an `Api` endpoint at the HTTP seam. The domain `IsAllowedPair_matrix` covers the rule and the Api lookup branch is exercised by the provider/consumer/graph integration tests; a dedicated HTTP case would be redundant. Accepted, not a gap.

## What looks good
1. **ADR-0110 cardinality reasoning applied correctly** — the ADR-0111 revision (and spec §2) grounds the FK→edge flip in the provider being many-cardinality (one contract, N connectors), which is exactly ADR-0110's test; the N-providers integration test `POST_two_services_can_provide_the_same_api` (CreateRelationshipTests.cs) encodes the driving requirement and (post-06cd66b) asserts two distinct provider edges.
2. **Enrichment oracle is strong** — `GetCatalogGraphTests.GET_graph_focused_on_api_returns_provider_edge_and_enriched_api_node` asserts the Api node's `DisplayName` and `TeamId` (not mere presence), proving `CatalogEntityLookup.Find`'s `EntityKind.Api` branch (CatalogEntityLookup.cs) actually resolves the node.
3. **ADR-0108 either-team authority genuinely exercised** — `POST_providesApiFor_by_member_of_api_team_returns_201` resolves the *Api's* team through the new lookup branch, a real authority check rather than a smoke test.
4. **Additive, migration-free enum evolution** — `Api`/`InstanceOf` appended, `PartOf` removed, all `HasConversion<string>()` (EfRelationshipConfiguration.cs); existing ordinals undisturbed; no CHECK constraint → the spec's "no schema migration" claim holds.
5. **Impact analysis held** — the plan's claim that `CatalogEntityLookup.Find`'s `_ => null` default is the sole production site that must change for `EntityKind.Api` is correct; grep confirms no orphaned `PartOf` reference remains repo-wide.

## DoD cross-check
- Guardrail "new permission = 5-sync": **N/A** — no new permission (reuses relationship-create authz).
- Guardrail "new list endpoint = cursor conventions": **N/A** — no new endpoint.
- Guardrail "cross-module only via bus/Kafka": **N/A** — entirely within Catalog module.
- Mutation gate: **blocking** (Domain rule logic changed) — runs via `ci-local.sh stryker`.
