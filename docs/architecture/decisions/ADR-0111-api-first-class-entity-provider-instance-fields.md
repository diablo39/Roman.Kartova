# ADR-0111: API Is a First-Class Entity — Provider/Instance as FK Fields, Consumers as Edges, Exposure Derived

**Status:** Accepted (Revised 2026-07-04 — provider/instance are edges, not FK fields; Amended 2026-07-07 — unified API entity, async is a `Style` value)
**Date:** 2026-07-03
**Deciders:** Roman Głogowski (solo developer)
**Category:** Domain Model
**Related:** ADR-0068 (**amends** — relationship vocabulary), ADR-0110 (precedent — structural refs are fields), ADR-0064 (fixed entity taxonomy), ADR-0103 (required owning team), ADR-0067 (relationship origin), ADR-0040 (dependency graph), ADR-0112 (spec-artifact storage, 2026-07-07 amendment), PRODUCT-REQUIREMENTS §3 (entity table)

## Context

The product taxonomy lists **API (Sync)** and **API (Async)** as first-class entity types (alongside Application, Service, Infrastructure, Broker). Applications and Services have shipped; the API entity is now being built (E-02.F-03), starting with sync.

Two existing decisions bear on how APIs connect to the rest of the catalog:

- **ADR-0068** fixed a closed 7-type relationship vocabulary including `provides-api-for` and `consumes-api-from`, but defined them **entity→entity** (API implicit) — authored before API was a first-class entity. It has no `instance-of` concept.
- **ADR-0110** ruled that a **structural, invariant-bearing, low-cardinality** reference belongs in a **dedicated FK field, not a relationship edge** (why the deprecation successor is a field).

We must decide, coherently: where the API's provider link lives, how a running Service relates to its Application and to APIs, how consumers are modelled, and what that means for the existing `ServiceEndpoint`.

## Decision

### Amendment 2026-07-07 (E-02.F-03.S-02) — unified API entity; async is a `Style` value

This amendment supersedes the "Sync only now; async... is additive" framing in §1 and the Neutral consequence below. API is **one unified entity**, not a sync/async split: `Api.Style` gains a fourth value, `AsyncApi`, alongside `Rest`/`Grpc`/`GraphQL`. Async's messaging protocol, channels, and operations are carried **by the stored AsyncAPI spec document** (ADR-0112), **not** by structured columns on `Api` — there is no separate "Api (Async)" entity type or async-specific schema. Structured `publishes-to`/`subscribes-from` edges and Broker linkage remain **deferred** (FU-C, needs E-02.F-04, edge-authoring path); when built, they will parse channels from the stored spec document rather than duplicate them as columns.

Spec documents (OpenAPI for `Rest`/`Grpc`/`GraphQL`, AsyncAPI for `AsyncApi`) are stored as `text` in the new `catalog_api_specs` table — see ADR-0112.

Implemented by `docs/superpowers/specs/2026-07-07-catalog-async-api-spec-storage-design.md`.

> **⚠️ DRAFT — PENDING HUMAN PREVIEW.** The amendment note immediately below (dated
> 2026-07-21) was authored by an implementation agent alongside the E-03.F-03.S-01
> System-grouping slice. Per this repo's ADR process ("When proposing new ADRs: preview
> decision before saving — user reviews first", CLAUDE.md), it is **not yet accepted** —
> it must not be treated as binding, cited as settled, or used to justify further work
> until Roman Głogowski reviews and confirms it. The `Status:` line above intentionally
> still reads only through the 2026-07-07 amendment; it will be updated once this draft
> is confirmed.

### Amendment 2026-07-21 (DRAFT — PENDING HUMAN PREVIEW) — `PartOf` reintroduced for System grouping (E-03.F-03.S-01)

Implements the reintroduction flagged in the 2026-07-04 revision's vocabulary consequence
(§"will be reintroduced for System `part-of`/`contains` in E-03.F-03") and in Decision 7
above. Implementing plan: `docs/superpowers/plans/2026-07-21-catalog-system-grouping.md`
(spec: `docs/superpowers/specs/2026-07-21-catalog-system-grouping-design.md`).

