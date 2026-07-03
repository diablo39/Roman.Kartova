# Slice — Catalog: Register sync API entity

**Date:** 2026-07-03
**Stories:** E-02.F-03.S-01 (Register sync API — REST/gRPC/GraphQL)
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-api-entity`
**Governing decision:** [ADR-0111](../../architecture/decisions/ADR-0111-api-first-class-entity-provider-instance-fields.md) (API is a first-class entity; provider/instance = FK fields; consumers = edges; exposure derived; amends ADR-0068)

---

## 1. Goal

Land the **third** catalog entity type — `Api` (synchronous) — end-to-end as a sibling aggregate to `Application`/`Service` in the existing Catalog module. A developer can register a sync API (style + version + optional spec URL), read one back by id, and list them cursor-paginated. This proves the entity pattern generalizes a **third** time with **no new architecture** — it is a faithful copy of the proven Service slice, minus the jsonb owned-collection, plus API-specific scalar fields.

This slice creates the API **node only**. Per ADR-0111 the API's structural links (provider FK, instance FK, derived exposure) and the consumer edges are **later, independent layers** — registered as follow-ups in §11. Nothing in this slice depends on them.

Concretely, for the MyShop worked example (ADR-0111 / brainstorm), this slice creates the four sync API nodes — `Products-HTTP`, `Orders-HTTP`, `Payments-HTTP`, `BFF-HTTP` — as standalone, team-owned records. Every edge/derivation is a follow-up.

---

## 2. Pre-requisites (already on master)

- Catalog module live with full `Application` + `Service` slices: `CatalogModule : IModule, IModuleEndpoints`, `CatalogDbContext`, `EnlistInTenantScopeInterceptor`, direct-dispatch handler convention (ADR-0093), `IfMatchEndpointFilter`, `CursorPage<T>` + cursor-list query-parameter transformer (ADR-0095).
- `KartovaApiFixtureBase` (real Postgres Testcontainer + role/grants + real `JwtBearer`/`TestJwtSigner`).
- `IAuditWriter.AppendAsync` (in-transaction, fail-closed) + `CatalogAuditActions` / `CatalogAuditTargetTypes`.
- RBAC: `KartovaPermissions` + `KartovaRolePermissions` + `CatalogPermissionMatrixTests`; web permission snapshot guard.
- Team-membership gate (`ITeamExistsInTenant`-style + membership check) as used by `RegisterService`.

---

## 3. Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Reuses the Catalog module; **no new csproj**. Faithful copy of the Service slice mechanics. | Third entity, same architecture — the point of the slice. |
| 2 | Scope = **POST + GET-by-id + GET-list** only. No edit, no provider/instance links, no UI, no async. | Walking-slice discipline; mirrors how Application/Service S-01 shipped. Links are ADR-0111 follow-ups (§11). |
| 3 | **Owning team required** (`TeamId`, ADR-0103) — same membership gate + 422 invalid-team as register-application/service. | No ownerless entities; consistency with Application/Service. |
| 4 | **API-specific scalar fields:** `Style` (enum), `Version` (string), `SpecUrl` (optional). No provider/instance FK **this slice**. | The node's own attributes (product taxonomy: "spec URL, version, style"). Provider FK = FU-1. |
| 5 | `Style` enum: `Rest, Grpc, GraphQL` — **sync only**, no `Other`. | The three named sync styles are exhaustive; async styles are a separate entity (S-02, FU-7). No dead enum member. |
| 6 | `Version` required, non-empty, ≤64, **freeform** (`v1`, `1.2.0`, …). **One version per API entity.** | Matches product ("version"). Version *history* (many versions per API) = E-21 (deferred). |
| 7 | `SpecUrl` **optional**; when present, absolute URI with host, ≤2048. | Spec may be unknown at registration (like Service's 0-endpoints). "APIs must have a spec" is a **policy** (E-14), not a registration invariant. Spec URL stays strict (it is a real fetchable link) — unlike `ServiceEndpoint` which relaxes (FU-4). |
| 8 | `Description` **required**, non-empty, ≤4096 (parity with Application/Service). | Consistency; catalog hygiene. |
| 9 | **No `Lifecycle`, no `Health`** column. | Health is a Service concept; lifecycle is deferred exactly as Service deferred it. YAGNI — no dead columns. |
| 10 | Concurrency token maps Postgres `xmin` to a `uint Xmin` property — **named `Xmin`, not `Version`** (which Application/Service used). | `Version` is a **domain field** on `Api` (the API version string). Reusing it for the xmin token would collide. Deliberate, documented deviation. |
| 11 | `TenantId` + `CreatedByUserId` + `CreatedAt` come from `ITenantContext` / `ICurrentUser` / `TimeProvider` — **never payload** (ADR-0090). `TeamId` from payload, validated exists-in-tenant. | Single-source rule pinned for Application/Service. `RegisterApiRequest` has no tenant/creator field, so nothing to override — asserted by test (register response `CreatedByUserId == caller sub`) + cross-tenant 404. |
| 12 | New permission `catalog.apis.register` (Member + OrgAdmin); reads reuse `catalog.read`. | Mirrors `catalog.services.register` + `catalog.read`. |
| 13 | Audit action `api.registered`, target type `Api`, appended in-transaction by the register handler (fail-closed). | Same pattern as `service.registered`. |
| 14 | **Sortable on all list columns:** `{ DisplayName (default asc), Style, Version, CreatedAt }`. Filters **deferred** (recorded in the filter registry). | Per user decision (sort every column). Backend-only slice → filter UI defers to the API-UI slice (FU-9); ADR-0095 still mandates `sortBy`/`sortOrder`/`cursor`/`limit` now. |
| 15 | `_id` backing-field + computed `ApiId`, `xmin` concurrency token, `(tenant_id, display_name)` keyset index — identical EF mechanics to Application/Service. | Reuse proven keyset-friendly pagination pattern. |
| 16 | **Aggregate/type name = `Api`** (parity with `Application`/`Service`). Web project `Kartova.Api` is a different namespace. | User-confirmed. Ubiquitous language. |

---

## 4. Architecture

### 4.1 Endpoint topology added by this slice

```
POST /api/v1/catalog/apis              (tenant-scoped, NEW; catalog.apis.register)
GET  /api/v1/catalog/apis/{id:guid}    (tenant-scoped, NEW; catalog.read)
GET  /api/v1/catalog/apis              (tenant-scoped, NEW; catalog.read; CursorPage<ApiResponse>)
```

### 4.2 Register happy-path flow (mirrors RegisterService)

```
Client → JWT auth → tenant-claims transform
  → TenantScopeBeginMiddleware (BEGIN TX, SET LOCAL app.current_tenant_id)
  → endpoint binding (POST /apis)
  → RegisterApiDelegate
      ├ claim gate: catalog.apis.register            → 403
      ├ validate cmd.TeamId exists in tenant          → 422 invalid-team
      ├ membership gate: OrgAdmin OR member of TeamId  → 403
      └ RegisterApiHandler.Handle(...)
          TenantId        ← ITenantContext
          CreatedByUserId ← ICurrentUser
          Api.Create(name/desc/style/version/specUrl/team, tenantId, clock)
          SaveChangesAsync()
          audit.AppendAsync(api.registered)  in-txn, fail-closed
      → Results.Created(201, ApiResponse)
  → TenantScopeCommitEndpointFilter (COMMIT TX)
