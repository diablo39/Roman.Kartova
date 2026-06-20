# Slice — Catalog: Register Service entity

**Date:** 2026-06-20
**Stories:** E-02.F-02.S-01 (register service with endpoints and protocol) + E-02.F-01.S-05 carry-over (required minimum fields enforced as a domain invariant, this time for the second entity type)
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-service-entity`

---

## 1. Goal

Land the **second** catalog entity type — `Service` — end-to-end as a sibling aggregate to `Application` in the existing Catalog module. A developer can register a service with zero-or-more endpoints (each carrying its own protocol), read one back by id, and list them with the standard cursor-paginated contract. The slice proves the Application patterns generalize to a second entity without new architecture: tenant-scope hybrid filter (ADR-0090), required-owning-team (ADR-0103), direct-dispatch handlers (ADR-0093), cursor pagination (ADR-0095), fail-closed audit (E-01.F-03.S-03).

The novel surface vs Application is exactly one thing: an **owned collection of endpoints persisted as `jsonb`**. Everything else is a faithful copy of the proven Application slice.

This is **not** a complete E-02.F-02 feature: no detail page with real-time health + consumers (S-02), no edit, no lifecycle transitions, no endpoint mutation after create, no dependency links (E-04), no UI.

---

## 2. Pre-requisites (already on master)

- Catalog module live with the full `Application` slice: `CatalogModule : IModule, IModuleEndpoints`, `CatalogDbContext`, `EnlistInTenantScopeInterceptor`, direct-dispatch handler convention (ADR-0093), `IfMatchEndpointFilter`, `CursorPage<T>` + cursor-list query-parameter transformer (ADR-0095).
- `KartovaApiFixtureBase` (real Postgres Testcontainer + role/grants seed + real `JwtBearer`/`TestJwtSigner`) — Service integration tests inherit it directly.
- Audit write path: `IAuditWriter.AppendAsync` (sync, in-transaction, fail-closed) + `CatalogAuditActions` / `CatalogAuditTargetTypes`.
- RBAC: `KartovaPermissions` + `KartovaRolePermissions` role→permission map; `CatalogPermissionMatrixTests` enumerates every catalog route.
- Team-membership gate + `ITeamExistsInTenant`-style validation used by `RegisterApplication` (422 invalid-team, 403 non-member).

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | New `Service` aggregate in the **Catalog** module, sibling to `Application` (not nested under it). | Application/Service are sibling entity types in the model; linking is E-04's job. Reuses module wiring; no new csproj. |
| 2 | Slice scope = POST + GET-by-id + GET-list only. No edit, lifecycle, endpoint mutation, or UI. | Walking-slice discipline; mirrors how Application S-01 shipped. S-02 (detail/health/consumers) is a separate story. |
| 3 | **Owning team required** (`TeamId`, ADR-0103). Same membership gate + 422 invalid-team as register-application. | No ownerless entities — consistency with Application. |
| 4 | **No `Lifecycle` field this slice.** Service has no lifecycle column at all (not even a fixed `Active`). | YAGNI — avoids a dead enum column. A future lifecycle story adds it via migration, as Application's did (S-04). |
| 5 | **Endpoints = `0..50`** `ServiceEndpoint` value objects; empty is allowed, **>50 rejected** by a `Service.Create` invariant. | Per user decision: a service may be registered before its endpoints are known (0 allowed). The 50 cap bounds the jsonb document size. Each endpoint, when present, is fully validated. |
| 6 | `ServiceEndpoint` = `{ Url, Protocol }`. `Url` required, absolute URI, ≤2048. `Protocol` is a required enum. | Minimal faithful model of "endpoints and protocol". No per-endpoint label/description (YAGNI). |
| 7 | `Protocol` enum: `Rest, Grpc, GraphQL, WebSocket, Tcp, Other`. | Covers the API styles named in E-02.F-03 plus generic transports; `Other` is the escape hatch so the enum need not churn. |
| 8 | `HealthStatus` enum: `Unknown, Healthy, Degraded, Unhealthy`. Field **defaults to `Unknown`**; **no write path** this slice. | AC: "health status defaults to unknown". Health is fed by the agent/probes later (E-15); storing the default now keeps the schema honest. |
| 9 | Endpoints persisted as a **`jsonb` column** via EF owned-collection `.ToJson()`. No child table. | Endpoints live inside the aggregate boundary; jsonb keeps writes atomic, needs no second RLS table, and avoids a join on read. |
| 10 | Required-field enforcement is a **domain invariant** on `Service.Create` (mirrors `Application.Create`), not a separate validation pipeline. | Single source of truth; E-02.F-01.S-05 pattern. |
| 11 | Tenant id + created-by user id always come from `ITenantContext` / `ICurrentUser`, never the payload (ADR-0090). Team id comes from the payload, validated to exist in the tenant. | Same single-source rule pinned for Application; a cross-tenant write probe re-pins it for Service. |
| 12 | New permission `catalog.services.register`; reads reuse the shared `catalog.read`. | Mirrors `catalog.applications.register` + `catalog.read`. Register mapped to Member + OrgAdmin in the role map. |
| 13 | Audit action `service.registered`, target type `Service`, appended in-transaction by the register handler. | Same fail-closed pattern as `application.registered`. |
| 14 | `_id` backing-field + computed `ServiceId`, `xmin` concurrency token, `(tenant_id, display_name)` keyset index — identical EF mechanics to Application. | Reuse the proven pattern that keeps EF LINQ translatable and pagination keyset-friendly. |

---

## 4. Architecture

### 4.1 Endpoint topology added by this slice

```
POST  /api/v1/catalog/services                    (tenant-scoped, NEW)
GET   /api/v1/catalog/services/{id:guid}          (tenant-scoped, NEW)
GET   /api/v1/catalog/services                    (tenant-scoped, NEW; CursorPage<ServiceResponse>)
```

### 4.2 Register happy-path flow (mirrors RegisterApplication)

```
Client → JWT auth → tenant-claims transform
      → TenantScopeBeginMiddleware (BEGIN TX, SET LOCAL app.current_tenant_id)
      → endpoint binding (POST /services)
      → RegisterServiceDelegate
           ├ claim gate: catalog.services.register
           ├ validate cmd.TeamId exists in tenant      → 422 invalid-team
           ├ membership gate: OrgAdmin OR member of TeamId → 403
           └ RegisterServiceHandler.Handle(...)
                ├ TenantId  ← ITenantContext
                ├ CreatedByUserId ← ICurrentUser
                ├ Service.Create(...)  ← invariants (name/desc/team/endpoints)
                ├ db.Services.Add(); SaveChangesAsync()  ← interceptor enlists
                └ audit.AppendAsync(service.registered)  ← in-txn, fail-closed
      → Results.Created(201, ServiceResponse)
      → TenantScopeCommitEndpointFilter (COMMIT TX)
