# Slice — Catalog: Manual entity Relationships (backend / Slice 1a)

**Date:** 2026-06-24
**Stories:** E-04.F-01.S-01 (create a manual relationship between two entities) + E-04.F-01.S-02 (list an entity's relationships with origin) — **backend portion only**. The on-page relationship **table UI** (E-04.F-02.S-02) is a separate follow-up, **Slice 1b** (`catalog-relationships-ui-surface`), mirroring how `catalog-service-entity` (backend) then `catalog-service-ui-surface` (UI) shipped as two specs.
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-relationships`

---

## 1. Goal

Land **directed manual relationships** between catalog entities as a new sibling aggregate in the existing Catalog module. A developer can declare that one entity relates to another (`depends-on` or `part-of`), read an entity's relationships back (outgoing/incoming/all) with the standard cursor contract, and remove a relationship. Every edge is tenant-scoped (RLS), origin-stamped `manual`, audited, and validated against a fixed type/directionality matrix (ADR-0068) — the data model the deferred dependency graph (E-04.F-02) reads over without rework.

The novel surface vs. Application/Service is exactly one thing: a **polymorphic edge** referencing two entities by `(kind, id)`, where `kind` is an **open string discriminator** so the model absorbs the remaining fixed entity types and the Phase-2 tenant-defined custom entity type (ADR-0064) with zero table changes.

This is **not** all of E-04. Slice 1a is backend only — no UI. Deferred: the on-page table (Slice 1b), the embedded React Flow mini-graph + standalone `/graph` explorer + filters + impact analysis (E-04.F-02.S-01/03–06), and pin/unpin (E-04.F-01.S-03/04 — they promote/demote a `scan`/`agent` origin that does not exist until E-07/E-08/E-15).

---

## 2. Pre-requisites (already on master)

- Catalog module live with the full `Application` and `Service` slices: `CatalogModule : IModule, IModuleEndpoints`, `CatalogDbContext`, `EnlistInTenantScopeInterceptor`, direct-dispatch handler convention (ADR-0093), `CursorPage<T>` + cursor-list query-parameter transformer (ADR-0095), `DomainValidationExceptionHandler` (ArgumentException → 400 ProblemDetails), `PagingExceptionHandler`.
- `KartovaApiFixtureBase` (real Postgres Testcontainer + role/grants seed + real `JwtBearer`/`TestJwtSigner`) — relationship integration tests inherit it directly.
- Audit write path: `IAuditWriter.AppendAsync` (sync, in-transaction, fail-closed) + `CatalogAuditActions` / `CatalogAuditTargetTypes`; catalog mutations already audited via direct-dispatch delegates (audit-catalog-event-wiring, 2026-06-19).
- RBAC: `KartovaPermissions` + `KartovaRolePermissions` role→permission map; `CatalogPermissionMatrixTests` enumerates every catalog route; team-membership gate (OrgAdmin OR member of a team) used by register-application/service (ADR-0101/0103).
- `Application` (`TeamId`, `CreatedByUserId`) and `Service` (`TeamId`, `CreatedByUserId`) aggregates — the two `EntityKind`s a relationship can reference today, and the source of governing-team + display-name for an edge endpoint.

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | New `Relationship` aggregate in the **Catalog** module (single `relationships` table, polymorphic `(source_kind, source_id, target_kind, target_id)`). | Edges relate Catalog entities; ADR-0082 forbids cross-module internal refs, so relationships live where their endpoints live. Single table = both directions cheap + the shared data model the graph (ADR-0040) reads later. Approach A (user-confirmed). |
| 2 | `*_kind` persisted as an **open string discriminator** (`HasConversion<string>()`), never a Postgres `enum`/`CHECK`. C# `EntityKind` enum is for ergonomics over today's two kinds. | Absorbs fixed kinds #3–#9 and the Phase-2 custom entity type (ADR-0064) additively — no `ALTER TYPE`/migration per new kind. (User-raised; confirmed.) |
| 3 | Slice 1a scope = **POST create + GET list-by-entity + DELETE**. No GET-by-id (no single-relationship view), no edit (edges are immutable; "change" = delete+recreate), no UI. | Walking-slice discipline; mirrors Service S-01. UI = Slice 1b. |
| 4 | **Creatable types = `DependsOn`, `PartOf` only.** Full 7-member `RelationshipType` enum is defined; the other 5 are valid members rejected at creation. | Only App/Service entities exist; the other types target queues/brokers/infra/API entities that do not (ADR-0068 + user decision). Enum stays stable; creation gating is a rule, not a schema. |
| 5 | **Type-pair matrix** (`RelationshipTypeRules`): `DependsOn` allows `{Application,Service} → {Application,Service}`; `PartOf` allows `Service → Application` only. | ADR-0068 "each type has well-defined directionality + permitted source/target kinds." `PartOf` = composition (which services make up an app). |
| 6 | **`origin` forced `Manual`** via a `Relationship.CreateManual(...)` factory; `RelationshipOrigin` enum (`Manual/Scan/Agent`) persisted as string. | ADR-0067 origin is first-class + immutable. `CreateManual` encodes it at the domain boundary, leaving room for `CreateDiscovered` when scan/agent land. |
| 7 | **Source-side authority.** Create/delete require OrgAdmin **or** membership of the **source** entity's owning team. Reads are tenant-scoped (`catalog.read`). | You declare *your* entity's outgoing edges, à la Backstage/Compass (edge lives in the source's descriptor). Incoming edges appear when others declare them. (User-confirmed ⚑.) |
| 8 | Governing team + endpoint existence resolved through one **`ICatalogEntityLookup`** port: `Find(EntityKind, Guid) → { TeamId, DisplayName }?` over `CatalogDbContext`. | One RLS-scoped lookup does triple duty: existence validation (source + target), source-team resolution for auth, and display enrichment. Cross-tenant target → not found → rejected. |
| 9 | **Provenance = `CreatedByUserId` + `CreatedAt` only** (no `source_ref`/scanner-version/`last_confirmed_at`). `CreatedByUserId` is a bare `Guid`, **not** an FK to the users projection. | Manual origin collapses ADR-0067's provenance to created-by/created-at. No FK = ADR-0103 immutable-provenance semantics: ADR-0102 hard-delete leaves it as a dangling id (history, not a live ref); readable actor survives in the audit log's `actor_display` snapshot; bare uuid is not PII post-erasure. (User-confirmed: keep it.) |
| 10 | **No optimistic-concurrency token** (`xmin`/`Version`) on `Relationship`. | Create + delete only; no update path needs a token. (Departs from the Application/Service `xmin` pattern deliberately — YAGNI.) |
| 11 | **Duplicate `(tenant, source, type, target)` rejected** by a unique index + a handler pre-check → 409. | Idempotent declaration intent; the unique index is the race backstop, the pre-check gives a clean ProblemDetails. |
| 12 | **Self-reference rejected; cycles allowed.** | `depends-on` cycles are real; cycle *detection* is a later graph concern. A self-edge is never meaningful. |
| 13 | New permission `catalog.relationships.write` (create + delete), mapped to Member + OrgAdmin; reads reuse `catalog.read`. | Mirrors `catalog.services.register` + `catalog.read`. |
| 14 | Audit actions `relationship.created` + `relationship.removed`, target type `Relationship`, appended in-transaction (fail-closed). | Catalog audit-wiring pattern (2026-06-19); EPICS changelog S-05 explicitly puts relationship changes in audit scope. |
| 15 | List default sort = **`CreatedAt` desc**; sort allowlist `{ CreatedAt, Type }`. | The relationship row has no `DisplayName` of its own; sorting by the *related entity's* name needs a cross-table keyset (deferred). Newest-first is the sensible default for "recently declared edges". ⚑ Deviates from the standardized displayName-asc default — see registry note. |

---

## 4. Architecture

### 4.1 Endpoint topology added by this slice

```
POST   /api/v1/catalog/relationships                                          (tenant-scoped, NEW)
GET    /api/v1/catalog/relationships?entityKind=&entityId=&direction=…        (tenant-scoped, NEW; CursorPage<RelationshipResponse>)
DELETE /api/v1/catalog/relationships/{id:guid}                                (tenant-scoped, NEW)
```
`direction ∈ {outgoing, incoming, all}` (default `all`); plus the ADR-0095 `sortBy`/`sortOrder`/`cursor`/`limit`.

### 4.2 Create happy-path flow

```
Client → JWT auth → tenant-claims transform
      → TenantScopeBeginMiddleware (BEGIN TX, SET LOCAL app.current_tenant_id)
      → endpoint binding (POST /relationships)
      → CreateRelationshipDelegate
           ├ claim gate: catalog.relationships.write                         → 403
           ├ lookup.Find(source)                → null ⇒ 422 invalid-source-entity
           ├ membership gate: OrgAdmin OR member of source.TeamId            → 403
           ├ lookup.Find(target)                → null ⇒ 422 invalid-target-entity
           └ CreateRelationshipHandler.Handle(...)
                ├ TenantId ← ITenantContext ; CreatedByUserId ← ICurrentUser
                ├ Relationship.CreateManual(source, target, type, …)
                │     ← invariants: self-ref (400), type-not-creatable (400), bad-pair (400)
                ├ duplicate pre-check (tenant, source, type, target)         → 409
                ├ db.Relationships.Add(); SaveChangesAsync()  ← interceptor enlists; unique index backstops race → 409
                └ audit.AppendAsync(relationship.created, target=Relationship,
                                    data={ sourceKind, sourceId, type, targetKind, targetId })  ← in-txn, fail-closed
      → Results.Created(201, RelationshipResponse {source/target enriched})
      → TenantScopeCommitEndpointFilter (COMMIT TX)
```
DELETE: load by id (404 if absent) → resolve `source` team via lookup → membership gate (403) → remove → `relationship.removed` audit → 204.

### 4.3 File map

**Created — Domain (`Kartova.Catalog.Domain`):**

| File | Purpose |
|---|---|
| `RelationshipId.cs` | `readonly record struct RelationshipId(Guid Value)` + `New()`. Mirrors `ServiceId`. |
| `EntityKind.cs` | Enum `Application, Service` (string-persisted discriminator). |
| `EntityRef.cs` | Value object `{ EntityKind Kind, Guid Id }`; ctor validates `Id != Guid.Empty`; value equality drives self-ref + duplicate checks. |
| `RelationshipType.cs` | Enum (7): `DependsOn, ProvidesApiFor, ConsumesApiFrom, PublishesTo, SubscribesFrom, DeployedOn, PartOf`. |
| `RelationshipOrigin.cs` | Enum `Manual, Scan, Agent`. |
| `RelationshipTypeRules.cs` | Static rules: `IsCreatable(type)` (DependsOn/PartOf), `IsAllowedPair(type, sourceKind, targetKind)` (the matrix). Single source of truth, exhaustively unit-tested. |
| `Relationship.cs` | Sealed aggregate; `CreateManual(...)` factory + invariants; `TimeProvider` + explicit-`createdAt` overload (Service pattern). Implements `ITenantOwned`. |

**Created — Application (`Kartova.Catalog.Application`):**

| File | Purpose |
|---|---|
| `ICatalogEntityLookup.cs` | Port `Task<EntityLookupResult?> Find(EntityKind, Guid, CancellationToken)`; `record EntityLookupResult(Guid TeamId, string DisplayName)`. |
| `CreateRelationshipCommand.cs` | `record (EntityRef Source, EntityRef Target, RelationshipType Type)`. |
| `ListRelationshipsForEntityQuery.cs` | Cursor query: entity ref + `RelationshipDirection` + sort/cursor/limit. |
| `RelationshipDirection.cs` | Enum `Outgoing, Incoming, All`. |
| `RelationshipResponseExtensions.cs` | `ToResponse()` + enriched overload (source/target `EntityDisplayInfo`). |

**Created — Contracts (`Kartova.Catalog.Contracts`, all `[ExcludeFromCodeCoverage]`):**

| File | Purpose |
|---|---|
| `CreateRelationshipRequest.cs` | `{ EntityKind SourceKind, Guid SourceId, RelationshipType Type, EntityKind TargetKind, Guid TargetId }`. |
| `RelationshipResponse.cs` | `{ Id, Source{Kind,Id,DisplayName}, Target{…}, Type, Origin, CreatedByUserId, CreatedAt }`. |
| `RelationshipSortField.cs` | Enum `CreatedAt, Type`. |

**Created — Infrastructure (`Kartova.Catalog.Infrastructure`):**

| File | Purpose |
|---|---|
| `EfRelationshipConfiguration.cs` | `_id` PK; `Source`/`Target` as EF **complex properties** → `source_kind/source_id`, `target_kind/target_id`; enums `HasConversion<string>()`; indexes (incl. unique). |
| `CatalogEntityLookup.cs` | Implements `ICatalogEntityLookup` over `CatalogDbContext` (queries `Applications`/`Services` by id; RLS-scoped). |
| `CreateRelationshipHandler.cs` | Direct-dispatch (depends on `CatalogDbContext`); duplicate pre-check; audit. |
| `ListRelationshipsForEntityHandler.cs` | Keyset pagination on `(created_at, id)`; per-row enrichment of the "other" endpoint's display name. |
| `DeleteRelationshipHandler.cs` | Load + source-team resolve + remove + audit. |
| `RelationshipSortSpecs.cs` | Sort-field → column map for the cursor codec. |
| `Migrations/<ts>_AddRelationships.cs` | `relationships` table + RLS (ENABLE+FORCE+`tenant_isolation`) + indexes. |

**Created — Tests:**

| File | Purpose |
|---|---|
| `Kartova.Catalog.Tests/RelationshipTests.cs` | Aggregate + `EntityRef` + `RelationshipTypeRules` matrix unit tests. |
| `Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs` | Create happy + negatives (real seam). |
| `Kartova.Catalog.IntegrationTests/ListRelationshipsTests.cs` | Direction filter + cursor pagination + tenant isolation. |
| `Kartova.Catalog.IntegrationTests/DeleteRelationshipTests.cs` | Delete happy + 404 + 403 + audit. |

**Modified:**

| File | Change |
|---|---|
| `Kartova.Catalog.Infrastructure/CatalogDbContext.cs` | Add `DbSet<Relationship> Relationships`; apply `EfRelationshipConfiguration`. |
| `Kartova.Catalog.Infrastructure/CatalogModule.cs` | Map the 3 endpoints; register 3 handlers + `CatalogEntityLookup` (`AddScoped`). |
| `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | Add `CreateRelationshipAsync`, `ListRelationshipsAsync`, `DeleteRelationshipAsync` (claim/lookup/membership gating). |
| `Kartova.Catalog.Application/CatalogAuditActions.cs` | Add `RelationshipCreated = "relationship.created"`, `RelationshipRemoved = "relationship.removed"`; `CatalogAuditTargetTypes.Relationship = "Relationship"`. |
| `Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` | Add `CatalogRelationshipsWrite`; include in `All`. |
| `Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` | Map `CatalogRelationshipsWrite` → Member + OrgAdmin. |
| `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs` | Assert the new permission's role mapping. |
| `Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs` | Rows for the 3 new routes (POST/DELETE → write, GET → read). |
| `docs/design/list-filter-registry.md` | New canonical row for the relationship list (see §7). |

---

## 5. Components

### 5.1 `Relationship` aggregate (illustrative)

```csharp
public sealed class Relationship : ITenantOwned
{
    private Guid _id;

    public RelationshipId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public EntityRef Source { get; private set; } = default!;
    public EntityRef Target { get; private set; } = default!;
    public RelationshipType Type { get; private set; }
    public RelationshipOrigin Origin { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Relationship() { }   // EF

    public static Relationship CreateManual(
        EntityRef source, EntityRef target, RelationshipType type,
        Guid createdByUserId, TenantId tenantId, TimeProvider clock)
        => CreateManual(source, target, type, createdByUserId, tenantId, clock.GetUtcNow());

    public static Relationship CreateManual(
        EntityRef source, EntityRef target, RelationshipType type,
        Guid createdByUserId, TenantId tenantId, DateTimeOffset createdAt)
    {
        if (source == target)
            throw new ArgumentException("a relationship cannot reference the same entity as source and target", nameof(target));
        if (!RelationshipTypeRules.IsCreatable(type))
            throw new ArgumentException($"relationship type '{type}' is not yet available", nameof(type));
        if (!RelationshipTypeRules.IsAllowedPair(type, source.Kind, target.Kind))
            throw new ArgumentException($"'{type}' is not valid from {source.Kind} to {target.Kind}", nameof(type));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId required", nameof(createdByUserId));

        return new Relationship
        {
            _id = RelationshipId.New().Value, TenantId = tenantId,
            Source = source, Target = target, Type = type,
            Origin = RelationshipOrigin.Manual,
            CreatedByUserId = createdByUserId, CreatedAt = createdAt,
        };
    }
}
```

### 5.2 `EntityRef` value object + `RelationshipTypeRules`

```csharp
public readonly record struct EntityRef
{
    public EntityKind Kind { get; }
    public Guid Id { get; }
    public EntityRef(EntityKind kind, Guid id)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("unknown entity kind", nameof(kind));
        if (id == Guid.Empty)      throw new ArgumentException("entity id required", nameof(id));
        Kind = kind; Id = id;
    }
}

public static class RelationshipTypeRules
{
    // Slice 1a: only these two types are creatable.
    public static bool IsCreatable(RelationshipType t)
        => t is RelationshipType.DependsOn or RelationshipType.PartOf;

    // Directionality + permitted source/target kinds (ADR-0068), restricted to today's kinds.
    public static bool IsAllowedPair(RelationshipType t, EntityKind source, EntityKind target) => t switch
    {
        RelationshipType.DependsOn => true,                                            // any of {App,Service} → any
        RelationshipType.PartOf    => source == EntityKind.Service
                                   && target == EntityKind.Application,                // Service part-of Application
        _ => false,
    };
}
```
`record struct` gives `EntityRef` value equality, so `source == target` (self-ref) and the duplicate pre-check are one comparison each.

### 5.3 EF persistence

`relationships` columns: `id` (uuid pk), `tenant_id` (uuid), `source_kind` (text), `source_id` (uuid), `type` (text), `target_kind` (text), `target_id` (uuid), `origin` (text), `created_by_user_id` (uuid), `created_at` (timestamptz). `Source`/`Target` map via `ComplexProperty` (value objects, no identity, same row); enums via `HasConversion<string>()`.

```csharp
b.HasKey("_id");
b.ComplexProperty(x => x.Source, s => { s.Property(p => p.Kind).HasColumnName("source_kind").HasConversion<string>();
                                        s.Property(p => p.Id).HasColumnName("source_id"); });
b.ComplexProperty(x => x.Target, t => { t.Property(p => p.Kind).HasColumnName("target_kind").HasConversion<string>();
                                        t.Property(p => p.Id).HasColumnName("target_id"); });
b.Property(x => x.Type).HasConversion<string>();
b.Property(x => x.Origin).HasConversion<string>();
```

Indexes: `ix_relationships_tenant_source (tenant_id, source_kind, source_id)`, `ix_relationships_tenant_target (tenant_id, target_kind, target_id)`, unique `ux_relationships_edge (tenant_id, source_kind, source_id, type, target_kind, target_id)`. Migration adds RLS `ENABLE` + `FORCE ROW LEVEL SECURITY` + `tenant_isolation` on `current_setting('app.current_tenant_id')::uuid`, matching `AddApplications`/`AddServices` so `Kartova.Migrator` stays sole schema owner. Registered through `AddModuleDbContext<CatalogDbContext>` (ADR-0090) — already done for the module.

> **EF complex-type caveat (verify in TDD):** `EntityRef` is mapped as a `ComplexProperty` (EF Core 8 value-object-in-row, used twice). Confirm both endpoints' columns round-trip and the `record struct` is accepted as a complex type; if EF8 struct-complex-type support bites, fall back to mapping `Source`/`Target` as inline scalar properties on the aggregate (or make `EntityRef` a `record` class) — neither changes the table shape or the value-equality the self-ref/duplicate checks rely on.

### 5.4 Contracts

```csharp
public sealed record CreateRelationshipRequest(
    EntityKind SourceKind, Guid SourceId, RelationshipType Type, EntityKind TargetKind, Guid TargetId);

public sealed record EntityRefDto(EntityKind Kind, Guid Id, string DisplayName);

public sealed record RelationshipResponse(
    Guid Id, EntityRefDto Source, EntityRefDto Target,
    RelationshipType Type, RelationshipOrigin Origin,
    Guid CreatedByUserId, DateTimeOffset CreatedAt);
```

### 5.5 Handlers

- **Create** — resolves `CatalogDbContext`, `ITenantContext`, `ICurrentUser`, `IAuditWriter` from the request scope; `Relationship.CreateManual(...)` (invariants → 400 via `DomainValidationExceptionHandler`); duplicate pre-check (`AnyAsync` on the edge tuple) → 409; `SaveChangesAsync`; `audit.AppendAsync(relationship.created…)`; returns `ToResponse()` enriched with the source/target display names already fetched during gating.
- **List** — filters by entity ref and `direction` (`Outgoing`: source = entity; `Incoming`: target = entity; `All`: either), keyset-paginates on `(created_at, id)`, enriches each row's *other* endpoint via `ICatalogEntityLookup` (batched).
- **Delete** — `FindAsync(id)` (404 if absent / other tenant via RLS); resolve `source` team; membership gate; `Remove`; `audit.AppendAsync(relationship.removed…)`; 204.

---

## 6. Error handling

Inherits the catalog ProblemDetails mapping — no new exception types except where noted:

| Trigger | Status | type |
|---|---|---|
| Self-ref; non-creatable type; disallowed type-pair; empty id/kind | 400 | `…/validation-failed` (`DomainValidationExceptionHandler` ← `ArgumentException`) |
| Malformed JSON / missing required field / undefined enum value | 400 | `…/malformed-request` |
| Valid JWT lacking `catalog.relationships.write` | 403 | (authz) |
| Caller not OrgAdmin and not a member of the **source** entity's team | 403 | membership gate |
| `SourceKind/SourceId` does not resolve in tenant | 422 | `…/invalid-source-entity` |
| `TargetKind/TargetId` does not resolve in tenant | 422 | `…/invalid-target-entity` |
| Duplicate `(tenant, source, type, target)` | 409 | `…/relationship-already-exists` |
| DELETE id not found in current tenant | 404 | `…/resource-not-found` |
| Bad `sortBy`/`sortOrder` on list | 400 | `InvalidSortFieldException` → `PagingExceptionHandler` |
| Bad `limit` on list | 400 | `InvalidLimitException` → `PagingExceptionHandler` (maps to 400; corrected from the original 422 draft during implementation) |

---

## 7. List surface & Filter Proposal (ADR-0107 / ADR-0095)

Per-field surface for the relationship list (drives the 1a endpoint's sort allowlist + filter params; the 1b UI renders it):

| Field | Column (1b) | Sort | Filter |
|---|---|---|---|
| Type | ✓ badge | ✓ `Type` | **defer** — only 2 creatable types now |
| Direction | grouping | — | **core param** (`outgoing/incoming/all`), not a `<FilterBar>` facet |
| Related entity (kind + name) | ✓ linked | defer (cross-table keyset) | **defer** related-kind — only 2 kinds now |
| Origin | ✓ badge | — | **defer** — only `manual` exists |
| Created | — | ✓ `CreatedAt` (**default, desc**) | — |

**Filter Proposal outcome:** **no `<FilterBar>` facet filters built** in this list (origin/type/related-kind all explicitly deferred — single/too-few values today); `direction` is a core query parameter. **Sort allowlist:** `{ CreatedAt (default desc), Type }`. **Deviation noted:** default sort is `CreatedAt` desc, not the standardized `displayName` asc, because a relationship row has no `DisplayName` of its own and sorting by the related entity's name needs a cross-table keyset (deferred). Mirrored as a new row in `docs/design/list-filter-registry.md`.

---

## 8. Testing strategy (gate-5 artifacts)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). This slice wires HTTP + auth + DB, so the **real seam** is mandatory: real `JwtBearer` validation + real Postgres/RLS via `KartovaApiFixtureBase`; ≥1 happy + ≥1 negative per endpoint. No Dockerfile/`COPY` change, so the existing `images` CI job (gate 4) covers container build; the new EF migration is exercised by the integration suite's migrate-on-start.

### 8.1 Domain unit (`RelationshipTests.cs`)
- `CreateManual` valid `depends-on` (Service→Service) and `part-of` (Service→Application) → fields set, `Origin == Manual`, fresh `Id`.
- Rejects: self-ref (same kind+id); non-creatable type (e.g. `PublishesTo`, `ConsumesApiFrom`); disallowed pair (`PartOf` Application→Service; `PartOf` Service→Service); empty `createdByUserId`.
- `EntityRef` rejects empty id / undefined kind; value equality holds.
- `RelationshipTypeRules`: exhaustive matrix — every `(type, sourceKind, targetKind)` triple asserts expected creatable/allowed (table-driven), so a future rule change can't silently widen.

### 8.2 Create integration (`CreateRelationshipTests.cs`, real seam)
- happy: 201 + body echoes source/target (with display names) + `Origin=Manual`; row persisted with `tenant_id` from scope and `created_by_user_id` from JWT `sub`.
- negatives: 400 self-ref; 400 non-creatable type; 400 bad pair; 422 unknown source; 422 unknown target; **422 cross-tenant target** (target id from another tenant → not found via RLS); 409 duplicate; 403 caller not member of source's team and not OrgAdmin; 401 no token.
- identity-from-context: `CreatedByUserId` == caller `sub` (request has no creator/tenant field to override).

### 8.3 List integration (`ListRelationshipsTests.cs`, real seam)
- `direction=outgoing/incoming/all` return the right edge sets for an entity; **incoming `depends-on` = the consumers view**.
- `CursorPage<RelationshipResponse>` envelope; forward/backward cursor; `sortBy=Type` honored; `limit` bound; tenant-isolated (another tenant's edges never appear).

### 8.4 Delete integration (`DeleteRelationshipTests.cs`, real seam)
- happy: 204 + row gone + `relationship.removed` audit row written.
- negatives: 404 nonexistent / other-tenant id; 403 non-member of source's team.

### 8.5 Permission matrix + role map
- 3 new rows in `CatalogPermissionMatrixTests` (POST/DELETE → `catalog.relationships.write`, GET → `catalog.read`).
- `KartovaRolePermissionsTests`: Member + OrgAdmin have `CatalogRelationshipsWrite`; Viewer does not.

---

## 9. Definition of Done

The eight always-blocking gates + conditional mutation gate as defined in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; this slice does not restate them. Mutation gate (6) **is blocking** here — the diff touches Domain (`Relationship`, `EntityRef`, `RelationshipTypeRules`) and Application/Infrastructure handler logic. Run `scripts/ci-local.sh` (Release mirror) green before push.

---

## 10. Out of scope (explicit deferrals)

- **Relationship table UI** on Application/Service detail pages (origin badge, Add dialog, entity picker, Dependencies/Dependents grouping) → **Slice 1b** (`catalog-relationships-ui-surface`), the immediate next slice; closes the "consumers deferred to E-04" thread.
- Embedded React Flow mini-graph (E-04.F-02.S-01); standalone `/graph` explorer + filters + impact analysis (E-04.F-02.S-03–06).
- Pin/unpin = promote/demote origin (E-04.F-01.S-03/04) — needs a `scan`/`agent` origin that doesn't exist until E-07/E-08/E-15; conflict queue (ADR-0056) likewise.
- The other 5 relationship types + their entity kinds (queues/brokers/infra/environments/API entities) → arrive with E-02.F-03/F-04/F-05.
- API-coupling types (`consumes-api-from`/`provides-api-for`) → deferred to avoid semantic drift before API entities (E-02.F-03) exist.
- `source_ref`/scanner-version provenance, `last_confirmed_at` → land with scan/agent.
- Sort by related-entity display name (cross-table keyset); search indexing (Elasticsearch, E-05); cycle detection / graph analytics.
- GET single relationship by id (no single-relationship view).

---

## 11. Implementation order (rough — finalised by writing-plans)

1. Enums (`EntityKind`, `RelationshipType`, `RelationshipOrigin`, `RelationshipDirection`) + `RelationshipId` + `EntityRef` VO + `RelationshipTypeRules` + unit tests (TDD, RED first).
2. `Relationship` aggregate + `CreateManual` invariants + unit tests.
3. EF config + `AddRelationships` migration (complex properties + indexes + RLS ENABLE/FORCE/policy) + DbSet wiring.
4. `ICatalogEntityLookup` port + `CatalogEntityLookup` impl.
5. Contracts (`CreateRelationshipRequest`, `EntityRefDto`, `RelationshipResponse`, `RelationshipSortField`) + `ToResponse` extensions.
6. `CreateRelationshipHandler` + audit action constants + duplicate pre-check.
7. `ListRelationshipsForEntityHandler` + `RelationshipSortSpecs`; `DeleteRelationshipHandler`.
8. Permissions + role map + their tests (RED → GREEN).
9. Endpoint delegates (claim/lookup/membership gating) + `CatalogModule` wiring.
10. Integration suites (create happy/negatives incl. cross-tenant 422 + duplicate 409 + 403 membership + identity-from-context; list direction/pagination/isolation; delete happy/404/403 + audit) + permission-matrix rows.
11. `list-filter-registry.md` row.
12. `scripts/ci-local.sh`, push, open PR, run DoD gates.

---

## 12. Self-review

**Spec coverage:** every §3 decision traces to §4–§8; every gate-5 artifact in §8 is a named deliverable that writing-plans will turn into one task each.

**Placeholder scan:** no TBD/TODO. §5 code is illustrative; final code lands in executing-plans.

**Type/contract consistency:**
- `Relationship.CreateManual(source, target, type, createdByUserId, tenantId, clock)` consistent across §4.2, §5.1, §8.1.
- `EntityRef(kind, id)` value-equality used for self-ref (§5.1) and duplicate (§5.5) — consistent.
- `RelationshipType` (7 members) / creatable subset (`DependsOn`,`PartOf`) consistent across §3, §5.2, §8.1.
- `RelationshipResponse` / `EntityRefDto` shape consistent across §5.4 and §8.2.
- Auth = source-side consistent across §3#7, §4.2, §5.5, §6, §8.2/8.4.

**Scope check:** single PR, backend only; ~20 new files (most tiny: enums, VO, DTOs, rules) + ~8 modified. ~380–450 LOC production business code — under the 800 ceiling. (Frontend deliberately carved out to Slice 1b to keep the combined feature under budget, per the brainstorming size check.)

**Ambiguity check:**
- Default sort resolved to `CreatedAt` desc with an explicit, registry-mirrored justification for deviating from the displayName-asc standard (no own displayName). ⚑ confirm at review.
- `PartOf` direction pinned to `Service → Application` only (matrix, §5.2) — one row in the rules table + rejection tests for the reverse and Service→Service.

**No blocking issues found.**
