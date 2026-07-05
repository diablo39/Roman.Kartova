# Slice — Catalog: API connectivity via edges (provider / instance / consumer)

**Date:** 2026-07-04
**Stories:** E-02.F-03 (API entity relationships) — supersedes follow-ups FU-1 (provider FK), FU-2 (instance FK), FU-11 (polymorphic provider)
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-api-connectivity-edges`
**Governing decision:** [ADR-0111](../../architecture/decisions/ADR-0111-api-first-class-entity-provider-instance-fields.md) (**Revised 2026-07-04** — provider/instance are edges, not FK fields), [ADR-0068](../../architecture/decisions/ADR-0068-relationship-vocabulary.md) (relationship vocabulary), [ADR-0108](../../architecture/decisions/ADR-0108-either-endpoint-relationship-authority.md) (either-endpoint authority)

---

## 1. Goal

Wire the `Api` catalog entity (shipped S-01) into the relationship graph, using **edges** — not FK fields. After this slice a developer can:

- Declare that an Application **or** Service **provides** an API contract (`provides-api-for` → `Api`). Many implementers → one contract is a first-class case (**N connector services implementing one shared contract** = N edges to one `Api` node).
- Declare that an Application or Service **consumes** an API (`consumes-api-from` → `Api`).
- Declare that a Service is an **instance of** an Application (`instance-of` → `Application`).

All three ride the **existing** relationship subsystem (create endpoint, either-team authz, duplicate 409, audit, graph traversal). This slice only teaches that subsystem the new `Api` node kind and the new/enabled edge types. It is the all-edge realization of ADR-0111's revised model.

**Backend + minimal frontend hygiene.** Full API-node graph/dialog UI (Api entity picker, Api node rendering) is a separate follow-up slice (FU-A below).

---

## 2. Why edges (not FK) — recorded in ADR-0111 revision

- Provider is **many-cardinality** (one contract, N connector implementations). A single FK cannot express it; ADR-0110's low-cardinality test therefore points to an edge, not a field.
- Uniform graph: one traversal path over all edges, no synthesize-from-FK special case.
- Discovery metadata (`RelationshipOrigin`, future confidence/last-seen) already hangs off edges.
- Trade-off: referential integrity is write-time (`ICatalogEntityLookup` → 422), exactly as `DependsOn` edges already validate. Accepted.

---

## 3. Pre-requisites (already on master)

- Relationship subsystem live: `Relationship` aggregate, `EntityRef{Kind,Id}`, `RelationshipType`, `RelationshipTypeRules.{IsCreatable,IsAllowedPair}`, `RelationshipOrigin`; `CreateRelationshipAsync` endpoint delegate (existence-check → 422 `invalid-source/target-entity`, either-team authz ADR-0108, duplicate 409 + unique index `ux_relationships_edge`, `relationship.created` audit); `DeleteRelationship`; `GraphTraversalHandler` + `ICatalogEntityLookup`; `ListRelationshipsForEntity`.
- `Api` aggregate + `catalog_apis` table with RLS + `db.Apis` DbSet (S-01, PR #55).
- Persistence: `EntityRef.Kind`, `Relationship.Type`, `Relationship.Origin` all map via `HasConversion<string>()` (varchar, no CHECK constraint) → enum-value additions/removals need **no schema migration**.
- Frontend relationship UI: `relationshipTypeRules.ts` (mirror of `IsCreatable`/`IsAllowedPair`), `AddRelationshipDialog.tsx`, graph explorer (`graphModel.ts`, `graphMerge.ts`).

---

## 4. Design decisions (locked in brainstorming 2026-07-04)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Provider/instance/consumer are **edges**, not FK fields. | ADR-0111 revision; provider is many-cardinality. |
| 2 | `EntityKind` gains `Api`. Edges may now reference `Api` nodes. | Api is a graph participant. |
| 3 | `RelationshipType` gains **`InstanceOf`**. | Distinct, clear instance-of semantic (`Service → Application`). |
| 4 | `PartOf` **removed** from `RelationshipType` + rules + FE + tests. | It overlapped instance-of; System grouping (its intended future home) not built. Clean slate — no prod data (dev seed creates no relationships). Reintroduced for System in E-03.F-03. |
| 5 | `ProvidesApiFor` / `ConsumesApiFrom` (dormant enum values) become **creatable**, target = `Api`. | Enables the provider + consumer story (B1). |
| 6 | Allowed pairs: `ProvidesApiFor`,`ConsumesApiFrom`: `{Application, Service} → Api`. `InstanceOf`: `Service → Application`. `DependsOn`: `any → any` (unchanged, may now touch `Api`). | Provider/consumer originate at a component and point at the contract; instance-of is service→app. |
| 7 | **No cardinality caps** on any new edge (a Service may be `InstanceOf` several Apps; an Api may have N providers). Exact-duplicate edges still blocked by `ux_relationships_edge` (409). | Max-flexibility (user decision). Guards can be added later if a real invariant appears. |
| 8 | `ICatalogEntityLookup.Find` gains an `Api` branch (existence + `DisplayName` + `TeamId` from `db.Apis`). | Powers 422-on-unknown-Api, either-team authz using the Api's team, and Api graph-node enrichment — all for free once the lookup resolves Api. |
| 9 | **No new endpoint, no new permission, no 5-sync, no FK column, no derivation, no schema migration.** | Reuses the existing create/delete/graph endpoints and their authz; enum values are string-persisted. |
| 10 | Frontend: **hygiene only** this slice — remove `partOf` from `relationshipTypeRules.ts` + its test. No `Api` kind in the dialog/graph yet. | Option 1 scope. Prevents the UI offering a now-rejected type; full API graph UI = FU-A. |

---

## 5. Changes

### 5.1 Backend (production)

| File | Change |
|------|--------|
| `Kartova.Catalog.Domain/EntityKind.cs` | `enum EntityKind { Application, Service, Api }` |
| `Kartova.Catalog.Domain/RelationshipType.cs` | remove `PartOf`; add `InstanceOf` |
| `Kartova.Catalog.Domain/RelationshipTypeRules.cs` | `IsCreatable`: `DependsOn, InstanceOf, ProvidesApiFor, ConsumesApiFrom`. `IsAllowedPair`: per §4 #6. |
| `Kartova.Catalog.Infrastructure/CatalogEntityLookup.cs` | add `EntityKind.Api` branch querying `db.Apis` (id via `EfApiConfiguration.IdFieldName`, projects `TeamId`,`DisplayName`). |

That is the entire production backend surface — 4 files. No handler, DTO, endpoint, migration, or permission change (the create/delete/graph delegates and their authz already accept whatever `IsAllowedPair`/lookup permit).

### 5.2 Frontend (hygiene only)

| File | Change |
|------|--------|
| `web/src/features/catalog/relationships/relationshipTypeRules.ts` | remove `"partOf"` from `CreatableRelationshipType`, `relationshipTypeLabel`, `CREATABLE_TYPES`, and the `isAllowedPair` switch. `dependsOn` remains the sole creatable UI type this slice. |
| `web/src/features/catalog/components/__tests__/AddRelationshipDialog.test.tsx` | drop/adjust `partOf` assertions. |
| `web/openapi-snapshot.json` (+ regenerated `web/src/generated/*`) | regenerate — `EntityKind`/`RelationshipType` enum members change in the schema. Rebuild API image → predev/prebuild regenerates → commit. |

> The FE deliberately does **not** learn `api`/`instanceOf`/`providesApiFor`/`consumesApiFrom` yet. Those land with the API graph UI (FU-A). Removing `partOf` keeps the UI honest against the backend now.

### 5.3 Tests

| File | Change |
|------|--------|
| `Kartova.Catalog.Tests/RelationshipTests.cs` | domain unit: `InstanceOf(Service→Application)` valid; `ProvidesApiFor`/`ConsumesApiFrom` `{App,Service}→Api` valid; reject wrong pairs (`ProvidesApiFor Api→App`, `InstanceOf App→Service`, `ConsumesApiFrom Api→Service`); `DependsOn` with an `Api` endpoint still valid; `same source==target` still rejected. Remove `PartOf` cases. |
| `Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs` | real-seam: create `Application→Api` `ProvidesApiFor` 201; `Service→Api` `ProvidesApiFor` 201 (proves N-implementers-of-one-contract via two providers → one Api); `App/Service→Api` `ConsumesApiFrom` 201; `Service→Application` `InstanceOf` 201; **422** when the `Api` id is unknown/cross-tenant; **409** exact duplicate; either-team authz honoring the **Api's** team (member of the Api's team may create). Remove `PartOf` create case. |
| `Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs` | graph traversal returns an `Api` node with `displayName`/`teamId` populated (lookup Api branch) when an edge touches it. |

Real-seam mandatory (HTTP + real JWT + real Postgres/RLS via `KartovaApiFixtureBase`) per [TESTING-STRATEGY](../../TESTING-STRATEGY.md); ≥1 happy + ≥1 negative present above. No Dockerfile/`COPY` change → container-build gate is unaffected but still runs.

---

## 6. Error semantics (all reuse existing handlers)

| Case | Status | Type |
|------|--------|------|
| Source/target entity (incl. unknown/cross-tenant `Api`) not found | 422 | `…/invalid-source-entity` / `…/invalid-target-entity` |
| Disallowed type/pair (e.g. `ProvidesApiFor Api→App`) | 400 | `…/validation-failed` (via `ArgumentException` from `Relationship.CreateManual`) |
| Non-creatable type | 400 | `…/validation-failed` |
| Exact-duplicate edge | 409 | `…/relationship-already-exists` |
| Caller not OrgAdmin and not a member of source or target team | 403 | authz (ADR-0108) |
| Malformed JSON / missing field | 400 | `…/malformed-request` |

---

## 7. Impact Analysis (codelens/LSP)

Changes to **existing shared enums** (`EntityKind`, `RelationshipType`) and one static rule class (`RelationshipTypeRules`). Enum literals are **under-reported by codelens** (const/enum carve-out) → blast radius established by **grep**, cross-checked with codelens for method/switch sites.

**`RelationshipType.PartOf` (removal) — grep blast radius (backend + FE + docs):**
- `RelationshipType.cs` (definition), `RelationshipTypeRules.cs` (both methods) — edited here.
- `Kartova.Catalog.Tests/RelationshipTests.cs`, `Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs` — `PartOf` cases removed here.
- `web/src/features/catalog/relationships/relationshipTypeRules.ts`, `web/.../__tests__/AddRelationshipDialog.test.tsx` — edited here.
- Docs (`specs/plans/verification`) — historical, not touched (superseded by this spec + ADR revision).
- **No other production references.** (Verify with a fresh `grep -rn "PartOf"` at plan time; every non-doc hit must map to a task.)

**`EntityKind` (add `Api`) — switch/consumer sites:**
- `CatalogEntityLookup.Find` (switch on `EntityKind`) — edited here; the `_ => null` default currently swallows `Api` → must add the branch or unknown-Api silently 422s even when it exists.
- `EntityRef` ctor (`Enum.IsDefined`) — additive, no change needed.
- Graph DTO mapping (`GraphTraversalHandler`, `GraphNodeDto`) passes `Kind` through — no change.
- FE `graphModel.ts` `parseEntityRef`/`ENTITY_KIND_LABEL` handle only `application|service` — **intentionally left** (Api nodes not rendered until FU-A). API edges are creatable via the API; `toGraphModel`/`mergeGraphs` explicitly skip any neighbour whose kind isn't `application`/`service`, so a backend-created Api edge is excluded from the graph rather than mis-routed. Full `api`-node rendering/navigation lands in FU-A.

**`ProvidesApiFor`/`ConsumesApiFrom` (enable):** already-defined enum values; grep confirms only `RelationshipType.cs` + rules reference them today. Making them creatable is additive.

Plan tasks must cover every backend + FE + test hit above; no caller is left behind.

---

## 8. Definition of Done

The eight always-blocking gates + conditional mutation gate in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim. Mutation gate (6) **is blocking** — the diff touches Domain rule logic (`RelationshipTypeRules`). Run `scripts/ci-local.sh` (Release mirror) green before push. DoD ledger + `gate-findings.yaml` under `docs/superpowers/verification/2026-07-04-catalog-api-connectivity-edges/`.

---

## 9. Follow-ups

| ID | Work item | Owning story / ADR |
|----|-----------|--------------------|
| **FU-A** | **API graph UI** — `api` kind in `relationshipTypeRules.ts`/`graphModel.ts`, `instanceOf`/`providesApiFor`/`consumesApiFrom` creatable in `AddRelationshipDialog` with an **Api entity picker**, Api node rendering + navigation to `/catalog/apis/:id`. | new (mirror relationships UI slice) |
| **FU-B** | **Derived exposure/dependency** — compute `exposes` (Service→Api via `instance-of ∘ provides-api-for`) and service↔service `depends-on` (`consumes ∘ exposes⁻¹`) over edges; surface in graph. | ADR-0111 §Decision 3/5 (revised) |
| **FU-C** | **Async API entity** + `publishes-to`/`subscribes-from` + Broker. | E-02.F-03.S-02; E-02.F-04 |
| **FU-D** | **System grouping** — reintroduce `PartOf`/`contains` for System; System API surface derives. | E-03.F-03 |
| **FU-E** | **Unified sync/async API view per service.** | E-02.F-03.S-03 |

On save: update `docs/product/CHECKLIST.md` E-02.F-03 note (FU-1/FU-2/FU-11 superseded by this edge slice; FU-A..FU-E registered).

---

## 10. Out of scope (explicit deferrals)

- Any Api-node UI (graph/dialog/picker) → FU-A.
- Derived exposure/dependency → FU-B.
- Async APIs, `publishes-to`/`subscribes-from` → FU-C.
- System `part-of`/`contains` → FU-D (also where `PartOf` returns).
- Cardinality-cap guards on edges → deferred until a real invariant appears (§4 #7).
- Edit/delete of API entity, versioning, spec rendering, search indexing → existing later stories/epics.

---

## 11. Self-review

**Spec coverage:** §4 decisions trace to §5 (files) and §5.3 (tests); gate-5 real-seam artifacts named as deliverables in §5.3 (writing-plans emits one task each). Mutation gate blocking called out (§8). Impact analysis is grep-grounded per the enum carve-out (§7).

**Type/contract check:** `EntityKind {Application, Service, Api}` and `RelationshipType {DependsOn, InstanceOf, ProvidesApiFor, ConsumesApiFrom, PublishesTo, SubscribesFrom, DeployedOn}` (PartOf removed) consistent across §4/§5/§7. Allowed-pair matrix (§4 #6) matches the test matrix (§5.3).

**Scope check:** 4 production backend files + 2 FE files + 3 test files + snapshot regen. ~120–200 LOC production. Single-slice; no decomposition.

**Ambiguity check:** provider direction (implementer→Api), instance direction (service→app), `DependsOn` remaining any→any, uncapped cardinality, and FE-hygiene-only scope all made explicit (§4). `PartOf` removal vs future System reuse resolved (§4 #4, FU-D).

**No blocking issues found.**
