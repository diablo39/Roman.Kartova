# ADR-0115: Infrastructure Is a Discriminated Catalog Entity with Indexed-JSONB Variant Storage

**Status:** Proposed (pending human acceptance — previewed per the ADR working agreement)
**Date:** 2026-09-08
**Deciders:** Roman Głogowski (solo developer, AI-assisted)
**Category:** Domain Model
**Related:** ADR-0111 (**precedent** — one unified aggregate keyed by a `Style`/type discriminator; async detail in the stored spec document, not columns), ADR-0112 (spec/variant payload storage in Postgres), ADR-0064 (fixed entity taxonomy — Application, Service, API, **Infrastructure, Broker**), ADR-0095 (cursor lists), ADR-0107 (filter surface), ADR-0090 (`ITenantScope` + RLS), ADR-0109 (camelCase wire), ADR-0085 (migrations via `Kartova.Migrator`), ADR-0068 (relationship vocabulary — `deployed-on` reserved for infra hosting)

## Context

The product taxonomy (ADR-0064; `PRODUCT-REQUIREMENTS §3`) lists **Infrastructure** and **Broker** as first-class entity types alongside Application, Service, and API. Feature E-02.F-04 ("Infrastructure & Broker Entity Management"), story E-02.F-04.S-01, opens this area, starting with **Virtual Machines**.

The existing catalog uses one C# aggregate + one table + one permission family + its own list/get handlers per `EntityKind` (`Application`, `Service`, `Api`, `System`). The only discriminated model is `Api`, keyed by `ApiStyle`, with variant detail pushed to a stored document (ADR-0111/0112). Infrastructure raises the question anew: is a Virtual Machine (and later a Broker, Cluster, Container…) a **dedicated `EntityKind` each**, or **one discriminated `Infrastructure` kind** keyed by a type?

Industry incumbents both discriminate: Backstage models all infrastructure as one `Resource` kind keyed by a free-form `spec.type`; Atlassian Compass uses one `Component` with a `type` enum (VM → `CLOUD_RESOURCE`). Neither ships a bespoke per-device entity type.

## Decision

1. **One new `EntityKind.Infrastructure`** (not a dedicated `EntityKind.VirtualMachine`), keyed by an **`InfrastructureType`** discriminator whose first member is `VirtualMachine`. This matches the product taxonomy (which names *Infrastructure*, not *VirtualMachine*), the `Api`/`ApiStyle` precedent (ADR-0111), and both incumbents. It avoids one full parallel stack (enum + aggregate + permission + handlers + nav + edge rules) per future infra device type.

