# Deep PR Review — VM Linking (E-02.F-04.S-01 slice 2b)

**Target:** branch `feat/catalog-vm-linking`, diff `master..HEAD` (terminal commit `e6259a1`), code files only (`src/**`, `web/**`).
**Status:** OPEN (pre-merge gate).
**Reviewed against:** spec `docs/superpowers/specs/2026-09-15-catalog-vm-linking-design.md`, plan `docs/superpowers/plans/2026-09-15-catalog-vm-linking.md`, ADR-0111/0115/0090/0095/0107/0109/0084, CLAUDE.md §Definition of Done.
**Recipe:** `docs/superpowers/templates/pr-deep-review-prompt.md`.

**Context honored (not re-flagged):** per-task reviews, whole-branch opus review (infra PartOf 500→409, `9c7f344`), /simplify (`c4bb4f5`), review-pr gate 7 (`e6259a1`); deferrals TD-005 (infra absent from `/catalog/hierarchy`), TD-006 (`SystemMembersSection` infra members), TD-007 (`ListVms` no `displayNameContains`), and the `InvalidTargetEntity`-reuse minor are treated as ruled decisions.

## Overview

The slice lets Infrastructure (VM) join the catalog graph via two relationship edges: `DeployedOn` (`{Application,Service}→Infrastructure`, VM-only) and `PartOf` (`Infrastructure→System`, at-most-one). Backend enables both edge types in `RelationshipTypeRules`, resolves Infrastructure in `CatalogEntityLookup`, adds a VM-only create guard reusing the already-loaded target `Type`, adds `PUT /infrastructure/{id}/system` over the generic `SetComponentSystemAsync`, and unifies the PartOf source-kind scoping (`IsPartOfSourceKind`) across pre-check and the 23505 catch so an infra duplicate returns 409 not 500. Frontend labels `deployedOn`, widens `ComponentKind`, adds a `DeployOnVm` dialog/action + VM detail System-membership and Hosted-components sections, and an infra graph-node icon.

## Blocking-class issues

None.

The two new edges, the VM-only guard, at-most-one PartOf for infra, and tenant isolation (source side on `PUT /system`, target side on `POST /relationships`) are all implemented and covered by real-seam integration tests. No DoD gate is left unsatisfied by the code diff.

## Should-fix issues

- **VM "Hosted components" list filters `deployedOn` client-side after a mixed 20-item page instead of the server-side `type` filter.**
  - **Evidence:** `web/src/features/catalog/pages/VmDetailPage.tsx:30-38` (`useRelationshipsList({ entityKind: "infrastructure", entityId, direction: "incoming" })` — no `type`) then `hostedEdges = hosted.items.filter(r => r.type === "deployedOn")`. `useRelationshipsList` supports a server-side `type` param (`web/src/features/catalog/api/relationships.ts:50`, `...(params.type ? { type: params.type } : {})`) and defaults `limit` to 20 (`relationships.ts:47`). The sibling System-membership read deliberately uses the server-side filter (`web/src/features/catalog/api/systems.ts:83-84` comment: "read with a SERVER-side `type=partOf` filter").
  - **Impact:** a VM with ≥20 incoming edges of mixed types, or whose `deployedOn` edges fall past the first page, silently under-counts or shows an empty "Hosted components" list — the client only ever sees page 1 (no load-more is rendered) and then discards non-`deployedOn` rows from it. Correctness gap, not just perf, and inconsistent with the established partOf pattern.
  - **Fix:** pass `type: "deployedOn"` in the `useRelationshipsList` params so the limit applies to `deployedOn` edges; drop the client-side `hostedEdges` filter (or keep it as a cheap guard). Add a VmDetailPage test seeding >20 mixed incoming edges and asserting all `deployedOn` sources render.

## Nits

- **`limit: String(10)` literal.** `web/src/features/catalog/api/relationships.ts:785` — `String(10)` is a constant; write `"10"` (or a named `VM_SEARCH_LIMIT`). The test pins `limit: "10"` so behavior is unaffected. `web/src/features/catalog/api/__tests__/relationships.test.tsx:731`.
- **Infra graph-node icon color is static across node states.** `web/src/features/catalog/components/EntityGraphNode.tsx:95-97` — the `HardDrive` icon is hardcoded `text-brand-secondary` while the sibling label uses the state-aware `labelColor` (`:100`). A dimmed/outside infra node keeps a full-strength icon. Cosmetic.