```

### 4.3 Files created

| File | Purpose |
|------|---------|
| `Kartova.Catalog.Domain/ApiId.cs` | `readonly record struct ApiId(Guid Value)` + `New()`. Mirrors `ServiceId`. |
| `Kartova.Catalog.Domain/Api.cs` | Aggregate; `TimeProvider`-clocked; `ITenantOwned, ITeamScopedResource`. |
| `Kartova.Catalog.Domain/ApiStyle.cs` | Enum `Rest, Grpc, GraphQL`. |
| `Kartova.Catalog.Application/RegisterApiCommand.cs` | `record (string DisplayName, string Description, ApiStyle Style, string Version, string? SpecUrl, Guid TeamId)`. |
| `Kartova.Catalog.Application/ApiResponseExtensions.cs` | `ToResponse()` (write path) + enriched overload (`CreatedBy`). Mirrors `ServiceResponseExtensions`. |
| `Kartova.Catalog.Application/ListApisQuery.cs` | Cursor query record. |
| `Kartova.Catalog.Application/GetApiByIdQuery.cs` | Read-by-id record. |
| `Kartova.Catalog.Contracts/RegisterApiRequest.cs` | DTO `{ DisplayName, Description, Style, Version, SpecUrl?, TeamId }`. `[ExcludeFromCodeCoverage]`. |
| `Kartova.Catalog.Contracts/ApiResponse.cs` | DTO (+ `CreatedBy` enrichment). `[ExcludeFromCodeCoverage]`. |
| `Kartova.Catalog.Contracts/ApiSortField.cs` | Enum `DisplayName, Style, Version, CreatedAt`. |
| `Kartova.Catalog.Infrastructure/EfApiConfiguration.cs` | `_id` PK, scalar columns, `style` smallint, `xmin`→`Xmin`, indexes. |
| `Kartova.Catalog.Infrastructure/RegisterApiHandler.cs` | Direct-dispatch handler (Infra — depends on `CatalogDbContext`). |
| `Kartova.Catalog.Infrastructure/GetApiByIdHandler.cs` | Read-by-id + `IUserDirectory` enrichment. |
| `Kartova.Catalog.Infrastructure/ListApisHandler.cs` | Cursor list. Mirrors `ListServicesHandler`. |
| `Kartova.Catalog.Infrastructure/ApiSortSpecs.cs` | Sort-field → keyset spec map. |
| `Kartova.Catalog.Infrastructure/Migrations/<ts>_AddApis.cs` | Table + RLS (ENABLE + FORCE + `tenant_isolation` policy) + indexes. |
| `Kartova.Catalog.Tests/ApiTests.cs` | Domain unit tests. |
| `Kartova.Catalog.IntegrationTests/RegisterApiTests.cs` | Register real-seam tests. |
| `Kartova.Catalog.IntegrationTests/ListApisPaginationTests.cs` | List/pagination real-seam tests. |

### 4.4 Files modified

| File | Change |
|------|--------|
| `Kartova.Catalog.Infrastructure/CatalogDbContext.cs` | `DbSet<Api>` + apply `EfApiConfiguration`. |
| `Kartova.Catalog.Infrastructure/CatalogModule.cs` | Register 3 handlers (`AddScoped`). |
| `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | `RegisterApiAsync`, `GetApiByIdAsync`, `ListApisAsync` + route mapping. |
| `Kartova.Catalog.Application/CatalogAuditActions.cs` | `ApiRegistered = "api.registered"`; `CatalogAuditTargetTypes.Api = "Api"`. |
| `Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` | `CatalogApisRegister = "catalog.apis.register"` (+ `All` set). |
| `Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` | Map `CatalogApisRegister` to Member + OrgAdmin. |
| `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs` | Assert Member + OrgAdmin have it, Viewer does not. |
| `Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs` | +3 rows (POST→`catalog.apis.register`, GET×2→`catalog.read`). |
| `web/src/shared/auth/permissions.snapshot.json` | Add `catalog.apis.register` (backend snapshot guard). |
| `web/src/shared/auth/permissions.ts` | Add the TS permission constant (Frontend job type-check). |
| `web/src/shared/auth/__tests__/usePermissions.test.tsx` | Add to the OrgAdmin mock set. |

