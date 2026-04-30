# Slice 3 — Catalog: Register Application

**Date:** 2026-04-29
**Stories:** E-02.F-01.S-01 (register application) + E-02.F-01.S-05 (required fields enforced as domain invariant)
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/slice-3-catalog-application`

---

## 1. Goal

Land the first vertical slice into the **Catalog** module: register an `Application` aggregate end-to-end through the new tenant-scope hybrid filter (slice-2-followup, ADR-0090). Slice 3 is a walking skeleton — minimum feature surface, maximum architectural confidence — and proves three things slice-2-followup didn't:

1. The new `EnlistInTenantScopeInterceptor` correctly enlists a *real domain write* in a different module's `DbContext`.
2. The Catalog DbContext, scaffolded but unused since slice-2, gets its first migration and its first row.
3. The "module owns its endpoints" pattern can be extracted as a tiny `SharedKernel.AspNetCore` helper without coupling to Catalog specifics — and Organization can adopt it in the same PR without behavior change.

Slice 3 is **not** a complete S-01 feature: no edit, no lifecycle transitions, no detail page projection, no relationships, no UI.

---

## 2. Pre-requisites

This spec assumes one separate PR has merged before slice 3 starts:

- **ADR-0092 — URL convention.** Records the rule "module slug as URL segment after `/api/v1/`, with admin-first prefix for admin-only routes; the segment matches the module's plural primary collection when one exists" (so `/api/v1/organizations/me` is correct as-is). The ADR is a documentation-only PR (no code change). Slice 3 references ADR-0092 throughout.

The following are **already on master** as of this spec:

- ADR-0090 (tenant-scope hybrid begin-middleware + commit-filter) and its addendum.
- `EnlistInTenantScopeInterceptor` shipped (slice-2-followup).
- `INpgsqlTenantScope.Transaction` is public.
- `SharedKernel.AspNetCore` no longer references `SharedKernel.Postgres`.
- `Kartova.Catalog.*` projects scaffolded; `CatalogDbContext` exists; `CatalogModule : IModule` exists; baseline migration `20260421192803_InitialCatalog` exists; no Catalog tables / rows yet.

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | Walking-skeleton scope. POST + GET-by-id + GET-list only. No edit, no lifecycle, no UI. | Slice-2 / slice-2-followup discipline. Each slice ships the smallest end-to-end change. |
| 2 | Route convention: `/api/v1/<module-slug>/<collection>`, admin-first for admin routes. Module slug equals plural primary-collection name when one exists; otherwise singular module name. | Adopts ADR-0092 (separate PR). |
| 3 | Endpoints exposed via a per-module `MapEndpoints` hook on `IModule`. New helpers `MapTenantScopedModule(slug)` and `MapAdminModule(slug)` in `SharedKernel.AspNetCore` enforce the URL + auth shape. | Approach 2 from brainstorming — extract one shared abstraction now; Slice 4+ becomes a few lines per entity. |
| 4 | Authorization on `POST /catalog/applications` is `RequireTenantScope()` only — any authenticated tenant member can register. | RBAC (E-01.F-04.S-03) lands later. Walking-skeleton stays honest about what's enforced today. |
| 5 | `Application.OwnerUserId = JWT 'sub'` claim of the registering user. Stored verbatim. | Acceptance criterion S-01/S-05 demands an owner; Team aggregate doesn't exist yet. Honest schema beats placeholder text or stub Team. When Team aggregate lands, a future migration adds nullable `owner_team_id`; `owner_user_id` stays as the audit-of-record. |
| 6 | Required-field enforcement (S-05) is implemented as a **domain invariant** on the `Application.Create` factory, not as a separate validation pipeline. | Single source of truth; matches Organization aggregate; bundles S-05 into this slice without growing scope. |
| 7 | Lifecycle status is fixed to `active` (no column persisted). Lifecycle transitions (S-04) ship later. | Walking-skeleton; no need for state machine yet. |
| 8 | Tenant-id source: always `ITenantScope.TenantId`, never the request payload. Cross-tenant write probe pins this. | ADR-0090 single-source rule. |
| 9 | `Application` row id is server-generated `Guid.NewGuid()` (handler), not client-supplied. | Same convention as Organization aggregate. |
| 10 | RLS policy applied to `catalog_applications` table (matches Organization pattern). | ADR-0001/0012. |
| 11 | KeyCloak realm seed gains a **second tenant user** (`admin@orgb.kartova.local`) with a distinct tenant claim, to enable cross-tenant integration tests. | Existing seed has only one tenant user; cross-tenant proofs require two. |

---

## 4. Architecture

### 4.1 Endpoint topology after slice 3

```
GET   /api/v1/version                                  (system, anonymous)

GET   /api/v1/organizations/me                         (tenant-scoped, existing)
POST  /api/v1/admin/organizations                      (admin, existing)

POST  /api/v1/catalog/applications                     (tenant-scoped, NEW)
GET   /api/v1/catalog/applications/{id}                (tenant-scoped, NEW)
GET   /api/v1/catalog/applications                     (tenant-scoped, NEW)
```

### 4.2 Request flow (POST happy path)

```
Client → JWT auth → TenantClaimsTransformation
                 → TenantScopeBeginMiddleware (BEGIN TX, SET LOCAL)
                 → endpoint binding (POST)
                 → Wolverine bus → RegisterApplicationHandler
                                    ├ TenantId from ITenantScope
                                    ├ OwnerUserId from ICurrentUser
                                    ├ Application.Create(...)  ← invariants
                                    ├ ctx.Applications.Add()
                                    └ SaveChangesAsync()       ← interceptor enlists
                 → endpoint returns Results.Created(...)
                 → TenantScopeCommitEndpointFilter (COMMIT TX)
                 → IResult.ExecuteAsync (writes 201 + body)
