# Design — VM Linking (E-02.F-04.S-01 slice 2b)

**Date:** 2026-09-15
**Slice:** E-02.F-04.S-01 slice 2b — VM linking
**Classification:** Bounded (reuses relationship-edge + at-most-one-System machinery)
**ADRs:** ADR-0111 (relationships-as-edges; PartOf at-most-one), ADR-0115 (kind-agnostic Infrastructure aggregate keyed by `InfrastructureType`), ADR-0040 (graph)

## Goal

Let Infrastructure (VM) participate in the catalog graph via two relationship edges:

1. **`DeployedOn`** — `{Service, Application} → Infrastructure`, target restricted to **VMs** (`InfrastructureType.VirtualMachine`).
2. **`PartOf`** — `Infrastructure → System` (at-most-one System per infra component, reusing existing machinery).

Plus graph wiring so Infrastructure nodes and both edges render in `/graph` and the per-entity mini-graph.

## Context / current gaps

- `CatalogEntityLookup.Find` returns `null` for `EntityKind.Infrastructure` (`_ => null`) — infra cannot resolve as a relationship endpoint today → 422 on any edge touching it. **Root blocker for both edges.**
- `RelationshipType.DeployedOn` exists in the enum but is absent from `RelationshipTypeRules.IsCreatable` and `IsAllowedPair`.
- `RelationshipTypeRules.IsAllowedPair` `PartOf` allows only `{Application, Service} → System`.
- `SetComponentSystemAsync` is already `EntityKind`-parameterized (App/Service routes call it); `SystemMembership.Decide` is kind-agnostic. Extending to Infrastructure = add route + pass `EntityKind.Infrastructure` + fix the hardcoded 409 detail message.
- FE `isRenderableKind` (graph.ts, relationships.ts) excludes Infrastructure; `EntityGraphNode` has no Infrastructure styling. (API-node rendering is separately deferred as FU-A and stays deferred.)

## Decisions (from brainstorming, 2026-09-15)

| Decision | Choice | Consequence |
|----------|--------|-------------|
| DeployedOn source kinds | **Service + Application** | Both `{Service,Application}→Infra` allowed. |
| DeployedOn target | **VM-only** | `IsAllowedPair` is kind-level and cannot see `InfrastructureType`; VM-only needs a targeted guard at the create path (load target infra, require `Type == VirtualMachine`, else 422). |
| Graph rendering scope | **Render Infra nodes + both edges this slice** | FE `isRenderableKind` + `EntityGraphNode` gain Infrastructure; API-node rendering (FU-A) stays deferred. |

## Backend changes

| File | Change |
|------|--------|
| `RelationshipTypeRules.cs` | `IsCreatable` += `DeployedOn`. `IsAllowedPair`: `DeployedOn => source is Application or Service && target == Infrastructure`; `PartOf` source += `Infrastructure`. |
| `CatalogEntityLookup.cs` | Add `EntityKind.Infrastructure => db.Infrastructure … Select(new EntityLookupResult(TeamId, DisplayName))`. |
| Relationship create path (`CreateRelationshipAsync`) | VM-only guard: when `DeployedOn` + target kind `Infrastructure`, load `InfrastructureResource.Type`; require `VirtualMachine` → else 422. |
| `CatalogEndpointDelegates.cs` | New route `PUT /api/v1/catalog/infrastructure/{id}/system` → `SetComponentSystemAsync(EntityKind.Infrastructure, …)`. Fix hardcoded 409 detail `{applications\|services}` → include infrastructure (kind-aware). |
| Graph traversal (`GraphTraversalHandler` / `DerivedEdgeLoader`) | Enumerate Infrastructure nodes + traverse `DeployedOn` / `PartOf`. **`DeployedOn` does NOT feed derived depends-on** (structural placement, not a dependency). |

At-most-one-System reused unchanged: `SystemMembership.Decide` is kind-agnostic; `ux_relationships_one_system` keys on source id (infra GUIDs distinct). Both PartOf write paths (`PUT /{id}/system`, `POST /relationships`) already route through `Decide`; the infra route is the sanctioned 4th path, not a raw insert.

No new `KartovaPermission` — relationship writes use existing either-team authz; `/system` write reuses the component-membership pattern.

## Frontend changes

- `graph.ts` + `relationships.ts`: `isRenderableKind` += Infrastructure.
- `EntityGraphNode.tsx`: Infrastructure node style + icon.
- `AddRelationshipDialog.tsx`: offer `DeployedOn` (`{Service,Application}→VM`) + `PartOf` (VM→System).
- VM detail page: "System" membership control + "Deployed on / hosts" section (mirrors app/service system-membership UI).
- Regenerate codegen client for the new endpoint.

## Testing strategy (real-seam per docs/TESTING-STRATEGY.md; DoD gate 3)

- **Domain unit:** new `IsCreatable` / `IsAllowedPair` rows.
- **Integration (KartovaApiFixtureBase, real Postgres/RLS + real JWT):**
  - happy: DeployedOn `Service→VM`; DeployedOn `Application→VM`; PartOf `VM→System`.
  - negative: DeployedOn → non-VM infra → 422; disallowed pair (e.g. DeployedOn target = System) → 422; infra endpoint resolves (regression on the `_ => null` 422); PartOf `VM→System` at-most-one replace; cross-tenant → 422.
- **FE:** `isRenderableKind` includes Infrastructure; graph renders an infra node; `AddRelationshipDialog` offers the new types.

## Out of scope (YAGNI / follow-ups)

- Derived depends-on via `DeployedOn`.
- Broker (`InfrastructureType.Broker`) DeployedOn — E-02.F-04.S-02.
- Environment / deployment-event tracking — E-02.F-05.
- API-node graph rendering — FU-A.

## Size

~250–350 LOC production business code (rules/lookup/guard/route small; bulk is FE render + dialog). Under the ~400 target → single slice.