> **Permission-addition touchpoints (5-sync — do not miss the web side).** Adding a `KartovaPermission` requires syncing: (1) C# const + `All`, (2) C# role map, (3) `web/permissions.snapshot.json`, (4) `web/permissions.ts`, (5) `usePermissions` OrgAdmin mock. Backend CI guards only C#↔snapshot; **missing the TS side fails the Frontend job**. Applies even though this slice is backend-only.

---

## 5. Domain model

### 5.1 `Api` aggregate

```csharp
public sealed class Api : ITenantOwned, ITeamScopedResource
{
    private Guid _id;

    public ApiId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = "";
    public string Description { get; private set; } = "";
    public ApiStyle Style { get; private set; }
    public string Version { get; private set; } = "";      // API version string (domain), NOT the xmin token
    public string? SpecUrl { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Xmin { get; private set; }                 // Postgres xmin concurrency token (renamed to avoid Version clash)

    Guid? ITeamScopedResource.TeamId => TeamId;

    private Api() { }   // EF

    public static Api Create(
        string displayName, string description, ApiStyle style, string version, string? specUrl,
        Guid createdByUserId, Guid teamId, TenantId tenantId, TimeProvider clock)
        => Create(displayName, description, style, version, specUrl, createdByUserId, teamId, tenantId, clock.GetUtcNow());

    public static Api Create(
        string displayName, string description, ApiStyle style, string version, string? specUrl,
        Guid createdByUserId, Guid teamId, TenantId tenantId, DateTimeOffset createdAt)
    {
        ValidateDisplayName(displayName);   // non-empty, ≤128
        ValidateDescription(description);   // non-empty, ≤4096
        if (!Enum.IsDefined(style)) throw new ArgumentException("unknown api style.", nameof(style));
        ValidateVersion(version);           // non-empty, ≤64
        ValidateSpecUrl(specUrl);           // null OK; else absolute URI w/ host, ≤2048
        if (createdByUserId == Guid.Empty) throw new ArgumentException(..., nameof(createdByUserId));
        if (teamId == Guid.Empty)          throw new ArgumentException(..., nameof(teamId));

        return new Api {
            _id = ApiId.New().Value, TenantId = tenantId,
            DisplayName = displayName, Description = description, Style = style,
            Version = version, SpecUrl = specUrl,
            CreatedByUserId = createdByUserId, TeamId = teamId, CreatedAt = createdAt };
    }
}
```