```

### 4.3 File map

**Created:**

| File | Purpose |
|---|---|
| `Kartova.Catalog.Domain/ServiceId.cs` | `readonly record struct ServiceId(Guid Value)` + `New()`. Mirrors `ApplicationId`. |
| `Kartova.Catalog.Domain/Service.cs` | Sealed aggregate; `Create(...)` factory + invariants; `TimeProvider` + explicit-`createdAt` overload. Implements `ITenantOwned, ITeamScopedResource`. |
| `Kartova.Catalog.Domain/ServiceEndpoint.cs` | Value object `{ Url, Protocol }` + validation in a `Create`/ctor. |
| `Kartova.Catalog.Domain/Protocol.cs` | Enum `Rest, Grpc, GraphQL, WebSocket, Tcp, Other`. |
| `Kartova.Catalog.Domain/HealthStatus.cs` | Enum `Unknown, Healthy, Degraded, Unhealthy`. |
| `Kartova.Catalog.Application/RegisterServiceCommand.cs` | `record (string DisplayName, string Description, Guid TeamId, IReadOnlyList<ServiceEndpointInput> Endpoints)`. |
| `Kartova.Catalog.Application/ServiceResponseExtensions.cs` | `ToResponse()` (write path, no enrichment) + enriched overload (`CreatedBy`). Mirrors `ApplicationResponseExtensions`. |
| `Kartova.Catalog.Application/ListServicesQuery.cs` | Cursor query record. |
| `Kartova.Catalog.Application/GetServiceByIdQuery.cs` | Read-by-id record. |
| `Kartova.Catalog.Contracts/RegisterServiceRequest.cs` | DTO `{ DisplayName, Description, TeamId, Endpoints[] }`. `[ExcludeFromCodeCoverage]`. |
| `Kartova.Catalog.Contracts/ServiceEndpointDto.cs` | DTO `{ Url, Protocol }`. `[ExcludeFromCodeCoverage]`. |
| `Kartova.Catalog.Contracts/ServiceResponse.cs` | DTO; `[ExcludeFromCodeCoverage]`. |
| `Kartova.Catalog.Contracts/ServiceSortField.cs` | Enum `DisplayName, CreatedAt`. |
| `Kartova.Catalog.Infrastructure/EfServiceConfiguration.cs` | EF config: `_id` field PK, jsonb endpoints via `.OwnsMany(...).ToJson()`, health smallint default Unknown, indexes. |
| `Kartova.Catalog.Infrastructure/RegisterServiceHandler.cs` | Direct-dispatch handler (lives in Infra — depends on `CatalogDbContext`). |
| `Kartova.Catalog.Infrastructure/GetServiceByIdHandler.cs` | Read-by-id + optional `IUserDirectory` enrichment. |
| `Kartova.Catalog.Infrastructure/ListServicesHandler.cs` | Keyset pagination; mirrors `ListApplicationsHandler`. |
| `Kartova.Catalog.Infrastructure/ServiceSortSpecs.cs` | Sort-field → column map for the cursor codec. |
| `Kartova.Catalog.Infrastructure/Migrations/<ts>_AddServices.cs` | `catalog_services` table + RLS (ENABLE+FORCE+policy) + indexes. |
| `Kartova.Catalog.Tests/ServiceTests.cs` | Aggregate + value-object unit tests. |
| `Kartova.Catalog.IntegrationTests/RegisterServiceTests.cs` | Register happy + negatives (real seam). |
| `Kartova.Catalog.IntegrationTests/ListServicesPaginationTests.cs` | Cursor pagination + tenant isolation. |

**Modified:**

| File | Change |
|---|---|
| `Kartova.Catalog.Infrastructure/CatalogDbContext.cs` | Add `DbSet<Service> Services`; apply `EfServiceConfiguration`. |
| `Kartova.Catalog.Infrastructure/CatalogModule.cs` | Map the 3 endpoints; register the 3 handlers (`AddScoped`). |
| `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | Add `RegisterServiceAsync`, `GetServiceByIdAsync`, `ListServicesAsync`. |
| `Kartova.Catalog.Application/CatalogAuditActions.cs` | Add `ServiceRegistered = "service.registered"`; `CatalogAuditTargetTypes.Service = "Service"`. |
| `Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` | Add `CatalogServicesRegister`; include in `All`. |
| `Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` | Map `CatalogServicesRegister` to Member + OrgAdmin. |
| `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs` | Assert the new permission's role mapping. |
| `Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs` | Add rows for the 3 new routes. |