```

### 4.3 File map

**Modified:**

| File | Change |
|---|---|
| `src/Kartova.SharedKernel/IModule.cs` | Add `string Slug { get; }` and `void MapEndpoints(IEndpointRouteBuilder app)`. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs` | Implement `Slug => "organizations"`. Move endpoint wiring from `Program.cs` into `MapEndpoints`. No URL changes. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` | Implement `Slug => "catalog"`. Implement `MapEndpoints`. |
| `src/Kartova.Api/Program.cs` | Replace per-module endpoint calls with uniform `foreach (m in modules) m.MapEndpoints(app);`. `/api/v1/version` stays out (system-level). |
| `tests/Kartova.ArchitectureTests/AssemblyRegistry.cs` | Add Catalog assembly handle if missing. |
| `tests/Kartova.ArchitectureTests/TenantScopeRules.cs` | Add `CatalogModule` to the `IModule[]` enumeration in §6.1 rule. |
| `deploy/keycloak/kartova-realm.json` | Add `admin@orgb.kartova.local` user with distinct tenant claim. |

**Created:**

| File | Purpose |
|---|---|
| `src/Kartova.SharedKernel.AspNetCore/ModuleRouteExtensions.cs` | `MapTenantScopedModule(slug)` + `MapAdminModule(slug)` extension methods. |
| `src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs` + `HttpContextCurrentUser.cs` | `Guid UserId { get; }` accessor over `IHttpContextAccessor`. Reads JWT `sub` claim. |
| `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs` | Sealed aggregate. `Create(name, description, ownerUserId, tenantId)` factory. |
| `src/Modules/Catalog/Kartova.Catalog.Application/RegisterApplicationCommand.cs` | Wolverine command record. |
| `src/Modules/Catalog/Kartova.Catalog.Application/RegisterApplicationHandler.cs` | Wolverine handler. |
| `src/Modules/Catalog/Kartova.Catalog.Application/GetApplicationByIdQuery.cs` + handler | Read by id. |
| `src/Modules/Catalog/Kartova.Catalog.Application/ListApplicationsQuery.cs` + handler | List for current tenant. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs` | EF entity config + RLS policy. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<ts>_AddApplications.cs` | First Catalog migration. |
| `src/Modules/Catalog/Kartova.Catalog.Contracts/RegisterApplicationRequest.cs` | DTO `{ Name, Description }`. `[ExcludeFromCodeCoverage]`. |
| `src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs` | DTO `{ Id, TenantId, Name, Description, OwnerUserId, CreatedAt }`. `[ExcludeFromCodeCoverage]`. |
| `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationTests.cs` | Aggregate unit tests. |
| `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApplicationTests.cs` | End-to-end integration. |
| `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CrossTenantWriteTests.cs` | Tenant-id-from-scope-only proof. |
| `tests/Kartova.ArchitectureTests/IModuleRules.cs` | New arch rules for Slug + MapEndpoints. |

---

## 5. Components

### 5.1 `IModule` interface

```csharp
public interface IModule
{
    string Slug { get; }                                   // NEW
    Type DbContextType { get; }                            // existing
    void RegisterServices(IServiceCollection services, IConfiguration config);  // existing
    void MapEndpoints(IEndpointRouteBuilder app);          // NEW
}
```

`Slug` is the single URL segment after `/api/v1/`. Must be lowercase kebab-case (regex `^[a-z][a-z0-9-]*$`).

### 5.2 `ModuleRouteExtensions`

```csharp
public static class ModuleRouteExtensions
{
    public static RouteGroupBuilder MapTenantScopedModule(this IEndpointRouteBuilder app, string slug)
        => app.MapGroup($"/api/v1/{slug}").RequireTenantScope();

    public static RouteGroupBuilder MapAdminModule(this IEndpointRouteBuilder app, string slug)
        => app.MapGroup($"/api/v1/admin/{slug}").RequireAuthorization("platform-admin");
}
```

`RequireTenantScope()` already chains `RequireAuthorization()` (slice-2-followup).

### 5.3 `ICurrentUser`

```csharp
public interface ICurrentUser
{
    Guid UserId { get; }            // throws if no claim — caller must run inside auth pipeline
}

public sealed class HttpContextCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _http;
    public HttpContextCurrentUser(IHttpContextAccessor http) => _http = http;

    public Guid UserId
    {
        get
        {
            var sub = _http.HttpContext?.User.FindFirstValue("sub")
                      ?? throw new InvalidOperationException("No 'sub' claim on current user");
            return Guid.Parse(sub);
        }
    }
}
```

Registered as `AddScoped<ICurrentUser, HttpContextCurrentUser>()` in `SharedKernel.AspNetCore` extensions.

**Assumption:** KeyCloak's `sub` claim is a `Guid`. Verified by the existing realm seed (Keycloak generates UUIDs for user IDs). If a future identity provider issues non-Guid `sub`, this contract breaks — flagged as a known dependency.

### 5.4 `Application` aggregate

```csharp
public sealed class Application
{
    public Guid Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = "";
    public string Description { get; private set; } = "";
    public Guid OwnerUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Application() { }       // EF

    public static Application Create(string name, string description, Guid ownerUserId, TenantId tenantId)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("name is required", nameof(name));
        if (name.Length > 256)              throw new ArgumentException("name exceeds 256 chars", nameof(name));
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("description is required", nameof(description));
        if (ownerUserId == Guid.Empty)      throw new ArgumentException("ownerUserId required", nameof(ownerUserId));
        // tenantId.Value == Guid.Empty is a programmer error if it occurs; not user-facing.