- **`EntityKind` gains `System`.** A new first-class, tenant-owned, **team-stewarded**
  aggregate (`CatalogSystem` in code — named to avoid shadowing the BCL `System`
  namespace; the wire/UI vocabulary stays "System"). Fields: `DisplayName`,
  `Description?`, `TeamId` (steward team — curates the grouping; this is **not** an
  ownership transfer of member components, which keep their own independent `TeamId`).
  No nested systems, no nested-membership hierarchy this slice.
- **`RelationshipType.PartOf` is reintroduced** (it was removed by the 2026-07-04
  revision alongside the old `Service→Application` overlap with `InstanceOf`). New
  shape: `{Application, Service} → System`, i.e. "this component is part of this
  System." `Api → System` and `System → System` remain disallowed pairs (400, same
  `RelationshipTypeRules.IsAllowedPair` mechanism as every other edge type) —
  a System groups components, not APIs or other Systems, this slice.
- **Visibility is "option A": no special-casing.** `PartOf` is a normal, queryable
  relationship type. Because `EfRelationshipConfiguration`'s tenant-scoped query filter
  enumerates `Enum.GetValues<RelationshipType>()` dynamically, re-adding `PartOf` makes
  it appear automatically — with no code change — on the generic relationship-list
  endpoint (`GET /relationships`) and the graph explorer (`GET /catalog/graph`), exactly
  like `DependsOn`/`InstanceOf`/`ProvidesApiFor`. There is no System-specific read path
  this slice; a System's "member list" is just `GET /relationships?entityKind=System&entityId=...&direction=incoming`
  filtered to `type=PartOf` (or, until a dedicated filter is added, filtered client-side).
  A System's derived API surface (Decision 7's "union of its members' exposed APIs") is
  **not** computed this slice — deferred alongside the browse-by-hierarchy story
  (E-03.F-03.S-02).
- **No schema migration for the enum values** — both `EntityKind.System` and
  `RelationshipType.PartOf` persist as strings, appended last for smallint/text
  stability. A dedicated `catalog_systems` table (RLS, `ENABLE`+`FORCE ROW LEVEL
  SECURITY`+tenant policy, ADR-0090/ADR-0012 pattern) is added for the aggregate itself.
- **Permission:** `catalog.systems.register` (Member + OrgAdmin), 5-synced per the usual
  C#↔TS pattern; register/list/get follow the existing team-scoped
  `IOrganizationTeamExistenceChecker` (422 unknown/cross-tenant team) +
  `AuthorizeTargetTeamAsync`/either-team-authority (403) conventions already used by
  Api/Application/Service.

**What this does not change:** Decisions 1–6 above (API-as-entity, provider/instance
edges, derived exposure, consumers, `ServiceEndpoint`) are untouched. This amendment is
scoped entirely to Decision 7's System-grouping follow-up.

## Revision — 2026-07-04 (provider/instance modeled as edges)

This ADR originally made the API **provider** link and the Service→App **instance** link dedicated **FK fields** (Decision 2). That is **reversed**: both are now **relationship edges**, making the model all-edge for connectivity. Consumers were already edges (Decision 4), so the catalog now has a single, uniform edge mechanism.

**Trigger:** the FK provider was single-valued (`Api.implementedByApplicationId` → one app). A real requirement — **one API contract implemented by N connector services** — is many-implementers-to-one-contract, which a single FK cannot express (and FU-11's polymorphic-provider only changes the provider's *type*, not its *cardinality*).

**Why this is consistent with ADR-0110, not a contradiction:** ADR-0110 reserves FK fields for *structural, invariant-bearing, **low-cardinality*** refs. Once provider is shown to be **many**, it fails the low-cardinality test — so ADR-0110's own rule points to an edge. Backstage (this ADR's cited model) likewise expresses `providesApis` as a reference list, not a single owner. Edges also keep one uniform graph-traversal path (no synthesize-from-FK special case) and let discovery metadata (`RelationshipOrigin`, future confidence) hang off the link.