---

## 5. Components

### 5.1 `Service` aggregate (illustrative)

```csharp
public sealed class Service : ITenantOwned, ITeamScopedResource
{
    private Guid _id;
    private readonly List<ServiceEndpoint> _endpoints = new();

    public ServiceId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = "";
    public string Description { get; private set; } = "";
    public Guid TeamId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public HealthStatus Health { get; private set; } = HealthStatus.Unknown;
    public IReadOnlyList<ServiceEndpoint> Endpoints => _endpoints;
    public uint Version { get; private set; }

    Guid? ITeamScopedResource.TeamId => TeamId;

    private Service() { }   // EF

    public static Service Create(
        string displayName, string description, Guid createdByUserId, Guid teamId,
        IEnumerable<ServiceEndpoint> endpoints, TenantId tenantId, TimeProvider clock)
        => Create(displayName, description, createdByUserId, teamId, endpoints, tenantId, clock.GetUtcNow());

    public static Service Create(
        string displayName, string description, Guid createdByUserId, Guid teamId,
        IEnumerable<ServiceEndpoint> endpoints, TenantId tenantId, DateTimeOffset createdAt)
    {
        ValidateDisplayName(displayName);     // non-empty, ≤128
        ValidateDescription(description);     // non-empty, ≤4096
        if (createdByUserId == Guid.Empty) throw new ArgumentException(..., nameof(createdByUserId));
        if (teamId == Guid.Empty)          throw new ArgumentException(..., nameof(teamId));
        var list = endpoints?.ToList() ?? new();  // 0 allowed; each VO self-validated on construction
        if (list.Count > 50) throw new ArgumentException("a service may have at most 50 endpoints", nameof(endpoints));
        var s = new Service { _id = ServiceId.New().Value, TenantId = tenantId,
            DisplayName = displayName, Description = description, CreatedByUserId = createdByUserId,
            TeamId = teamId, CreatedAt = createdAt };
        s._endpoints.AddRange(list);
        return s;
    }
}
```