        return new Application
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name,
            Description = description,
            OwnerUserId = ownerUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
```

`CreatedAt` uses `DateTimeOffset.UtcNow` directly — matches the existing convention in `Organization.cs`. TimeProvider adoption across aggregates is tech debt called out separately; not in this slice's scope.

### 5.5 `RegisterApplicationCommand` + handler

```csharp
public record RegisterApplicationCommand(string Name, string Description);

public sealed class RegisterApplicationHandler
{
    public async Task<ApplicationResponse> Handle(
        RegisterApplicationCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        CancellationToken ct)
    {
        // Handlers depend on the read-only ITenantContext, never the lifecycle ITenantScope —
        // Begin/Commit are owned by the transport adapter (TenantScopeBeginMiddleware +
        // TenantScopeCommitEndpointFilter, ADR-0090).
        var app = Application.Create(cmd.Name, cmd.Description, user.UserId, tenant.TenantId);
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        return ApplicationResponse.From(app);
    }
}
```

Tenant-id and owner-user-id never come from the command payload — handler reads them from `ITenantScope` and `ICurrentUser`. `CrossTenantWriteTests` pin this rule.

### 5.6 EF entity + migration

`catalog_applications` table:

```sql
CREATE TABLE catalog_applications (
    id UUID PRIMARY KEY,
    tenant_id UUID NOT NULL,
    name TEXT NOT NULL,
    description TEXT NOT NULL,
    owner_user_id UUID NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    CONSTRAINT name_length CHECK (length(name) <= 256)
);
CREATE INDEX ix_catalog_applications_tenant_id ON catalog_applications (tenant_id);
ALTER TABLE catalog_applications ENABLE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON catalog_applications
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
```

> **Strict `current_setting` form (no second arg).** Matches the slice-2 Organization migration (`20260423080230_InitialOrganization.cs`). A missing GUC raises an error rather than silently returning every row — the `TenantScopeRequiredInterceptor` already prevents `SaveChanges` outside a scope, so a missing GUC at query time is a programmer error worth surfacing loudly. Earlier drafts of this spec used the lenient `('app.current_tenant_id', true)` form; the strict form is the project convention.

Migration generated via `dotnet ef migrations add AddApplications --project Kartova.Catalog.Infrastructure --startup-project Kartova.Migrator` and committed.

### 5.7 Endpoint wiring (`CatalogModule.MapEndpoints`)

```csharp
public void MapEndpoints(IEndpointRouteBuilder app)
{
    var group = app.MapTenantScopedModule(Slug);            // /api/v1/catalog + RequireTenantScope()

    group.MapPost("/applications", RegisterApplication).WithName("RegisterApplication");
    group.MapGet ("/applications/{id:guid}", GetApplicationById).WithName("GetApplicationById");
    group.MapGet ("/applications", ListApplications).WithName("ListApplications");
}
```

Endpoint delegates `RegisterApplication` etc. live in the same file as private static methods or a separate `CatalogEndpoints` class — match the Organization module's existing convention (decided at implementation time, not a spec-level choice).

---

## 6. Data flow

See Section 3 of the brainstorming transcript — all three flows (happy POST, cross-tenant 404, validation 400) follow the slice-2-followup ordering: begin-middleware → endpoint → handler → factory invariant → SaveChanges (interceptor enlists) → endpoint result → commit-filter → ExecuteAsync.

Key invariants pinned by tests:

- COMMIT runs **before** `IResult.ExecuteAsync` — pinned by the existing streaming-durability test (slice-2-followup).
- TenantId in the persisted row equals the scope's tenant — pinned by `CrossTenantWriteTests`.
- The handler never reads tenant-id from the command payload.

---

## 7. Error handling

No new ProblemDetails types. Inherits the slice-1/2 mapping table:

| Trigger | Status | `type` |
|---|---|---|
| No / invalid JWT | 401 | (default) |
| Valid JWT, no tenant claim | 403 | (default) |
| `BeginAsync` Postgres failure | 503 | `…/tenant-scope-unavailable` |
| Empty / over-length `name`, empty `description`, `ownerUserId == Guid.Empty` | 400 | `…/invalid-request` |
| Malformed JSON body / missing required field | 400 | `…/malformed-request` |
| GET by id, not found in current tenant | 404 | `…/resource-not-found` |
| Commit failure | 503 | `…/tenant-scope-commit-failed` |
| Unhandled exception | 500 | `…/internal-server-error` |

ADR-0091 ("no ad-hoc error response shapes") is satisfied — every endpoint maps via `Results.Problem(...)` only.

---

## 8. Testing

Five layers, mirroring slice-2.

### 8.1 Domain unit tests (`Kartova.Catalog.Tests/ApplicationTests.cs`)

- `Create_with_valid_args_returns_application`
- `Create_throws_on_empty_name`
- `Create_throws_on_whitespace_name`
- `Create_throws_on_name_over_256_chars`
- `Create_throws_on_empty_description`
- `Create_throws_on_whitespace_description`
- `Create_throws_on_empty_owner_user_id`
- `Create_assigns_passed_tenant_and_owner_ids`
- `Create_assigns_fresh_id_each_call`
- `Create_assigns_recent_utc_CreatedAt` (asserts within ±1s of `DateTimeOffset.UtcNow` at test time)

### 8.2 Architecture tests (new `IModuleRules.cs`)

- `Every_IModule_has_non_empty_Slug` — assert via reflection.
- `Every_IModule_overrides_MapEndpoints` — assert method declared on derived type.
- `Slug_is_lowercase_kebab_case` — regex `^[a-z][a-z0-9-]*$`.

Plus `TenantScopeRules.cs` extension to include `CatalogModule` in §6.1.

### 8.3 Integration — `RegisterApplicationTests.cs`

Uses existing `KartovaApiFixture`.

- `POST_with_valid_payload_creates_row`
- `POST_persists_owner_user_id_from_jwt_sub`
- `POST_persists_tenant_id_from_scope`
- `POST_with_empty_name_returns_400`
- `POST_with_missing_field_returns_400`
- `POST_without_token_returns_401`
- `GET_by_id_returns_row_in_same_tenant`
- `GET_by_id_returns_404_for_nonexistent_id`
- `GET_list_returns_only_current_tenant_rows`
- `GET_by_id_for_other_tenants_row_returns_404`

### 8.4 Cross-tenant write probe — `CrossTenantWriteTests.cs`

- `Cannot_write_application_under_different_tenant_id_via_handler` — direct handler invocation; pin that command payload cannot override scope's tenant-id.

### 8.5 KeyCloak realm seed change

Add `admin@orgb.kartova.local` user (password `dev_pass`, distinct tenant claim) so cross-tenant integration tests can authenticate as a second tenant. Document in this slice's PR.

### 8.6 Test counts (target)

| Suite | Before | After |
|---|---|---|
| `Catalog.Tests` | 2 | ~12 |
| `Catalog.IntegrationTests` | 2 | ~14 |
| `ArchitectureTests` | 30 | ~33 (3 new IModule rules) |
| Other suites | unchanged | unchanged |

### 8.7 Docker compose smoke (DoD §5)

Slice-2 checks plus:

- POST `/api/v1/catalog/applications` (admin@orga JWT) → 201
- GET `/api/v1/catalog/applications/{id}` (admin@orga JWT) → 200
- GET `/api/v1/catalog/applications/{id}` (admin@orgb JWT, against orga's id) → 404

---

## 9. Out of scope (explicit deferrals)

- Edit endpoint (`E-02.F-01.S-03`).
- Lifecycle transitions (`E-02.F-01.S-04`).
- Detail-page projection (`E-02.F-01.S-02`).
- Owner-as-Team (Team aggregate is `E-03.F-02`, future slice).
- Tags, relationships, search, audit log, notifications.
- Other entity types (Service, API, Infrastructure, Broker, Environment, Deployment).
- Pagination on `GET /applications` — full list for now; pagination ships when list size justifies it.
- React UI / web flows.
- Idempotency keys.
- Bulk operations.
- Field validation library (FluentValidation etc.) — domain factory is the single source of truth.
- Custom-attributes JSONB (ADR-0064 phase-2 feature).

---

## 10. Success criteria

1. `dotnet build Kartova.slnx -c Debug` clean (`TreatWarningsAsErrors`).
2. All architecture tests green, including the three new `IModuleRules` and the extended `TenantScopeRules` enumeration.
3. All unit tests green.
4. Catalog integration tests green: register, get-by-id, list (same tenant + cross-tenant 404), cross-tenant-write probe.
5. Existing Organization integration suite still green (no regression from `IModule` interface change).
6. KeyCloak smoke (`Kartova.Api.IntegrationTests`) still green.
7. `IModule` has `Slug` and `MapEndpoints`; `OrganizationModule` and `CatalogModule` both implement them.
8. `Program.cs` wires module endpoints via uniform `foreach`.
9. `MapTenantScopedModule` and `MapAdminModule` exist in `SharedKernel.AspNetCore` and are used by both modules.
10. `ICurrentUser` exists in `SharedKernel.AspNetCore` and is consumed by `RegisterApplicationHandler`.
11. `Application` aggregate has factory + invariants matching §5.4.
12. Migration `AddApplications` creates the table with RLS enabled.
13. KeyCloak realm seed includes `admin@orgb.kartova.local` with a distinct tenant claim.
14. ADR-0092 (URL convention) is referenced in the PR description and already merged.
15. Docker compose smoke passes the three new Catalog HTTP checks.

---

## 11. Implementation order (rough — finalised by writing-plans)

1. ADR-0092 PR — documentation only, merge first.
2. `IModule` interface change + `ModuleRouteExtensions` + `ICurrentUser` (SharedKernel layer).
3. New arch rules (`IModuleRules`) — RED first against the unchanged Organization module, then GREEN after Organization adopts.
4. `OrganizationModule` adopts `Slug` + `MapEndpoints` (no URL change; refactor only).
5. `CatalogModule` adopts `Slug` + `MapEndpoints` (empty group at first).
6. `Application` aggregate + unit tests (TDD, RED first).
7. EF config + migration + DbContext registration.
8. `RegisterApplicationCommand` + handler.
9. `GetApplicationByIdQuery` + `ListApplicationsQuery` + handlers.
10. Endpoint wiring in `CatalogModule.MapEndpoints`.
11. `RegisterApplicationTests` integration suite.
12. KeyCloak realm seed change (add `admin@orgb`).
13. `CrossTenantWriteTests`.
14. Docker compose smoke verification, push, open PR.

---

## 12. Self-review

**Spec coverage check:** Every decision in §3 traces to a section in §4-§8. Every success criterion in §10 traces to a decision and a test.

**Placeholder scan:** No "TBD" or "TODO" tokens. Sections 5.4–5.7 contain illustrative code; final code lands in writing-plans / executing-plans.

**Type / contract consistency:**

- `IModule.Slug` type (`string`) consistent across §5.1 and §8.2 (the regex test).
- `ICurrentUser.UserId` type (`Guid`) consistent across §5.3, §5.5 (handler), and §5.4 (aggregate).
- `Application.Create` signature `(name, description, ownerUserId, tenantId, clock)` consistent across §5.4, §5.5, §8.1.
- `RegisterApplicationCommand` shape `{ Name, Description }` consistent across §5.5 and §8.3.
- `ApplicationResponse` shape `{ Id, TenantId, Name, Description, OwnerUserId, CreatedAt }` consistent across §4.3, §5.5, §8.3.

**Scope check:** Single PR. ~10 new files in Catalog + 3-4 helpers + 1 arch-test file. Comparable to slice-2-followup. Not too large.

**Ambiguity check:**

- "match the Organization module's existing convention" in §5.7 (file layout for endpoint delegates) — intentional ambiguity, resolved at implementation time.

**No issues found.**

---

## 13. Follow-up slices (registered for future planning)

These items are deliberately out of slice-3 scope but recorded here so they aren't forgotten.

### 13.1 TimeProvider adoption across aggregates

**Why:** Both `Organization` and `Application` (slice 3) use `DateTimeOffset.UtcNow` directly inside `Create` factories. This is testable today only via "within ±1s of now" assertions, which are inherently flaky and gain no protection against clock-related regressions.

**Scope:**
- Adopt `TimeProvider` in `Kartova.SharedKernel` (DI-registered as `TimeProvider.System` by default).
- Refactor `Organization.Create` and `Application.Create` to take an injected `TimeProvider`, replacing direct `DateTimeOffset.UtcNow`.
- Update existing unit tests to use `FakeTimeProvider` (xUnit fixture from `Microsoft.Extensions.TimeProvider.Testing`).
- Update Wolverine handlers and any other aggregate factory call sites.
- Run `dotnet-test:detect-static-dependencies` skill to surface any other `DateTime.*` / `DateTimeOffset.*` static call sites that should join this migration.

**Trigger:** Run `dotnet-test:migrate-static-to-wrapper` skill against `src/` after slice 3 merges, scoped to `DateTimeOffset.UtcNow` and related statics.

**Effort estimate:** ~1 day. Mechanical migration; the skill handles call-site rewriting; only test updates need human review.

**Ordering:** Can ship anytime after slice 3 merges. Independent of slice 4+ feature work, but should land before any slice that needs `IClock`-style behavior (e.g., expiry, scheduling, audit-log timestamps under MiFID II — `E-01.F-05.S-07`).

### 13.2 Wolverine vs direct-dispatch ADR addendum (raised by slice-boundary review) — RESOLVED 2026-04-30

**Resolution:** Recorded in [ADR-0093 — Wolverine Scope: Outbox/Async Only, Direct Dispatch for Sync HTTP](../../architecture/decisions/ADR-0093-wolverine-scope-narrowed.md) (PR #11, merged 2026-04-30). ADR-0028 status banner updated to reflect the narrowed scope. Slice 3 handlers already follow this pattern; no retrofit needed. `WolverineFx.Http` evaluation remains deferred to post-slice-6.

Original entry preserved below for historical context:

**Why:** Slice 3's command/query handlers are invoked **directly** from endpoint delegates (resolved via `IServiceProvider` from the HTTP request scope, then `await handler.Handle(...)`), **not** through `IMessageBus.InvokeAsync<T>`. Reason: Wolverine's bus opens its own internal IoC scope for handler dispatch, and that scope is **not** the HTTP request scope where `ITenantScope` and `CatalogDbContext` live. Resolving `CatalogDbContext` inside Wolverine's scope throws "TenantScope is not active" because `TenantScopeBeginMiddleware` populated the request scope, not Wolverine's.

This is a real divergence from ADR-0028 ("Wolverine — mandatory pattern") and needs ADR-grade documentation before a second module copies the precedent.

**Scope:**
- File an ADR-0093 (or addendum to ADR-0028) recording the deviation: synchronous endpoint handlers resolve directly to share the request scope; Wolverine remains mandatory for in-process *async* mediation, the outbox path, and any future event-driven flow.
- Evaluate `WolverineFx.Http` (or equivalent) as the proper integration that lets Wolverine handlers reach via the bus while sharing request scope. If adopted, this slice's handlers can be re-routed without changing handler signatures.

**Trigger:** Before any slice that adds another tenant-scoped synchronous handler (i.e., before Slice 4 / Service entity).

**Effort estimate:** ~half day for the ADR; ~1 day if `WolverineFx.Http` integration is bundled.

### 13.3 Validation-error mapping (raised by slice-boundary review) — RESOLVED 2026-04-30

**Resolution:** Option (a) implemented. `DomainValidationExceptionHandler` (registered via `AddExceptionHandler<DomainValidationExceptionHandler>()` in `Program.cs`) maps `ArgumentException` to RFC 7807 400 with `type = ProblemTypes.ValidationFailed`. The per-endpoint try/catch in `CatalogEndpointDelegates.RegisterApplicationAsync` was removed. Pinning tests in `DomainValidationExceptionHandlerTests` cover the mapping plus pass-through for non-`ArgumentException` types.

Original entry preserved below for historical context:

**Why:** `CatalogEndpointDelegates.RegisterApplicationAsync` catches `ArgumentException` from the domain factory and maps it to `Results.Problem(type: ProblemTypes.ValidationFailed, statusCode: 400)`. The existing global `IExceptionHandler` should ideally own that mapping so future write endpoints don't copy-paste the catch.

**Scope:** Pick one:
- (a) Move `ArgumentException → 400` mapping into the global `IExceptionHandler` (ADR-0091's exception-to-ProblemDetails layer).
- (b) Document in `Program.cs` near `app.UseExceptionHandler()` that the global handler intentionally does not map domain-validation exceptions, and per-endpoint catch blocks are the convention.

**Trigger:** Before the second write endpoint copies the pattern.

**Effort estimate:** ~30 minutes either way.

### 13.4 `MapInboundClaims = false` pinning test (raised by Task 2 code review) — RESOLVED 2026-04-30

**Resolution:** Already covered. `JwtAuthenticationExtensionsTests.AddKartovaJwtAuth_SetsMapInboundClaimsFalse` asserts `options.MapInboundClaims.Should().BeFalse()` against the configured `JwtBearerOptions`. Re-enabling claim mapping fails this test; `HttpContextCurrentUser.UserId` reading the literal `"sub"` claim stays load-bearing.

Original entry preserved below for historical context:

**Why:** `HttpContextCurrentUser.UserId` reads the literal claim `"sub"`. This works only because `JwtAuthenticationExtensions.cs` sets `MapInboundClaims = false`. If anyone re-enables claim mapping, `"sub"` becomes `ClaimTypes.NameIdentifier` and `UserId` silently throws `InvalidOperationException` for every request.

**Scope:** Add a small unit or integration test asserting the JWT options shape includes `MapInboundClaims = false` (or, alternatively, broaden `HttpContextCurrentUser` to read both `"sub"` and `ClaimTypes.NameIdentifier`).

**Trigger:** Same slice as the next JWT-config touch, OR within a week.

**Effort estimate:** ~15 minutes.

### 13.5 API-entity URL naming

**Why:** Phase 1 will introduce `E-02.F-03` (sync APIs + async APIs). The URL collection name is unresolved (`/api/v1/catalog/apis` is awkward; `/api/v1/catalog/sync-apis` + `/async-apis` is verbose; `/contracts` or `/interfaces` rename loses domain term). Defer until slice 4 (Service entity) is merged so two adjacent collection names exist for context.

**Trigger:** Before slice that introduces API-entity endpoints (after Service slice).

**Effort estimate:** ~30-min ADR + zero code (no APIs shipped yet).

### 13.6 Mutation-sentinel rerun on slice-3 surface (raised by deep-review)

**Why:** `mutation-report-surviving.md` is timestamped `2026-04-26T17:01:02` against the pre-slice-3 baseline. The Catalog mutants it lists target slice-2's DbContext registration; the slice-3 domain factory, three handlers, endpoint delegates, `DomainValidationExceptionHandler`, and cross-tenant probe have never been mutated. The project's mutation feedback loop (`init-code-quality` + `mutation-sentinel` skills, ≥80% target per CLAUDE.md) is operating on stale data.

**Scope:**
- Run `mutation-sentinel` against `src/Modules/Catalog/**` and `src/Kartova.SharedKernel.AspNetCore/{HttpContextCurrentUser,DomainValidationExceptionHandler,ModuleRouteExtensions}.cs`.
- Address survivors per usual loop or document explicit accepted-survivors.
- Regenerate `mutation-report-surviving.md` so the next slice doesn't inherit a stale baseline.

**Trigger:** Before slice 4 starts. Same window as the §13.2-driven Wolverine config work — both touch the slice-3 handler shape.

**Effort estimate:** ~half-day (Stryker run + survivor triage).

### 13.7 `KartovaApiFixture` extraction to shared test infrastructure (raised by deep-review) — RESOLVED 2026-04-30

**Resolution:** New `Kartova.Testing.Auth.KartovaApiFixtureBase` owns the cross-module plumbing — Postgres `Testcontainer`, role/grants seed (`PostgresTestBootstrap.SeedRolesAndSchemaAsync`), `TestJwtSigner` swap into the JWT-bearer pipeline, env-var wiring of the API host, and the deterministic JWT minting helpers (`CreateAuthenticatedClientAsync`, `CreateAnonymousClient`, `GetSubClaimAsync`, `GetTenantIdClaimAsync`). Module-specific fixtures (`Kartova.Catalog.IntegrationTests.KartovaApiFixture`, `Kartova.Organization.IntegrationTests.KartovaApiFixture`) shrink to ~10–35 lines: declare which `DbContext` to migrate via the abstract `RunModuleMigrationsAsync` hook, plus any module-specific seeding helpers (Organization keeps `SeedOrganizationAsync` for cross-tenant probes). `Kartova.Testing.Auth` gained `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.PostgreSql 4.0.0`, and `xunit.extensibility.core` package refs plus project refs to `Kartova.Api` and `Kartova.SharedKernel.AspNetCore`. `Testcontainers.PostgreSql` was unified to 4.0.0 across the solution (Catalog was on 3.10.0, Organization on 4.0.0). Slice 4's integration tests will inherit the base directly.

Original entry preserved below for historical context:



**Why:** `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/KartovaApiFixture.cs` is ~140 lines structurally identical to the Organization fixture — Postgres container bootstrap, role grants, JWT signer wiring. The only Catalog-specific bit is `RunMigrationsAsync<CatalogDbContext>`. Slice 4 (Service entity) will need a third copy.

**Scope:** Extract the common parts to `Kartova.Testing.Auth` (or a new `Kartova.Testing.Api`) so the next module's integration tests don't copy 140 lines of fixture plumbing. The migration-runner step stays per-module.

**Trigger:** Before slice 4 stands up its own integration test project.

**Effort estimate:** ~2 hours including the Catalog + Organization migration to the shared fixture.

### 13.8 `KartovaConnectionStrings.RequireMain` helper (raised by deep-review) — RESOLVED 2026-04-30

**Resolution:** `KartovaConnectionStrings` gained `Require(IConfiguration, string)`, `RequireMain(IConfiguration)`, and `RequireBypass(IConfiguration)` static helpers that resolve `ConnectionStrings:<name>` and throw `InvalidOperationException` with a uniform diagnostic naming the env-var form on miss. Four call sites collapsed to one-liners: `Program.cs` (Main + Bypass) and the `RegisterForMigrator` overrides on `CatalogModule` and `OrganizationModule`. New `KartovaConnectionStringsTests` (6 tests) pin the helper's contract — including the exact error-message shape that CI logs scrape on bootstrap failures. Slice 4's `RegisterForMigrator` (if it grows beyond the existing Catalog module) inherits the helper directly.

Original entry preserved below for historical context:



**Why:** The same `ConnectionStrings__{name} missing` `InvalidOperationException` literal exists in `Program.cs:38`, `OrganizationModule.cs:49`, and `CatalogModule.cs:52`. Three copies drift the moment one of them changes (e.g., adding diagnostic context). Slice 4 will add a fourth.

**Scope:** Lift to a single static helper on `KartovaConnectionStrings`, e.g. `RequireMain(IConfiguration)` returning the resolved connection string or throwing the canonical message. Call it from all three (eventually four) sites.

**Trigger:** Bundle with §13.7 or take in slice 4's `RegisterForMigrator` for the Service module.

**Effort estimate:** ~30 minutes.

### 13.9 Endpoint route-name pinning arch test (raised by deep-review) — RESOLVED 2026-04-30

**Resolution:** New `Kartova.ArchitectureTests.EndpointRouteRules` boots a minimal `WebApplication`, instantiates every `IModuleEndpoints` implementation found in production assemblies, and walks the resulting `EndpointDataSource` (via `IEndpointRouteBuilder.DataSources`) to assert: (1) every expected named route exists with the right verb + URL template, (2) every endpoint registered by a module has a `.WithName(...)`, (3) names are unique across modules. The hard inventory check kills the `MapPost(...) → ;` style mutants flagged in the slice-2 mutation report; the soft "every endpoint has a name" guard catches accidental drops on routes not yet listed in the inventory. To make `RequestDelegateFactory` metadata inference pass without standing up the full DI graph, the test discovers reference-type parameters on every `*EndpointDelegates` class and registers a null-factory transient for each — RDF then classifies them as `[FromServices]` rather than `[FromBody]`. Organization + Admin Organization endpoints, which had skipped `.WithName(...)` in slice 2, now declare names alongside the test. Slice 4's three new Service-entity endpoints will inherit the gate the moment they're added to the inventory.

Original entry preserved below for historical context:



**Why:** Plan tasks 9-11 specify `.WithName("RegisterApplication")` / `"GetApplicationById"` / `"ListApplications"`. The Organization mutation report shows `MapGet(...)` mutated to `;` survives. There is no test that enumerates the registered `EndpointDataSource` and pins each named route's HTTP method + path.

**Scope:** Add an architecture test (or extend `IModuleRules`) that boots a `WebApplicationFactory`, walks `EndpointDataSource.Endpoints`, and asserts the expected named routes exist with the expected verbs and templates. Kills `MapPost`/`MapGet` statement mutants in module `MapEndpoints` overrides.

**Trigger:** Bundle with §13.6 mutation rerun or take whenever the fourth named endpoint is added.

**Effort estimate:** ~1 hour.

### 13.10 `RegisterForMigrator` coverage parity with Organization (raised by deep-review)

**Why:** `CatalogModule.RegisterForMigrator` mirrors the Organization variant but has no caller in tests; the Organization mutation report shows the parallel surface in that module has surviving NoCoverage mutants. Without a unit test, mutating the migrator-only registration silently passes.

**Scope:** `Kartova.Catalog.Infrastructure.Tests.CatalogModuleRegisterForMigratorTests` — call `RegisterForMigrator(services, config)` with a stub config, resolve `CatalogDbContext`, assert it's wired to the configured connection string and not registered through the tenant-scoped `AddModuleDbContext` path.

**Trigger:** Pair with §13.7 fixture extraction (test project may need a small bootstrap helper).

**Effort estimate:** ~30 minutes.

### 13.11 Branch-coverage gap to 85% target (raised by coverage run on slice-3 tip) — RESOLVED 2026-04-30

**Resolution:** Closed all five hot spots from the original inventory and lifted branch coverage to **87.5 %** (above the 85 % target). Headline shift: line 84.4 % → 92.0 %, branch 74.3 % → 87.5 %, method 85.8 % → 92.1 %.

Specifics:
- `TenantScopeWolverineMiddleware` 0 % → **100 %** via `TenantScopeWolverineMiddlewareTests` (7 tests). Middleware kept (rather than deleted) — it remains the canonical Wolverine-side integration for ADR-0090 once the first async tenant-scoped handler ships.
- `AdminOrganizationEndpointDelegates` 33.3 % → **100 %** and `OrganizationEndpointDelegates` 40 % → **90 %** via new `OrganizationEndpointNegativePathTests` covering blank-name 400, over-length 400, exact-boundary 201, and the GET `/me` no-org 404 path.
- `TenantScopeCommitEndpointFilter` 60 % → **100 %** via three unit tests on commit happy-path, missing-handle programmer-error, and commit-throws bubbling. Required adding `[InternalsVisibleTo("Kartova.SharedKernel.AspNetCore.Tests")]` so tests can reach the internal `HandleKey` constant.
- `TenantScope` 63 % → **66.1 %** via three new integration tests in `TenantScopeMechanismTests`: "already begun" guard, idempotent `Handle.DisposeAsync`, and `CommitAsync`-after-dispose programmer error. The remaining gap (~1/3) is in the `BeginAsync` `NpgsqlException`-cleanup path and `DisposeAsyncCore` rollback-throws path — both real fault-injection territory that the existing `KartovaApiFaultInjectionFixture` is the right venue for, deliberately deferred.
- Cosmetic exclusions on framework types: `IModule.RegisterForMigrator` (default-implementation method on the interface — interface-level `[ExcludeFromCodeCoverage]` is a CS0592 error, applied at method level), `DomainEvent` abstract record, `TenantScopeBeginException`. Each had 0 % method coverage as a counting artifact.

Test count: 96 → **143** (+47 unit/arch + integration). Build clean under `TreatWarningsAsErrors=true`. Full integration suite green.

Original entry preserved below for historical context:



**Why:** Coverage run on `b8cbe94` (full suite, 96 unit/arch + 32 integration) lands at 84.1% line / **73.9% branch** — below CLAUDE.md's 85% branch target. Slice-3 surface itself sits at 90–100%; the gap concentrates in slice-2 code that shipped before this target was tracked at the branch level.

**Hot-spot inventory (uncovered branches, ranked):**
- `Kartova.SharedKernel.Wolverine.TenantScopeWolverineMiddleware` — **0%** line coverage; never invoked. ADR-0093 makes Wolverine async-only, so this middleware now belongs to the future async/event slice. Either delete (no caller today) or pin with a focused unit test once the first async handler lands.
- `OrganizationEndpointDelegates` — **40%**. Negative paths (validation 400 via the new `DomainValidationExceptionHandler`, missing fields) are unexercised by integration tests.
- `AdminOrganizationEndpointDelegates` — **33.3%**. Admin-only auth-failure branch + duplicate-org conflict branch lack coverage.
- `TenantScopeCommitEndpointFilter` — **60%**. The exception-during-commit branch (commit fails → exception bubbles to `UseExceptionHandler`) is unexercised.
- `TenantScope` — **63%**. Rollback-on-error and dispose-while-already-disposed branches.
- `Kartova.SharedKernel.{IModule, DomainEvent, TenantScopeBeginException}` — **0%** but cosmetic (interface/abstract/exception type with default ctor); marking `[ExcludeFromCodeCoverage]` would clean up the score without adding tests.

**Scope:**
- (a) Decide Wolverine middleware: delete or pin with a smoke test.
- (b) Add Organization endpoint negative-path tests (validation 400, conflict 409, auth 401/403).
- (c) Add `TenantScopeCommitEndpointFilter` exception-path test (commit throws → response intact).
- (d) Add `TenantScope` rollback-path test.
- (e) Add `[ExcludeFromCodeCoverage]` to `IModule`, `DomainEvent`, `TenantScopeBeginException` (cosmetic exclusions matching the existing convention for SharedKernel framework types).

**Trigger:** Bundle with §13.6 (mutation rerun) — same set of files end up under both microscopes, and survivors will likely overlap with these branch gaps. Resolves the divergence before it widens.

**Effort estimate:** ~1 day total (mostly negative-path tests for Organization endpoints + the small SharedKernel exclusions).

### 13.12 NetArchTest false positives under coverlet instrumentation (raised by coverage run) — RESOLVED 2026-04-30

**Resolution:** `ContractsCoverageRules.All_types_in_Contracts_assemblies_have_ExcludeFromCodeCoverage` and `DTO_like_types_have_ExcludeFromCodeCoverage` now skip three categories of synthetic types: `[CompilerGenerated]`-decorated types, anything residing in `Coverlet.*` namespaces (the instrumentation tracker classes), and types whose CLR name matches `^<.*>.*$` (closures, anonymous-type backing classes, `<Module>`, `<PrivateImplementationDetails>`). Verified by running the full suite under `dotnet test --settings coverlet.runsettings`: 128/128 pass with the data collector attached, where previously the contracts-coverage rule reported `Coverlet.Core.Instrumentation.Tracker.<assembly>_<guid>` as undecorated classes.

Original entry preserved below for historical context:



**Why:** When `dotnet test --settings coverlet.runsettings` runs against `Kartova.ArchitectureTests`, `ContractsCoverageRules.All_types_in_Contracts_assemblies_have_ExcludeFromCodeCoverage` fails because coverlet injects compiler-generated helper types (e.g. `<Module>`, `<PrivateImplementationDetails>`) that NetArchTest's `Types.InAssemblies(...).That().AreClasses()` predicate sees as undecorated classes. Standalone (`dotnet test --no-build` without the data collector) the test passes. CI runs that need both the architecture gate and coverage collection currently can't enable both simultaneously.

**Scope:** Tighten the predicate to skip compiler-generated artifacts: filter out types decorated with `[CompilerGenerated]`, types whose name matches `<*>`, and the `<Module>` synthetic type. Apply the same fix to `DTO_like_types_have_ExcludeFromCodeCoverage` if it shows the same pattern.

**Trigger:** Bundle with the next CI workflow change that wires coverage into the build, or §13.11 (coverage gap closure) — both want the architecture gate to coexist with coverage collection.

**Effort estimate:** ~30 minutes.