**Trade-off accepted:** referential integrity moves from DB-level FK to write-time existence checks (`ICatalogEntityLookup` → 422), consistent with how `DependsOn` edges already validate. Cardinality is intentionally **not** capped (max-flexibility); exact-duplicate edges remain blocked by the unique edge index.

### What the revision overrides
- **Decision 2 (FK fields):** superseded. Provider = `provides-api-for` edge (`{Application, Service} → Api`); instance = new `instance-of` edge (`Service → Application`). No FK columns added.
- **Decision 3 (derived exposure):** exposure is no longer derived from FKs. For now it is expressed by explicit edges; deriving `exposes`/`depends-on` from `instance-of ∘ provides-api-for` is a deferred follow-up (compute-over-edges, not over-FKs).
- **Decision 4 (consumers as edges):** unchanged — `consumes-api-from` edge → Api.
- **FU-11 (polymorphic provider):** obsolete — edges already allow App *or* Service (and future System) as provider with no schema change.

### Vocabulary consequence
`RelationshipType` gains `InstanceOf`; `ProvidesApiFor`/`ConsumesApiFrom` (dormant) become creatable with `Api` as target; `EntityKind` gains `Api`. The pre-existing `PartOf(Service→Application)` edge — which overlapped instance-of — is **removed** (System grouping not yet built) and will be reintroduced for System `part-of`/`contains` in E-03.F-03. All enum values persist as strings → **no schema migration**. Because `PartOf` was a *shipped, creatable* type, a **data-only migration** purges any pre-existing `type='PartOf'` rows (which would otherwise fail enum materialization → 500); no schema change. (Surfaced by browser verification, ADR-0084; fresh test DBs had no such row.)

Implemented by `docs/superpowers/specs/2026-07-04-catalog-api-connectivity-edges-design.md`.

---

**1. API is a first-class, tenant-owned, team-owned aggregate** (ADR-0103), sibling to Application/Service. Sync API carries `Style ∈ {Rest, Grpc, GraphQL}`, `Version` (freeform), optional `SpecUrl`. Async API is deferred (E-02.F-03.S-02) and adds messaging protocol + channels.

**2. Provider and instance links are dedicated nullable FK fields, not edges** (ADR-0110 precedent):

- `Api.implementedByApplicationId : Guid?` → `catalog.applications(id)` — the owning Application that implements the contract. **App-only** (not polymorphic); a standalone-Service-provided API is deferred, widen-later, exactly as ADR-0110 deferred App→Service succession.
- `Service.applicationId : Guid?` → `catalog.applications(id)` — the Application this Service is a running instance of. Nullable for standalone services.

**3. Exposure is derived, never stored:** a Service exposes the APIs implemented by its Application (`Service.applicationId → Application ← Api.implementedByApplicationId`). **Full-auto** — a Service exposes *all* of its Application's APIs; per-service opt-out (for worker/partial deployments) is deferred until a real case appears (YAGNI).

**4. Consumers stay relationship edges** (ADR-0068), **repointed at the API node**: `consumes-api-from` links an App/Service **→ API entity** (was entity→entity). Provider-side `provides-api-for` becomes **derived** from the provider FK, not hand-created.

**5. Service↔Service `depends-on` derives** (`consumes ∘ exposes⁻¹`) and remains additionally assertable as an explicit ADR-0068 edge.

**6. `ServiceEndpoint` drops `Protocol`, gains optional `Description`** — becoming a plain labeled network address `{ Url, Description? }`. Style/protocol now lives solely on the API. URL validation **relaxes** (bare `host:port` allowed, not only absolute-URI-with-host); `Description` ≤256 chars, optional. (Executed as a Service-modification slice with migration.)

**7. System grouping** (E-03.F-03) uses `part-of`/`contains` (ADR-0068); a System's API surface derives as the union of its members' exposed APIs.

### Amendment to ADR-0068