`Health` has no mutator this slice — it is read-only at `Unknown` until the health-update story lands.

### 5.2 `ServiceEndpoint` value object

```csharp
public sealed record ServiceEndpoint
{
    public string Url { get; }
    public Protocol Protocol { get; }

    public ServiceEndpoint(string url, Protocol protocol)
    {
        if (string.IsNullOrWhiteSpace(url)) throw new ArgumentException("endpoint url required", nameof(url));
        if (url.Length > 2048) throw new ArgumentException("endpoint url ≤ 2048 chars", nameof(url));
        if (!Uri.TryCreate(url, UriKind.Absolute, out _)) throw new ArgumentException("endpoint url must be absolute", nameof(url));
        if (!Enum.IsDefined(protocol)) throw new ArgumentException("unknown protocol", nameof(protocol));
        Url = url; Protocol = protocol;
    }
}
```

### 5.3 EF persistence (jsonb endpoints)

`catalog_services` columns mirror `catalog_applications` (`id`, `tenant_id`, `display_name`, `description`, `team_id`, `created_by_user_id`, `created_at`, `health` smallint default 0, `xmin`). Endpoints map as an owned collection serialized to a single `jsonb` column:

```csharp
b.OwnsMany(x => x.Endpoints, nav =>
{
    nav.ToJson("endpoints");
    nav.Property(e => e.Url);
    nav.Property(e => e.Protocol).HasConversion<short>();
});
```

Migration adds RLS (`ENABLE` + `FORCE ROW LEVEL SECURITY` + `tenant_isolation` policy on the strict `current_setting('app.current_tenant_id')::uuid` form), matching the `AddApplications` catalog convention so the migrator stays the sole schema owner. (Note: catalog tables use FORCE-RLS, not the `REVOKE` form — that is audit-only.) Indexes: `ix_catalog_services_tenant_id`, `ix_catalog_services_tenant_id_display_name` (keyset), `idx_catalog_services_team`.

> **Owned-collection-to-json caveat (verify in TDD):** querying/paginating the parent does not require the jsonb; keyset pagination orders by `(display_name, id)` on the parent row only. Confirm `.OwnsMany(...).ToJson()` round-trips an **empty** list as `[]` (not null) so reads of endpoint-less services don't NRE.