`SpecUrl` validation reuses the **strict** absolute-URI-with-host rule (a spec URL is a real fetchable link). This is deliberately *not* the relaxed rule ADR-0111 grants `ServiceEndpoint` (FU-4).

### 5.2 EF persistence

`catalog_apis` columns: `id` (PK, from `_id`), `tenant_id`, `display_name`, `description`, `style` (smallint), `version` (text), `spec_url` (text null), `team_id`, `created_by_user_id`, `created_at`, `xmin` (mapped to `Xmin`). Migration adds RLS (`ENABLE` + `FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy on strict `current_setting('app.current_tenant_id')::uuid`), matching the catalog convention (migrator is sole schema owner). Indexes: `ix_catalog_apis_tenant_id`, `ix_catalog_apis_tenant_id_display_name` (keyset), `idx_catalog_apis_team`.

### 5.3 Contracts

```csharp
public sealed record RegisterApiRequest(
    string DisplayName, string Description, ApiStyle Style, string Version, string? SpecUrl, Guid TeamId);

public sealed record ApiResponse(
    Guid Id, Guid TenantId, string DisplayName, string Description, ApiStyle Style,
    string Version, string? SpecUrl, Guid TeamId, Guid CreatedByUserId,
    DateTimeOffset CreatedAt, UserDisplayInfo? CreatedBy);

public enum ApiSortField { DisplayName, Style, Version, CreatedAt }
```

Enums serialize camelCase over the wire (ADR-0109).

---

## 6. Error semantics (ProblemDetails, reuse existing handlers)

| Case | Status | Type |
|------|--------|------|
| Empty/over-length name/description/version; bad spec-url; undefined style; empty team id | 400 | `…/validation-failed` (via `DomainValidationExceptionHandler` mapping `ArgumentException`) |
| Malformed JSON / missing required field | 400 | `…/malformed-request` |
| Valid JWT lacking `catalog.apis.register` | 403 | authz |
| Caller not OrgAdmin and not member of `TeamId` | 403 | membership gate |
| `TeamId` does not resolve to a team in the tenant | 422 | `…/invalid-team` |
| GET by id not found in current tenant | 404 | `…/resource-not-found` |
| Bad `sortBy`/`sortOrder` on list | 400 | `InvalidSortFieldException` → `PagingExceptionHandler` |
| Bad `limit` on list | 422 | `InvalidLimitException` (ADR-0095 cursor-list envelope) |

---

## 7. Testing strategy (gate-5 artifacts)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). This slice wires HTTP + auth + DB, so the **real seam** is mandatory: real `JwtBearer` validation + real Postgres/RLS via `KartovaApiFixtureBase`; ≥1 happy + ≥1 negative per endpoint. No Dockerfile/`COPY` change → the existing `images` CI job (gate 4) covers container build; the new EF migration is exercised by the integration suite's migrate-on-startup.

**7.1 Domain unit — `ApiTests.cs`**
- `Create` valid → fields set, `Id` fresh each call, `SpecUrl` null allowed.
- Rejects: empty/whitespace name, name >128, empty/whitespace description, description >4096, empty/whitespace version, version >64, undefined `style`, empty `createdByUserId`, empty `teamId`.
- `SpecUrl`: null OK; present-and-relative rejected; present->2048 rejected; present-without-host rejected; valid absolute accepted.

**7.2 Register (real seam) — `RegisterApiTests.cs`**
- 201 + `Location` + JWT with `catalog.apis.register`; response round-trips via GET.
- 400 validation; 400 malformed; 403 missing permission; 403 non-member non-OrgAdmin; 422 invalid-team.
- **Identity-from-context:** response `CreatedByUserId` equals caller JWT `sub` (request has no tenant/creator field to override).
- GET-by-id: 200; 404 unknown; **404 cross-tenant** (RLS).
- Audit row `api.registered` written in the same transaction.

**7.3 List + pagination (real seam) — `ListApisPaginationTests.cs`**
- `CursorPage<ApiResponse>` envelope; forward/backward cursor; **each `sortBy` honored** (`DisplayName`/`Style`/`Version`/`CreatedAt`); `sortOrder`; `limit` bound; tenant-isolated result set (another tenant's APIs never appear).

**7.4 Permission matrix + role map**
- 3 new rows in `CatalogPermissionMatrixTests` (POST→`catalog.apis.register`, GET×2→`catalog.read`).
- `KartovaRolePermissionsTests`: Member + OrgAdmin have `CatalogApisRegister`; Viewer does not.
- Web permission snapshot guard passes (C#↔`permissions.snapshot.json`); Frontend job type-checks `permissions.ts`.

---

## 8. Impact / touchpoints

Almost entirely **new code**. Shared-symbol touches are **additive** (no existing signature/behavior change): a new `KartovaPermissions` const (+`All`), a role-map entry, two new `CatalogAuditActions`/`TargetTypes` constants, three new delegates + `CatalogModule` registrations, one `DbSet`. The implementation plan's **Impact Analysis (codelens/LSP)** section will ground the shared-symbol edits — `KartovaPermissions.All` / role map / `CatalogPermissionMatrixTests` consumers via `find_references` — confirming every consumer is covered, per the CLAUDE.md writing-plans rule.

---

## 9. List surface — registry update (ADR-0107 / ADR-0095)

| Field | Column? | Sortable? | Filter? |
|-------|---------|-----------|---------|
| displayName | yes | ✅ default **asc** | defer → FU-9 (typeahead) |
| style | yes | ✅ | defer → FU-9 (multi-select) |
| version | yes | ✅ | none |
| team | yes (UI slice, FU-9) | defer (needs join) | defer → FU-9 (team multi-select) |
| createdAt | yes | ✅ | none |

Sort allowlist shipped now = `{ displayName, style, version, createdAt }`. A row for the (future) `/catalog/apis` list is added to `docs/design/list-filter-registry.md` under **Planned filtering surfaces** with all facets **deferred → FU-9** (explicit, never silent).

---

## 10. Definition of Done

The eight always-blocking gates + conditional mutation gate defined in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; this slice does not restate them. Mutation gate (6) **is blocking** here — the diff touches Domain (`Api`) + Application/Infrastructure handler logic. Run `scripts/ci-local.sh` (Release mirror) green before push. DoD ledger + `gate-findings.yaml` live under `docs/superpowers/verification/2026-07-03-catalog-api-entity/`.

---

## 11. Follow-ups (registered work items)

Deferred layers of the ADR-0111 model. Each is an independent future slice; cite as `§11.FU-N`.

| ID | Work item | Owning story / ADR |
|----|-----------|--------------------|
| **FU-1** | **Provider FK** — `Api.implementedByApplicationId` (nullable, App-only) + set-path (register/edit) + 422 `invalid-application` + migration; `provides-api-for` derives. | ADR-0111 §Decision 2/4; E-02.F-03 (new) |
| **FU-2** | **Instance FK** — `Service.applicationId` (nullable) on the shipped Service aggregate + set-path + migration; enables derived `exposes`. **Modifies shipped Service.** | ADR-0111 §Decision 2/3 |
| **FU-3** | **Derived exposure** — compute Service→API `exposes` (full-auto) from instance ∘ provider; surface on Service/API detail + graph. | ADR-0111 §Decision 3 |
| **FU-4** | **Endpoint redefinition** — `ServiceEndpoint`: drop `Protocol`, add optional `Description` (≤256), **relax** URL (bare host:port); migration folds/drops old protocol. **Modifies shipped Service.** | ADR-0111 §Decision 6 |
| **FU-5** | **Consumer edges + `Api` kind in E-04** — teach relationships the `Api` entity kind; `consumes-api-from` targets the API node (ADR-0068 amendment realized); service↔service `depends-on` derives. | ADR-0111 §Decision 4/5; E-04.F-01 |
| **FU-6** | **System grouping surface** — `contains`/`part-of`; System API surface = ∪ members' exposed APIs (derived). | E-03.F-03 |
| **FU-7** | **Async API entity** — messaging protocol + channels; `publishes-to`/`subscribes-from`; Broker dependency. | E-02.F-03.S-02; E-02.F-04 |
| **FU-8** | **Unified sync/async API view per service.** | E-02.F-03.S-03 |
| **FU-9** | **API UI surface** — list/detail/register React screens (mirror Service UI); **build the deferred list filters** (style multi-select, team multi-select, name typeahead) via `<FilterBar>`. | new (mirror Service UI slice) |
| **FU-10** | **Per-service exposure opt-out** — for worker/partial deployments that don't expose all of the app's APIs. | ADR-0111 §Decision 3 (deferred until real need) |
| **FU-11** | **Standalone-Service-provided API** — widen provider to polymorphic `{kind,id}` (App or Service). | ADR-0111 §Consequences (widen-later) |

Mirror on save: CHECKLIST.md note under E-02.F-03 (S-01 in progress; FU-1..FU-11 registered here); filter-registry row (FU-9).

---

## 12. Out of scope (explicit deferrals)

- All provider/instance/consumer links and derived exposure/dependency → FU-1..FU-5.
- `ServiceEndpoint` change → FU-4.
- Async APIs, unified view → FU-7, FU-8.
- API UI + list filters → FU-9.
- Version history (many versions per API) → E-21.
- Spec rendering (OpenAPI/proto/GraphQL) → E-11.
- Search indexing → E-05.
- Edit / lifecycle / delete of an API → later E-02.F-03 stories.

---

## 13. Self-review

**Spec coverage:** §3 decisions trace to §4–§9; gate-5 artifacts in §7 are named deliverables writing-plans will turn into one task each; the permission 5-sync (§4.4 note) and mutation-blocking DoD (§10) are called out.

**Placeholder scan:** no TBD/TODO. §5 code is illustrative; final code lands in executing-plans.

**Type/contract consistency:**
- `Api.Create(displayName, description, style, version, specUrl, createdByUserId, teamId, tenantId, clock)` consistent across §5.1, §5.3 (command), §7.1.
- `ApiResponse` / `RegisterApiRequest` fields consistent §5.3 ↔ §4.4 ↔ §7.2.
- `ApiStyle {Rest,Grpc,GraphQL}` and `ApiSortField {DisplayName,Style,Version,CreatedAt}` consistent §3, §5, §9.
- `Version` (domain field) vs `Xmin` (concurrency token) collision resolved and documented (§3 #10, §5.1).

**Scope check:** single PR; ~17 new files (most tiny: enum, id, DTOs, handlers) + ~11 modified. Est. ~450–550 LOC production business code — under the 800 ceiling; no decomposition needed.

**Ambiguity check:** `SpecUrl` optionality + strict-URL (vs relaxed endpoint) resolved (§3 #7, §5.1); sort-all-columns resolved (§3 #14, §9); provider link explicitly out-of-slice (FU-1).

**No blocking issues found.**