`provides-api-for` and `consumes-api-from` are **redefined to target the first-class API entity** (App/Service → API), superseding their original entity→entity wording. `provides-api-for` is now **derived** from `Api.implementedByApplicationId` rather than user-created. No new relationship *type* is added for instance-of (it is a field, per Decision 2). The vocabulary remains closed.

## Rationale

- **Consistency with ADR-0110.** Provider (≈1 owning app) and instance (exactly one app) are structural, low-cardinality, invariant-bearing — the exact profile ADR-0110 assigned to FK fields. A real FK gives referential integrity; RLS gives tenant isolation; both for free.
- **Derivation is the payoff.** Declaring provider + instance (fields) and consumers (edges) lets the entire dependency graph, per-service exposed/consumed API lists, and system API surface compute — no hand-maintained "SPA depends on BFF." This is Backstage's stitching model.
- **Keep topology clean.** Consumers are genuinely many-to-many and scanner-discovered → edges. Structural ownership is not → fields. Putting ownership in the graph would pollute blast-radius/dependency queries with non-topology links (the same argument ADR-0110 made for successor).
- **Style belongs to the contract.** Once `Style` is on the API, `Endpoint.Protocol` is pure redundancy; dropping it removes a dual source of truth and lets an endpoint be what it is — an address.

## Alternatives Considered

- **Pure-edge model (everything a relationship, incl. provider/instance).** Uniform, handles standalone-service providers naturally. Rejected: contradicts ADR-0110, weakens integrity to write-time lookup, and pollutes the topology graph with ownership edges.
- **Keep ADR-0068 provide/consume as entity→entity (API implicit).** No first-class API node. Rejected: contradicts the product taxonomy; blocks per-API docs (E-11), search (E-05), and versioning (E-21).
- **Polymorphic provider `{kind,id}` (App or Service) now.** Covers standalone services. Rejected for now (loses cross-table FK integrity; speculative) — recorded as the sanctioned widen-later path, mirroring ADR-0110.
- **Keep `Endpoint.Protocol`.** Rejected: dual source of truth with `Api.Style`.

## Consequences

**Positive**

- Provider/instance carry DB-level integrity + tenant isolation; exposure/dependency graph derive with zero maintenance.
- API becomes first-class → unlocks E-05 search, E-11 docs, E-21 versioning per API.
- Endpoint simplifies to a labeled address; single source of truth for style.

**Negative / trade-offs**

- **Standalone-Service-provided APIs are not yet expressible** (App-only provider FK). Widen to polymorphic later if a real need appears.
- **Full-auto exposure can over-claim** for worker/partial deployments until per-service opt-out is added.
- `ServiceEndpoint` change is a **breaking modification** to a shipped aggregate (migration, contract, tests).
- Amending ADR-0068's two API types is a semantic change; any future importer/scanner must map to the API-node target.

**Neutral**

- Sync only now; async (messaging protocol, channels, `publishes-to`/`subscribes-from`) is additive (E-02.F-03.S-02). **Superseded by the 2026-07-07 amendment above:** async landed as `Api.Style = AsyncApi` on the same unified entity, with protocol/channel detail carried by the stored spec document (ADR-0112), not structured columns.

## References

- ADR-0068 (amended), ADR-0110 (precedent), ADR-0064, ADR-0103, ADR-0040, ADR-0067, ADR-0112 (spec-artifact storage — 2026-07-07 amendment).
- PRODUCT-REQUIREMENTS §3 (entity taxonomy).
- First implementing slice: `docs/superpowers/specs/2026-07-03-catalog-api-entity-design.md` (this slice — the API node).
- 2026-07-07 amendment implementing slice: `docs/superpowers/specs/2026-07-07-catalog-async-api-spec-storage-design.md` (E-02.F-03.S-02 — unified entity, async `Style` value, spec storage).
- Downstream layers registered as follow-ups therein (provider FK, instance FK + derived exposure, consumer-edge repoint, endpoint redefinition, System surface, async).