### 5.4 Contracts

```csharp
public sealed record RegisterServiceRequest(
    string DisplayName, string Description, Guid TeamId, IReadOnlyList<ServiceEndpointDto> Endpoints);
public sealed record ServiceEndpointDto(string Url, Protocol Protocol);
public sealed record ServiceResponse(
    Guid Id, Guid TenantId, string DisplayName, string Description,
    Guid TeamId, Guid CreatedByUserId, DateTimeOffset CreatedAt,
    HealthStatus Health, IReadOnlyList<ServiceEndpointDto> Endpoints, string Version)
{ public UserDisplayInfo? CreatedBy { get; init; } }
```

### 5.5 Register handler (mirrors `RegisterApplicationHandler`)

Same shape: resolves `CatalogDbContext`, `ITenantContext`, `ICurrentUser`, `IAuditWriter` from the request scope; maps `cmd.Endpoints` DTOs → `ServiceEndpoint` VOs (constructor validation surfaces as 400 via the existing `DomainValidationExceptionHandler`); `Service.Create(...)`; `SaveChangesAsync`; `audit.AppendAsync(service.registered, target=Service, data={ displayName, teamId, endpointCount })`; returns `ToResponse()`.

---

## 6. Error handling

No new ProblemDetails types — inherits the catalog mapping:

| Trigger | Status | type |
|---|---|---|
| Empty/over-length name or description; bad endpoint url/protocol; empty team id | 400 | `…/validation-failed` (via `DomainValidationExceptionHandler`, mapping `ArgumentException`) |
| Malformed JSON / missing required field | 400 | `…/malformed-request` |
| Valid JWT lacking `catalog.services.register` | 403 | (authz) |
| Caller not OrgAdmin and not a member of `TeamId` | 403 | membership gate |
| `TeamId` does not resolve to a team in the tenant | 422 | `…/invalid-team` |
| GET by id not found in current tenant | 404 | `…/resource-not-found` |
| Bad `sortBy`/`sortOrder` on list | 400 | `InvalidSortFieldException` → `PagingExceptionHandler` (matches `ListApplications`) |
| Bad `limit` on list | 422 | `InvalidLimitException` (ADR-0095 cursor-list envelope) |

---

## 7. Testing strategy (gate-5 artifacts)

Per [docs/TESTING-STRATEGY.md](../../TESTING-STRATEGY.md). This slice wires HTTP + auth + DB, so the **real seam** is mandatory: real `JwtBearer` validation + real Postgres/RLS via `KartovaApiFixtureBase`; ≥1 happy + ≥1 negative per endpoint. No Dockerfile/`COPY` change in this slice, so the existing `images` CI job (gate 4) covers container build — the new EF migration is picked up by the migrator automatically and exercised by the integration suite's migrate-on-startup.

### 7.1 Domain unit (`ServiceTests.cs`)
- `Create` valid → fields set, `Health == Unknown`, `Id` fresh each call.
- Rejects: empty/whitespace name, name > 128, empty/whitespace description, description > 4096, empty `createdByUserId`, empty `teamId`.
- **Endpoints: 0 allowed** (empty list → valid service, `Endpoints` empty not null); **51 endpoints rejected** (cap = 50).
- Multiple endpoints round-trip in order.
- `ServiceEndpoint` rejects: empty url, url > 2048, relative/non-absolute url, undefined protocol.

### 7.2 Integration (`RegisterServiceTests.cs`, real seam)
- happy: 201 + body echoes endpoints + `Health=Unknown`; row persisted with `tenant_id` from scope and `created_by_user_id` from JWT `sub`.
- happy: register with **zero endpoints** → 201, jsonb `[]` round-trips on GET.
- negative: 400 empty name; 400 bad endpoint url; 403 non-member non-OrgAdmin; 422 unknown team; 401 no token.
- GET-by-id: 200 same tenant; 404 nonexistent; 404 other tenant's id (RLS).
- cross-tenant write probe: payload cannot override scope tenant id.