## Missing tests

- **Graph endpoint returns an Infrastructure node + `DeployedOn`/`PartOf` edges (spec Goal #3, "graph wiring").** The backend claim is "traversal is kind-agnostic, no change needed" (`GraphTraversalHandler.cs:110` `db.Relationships.Where(ids.Contains(...))`, enrichment via `lookup.Find`) — correct by construction, but nothing asserts it end-to-end. Only FE `EntityGraphNode` rendering is unit-tested (`EntityGraphNode.test.tsx:98-108`).
  - **Test to add:** `Kartova.Catalog.IntegrationTests/InfrastructureRelationshipTests` — seed Service→VM `DeployedOn` + VM→System `PartOf`, `GET` the graph focused on the VM, assert the response contains the infra node and both edges with the VM's `DisplayName` (proves `lookup.Find`'s infra arm feeds traversal enrichment, not just the create path). Gate 9 covers this visually, but a deterministic regression belongs in the suite ("any bug it finds becomes a regression test").
- **Same-tenant, non-owning-team caller is 403 on `PUT /infrastructure/{id}/system`.** Tenant isolation is tested (`InfrastructureRelationshipTests.cs:388` source-side 422) but the 403 authz branch for the new route is only implied by handler reuse.
  - **Test to add:** `InfrastructureRelationshipTests` — a same-tenant caller who is neither OrgAdmin nor a member of the VM's team gets 403 from `PUT /infrastructure/{id}/system`, mirroring the app/service `SetComponentSystemTests` 403 case, pinning that the reused authz actually runs for the infra wrapper.

## What looks good

- **VM-only guard reuses the already-loaded `targetInfo.Type` and is fail-closed on null.** `CatalogEndpointDelegates.cs:1244-1261` avoids a second `db.Infrastructure` query (a /simplify win) and treats a null `Type` as reject rather than defaulting to `default(InfrastructureType)` (= `VirtualMachine`, enum 0) — the exact trap the `EntityLookupResult.Type` doc comment calls out (`ICatalogEntityLookup.cs:12-18`). Gating on `IsAllowedPair` (not raw `type == DeployedOn && target == Infrastructure`) correctly lets an invalid pair fall through to the 400 path instead of being mislabeled "not a VM".
- **`IsPartOfSourceKind` unifies the two PartOf source-kind scopings.** `RelationshipTypeRules.cs:8-9` + its use at `CatalogEndpointDelegates.cs:1231` (pre-check) and `:1276` (23505 catch `when`) closes the drift that produced the earlier infra-duplicate 500; the shared predicate makes the pre-check and the race backstop provably agree. Directly regression-tested by `POST_partOf_infra_duplicate_returns_409_not_500` (`InfrastructureRelationshipTests.cs:462-480`).
- **Reuse of the generic `SetComponentSystemAsync` for the infra route is genuinely additive.** `CatalogEndpointDelegates.cs:1445-1458` is a thin wrapper passing `EntityKind.Infrastructure`; no handler change, at-most-one enforced by the same `SystemMembership.Decide` + `ux_relationships_one_system` path (ADR-0111), and no new `KartovaPermission` — the 5-sync correctly does not apply.
- **Tenant-isolation tests are discriminative, not just status-code checks.** `InfrastructureRelationshipTests.cs:404-405` and `:506-507` assert the specific `ProblemTypes` (`InvalidSourceEntity` vs `InvalidTargetEntity`) so a regression that flipped source/target lookup order or leaked existence would still be caught — real-seam (real Postgres/RLS + real JWT) per ADR-0090/gate 3.
- **`DeployOnVmAction` extraction takes scalars, not a `component` object.** `web/src/features/catalog/components/DeployOnVmAction.tsx:33-38` + docblock — de-duplicates the App/Service detail blocks and deliberately avoids a per-render prop object needing memoization; the `canDeploy` gate keeps the ownership decision (`isOwningTeamMemberOrAdmin`, `teamOwnership.ts`) with the caller, and the `react-aria` `isRowHeader` rule (ADR-0084) is honored and asserted in `VmDetailPage.test.tsx:1782`.