2. **`InfrastructureType` is a bounded enum**, persisted as `smallint` via `HasConversion<short>()`, **append-only** (values never reordered) — exactly the `ApiStyle` treatment. A bounded enum (vs Backstage's free string) is chosen for precise RBAC and typed Postgres filtering.

3. **The aggregate is kind-agnostic.** `InfrastructureResource` carries typed shared columns (`Id`, `Type`, `DisplayName`, `Description`, `TeamId`, nullable `SystemId`, `CreatedByUserId`, `CreatedAt`, `xmin`) plus an **opaque `jsonb` `attributes` string**. Type-variant attributes (for VM: `powerState`, `os`, `vcpu`, `memoryGb`, `hostname`, `ipAddresses[]`, `region`) are typed **only at the application layer** (a `VmAttributes` record ↔ JSON), never as domain properties or table columns. Mirrors ADR-0111 holding the API's variant detail in the stored spec document. A future type (Broker) adds an app-layer attributes record, with **zero** domain/table change.

4. **Flat JSONB shape** (`{ powerState, os, … }`), not type-namespaced (`{ vm: {…} }`). Each row has exactly one scalar `Type`, so the discriminator column already namespaces; an envelope would always be a single-key echo and would deepen every index/sort/filter expression. (Type-namespacing would only pay off under a multi-facet model — a row that is several types at once — which is explicitly out of scope: one scalar `Type` per row.)

5. **Two endpoint tiers.** A **generic** resource-level endpoint (`GET /catalog/infrastructure`) returns shared columns only and **never deserializes** `attributes` — it stays kind-agnostic forever. A **type-specific** VM tier (`GET /catalog/infrastructure/vms`, `GET .../vms/{id}`, `POST .../vms`) rehydrates `attributes` into a typed `VmAttributes` DTO and owns VM write validation. Filterable VM attributes are queried with `EF.Functions.JsonContains` (`@>` containment), served by a **GIN `jsonb_path_ops`** index on `attributes`. Attribute validation (enum power state, positive `vcpu`/`memoryGb`, per-NIC IP parse, length/count caps) lives in the application layer and surfaces as `400` via the shared domain-validation handler.

6. **Permissions and RLS follow the established per-kind pattern.** Reads gate on the shared `catalog.read`; writes on a new `catalog.infrastructure.register` (the standard 5-touchpoint C#↔TS sync). The `catalog_infrastructure` table carries the same hand-written `tenant_isolation` RLS policy (`FORCE ROW LEVEL SECURITY`) as every sibling catalog table (ADR-0090), registered via `AddModuleDbContext`.

### Deferred (explicit)

- **JSONB-column sort.** Slice-1 sortable fields are the **typed columns** `displayName` (default `asc`) / `type` / `createdAt` only. Sorting on a JSONB attribute needs an EF-translatable, index-matched keyset ORDER BY expression that cannot be produced from the opaque-string `attributes` column without leaking a typed POCO into the shared aggregate (violating decision #3). VM attributes are therefore **filter-only** (`@>`, GIN) in slice 1; JSONB-column sort + its partial btree-expression indexes are a follow-up. (This narrows the original slice spec §5.3/§6.5, which proposed JSONB sort + partial expression indexes.)
- **Relationships / edges.** `PartOf` System membership (`SystemId` column present but unwritten) and `DeployedOn` (`{Application,Service}→Infrastructure`, the reserved ADR-0068 edge) are deferred to a later slice, along with hierarchy/graph integration. Until then Infrastructure does not appear in the relationships/graph/hierarchy model.
- **`provider` field** (part of the S-01 acceptance) and the long-tail VM attributes (`diskGb`, `hypervisor`, `tags[]`) are deferred.
- **Broker** (E-02.F-04.S-02) is a later slice, reusing this aggregate with `InfrastructureType.Broker`.

## Consequences

**Positive.** One nav group, one permission family, one pair of endpoint tiers, and one table absorb every future infrastructure sub-type. The aggregate never learns VM (or Broker) specifics. Adding an infra type is an enum member + an app-layer attributes record + a nav link. Consistent with ADR-0111 and with both incumbent developer portals.

**Negative / costs.** Attribute type-safety is application-layer only — the `jsonb` column stores text, so the DB will not enforce `int`/enum shapes; the VM write path validates before persisting, and `FromJson` reads already-validated stored data. Filter-only JSONB in slice 1 means no sort-by-power-state yet. A single `GIN jsonb_path_ops` index serves containment filters; the `type` column is unindexed while only one `InfrastructureType` exists (a partial/composite index becomes worthwhile when a second type lands).

**Neutral.** `EntityKind` gains `Infrastructure`; C# consumers are safe (`EntityRef` stores the kind as a string; `CatalogEntityLookup` has a `_ => null` default; the hierarchy switch is unreachable for infra in slice 1). The generated TypeScript client's `EntityKind` widened correspondingly, requiring the frontend's local `EntityKind` union + kind-keyed maps to include `"infrastructure"`.

## Alternatives considered

- **Dedicated `EntityKind.VirtualMachine`** (a top-level kind per device): rejected — contradicts the product taxonomy, sets a precedent where Broker/Cluster/Container each need a full parallel stack, and diverges from both incumbents.
- **Type-namespaced JSONB** (`{ vm:{…}, broker:{…} }`): rejected — solves a cross-type key collision that cannot occur under a scalar `Type`, and deepens every expression for no benefit. Only warranted by a multi-facet model, which is out of scope.
- **Free-string `spec.type`** (Backstage-style): rejected — a bounded enum serves RBAC + typed Postgres filters and matches the `ApiStyle` drift-hardening precedent.
- **Single overloaded endpoint** with a nullable VM block for both views: rejected — the generic tier would gain per-type knowledge it must never have; two tiers keep it kind-agnostic.
- **All variant attributes in opaque JSON, list rehydrated for sort/filter**: rejected for sort — a JSONB sort key without an expression index breaks ADR-0095 cursor keyset (seq-scan, non-deterministic order); hence the filter-only decision above.

Implemented by `docs/superpowers/specs/2026-09-08-infrastructure-vm-slice1-design.md` (E-02.F-04.S-01, slice 1).