### 7.3 List + pagination (`ListServicesPaginationTests.cs`, real seam)
- `CursorPage<ServiceResponse>` envelope; forward/backward cursor; `sortBy`/`sortOrder` honored; `limit` bound; tenant-isolated result set.

### 7.4 Permission matrix + role map
- 3 new rows in `CatalogPermissionMatrixTests` (POST→`catalog.services.register`, GET×2→`catalog.read`).
- `KartovaRolePermissionsTests`: Member + OrgAdmin have `CatalogServicesRegister`; Viewer does not.

---

## 8. Definition of Done

The eight always-blocking gates + conditional mutation gate as defined in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; this slice does not restate them. Mutation gate (6) **is blocking** here — the diff touches Domain (`Service`, `ServiceEndpoint`) and Application/Infrastructure handler logic. Run `scripts/ci-local.sh` (Release mirror) green before push.

---

## 9. Out of scope (explicit deferrals)

- Detail page with real-time health + consumer list + dependency-graph snippet → **E-02.F-02.S-02**.
- Health **write path** / probe ingestion → E-15 (agent) / E-16 (monitoring).
- Edit service metadata, endpoint add/remove after create, lifecycle transitions → later E-02.F-02 stories.
- Service ↔ Application / API relationships → E-04.
- Tags, search indexing (Elasticsearch) → E-03.F-04 / E-05.
- React UI / web flows.
- Per-endpoint health, auth metadata, or spec URLs (those belong to API entities, E-02.F-03).

---

## 10. Implementation order (rough — finalised by writing-plans)

1. Enums (`Protocol`, `HealthStatus`) + `ServiceId` + `ServiceEndpoint` VO + unit tests (TDD, RED first).
2. `Service` aggregate + `Create` invariants + unit tests.
3. EF config + `AddServices` migration (jsonb endpoints + RLS ENABLE/FORCE/policy) + DbSet wiring.
4. Contracts (`RegisterServiceRequest`, `ServiceEndpointDto`, `ServiceResponse`, `ServiceSortField`) + `ToResponse` extensions.
5. `RegisterServiceHandler` + audit action constants.
6. `GetServiceByIdHandler` + `ListServicesHandler` + `ServiceSortSpecs`.
7. Permissions + role map + their tests (RED → GREEN).
8. Endpoint delegates + `CatalogModule` wiring.
9. Integration suites (register happy/negatives, get-by-id, list pagination, cross-tenant probe) + permission-matrix rows.
10. `scripts/ci-local.sh`, push, open PR, run DoD gates.

---

## 11. Self-review

**Spec coverage:** every §3 decision traces to §4–§7; every gate-5 artifact in §7 is a named deliverable that writing-plans will turn into one task each.

**Placeholder scan:** no TBD/TODO. §5 code is illustrative; final code lands in executing-plans.

**Type/contract consistency:**
- `Service.Create(displayName, description, createdByUserId, teamId, endpoints, tenantId, clock)` consistent across §5.1, §5.5, §7.1.
- `ServiceEndpoint(url, protocol)` consistent across §5.2, §5.3, §5.4, §7.1.
- `Protocol` / `HealthStatus` enum members consistent across §3, §5, §7.
- `ServiceResponse` shape consistent across §5.4 and §7.2.
- Endpoints cardinality `0..N` consistent across §3 (#5), §5.1, §7.1, §7.2.

**Scope check:** single PR; ~18 new files (most tiny: enums, VOs, DTOs) + ~8 modified. ~450–550 LOC production business code — under the 800 ceiling, no decomposition.

**Ambiguity check:** endpoint-count cap resolved to **50** (decision #5, user-confirmed) — a one-line `Service.Create` invariant + a 51-endpoint rejection test.

**No blocking issues found.**
