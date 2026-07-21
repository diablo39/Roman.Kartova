# Design — System grouping entity + PartOf assignment (backend-first)

**Story:** E-03.F-03.S-01 — Create System and assign components
**Date:** 2026-07-21 · **Author:** Roman Głogowski (AI-assisted)
**ADRs touched:** ADR-0111 (§7 reserved `part-of` for System — this doc is the required design note; **amendment proposed**: `PartOf` reintroduced with allowed pair `{Application, Service} → System`) · ADR-0068 (fixed relationship vocabulary — `part-of`, *unchanged*) · ADR-0065 (Org→Team→System→Component hierarchy — *unchanged*) · ADR-0095/0107 (list surface) · ADR-0090 (tenant scope) · ADR-0108 (either-team edge authz) · ADR-0103 (team-exists 422 pre-check)

---

## 1. Goal

Add `System` as a first-class catalog entity that groups components, and let components be assigned to a System via a `PartOf` relationship edge. Backend-first: register/get/list endpoints + edge assignment, no UI.

A `System` is a **grouping node stewarded by one team**. "Steward" ≠ owner of members: the steward team can rename/delete the System and curate membership, while each member Application/Service keeps its own independent team ownership. This supports large cross-team systems (e.g. a bank's transactional platform stewarded by an architecture team, with member services owned by many teams) without a teamless-ownership model.

## 2. Locked decisions

| Decision | Choice | Rationale |
|---|---|---|
| Membership model | `PartOf` edges via existing `POST /relationships` | ADR-0111 §7 / ADR-0068; reuses all edge infra, no new endpoint |
| PartOf sources | `{Application, Service} → System` | ADR-0065 components = apps/services; APIs attach to their provider, System API-surface derives (deferred) |
| System fields | `DisplayName`, `Description`, `TeamId` (steward) | Leanest grouping node; extra fields via field-addition trigger later |
| Ownership | **Required** `TeamId` (steward team) | Uniform `ITeamScopedResource` authz; cross-team membership handled by steward/member split + either-team edge authz |
| DisplayName uniqueness | non-unique `(TenantId, DisplayName)` index | consistent with `catalog_apis` |

## 3. Non-goals / out of scope

Derived API-surface (union of members' exposed APIs — ADR-0111 §7 follow-up) · hierarchy browse (E-03.F-03.S-02) · all UI (list screen + detail, follow-up like Api FU-9) · `memberCount` column (derived aggregate, deferred — flagged, not silent) · lifecycle/status · nested Systems (`System → System`) · teamless/org-owned Systems (additive later: widen column + fallback authz). `Contains` (inverse of `PartOf`) deliberately not reintroduced — redundant with `PartOf` queried by target.

## 4. Components / changes

Replicates the `Api` template across every layer (Api is the cleanest ADR-0111-era template). No new cross-cutting infrastructure.

### 4.1 Domain (`Kartova.Catalog.Domain`)
> **Naming amendment (2026-07-21, post Arm-A chunk-1):** the C# aggregate is **`CatalogSystem`** (id **`CatalogSystemId`**), not `System` — a bare `System` type shadows the BCL `System` namespace within this namespace. The *concept* stays "System" everywhere user-facing (`EntityKind.System`, `/systems`, `SystemResponse`, `catalog_systems`, `catalog.systems.register`).
- **New** `CatalogSystem.cs` — `sealed class CatalogSystem : ITenantOwned, ITeamScopedResource`; shadow `_id` + `CatalogSystemId Id`; private EF ctor + private all-args ctor; `CatalogSystem.Create(string displayName, string? description, Guid createdByUserId, Guid teamId, TenantId tenantId, TimeProvider clock)` factory + explicit-`createdAt` overload (seeds/tests); `Xmin` (`xid`) concurrency token. Fields: `DisplayName` (≤128, required), `Description` (≤4096, optional), `TeamId` (required), `CreatedByUserId`, `CreatedAt`, `TenantId`. Invariants in private `Validate*` methods.
- **New** `CatalogSystemId.cs` — `readonly record struct CatalogSystemId(Guid Value)` + `New()`.
- **Edit** `EntityKind.cs` — append `System`.
- **Edit** `RelationshipType.cs` — re-add `PartOf` (enum persists as string → no migration).
- **Edit** `RelationshipTypeRules.cs` — `PartOf` in `IsCreatable`; new `IsAllowedPair` arm `PartOf ⇒ source ∈ {Application, Service} && target == System`.

### 4.2 Application (`Kartova.Catalog.Application`)
- **New** `RegisterSystemCommand`, `ListSystemsQuery` (`SortBy/SortOrder/Cursor/Limit` + `TeamId[]` + `DisplayNameContains`), `GetSystemByIdQuery`, `SystemResponseExtensions.ToResponse()`.
- **Edit** `CatalogAuditActions.cs` — `SystemRegistered = "system.registered"`; `CatalogAuditTargetTypes.System = "System"`.

### 4.3 Contracts (`Kartova.Catalog.Contracts`)
- **New** `RegisterSystemRequest`, `SystemResponse`, `SystemSortField { DisplayName, CreatedAt }`. All DTOs `[ExcludeFromCodeCoverage]`.

### 4.4 Infrastructure (`Kartova.Catalog.Infrastructure`)
- **New** `RegisterSystemHandler`, `ListSystemsHandler`, `GetSystemByIdHandler` (direct-dispatch, ADR-0093); `SystemSortSpecs` (allowlist `{ displayName, createdAt }`, `Resolve` throws `InvalidSortFieldException`); `EfSystemConfiguration` (`catalog_systems`).
- **Edit** `CatalogEntityLookup.cs` — `EntityKind.System` arm (node resolution for edge 422 / `DisplayName` enrichment / `TeamId` either-team authz).
- **Edit** `CatalogDbContext.cs` — `DbSet<System> Systems` + `ApplyConfiguration(new EfSystemConfiguration())`.
- **Edit** `CatalogModule.cs` — map `POST /systems` (`CatalogSystemsRegister`), `GET /systems/{id:guid}` (`CatalogRead`), `GET /systems` (`CatalogRead`); `AddScoped` the three handlers.
- **Edit** `CatalogEndpointDelegates.cs` — `RegisterSystemAsync` / `GetSystemByIdAsync` / `ListSystemsAsync` (mirror the Api delegates + `CursorListBinding.Bind<SystemSortField>`).

### 4.5 Persistence
- **New** migration `AddSystems` — create `catalog_systems`, indexes `(TenantId)`, `(TenantId, DisplayName)`, `(TeamId)`; RLS `ENABLE` + `FORCE` + `CREATE POLICY tenant_isolation ... current_setting('app.current_tenant_id')::uuid` (copied verbatim from `AddApis`). `Down` reverses. Regenerate `CatalogDbContextModelSnapshot`. Registered via `AddModuleDbContext<CatalogDbContext>` (ADR-0090) — never raw `AddDbContext` for the app path.

### 4.6 Permission (5-sync + arch guard)
`catalog.systems.register`: (1) `KartovaPermissions.cs` const + `All`; (2) `KartovaRolePermissions.cs` → Member + OrgAdmin (not Viewer); (3) `permissions.snapshot.json`; (4) `permissions.ts`; (5) `usePermissions.test.tsx`. `KartovaPermissionsRules` arch test enforces C#↔snapshot sync.

## 5. List surface (ADR-0107 / ADR-0095)

| Field | Column? | Sortable? | Filter |
|---|---|---|---|
| `displayName` | ✅ | ✅ (**default asc**) | `displayNameContains` (typeahead substring) |
| `team` (teamId → resolved name) | ✅ | ❌ | `teamId[]` (multi-select) |
| `createdBy` (enriched via `IUserDirectory`) | ✅ | ❌ (enriched FK — sortable on no catalog entity) | none |
| `createdAt` | ✅ | ✅ | none (date-range deferred) |
| `description` | ❌ (detail only) | ❌ | none |

- **Sort allowlist:** `{ displayName, createdAt }`, default `displayName asc` — matches Service/Application.
- **Filter proposal:** implement now — `displayNameContains`, `teamId[]` (mirror `ListApis`/`ListServices`). Deferred — date-range on `createdAt`; `memberCount` (needs edge-count aggregate). Record row in `docs/design/list-filter-registry.md`.

## 6. Data flow

- **Register** `POST /systems`: tenant scope opens → auth `catalog.systems.register` → team-exists 422 pre-check (`IOrganizationTeamExistenceChecker`, RLS-scoped) → `AuthorizeTargetTeamAsync` 403 → `System.Create` → `Systems.Add` → `SaveChangesAsync` → in-txn `IAuditWriter.AppendAsync(system.registered)` fail-closed → `201` + `Location /systems/{id}`.
- **Assign** `POST /relationships` `{type: PartOf, sourceKind, sourceId, targetKind: System, targetId}`: `lookup.Find` both → 422 if missing → `IsAllowedPair` → 422 if not `{App,Service}→System` → `AuthorizeEitherTeamAsync` (source-component team **or** System steward team) → 403 → duplicate → 409 → `201`.
- **List** `GET /systems`: `CursorListBinding.Bind` → parse `teamId[]`/`displayNameContains` (repeated tokens, `HashSet` de-dup, unknown/numeric → 400) → filter → `ToCursorPagedAsync` keyset → enrich `createdBy` via `IUserDirectory.GetManyAsync` → `CursorPage<SystemResponse>`.
- **Get** `GET /systems/{id}`: 200 / 404.

## 7. Error handling

| Case | Code |
|---|---|
| Validation (name/description length, empty) | 400 |
| Missing permission | 403 |
| Steward team missing / cross-tenant | 422 |
| Actor not on steward team (register) | 403 |
| Bad sort field / filter token | 400 |
| Edge endpoint missing | 422 |
| Edge pair not allowed (`Api→System`, `System→System`) | 400 |
| Edge neither team | 403 |
| Edge duplicate | 409 |
| Get by id not found | 404 |
| Concurrency (`Xmin`) | 409 |

All paths already exist in `CatalogEndpointDelegates` (Api + edge flows) — reused verbatim.

## 8. Testing strategy (per docs/TESTING-STRATEGY.md)

Wiring slice (HTTP/auth/DB/edge) → gate-5 real-seam artifacts named as deliverables. Container-build gate 4 = regression-only (no Dockerfile/`COPY` change). Mutation gate 6 **blocking** (Domain/Application logic: `System.Create` validation, `RelationshipTypeRules` arm, `ListSystemsHandler` filters).

**Unit** (`Kartova.Catalog.Tests`): `SystemTests` (invariants + factory overloads) · `ListSystemsHandlerFilterTests` + sort tests (allowlist, `InvalidSortFieldException`) · `EfSystemConfigurationTests` (mapping, `Xmin`).

**Integration — real seam** (`Kartova.Catalog.IntegrationTests`; Testcontainers Postgres/RLS + real JWT via `KartovaApiFixture`; ≥1 happy + ≥1 negative each):
- `RegisterSystemTests` — 201 + `Location` + audit row; 400 validation; 403 non-steward; 422 missing/cross-tenant team.
- `ListSystemsPaginationTests` — keyset paging, default `displayName asc`, `teamId`/`displayNameContains` filters, bad sort/filter → 400, RLS tenant isolation.
- `GetSystemSurfaceTests` — 200 / 404.
- `CreatePartOfRelationshipTests` — `{App,Service}→System` 201; `Api→System` 400; `System→System` 400; missing endpoint 422; neither-team 403; duplicate 409.
- `CatalogPermissionMatrixTests` — extend: `catalog.systems.register` Member/OrgAdmin allow, Viewer deny.
- `AuditWiringTests` — `system.registered` row shape.
- **Fixture:** add `SeedSystemAsync` (bypass-RLS) for list/get/edge seeding.

**Arch:** permission 5-sync (`KartovaPermissionsRules`) + contracts coverage (`ContractsCoverageRules`) — auto-guarded.

## 9. DoD

Governed by CLAUDE.md's eleven gates (ten always-blocking + conditional mutation gate 6, blocking here). Ledger at `docs/superpowers/verification/2026-07-21-catalog-system-grouping/dod.md` (+ `gate-findings.yaml`). E2E-impact trigger: no existing `e2e/` spec traverses a System flow (new entity, no UI) → N/A. Field-addition trigger: new entity, no pre-existing list screen → N/A.

## 10. A/B experiment note (baseline only)

This spec + the forthcoming plan are the **frozen shared input** to a 2×2 comparison (dev-hm agents vs default flow, implementer × reviewer) run at the **execution** stage in two isolated worktrees off the same commit. The design and plan are single-authored on the main thread and are **not** part of the comparison. The slice DoD ledger gains a comparison section; `gate-findings.yaml` is extended with `produced_by` (A/B) + `found_by` (dev-hm / gates) tags, findings blind-adjudicated `real|delusion`. See memory `project-dev-hm-agent-trial`.
