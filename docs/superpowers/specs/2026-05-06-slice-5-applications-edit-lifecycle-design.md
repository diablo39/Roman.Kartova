# Slice 5 — Catalog: Edit Application + Lifecycle Transitions

**Date:** 2026-05-06
**Stories:** E-02.F-01.S-03 (edit application metadata) + E-02.F-01.S-04 (application lifecycle status transitions)
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/slice-5-applications-edit-lifecycle`

---

## 1. Goal

Slice 5 lands the project's first **edit endpoint** and first **lifecycle transition endpoints**, end-to-end through the Catalog module, on the same `Application` aggregate that slice 3 introduced and slice 4 surfaced in the SPA. It is the second vertical slice through Catalog (after register-only) and proves four things slices 3 and 4 didn't:

1. The optimistic-concurrency contract (Postgres `xmin` rowversion + `If-Match` request header + `412 Precondition Failed`) works end-to-end and is cheap to copy across the ~20 future edit endpoints in the Catalog (Service, API, Infrastructure, Broker, Environment, Deployment).
2. Domain-driven action endpoints (`POST /{id}/deprecate`, `POST /{id}/decommission`) work cleanly with the slice-3 Wolverine handler shape, set the project's REST verb policy, and are arch-test-pinnable.
3. ADR-0073's lifecycle state machine (Active → Deprecated → Decommissioned, linear forward, sunset-date strict on Decommissioned) is enforceable as domain invariants today, with the audit/notification/admin-override subsystems honestly deferred to follow-up slices.
4. The slice-4 SPA can absorb three new mutation flows (edit, deprecate, decommission) and a state-aware lifecycle dropdown without growing new primitives — `Dialog`, `DropdownMenu`, `Form`, `applyProblemDetailsToForm`, and Untitled UI's `DatePicker` already exist in the repo.

It also ships **ADR-0096 — REST verb policy** (PUT for full replacement, POST for actions, no PATCH) bundled in the same PR. The first edit slice instantiates the policy; arch tests pin it.

Slice 5 is **not** a complete S-04: no audit log of transitions (E-01.F-03.S-03 unbuilt), no notifications to dependents (ADR-0047 / E-06 unbuilt), no admin override on backward transitions (E-01.F-04.S-03 RBAC unbuilt), no successor reference on Deprecated transitions. Each is captured in §13 Backlog with a concrete trigger.

It is also **not** an "MVP-everything" slice: no rename of `Application.Name` (kebab slug stays immutable), no ownership transfer (separate flow tied to E-03 Team aggregate), no soft-delete (Decommissioned is the project's terminal state for now), no bulk lifecycle operations (E-01.F-06.S-03 territory).

**Stories closed:** E-02.F-01.S-03, E-02.F-01.S-04.
**Stories ticked as housekeeping** (already shipped in PR #18 — slice-4-cleanup — checklist stale): E-02.F-01.S-06 (field-level ProblemDetails), E-02.F-01.S-07 (kebab-case domain invariant), E-01.F-01.S-04 (dev seed Org A).

---

## 2. Pre-requisites

The following are **already on master** as of this spec:

- Slices 0–4 merged plus the slice-4 cleanup PR (#18), the cursor-pagination contract (ADR-0095, PR #20), and the slice-3 follow-up bundle.
- `Application` aggregate exists with `Create` factory, `ValidateName` enforcing kebab-case (`^[a-z][a-z0-9]*(-[a-z0-9]+)*$`), `ValidateDisplayName` (≤128), `ValidateDescription` (non-whitespace).
- `DomainValidationExceptionHandler` already maps `ArgumentException` to RFC 7807 `400 Bad Request` with `ValidationProblemDetails.errors` populated from `ParamName` (slice-4-cleanup, slice-3 §13.3 RESOLVED).
- Cursor-paginated list at `GET /api/v1/catalog/applications` returns `CursorPage<ApplicationResponse>` sorted by `createdAt:desc` (default) or `name:asc/desc`.
- `KartovaApiFixtureBase` (slice-3 §13.7 RESOLVED) provides shared Testcontainers + JWT plumbing for Catalog integration tests.
- `EndpointRouteRules` arch test (slice-3 §13.9 RESOLVED) walks `EndpointDataSource` and asserts named-route inventory; slice 5 extends the inventory.
- `[ExcludeFromCodeCoverage]` enforced on `*.Contracts` types and `*Request`/`*Response` DTOs (`ContractsCoverageRules`).
- SPA: `RegisterApplicationDialog`, `applyProblemDetailsToForm`, openapi-fetch typed client, react-hook-form + zod, Untitled UI components (Dialog, DropdownMenu, DatePicker, Badge, Button, Form, Skeleton, Card).

Nothing in slice 5 depends on a separate doc-only PR landing first; **ADR-0096 is bundled in the same PR as the code** (Decision #5).

---

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | Slice scope = S-03 + S-04 only. S-06, S-07, F-01.S-04 ticked as housekeeping (already shipped in PR #18, checklist stale). | Smallest walking-skeleton increment over slice 4. |
| 2 | Lifecycle = ADR-0073 core, infra-aware deferrals: 3 states, linear forward only, `sunsetDate` required for Deprecated, strict "no Decommissioned before sunsetDate" rule, backward transitions return 400, no audit log, no notifications, no admin override. | Honors the ADR's domain invariants (the part that gets copied to every other entity's lifecycle). Defers subsystems that don't exist yet (audit table, notification fan-out, RBAC) to dedicated follow-up slices §13.1–§13.3. |
| 3 | Edit field scope = `displayName` + `description` only. `name` (kebab slug) immutable; `ownerUserId` change is a separate ownership-transfer flow tied to E-03 Team aggregate; `lifecycle` has its own endpoints; `id`/`tenantId`/`createdAt` immutable. | `name` is identity-stable for URLs / CLI / future agents and webhook subscribers — renaming is a real "rename" feature with redirects, not metadata edit. Ownership transfer belongs in the Team epic. |
| 4 | REST verb shape: **PUT** for `applications/{id}` (full replacement); **POST** for `/{id}/deprecate` + `/{id}/decommission` (named action endpoints — one per domain method). | Per-route auth granularity is declarative (when RBAC lands, future `restore` is one `[Authorize(Policy = "platform-admin")]` attribute on a separate route, no in-handler branching). Per-action DTOs are precise — no nullable-everywhere wide-DTO antipattern. OpenAPI surface reads like the ubiquitous language. |
| 5 | **ADR-0096 — REST verb policy** (PUT for full replacement, POST for actions, no PATCH) authored and merged in **this PR**. | First edit slice instantiates the policy. Arch test pins absence of PATCH routes. Bundled (not separate doc-PR) per user instruction (solo dev, ADR-as-PR-companion). |
| 6 | Optimistic concurrency on edit: Postgres `xmin` system column mapped as EF concurrency token; `ETag` response header + `If-Match` request header + `412 Precondition Failed` ProblemDetails on mismatch. Missing `If-Match` → `428 Precondition Required`. | First edit endpoint of ~20 across catalog entities. Cheap now (`xmin` adds zero schema cost), expensive retrofit later (multi-aggregate migration + every-client breakage). |
| 7 | Lifecycle endpoints **don't** take `If-Match`. Domain invariant ("current Lifecycle must be the previous state per ADR-0073's linear progression") is the implicit version. Race between two clients deprecating the same Active app → second client gets 409 Conflict with current state in the body. | Linear ADR-0073 transitions are their own concurrency control. ETag header would force callers to fetch-then-act for transitions that the domain already serializes. |
| 8 | Successor reference deferred entirely (no column, no DTO field, no UI). | "Where applicable" cover in ADR-0073. Field is write-only without a consumer (notifications/graph unbuilt). Avoids half-shipping FK without the cascade-on-decommissioned answer. Captured §13.4. |
| 9 | UI: Edit button in detail-page header → `EditApplicationDialog` (mirrors `RegisterApplicationDialog`); lifecycle Badge becomes interactive → `LifecycleMenu` (`DropdownMenu` filtered by current state) → `DeprecateConfirmDialog` (with `sunsetDate` picker) or `DecommissionConfirmDialog` (plain confirm). | Reuses existing primitives. State-aware filtering matches ADR-0073 linear progression (Active → "Deprecate…"; Deprecated → "Decommission" enabled iff `now ≥ sunsetDate`; Decommissioned → dropdown hides + Edit button hides). |
| 10 | Sunset date validation: must be strictly `> clock.GetUtcNow()` at deprecation time, enforced as domain invariant in `Application.Deprecate`. | Past dates have no semantic meaning for "consumers should have migrated by". Strict-greater-than (not ≥) keeps the boundary unambiguous. |
| 11 | Re-deprecating an already-Deprecated application = 409 Conflict, **not** idempotent no-op. | Idempotency keys are deferred (slice 3 §13). Honest "already in this state" signal helps clients distinguish "I succeeded" from "someone beat me to it". |
| 12 | `EditMetadata` on a `Decommissioned` application = 409 (`…/lifecycle-conflict`, `attemptedTransition: "EditMetadata"`). Decommissioned is the project's terminal read-only state. | Single domain rule: Decommissioned forbids all writes. Server is authority; SPA hides the Edit button optimistically. |
| 13 | `Lifecycle` enum stored as `smallint` with explicit numeric values 1=Active, 2=Deprecated, 3=Decommissioned. JSON wire shape is **string** (`"Active"` / `"Deprecated"` / `"Decommissioned"`) via the project's existing `JsonStringEnumConverter`. Linear ordering pinned by arch test. | Numeric DB encoding keeps comparisons cheap and lets future SQL filters use `<=` / `>=`. String wire shape keeps the API self-documenting and avoids "what does 2 mean" debugging. |
| 14 | `TimeProvider` adopted **only** for new methods (`Deprecate`, `Decommission`). `Application.Create` still uses `DateTimeOffset.UtcNow` directly — the retrofit is registered backlog (slice-3 §13.1) and lands in its own slice. | Sunset-date arithmetic needs deterministic tests; retrofitting `Create` would balloon scope and conflict with the existing backlog item. |
| 15 | Migration `AddApplicationLifecycle` adds `lifecycle smallint NOT NULL DEFAULT 1` and `sunset_date timestamptz NULL` columns in a single migration. Same RLS toggle dance as `AddApplicationDisplayName` (NO FORCE → backfill if needed → FORCE). | Default value of 1=Active backfills every existing row in one ALTER. No row-by-row UPDATE needed. |

---

## 4. Architecture

### 4.1 Endpoint topology after slice 5

```
GET   /api/v1/version                                        (system, anonymous)

GET   /api/v1/organizations/me                               (tenant-scoped, existing)
POST  /api/v1/admin/organizations                            (admin, existing)

POST  /api/v1/catalog/applications                           (tenant-scoped, existing — register)
GET   /api/v1/catalog/applications/{id}                      (tenant-scoped, existing — get one)
GET   /api/v1/catalog/applications                           (tenant-scoped, existing — cursor-paged list)
PUT   /api/v1/catalog/applications/{id}                      (tenant-scoped, NEW — edit metadata)
POST  /api/v1/catalog/applications/{id}/deprecate            (tenant-scoped, NEW — Active → Deprecated)
POST  /api/v1/catalog/applications/{id}/decommission         (tenant-scoped, NEW — Deprecated → Decommissioned)
```

All three new endpoints inherit the slice-3 module pipeline:

```
JWT auth → TenantClaimsTransformation
        → TenantScopeBeginMiddleware (BEGIN TX, SET LOCAL app.current_tenant_id)
        → endpoint binding
        → Wolverine/direct-dispatch handler (slice-3 ADR-0093 pattern)
                ├ load Application (RLS scopes to tenant; cross-tenant id → null → 404)
                ├ Application.<DomainMethod>(...)  ← invariants
                ├ db.SaveChangesAsync()  ← TenantScopeEnlistInterceptor enlists tx
                │       ↑ DbUpdateConcurrencyException on xmin mismatch (edit only)
                └ project ApplicationResponse
        → endpoint returns Results.Ok(...) [+ ETag header on edit]
        → TenantScopeCommitEndpointFilter (COMMIT TX)
        → IResult.ExecuteAsync (writes status + body)
```

### 4.2 Domain model changes

**New enum** in `Kartova.Catalog.Domain`:

```csharp
public enum Lifecycle
{
    Active = 1,
    Deprecated = 2,
    Decommissioned = 3,
}
```

Underlying numeric values are **explicit and load-bearing** — linear ordering is pinned by an architecture test, since `Decommission` validates `Lifecycle == Deprecated` and `Deprecate` validates `Lifecycle == Active`. Reordering or inserting a state shifts all downstream `int`-based comparisons; the test forces a deliberate reckoning.

**`Application` aggregate gains:**

```csharp
public Lifecycle Lifecycle { get; private set; }            // default Active on Create
public DateTimeOffset? SunsetDate { get; private set; }     // null unless transitioned to Deprecated
public uint Version { get; private set; }                   // shadow-mapped to Postgres xmin (concurrency token)

public void EditMetadata(string displayName, string description);
public void Deprecate(DateTimeOffset sunsetDate, TimeProvider clock);
public void Decommission(TimeProvider clock);
```

`Version` is private-set with EF Core's `IsRowVersion()` + `IsConcurrencyToken()` configured via the entity config (no public setter — Postgres maintains `xmin` automatically). Reading is via getter only; serialization to wire format happens in the response projection.

**Invariants:**

| Method | Invariant | On violation |
|---|---|---|
| `EditMetadata` | `Lifecycle != Decommissioned` | `InvalidLifecycleTransitionException(currentLifecycle, attemptedTransition: "EditMetadata")` → 409 |
| `EditMetadata` | `displayName`: 1–128 non-whitespace | `ArgumentException` → 400 ValidationProblemDetails (existing handler) |
| `EditMetadata` | `description`: non-whitespace (no upper bound currently — matches `Create`) | `ArgumentException` → 400 |
| `Deprecate` | `Lifecycle == Active` | `InvalidLifecycleTransitionException` → 409 |
| `Deprecate` | `sunsetDate > clock.GetUtcNow()` (strict) | `ArgumentException(nameof(sunsetDate))` → 400 |
| `Decommission` | `Lifecycle == Deprecated` | `InvalidLifecycleTransitionException` → 409 |
| `Decommission` | `clock.GetUtcNow() >= SunsetDate` | `InvalidLifecycleTransitionException(reason: "before-sunset-date")` → 409 (admin override comes with RBAC slice §13.2) |

`Application.Create` factory remains unchanged in this slice — sets `Lifecycle = Active`, `SunsetDate = null` implicitly via the new defaults on the field declarations.

### 4.3 New domain exception

```csharp
public sealed class InvalidLifecycleTransitionException : InvalidOperationException
{
    public Lifecycle CurrentLifecycle { get; }
    public string AttemptedTransition { get; }
    public DateTimeOffset? SunsetDate { get; }
    public string? Reason { get; }                  // e.g. "before-sunset-date"

    public InvalidLifecycleTransitionException(
        Lifecycle current, string attempted, DateTimeOffset? sunsetDate = null, string? reason = null)
        : base($"Cannot {attempted.ToLowerInvariant()} application currently in state {current}.")
    { ... }
}
```

Lives in `Kartova.Catalog.Domain`. Caught by a new `LifecycleConflictExceptionHandler` in `Kartova.SharedKernel.AspNetCore` and projected to a 409 ProblemDetails (§7).

### 4.4 File map

**Modified:**

| File | Change |
|---|---|
| `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs` | Add `Lifecycle`, `SunsetDate`, `Version` properties. Add `EditMetadata`, `Deprecate`, `Decommission` methods + invariant validation. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs` | Map `Lifecycle` (smallint, NOT NULL, default 1=Active), `SunsetDate` (timestamptz, nullable), `Version` (xmin, IsRowVersion + IsConcurrencyToken). |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (or `CatalogEndpoints.cs`) | Register three new endpoints in `MapEndpoints`. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | Add `EditApplicationAsync`, `DeprecateApplicationAsync`, `DecommissionApplicationAsync` static delegates. |
| `src/Modules/Catalog/Kartova.Catalog.Application/ApplicationResponseExtensions.cs` | Project `Lifecycle`, `SunsetDate`, `Version` into `ApplicationResponse`. |
| `src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs` | Add `Lifecycle Lifecycle`, `DateTimeOffset? SunsetDate`, `string Version` fields. |
| `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs` | Add `ConcurrencyConflict`, `PreconditionRequired`, `LifecycleConflict` URI constants. |
| `web/src/features/catalog/api/applications.ts` | Add `useEditApplication`, `useDeprecateApplication`, `useDecommissionApplication`. Edit hook attaches `If-Match` from cached `version`. |
| `web/src/features/catalog/pages/ApplicationDetailPage.tsx` | Replace hardcoded `Active` badge with state-driven `LifecycleBadge`; render `sunsetDate` subline; add Edit button + `LifecycleMenu`. |
| `web/src/features/catalog/components/ApplicationsTable.tsx` | Add Lifecycle column (compact pill) to list rows. |
| `web/src/features/catalog/components/RegisterApplicationDialog.tsx` | Mark the read-only "Active" badge as the new `LifecycleBadge` for visual consistency (no logic change). |
| `docs/product/CHECKLIST.md` | Tick S-03, S-04, plus stale S-06, S-07, F-01.S-04. |
| `docs/architecture/decisions/README.md` | Index ADR-0096. |

**Created (backend):**

| File | Purpose |
|---|---|
| `src/Modules/Catalog/Kartova.Catalog.Domain/Lifecycle.cs` | Enum (3 explicit values). |
| `src/Modules/Catalog/Kartova.Catalog.Domain/InvalidLifecycleTransitionException.cs` | Sealed domain exception with `CurrentLifecycle`, `AttemptedTransition`, `SunsetDate?`, `Reason?`. |
| `src/Modules/Catalog/Kartova.Catalog.Application/EditApplicationCommand.cs` + handler | `(ApplicationId Id, string DisplayName, string Description, uint ExpectedVersion)`. |
| `src/Modules/Catalog/Kartova.Catalog.Application/DeprecateApplicationCommand.cs` + handler | `(ApplicationId Id, DateTimeOffset SunsetDate)`. |
| `src/Modules/Catalog/Kartova.Catalog.Application/DecommissionApplicationCommand.cs` + handler | `(ApplicationId Id)`. |
| `src/Modules/Catalog/Kartova.Catalog.Contracts/EditApplicationRequest.cs` | DTO `{ DisplayName, Description }` with `[ExcludeFromCodeCoverage]`. |
| `src/Modules/Catalog/Kartova.Catalog.Contracts/DeprecateApplicationRequest.cs` | DTO `{ SunsetDate }` with `[ExcludeFromCodeCoverage]`. |
| `src/Kartova.SharedKernel.AspNetCore/IfMatchEndpointFilter.cs` | Reads `If-Match` request header → stores parsed `uint` in `HttpContext.Items["expected-version"]`; throws `PreconditionRequiredException` when missing or malformed. |
| `src/Kartova.SharedKernel.AspNetCore/PreconditionRequiredException.cs` | Sealed exception type carrying the failure reason; thrown by `IfMatchEndpointFilter`. |
| `src/Kartova.SharedKernel.AspNetCore/ConcurrencyConflictExceptionHandler.cs` | Maps EF `DbUpdateConcurrencyException` → 412 ProblemDetails with `currentVersion` extension. |
| `src/Kartova.SharedKernel.AspNetCore/PreconditionRequiredExceptionHandler.cs` | Maps `PreconditionRequiredException` → 428 ProblemDetails. |
| `src/Kartova.SharedKernel.AspNetCore/LifecycleConflictExceptionHandler.cs` | Maps `InvalidLifecycleTransitionException` → 409 ProblemDetails with `currentLifecycle`, `attemptedTransition`, `sunsetDate?`, `reason?`. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<ts>_AddApplicationLifecycle.cs` | Adds `lifecycle smallint NOT NULL DEFAULT 1` + `sunset_date timestamptz NULL` columns. RLS toggle dance for safe ALTER. |
| `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationLifecycleTests.cs` | New domain unit tests (~16 cases). |
| `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/EditApplicationTests.cs` | PUT happy + 4xx + 412 paths. |
| `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DeprecateApplicationTests.cs` | POST `/deprecate` happy + invariant + 409 paths. |
| `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DecommissionApplicationTests.cs` | POST `/decommission` happy + 409 paths. |
| `src/Kartova.SharedKernel.AspNetCore.Tests/IfMatchEndpointFilterTests.cs` | Header parsing + missing-header → 428. |
| `src/Kartova.SharedKernel.AspNetCore.Tests/ConcurrencyConflictExceptionHandlerTests.cs` | Mapping pinning. |
| `src/Kartova.SharedKernel.AspNetCore.Tests/LifecycleConflictExceptionHandlerTests.cs` | Mapping pinning. |
| `tests/Kartova.ArchitectureTests/RestVerbPolicyRules.cs` | Pins ADR-0096 — no PATCH route registered. |
| `tests/Kartova.ArchitectureTests/LifecycleEnumRules.cs` | Pins enum stability (3 members, explicit values, linear ordering). |
| `docs/architecture/decisions/ADR-0096-rest-verb-policy.md` | New ADR. |

**Created (frontend):**

| File | Purpose |
|---|---|
| `web/src/features/catalog/components/EditApplicationDialog.tsx` | Edit modal. |
| `web/src/features/catalog/components/LifecycleMenu.tsx` | DropdownMenu anchored on Badge. |
| `web/src/features/catalog/components/LifecycleBadge.tsx` | Reused everywhere lifecycle renders (detail header, list cell, register dialog). |
| `web/src/features/catalog/components/DeprecateConfirmDialog.tsx` | Confirm w/ sunset-date picker. |
| `web/src/features/catalog/components/DecommissionConfirmDialog.tsx` | Plain confirm. |
| `web/src/features/catalog/schemas/editApplication.ts` | zod schema (DisplayName, Description). |
| `web/src/features/catalog/schemas/deprecateApplication.ts` | zod schema (sunsetDate, future-only). |
| `web/src/features/catalog/components/__tests__/EditApplicationDialog.test.tsx` | Vitest. |
| `web/src/features/catalog/components/__tests__/LifecycleMenu.test.tsx` | Vitest. |
| `web/src/features/catalog/api/__tests__/editApplication.test.ts` | Vitest mutation hook tests. |
| `web/src/features/catalog/api/__tests__/deprecateApplication.test.ts` | Vitest. |
| `web/src/features/catalog/api/__tests__/decommissionApplication.test.ts` | Vitest. |

---

## 5. Components

### 5.1 `Application` aggregate (after slice 5)

```csharp
public sealed partial class Application : ITenantOwned
{
    private Guid _id;
    public ApplicationId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string Name { get; private set; } = string.Empty;             // immutable post-Create (slice 5)
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public Guid OwnerUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Lifecycle Lifecycle { get; private set; } = Lifecycle.Active; // NEW
    public DateTimeOffset? SunsetDate { get; private set; }              // NEW
    public uint Version { get; private set; }                            // NEW — xmin shadow

    // Existing Create factory unchanged. New methods:

    public void EditMetadata(string displayName, string description)
    {
        if (Lifecycle == Lifecycle.Decommissioned)
            throw new InvalidLifecycleTransitionException(Lifecycle, "EditMetadata");

        ValidateDisplayName(displayName);
        ValidateDescription(description);

        DisplayName = displayName;
        Description = description;
    }

    public void Deprecate(DateTimeOffset sunsetDate, TimeProvider clock)
    {
        if (Lifecycle != Lifecycle.Active)
            throw new InvalidLifecycleTransitionException(Lifecycle, "Deprecate", SunsetDate);

        if (sunsetDate <= clock.GetUtcNow())
            throw new ArgumentException(
                "sunsetDate must be in the future.", nameof(sunsetDate));

        Lifecycle = Lifecycle.Deprecated;
        SunsetDate = sunsetDate;
    }

    public void Decommission(TimeProvider clock)
    {
        if (Lifecycle != Lifecycle.Deprecated)
            throw new InvalidLifecycleTransitionException(Lifecycle, "Decommission", SunsetDate);

        if (clock.GetUtcNow() < SunsetDate!.Value)
            throw new InvalidLifecycleTransitionException(
                Lifecycle, "Decommission", SunsetDate, reason: "before-sunset-date");

        Lifecycle = Lifecycle.Decommissioned;
    }
}
```

### 5.2 EF entity config (additions)

```csharp
public class EfApplicationConfiguration : IEntityTypeConfiguration<Application>
{
    public void Configure(EntityTypeBuilder<Application> b)
    {
        // existing mappings...

        b.Property(a => a.Lifecycle)
            .HasColumnName("lifecycle")
            .HasColumnType("smallint")
            .HasConversion<short>()                         // enum → smallint
            .HasDefaultValue(Lifecycle.Active)
            .IsRequired();

        b.Property(a => a.SunsetDate)
            .HasColumnName("sunset_date")
            .HasColumnType("timestamptz");                  // nullable by default (DateTimeOffset?)

        b.Property(a => a.Version)
            .HasColumnName("xmin")
            .HasColumnType("xid")                           // Postgres xmin system column
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion()
            .IsConcurrencyToken();
    }
}
```

### 5.3 `EditApplicationHandler`

```csharp
public sealed class EditApplicationHandler
{
    // Nullable return matches the existing GetApplicationByIdHandler convention —
    // handler returns null when the row is not visible (RLS hides cross-tenant
    // rows or id is unknown); endpoint delegate maps null to RFC 7807 404 with
    // ProblemTypes.ResourceNotFound, identical to GET-by-id (ADR-0090).
    public async Task<ApplicationResponse?> Handle(
        EditApplicationCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(a => a.Id == cmd.Id, ct);
        if (app is null) return null;

        // Set ExpectedVersion on the tracked entity so EF's UPDATE includes
        // WHERE xmin = :expected; mismatch raises DbUpdateConcurrencyException
        // which the ConcurrencyConflictExceptionHandler maps to 412.
        db.Entry(app).Property(a => a.Version).OriginalValue = cmd.ExpectedVersion;

        app.EditMetadata(cmd.DisplayName, cmd.Description);
        await db.SaveChangesAsync(ct);

        return ApplicationResponse.From(app);
    }
}
```

`DeprecateApplicationHandler` and `DecommissionApplicationHandler` follow the same load → invariant → save → null-or-projection shape, minus the `ExpectedVersion` step. Endpoint delegates check the nullable result and emit the same 404 ProblemDetails as `GetApplicationByIdAsync`.

### 5.4 `IfMatchEndpointFilter`

```csharp
public sealed class IfMatchEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var headers = ctx.HttpContext.Request.Headers;
        if (!headers.TryGetValue("If-Match", out var values) || string.IsNullOrWhiteSpace(values))
            throw new PreconditionRequiredException("If-Match header is required for this endpoint.");

        var raw = values.ToString().Trim('"');
        if (!TryDecodeVersion(raw, out var expected))
            throw new PreconditionRequiredException("If-Match header is not a valid version token.");

        ctx.HttpContext.Items["expected-version"] = expected;
        return await next(ctx);
    }

    private static bool TryDecodeVersion(string raw, out uint version) { /* base64 → 4 bytes → uint */ }
}
```

Attached to `PUT /applications/{id}` via `.AddEndpointFilter<IfMatchEndpointFilter>()` in the route registration. The endpoint delegate reads `expected-version` from `HttpContext.Items` and passes to the command.

### 5.5 `ApplicationResponse` after slice 5

```csharp
[ExcludeFromCodeCoverage]
public sealed record ApplicationResponse(
    Guid Id,
    Guid TenantId,
    string Name,
    string DisplayName,
    string Description,
    Guid OwnerUserId,
    DateTimeOffset CreatedAt,
    Lifecycle Lifecycle,                   // NEW
    DateTimeOffset? SunsetDate,            // NEW
    string Version);                       // NEW — base64 of xmin uint
```

`Version` is the wire form (string). The `ETag` response header (only on edit/get-by-id) carries the same value as `"<Version>"` (RFC 7232 quoted).

### 5.6 SPA — `EditApplicationDialog` (sketch)

```tsx
export function EditApplicationDialog({ application, open, onOpenChange }: Props) {
    const form = useForm<EditApplicationFormValues>({
        resolver: zodResolver(editApplicationSchema),
        defaultValues: { displayName: application.displayName, description: application.description },
    });
    const mutation = useEditApplication(application.id);

    const onSubmit = form.handleSubmit(async values => {
        try {
            await mutation.mutateAsync({ values, expectedVersion: application.version });
            toast.success("Application updated");
            onOpenChange(false);
        } catch (err) {
            const handled = applyProblemDetailsToForm(err, form);
            if (!handled) toast.error("Could not update application");
        }
    });
    // ... Dialog + Form rendering
}
```

`useEditApplication` attaches `If-Match: "<expectedVersion>"` to the PUT request via the openapi-fetch `headers` option.

### 5.7 SPA — `LifecycleMenu` (sketch)

```tsx
export function LifecycleMenu({ application, now, onTransitionRequested }: Props) {
    const items: MenuItem[] = [];
    if (application.lifecycle === "Active") {
        items.push({ id: "deprecate", label: "Deprecate…", action: "deprecate" });
    } else if (application.lifecycle === "Deprecated") {
        const canDecommission = now >= new Date(application.sunsetDate!);
        items.push({
            id: "decommission",
            label: "Decommission",
            action: "decommission",
            disabled: !canDecommission,
            tooltip: canDecommission ? undefined : `Available after ${formatDate(application.sunsetDate!)}`,
        });
    }
    if (items.length === 0) return null;            // Decommissioned → menu doesn't render

    return <DropdownMenu items={items} trigger={<LifecycleBadge lifecycle={application.lifecycle} sunsetDate={application.sunsetDate} interactive />} />;
}
```

---

## 6. Data flow

### 6.1 PUT edit happy path

```
Client (with ETag from prior GET)
  → JWT auth
  → TenantScopeBeginMiddleware
  → IfMatchEndpointFilter parses header → HttpContext.Items["expected-version"]
  → endpoint binds EditApplicationRequest
  → EditApplicationHandler
       ├ load Application by id (RLS scopes tenant; missing → null → endpoint emits 404 ProblemTypes.ResourceNotFound)
       ├ set EF OriginalValue(Version) = expectedVersion
       ├ Application.EditMetadata(req.DisplayName, req.Description)  ← invariants
       ├ db.SaveChangesAsync()
       │       ↑ DbUpdateConcurrencyException if WHERE xmin = :expected matched 0 rows
       └ return ApplicationResponse with new Version
  → endpoint returns Results.Ok(...) + ETag header
  → TenantScopeCommitEndpointFilter (COMMIT TX)
  → IResult.ExecuteAsync (200 + body)
```

### 6.2 412 concurrency conflict path

```
... DbUpdateConcurrencyException thrown by SaveChanges
  → ConcurrencyConflictExceptionHandler.TryHandleAsync (IExceptionHandler)
  → 412 + ProblemDetails {
        type: …/concurrency-conflict,
        title: "Concurrency conflict",
        detail: "The application was modified by another request.",
        extensions: { currentVersion: "<refreshed-base64>" }    // refreshed via separate read after rollback
      }
  → COMMIT path skipped (transaction rolls back via the begin-middleware's `using` scope)
```

### 6.3 POST deprecate happy path

```
Client → JWT → BeginMiddleware
  → endpoint binds DeprecateApplicationRequest { SunsetDate }
  → DeprecateApplicationHandler
       ├ load Application by id
       ├ Application.Deprecate(req.SunsetDate, clock)
       │       ├ if Lifecycle != Active → throw InvalidLifecycleTransitionException
       │       ├ if sunsetDate <= clock.GetUtcNow() → throw ArgumentException
       │       └ set Lifecycle=Deprecated, SunsetDate=req.SunsetDate
       ├ SaveChangesAsync()
       └ return ApplicationResponse (lifecycle endpoints don't expose ETag header)
  → 200 + body
  → CommitFilter
```

### 6.4 409 lifecycle conflict path

```
... InvalidLifecycleTransitionException thrown by Application.Deprecate
  → LifecycleConflictExceptionHandler
  → 409 + ProblemDetails {
        type: …/lifecycle-conflict,
        title: "Lifecycle transition not allowed",
        detail: "Cannot deprecate application currently in state Deprecated.",
        extensions: {
            currentLifecycle: "Deprecated",
            attemptedTransition: "Deprecate",
            sunsetDate: "2026-12-31T23:59:59Z",
            reason: null
        }
      }
```

### 6.5 POST decommission "before-sunset-date" 409

```
... InvalidLifecycleTransitionException(reason: "before-sunset-date")
  → 409 + ProblemDetails {
        ...,
        extensions: {
            currentLifecycle: "Deprecated",
            attemptedTransition: "Decommission",
            sunsetDate: "2026-12-31T23:59:59Z",
            reason: "before-sunset-date"
        }
      }
```

Key invariants pinned by tests (in addition to slice-3 invariants):

- `xmin` mismatch on PUT produces 412 with the **current** version in `extensions.currentVersion` (not the stale one).
- Lifecycle endpoints don't read or emit `If-Match` / `ETag`.
- Decommissioned applications return 409 (not 403) on edit attempts — distinguishes "you can't do this here" from "you don't have permission".

---

## 7. Error handling

Inherits the slice-1/2/3 mapping table; **adds** the rows below.

| Trigger | Status | `type` |
|---|---|---|
| Missing `If-Match` on PUT | 428 | `https://kartova.io/problems/precondition-required` |
| `If-Match` doesn't match current `xmin` | 412 | `https://kartova.io/problems/concurrency-conflict` |
| Lifecycle transition forbidden by ADR-0073 (wrong source state, before-sunset-date, edit on Decommissioned) | 409 | `https://kartova.io/problems/lifecycle-conflict` |
| `displayName`/`description` invariant fails on PUT | 400 | `https://kartova.io/problems/validation-failed` (existing) |
| `sunsetDate` not strictly future on POST `/deprecate` | 400 | `https://kartova.io/problems/validation-failed` (`errors: { sunsetDate: [...] }`) |

ADR-0091 ("no ad-hoc error response shapes") satisfied — every endpoint maps via `Results.Problem(...)` only, and every new `type` URI is registered in `ProblemTypes`.

---

## 8. UI surface

### 8.1 Detail page state-driven affordance matrix

```
[ DisplayName ]  [ name pill ]  [ LifecycleBadge ▾ ]    [ Edit button ]
                                       └─ DropdownMenu (LifecycleMenu)
```

| Lifecycle | Edit button | Badge color | Dropdown items |
|---|---|---|---|
| `Active` | enabled | green (success) | "Deprecate…" (enabled) |
| `Deprecated`, `now < sunsetDate` | enabled | amber (warning) + "Sunset: YYYY-MM-DD" subline | "Decommission" disabled with tooltip "Available after \<sunsetDate\>" |
| `Deprecated`, `now ≥ sunsetDate` | enabled | amber | "Decommission" enabled |
| `Decommissioned` | hidden | gray (muted) | dropdown not rendered (badge non-interactive) |

Server is the authority — every disabled/hidden affordance still has a server-side 409 path.

### 8.2 Cache invalidation on mutation success

Each of the three mutations invalidates:
- `['application', id]` — single-resource cache
- `['applications']` (prefix match) — all cursor-paged list pages, every sort combination

Achieved via `queryClient.invalidateQueries({ queryKey: ['applications'] })` (prefix match) + `queryClient.setQueryData(['application', id], updated)` (warm the detail with the response body).

### 8.3 Error mapping in mutation hooks

| Status | UX |
|---|---|
| 200 | toast success + dialog closes + caches updated |
| 400 (`validation-failed`) | `applyProblemDetailsToForm` → field errors; dialog stays open |
| 400 (`malformed-request`) | toast generic error; dialog stays open |
| 409 (`lifecycle-conflict`, `attemptedTransition: "EditMetadata"`) | toast "This application has been decommissioned and can no longer be edited."; invalidate cache; dialog closes |
| 409 (`lifecycle-conflict`, other) | toast with current state and a "Reload" affordance; invalidate cache; dialog closes |
| 412 (`concurrency-conflict`) | toast "Someone else edited this. Reloaded latest values."; invalidate `['application', id]`; dialog stays open with refreshed pre-fill (form `reset()` to new values) |
| 428 (`precondition-required`) | shouldn't reach the user — SPA always sends If-Match — log + Sentry-style breadcrumb (post-MVP) |
| 401 | global re-auth handler (slice 4 unchanged) |
| 5xx | toast error; dialog stays open |

### 8.4 Date picker UX

- Use Untitled UI `DatePicker`.
- Default `sunsetDate` value: `today + 30 days` (sensible default; users override).
- Min selectable: `tomorrow` (server validates `> now`; UI prevents submission).
- Sends ISO-8601 with explicit timezone: `new Date(year, month, day).toISOString()` resolves to user-local midnight, then UTC-encoded. Cross-TZ off-by-one-day risk captured §13.7.

### 8.5 List page lifecycle column

`ApplicationsTable` adds a Lifecycle column (between `name` and `description`). Renders the same `LifecycleBadge` (compact size). Default sort and cursor pagination unchanged. Filtering by lifecycle is **out of scope** (E-05.F-01.S-02 territory) — Decommissioned rows visible in default views, gray pill is the cue.

---

## 9. Testing

Five layers, mirroring slice 3's pyramid.

### 9.1 Domain unit tests (`Kartova.Catalog.Tests/ApplicationLifecycleTests.cs`)

- `EditMetadata_with_valid_args_updates_fields`
- `EditMetadata_throws_on_empty_displayName`
- `EditMetadata_throws_on_displayName_over_128`
- `EditMetadata_throws_on_empty_description`
- `EditMetadata_does_not_change_Name_or_OwnerUserId_or_TenantId_or_CreatedAt`
- `EditMetadata_on_Decommissioned_throws_InvalidLifecycleTransitionException`
- `Deprecate_with_valid_args_sets_state_and_sunsetDate`
- `Deprecate_throws_on_past_sunsetDate`
- `Deprecate_throws_on_now_sunsetDate` (boundary: must be strictly future)
- `Deprecate_when_already_Deprecated_throws_InvalidLifecycleTransitionException`
- `Deprecate_when_Decommissioned_throws_InvalidLifecycleTransitionException`
- `Decommission_when_Deprecated_and_after_sunsetDate_succeeds`
- `Decommission_when_Deprecated_and_before_sunsetDate_throws_with_reason_before_sunset_date`
- `Decommission_when_Active_throws_InvalidLifecycleTransitionException`
- `Decommission_when_already_Decommissioned_throws_InvalidLifecycleTransitionException`
- `New_application_starts_in_Active_state_with_null_sunsetDate`

Uses `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` for deterministic now-arithmetic.

### 9.2 Architecture tests

- `RestVerbPolicyRules.No_endpoint_uses_PATCH_verb` — pins ADR-0096.
- `LifecycleEnumRules.Lifecycle_has_exactly_three_members_with_explicit_values` — pins enum stability.
- `LifecycleEnumRules.Lifecycle_members_are_linearly_ordered` — `(int)Active < (int)Deprecated < (int)Decommissioned`.
- `EndpointRouteRules` extension — three new named routes (`EditApplication`, `DeprecateApplication`, `DecommissionApplication`) added to inventory; kills `MapPut/MapPost(...) → ;` mutants.

### 9.3 Integration — three new test classes

`EditApplicationTests` (uses `KartovaApiFixtureBase`):
- `PUT_with_valid_payload_returns_200_and_advances_version`
- `PUT_response_ETag_header_matches_Version_field_in_body`
- `PUT_without_If_Match_returns_428`
- `PUT_with_stale_If_Match_returns_412_with_currentVersion`
- `PUT_with_blank_displayName_returns_400_with_field_error`
- `PUT_with_over_length_displayName_returns_400`
- `PUT_with_blank_description_returns_400`
- `PUT_on_Decommissioned_application_returns_409`
- `PUT_for_other_tenants_id_returns_404`
- `PUT_without_token_returns_401`

`DeprecateApplicationTests`:
- `POST_deprecate_with_future_sunsetDate_returns_200_and_sets_lifecycle_and_sunsetDate`
- `POST_deprecate_with_past_sunsetDate_returns_400_with_field_error`
- `POST_deprecate_already_Deprecated_returns_409_with_currentLifecycle_Deprecated`
- `POST_deprecate_Decommissioned_returns_409`
- `POST_deprecate_for_other_tenants_id_returns_404`
- `POST_deprecate_without_token_returns_401`

`DecommissionApplicationTests`:
- `POST_decommission_when_Deprecated_and_past_sunsetDate_returns_200_and_sets_lifecycle_to_Decommissioned`
- `POST_decommission_when_Deprecated_and_before_sunsetDate_returns_409_with_reason_before_sunset_date`
- `POST_decommission_when_Active_returns_409`
- `POST_decommission_when_already_Decommissioned_returns_409`
- `POST_decommission_for_other_tenants_id_returns_404`

### 9.4 SharedKernel.AspNetCore unit tests

- `IfMatchEndpointFilterTests`: parses quoted base64 header, missing → throws `PreconditionRequiredException`, malformed → throws.
- `ConcurrencyConflictExceptionHandlerTests`: maps `DbUpdateConcurrencyException` → 412 ProblemDetails; refreshes currentVersion via the supplied row reader.
- `LifecycleConflictExceptionHandlerTests`: maps `InvalidLifecycleTransitionException` → 409 with all extensions populated.

### 9.5 Frontend Vitest

- `useEditApplication`: PUT request shape, `If-Match` header attached from cached version, success invalidates `['application', id]` + `['applications']`.
- `useDeprecateApplication`, `useDecommissionApplication`: POST shape, success invalidates correct keys.
- `editApplicationSchema`, `deprecateApplicationSchema`: required fields, length bounds, future-date rule.
- `LifecycleMenu`: state-aware item rendering — given `(lifecycle, now, sunsetDate)`, asserts which items render and which are disabled.
- `EditApplicationDialog`: pre-fills, submits, calls mutation, closes on success.
- `applyProblemDetailsToForm`: extended pinning if needed (already covered slice 4).

### 9.6 Mutation testing scope

After merge, run `mutation-sentinel` against:
- `Application.cs` (modified)
- `Lifecycle.cs`, `InvalidLifecycleTransitionException.cs` (new)
- `EditApplicationCommand.cs`, `DeprecateApplicationCommand.cs`, `DecommissionApplicationCommand.cs` + handlers
- `IfMatchEndpointFilter.cs`, `ConcurrencyConflictExceptionHandler.cs`, `LifecycleConflictExceptionHandler.cs`
- `CatalogEndpointDelegates.cs` (modified)

Target ≥80% per `stryker-config.json`. Survivors triaged via `test-generator`.

### 9.7 Test count targets

| Suite | Before | After |
|---|---|---|
| `Catalog.Tests` | ~25 | ~41 (+16 lifecycle/edit unit) |
| `Catalog.IntegrationTests` | ~28 | ~46 (+10 edit + 6 deprecate + 5 decommission ≈ +18) |
| `ArchitectureTests` | ~38 | ~42 (+4: 1 verb policy + 2 enum + 1 endpoint inventory extension) |
| `Kartova.SharedKernel.AspNetCore.Tests` | existing | +6 |
| Vitest (`web/`) | existing | +~14 |

### 9.8 Docker compose smoke (DoD §5)

Slice-3/4 checks plus:

1. PUT `/api/v1/catalog/applications/{id}` (admin@orga JWT, valid If-Match, valid body) → 200 + ETag advanced.
2. PUT same id with the now-stale If-Match → 412.
3. POST `/api/v1/catalog/applications/{id}/deprecate` (sunsetDate=now+1d) → 200 + lifecycle Deprecated.
4. POST `/api/v1/catalog/applications/{id}/decommission` immediately (before sunsetDate) → 409 with reason before-sunset-date.
5. PUT same id (now Deprecated) → 200 (Deprecated still allows edit).
6. POST decommission for an Active app → 409.
7. SPA Playwright MCP run per §8 + screenshots in PR description.

---

## 10. Out of scope (explicit deferrals)

- Audit log of lifecycle transitions (depends on E-01.F-03.S-03 — append-only audit table). §13.1.
- Notifications to dependents on transitions (depends on ADR-0047 / E-06 notification infra). §13.3.
- Admin-only backward transitions: `Deprecated → Active`, `Decommissioned → Deprecated`, admin override on "Decommissioned before sunsetDate" (depends on E-01.F-04.S-03 RBAC). §13.2.
- Successor reference on Deprecated transitions. §13.4.
- Rename `Application.Name` (kebab slug). Identity-stable today; rename is its own future story with redirects.
- Ownership transfer (change `OwnerUserId`). Belongs in E-03 Team aggregate work.
- Filter Decommissioned out of default list views (E-05.F-01.S-02 — Search filters epic).
- Bulk lifecycle operations (E-01.F-06.S-03 — Bulk operations).
- TimeProvider retrofit on `Application.Create` (slice-3 §13.1, separate slice).
- Soft-delete distinct from Decommissioned. Decommissioned is the project's terminal read-only state today; ADR-0019 soft-delete with 30-day purge is a future story for actual deletion.
- BFF cookie-session auth (slice-4 §7 backlog, post-MVP).
- Checked-in Playwright E2E suite (slice-4 §7 backlog, post-slice-4).

---

## 11. Success criteria

1. `dotnet build Kartova.slnx -c Debug` clean (`TreatWarningsAsErrors`).
2. `cd web && npm run build` clean (TS strict, ESLint clean).
3. All architecture tests green, including the 4 new ones (`RestVerbPolicyRules`, 2× `LifecycleEnumRules`, extended `EndpointRouteRules`).
4. All unit tests green: ~16 new domain tests + 6 new SharedKernel.AspNetCore.Tests + ~14 new Vitest.
5. All integration tests green: ~18 new across the three new test classes; existing slice-3/4 suites still green.
6. KeyCloak smoke (`Kartova.Api.IntegrationTests`) still green.
7. `Application` aggregate has `Lifecycle`, `SunsetDate`, `Version` properties + `EditMetadata`/`Deprecate`/`Decommission` methods matching §5.1.
8. Migration `AddApplicationLifecycle` adds the columns and existing rows backfill to `Active` automatically.
9. `Lifecycle` enum has exactly three explicit values (1=Active, 2=Deprecated, 3=Decommissioned), pinned by arch test.
10. ADR-0096 — REST verb policy authored, indexed in `docs/architecture/decisions/README.md`, and pinned by `RestVerbPolicyRules.No_endpoint_uses_PATCH_verb`.
11. SPA `EditApplicationDialog`, `LifecycleMenu`, `LifecycleBadge`, `DeprecateConfirmDialog`, `DecommissionConfirmDialog` render and wire mutations correctly.
12. Cache invalidation on mutation success refreshes detail + list per §8.2.
13. `CHECKLIST.md` reflects S-03, S-04 ticked plus stale-housekeeping ticks for S-06, S-07, F-01.S-04.
14. Docker compose smoke passes the 7 new HTTP + UI checks (§9.8).
15. **DoD per CLAUDE.md** — all nine bullets cited in PR description with command/output evidence:
    1. Full solution + `web/` build clean (warnings-as-errors).
    2. Per-task subagent reviews (spec-compliance + code-quality) executed for every task.
    3. `superpowers:requesting-code-review` invoked at slice boundary against full branch diff.
    4. Full test suite green per (3)–(6) above.
    5. `docker compose up` real-HTTP verification per §9.8 captured in PR description.
    6. `/simplify` against branch diff — findings either fixed or skipped with reason.
    7. `/deep-review` against branch diff — Blocking + Should-fix items resolved; nits triaged.
    8. `mutation-sentinel` per §9.6 hits ≥80%; surviving mutants killed by `test-generator` or accepted with reason.
    9. GitHub Copilot review requested on PR; findings addressed or dismissed with reason.

---

## 12. Implementation order (rough — finalised by writing-plans)

1. **ADR-0096** authored and indexed — first commit.
2. **`xmin` mapping spike** — minimal Testcontainer test asserting EF Core roundtrips Postgres `xmin` as concurrency token + raises `DbUpdateConcurrencyException` on mismatch. Validates the choice before wiring endpoints; if brittleness appears, fall back is an explicit `version BIGINT` column.
3. **`Lifecycle` enum + arch tests** (RED first).
4. **`InvalidLifecycleTransitionException`**.
5. **Domain methods on `Application`** (~16 unit tests RED first).
6. **EF entity config update + migration `AddApplicationLifecycle`**.
7. **`ApplicationResponse` + projection update** + integration test for shape (`Lifecycle`, `SunsetDate`, `Version`, `ETag` header on get-by-id).
8. **`IfMatchEndpointFilter`** + `PreconditionRequiredException` + `PreconditionRequiredExceptionHandler` (and unit tests).
9. **`ConcurrencyConflictExceptionHandler`** (and unit tests).
10. **`LifecycleConflictExceptionHandler`** (and unit tests).
11. **`EditApplicationCommand` + handler + endpoint** + `EditApplicationTests`.
12. **`DeprecateApplicationCommand` + handler + endpoint** + `DeprecateApplicationTests`.
13. **`DecommissionApplicationCommand` + handler + endpoint** + `DecommissionApplicationTests`.
14. **Endpoint inventory arch test extension**.
15. **OpenAPI codegen regenerated**, SPA mutation hooks added (`useEditApplication`, `useDeprecateApplication`, `useDecommissionApplication`).
16. **Schemas** (`editApplicationSchema`, `deprecateApplicationSchema`).
17. **`LifecycleBadge`** extracted; `RegisterApplicationDialog` updated to consume it.
18. **`EditApplicationDialog`** + tests.
19. **`LifecycleMenu`** + tests.
20. **`DeprecateConfirmDialog`**, **`DecommissionConfirmDialog`** + tests.
21. **`ApplicationDetailPage`** wires Edit button + LifecycleMenu + state-driven rendering.
22. **`ApplicationsTable`** lifecycle column.
23. **`docker compose up` real-HTTP verification + Playwright MCP screenshots** (§9.8).
24. **CHECKLIST.md ticks** (S-03, S-04, S-06, S-07, F-01.S-04).
25. **Push, open PR, request reviews** (subagent + `requesting-code-review` + `/simplify` + `/deep-review` + Copilot).

---

## 13. Follow-up slices (registered for future planning)

These items are deliberately out of slice-5 scope but recorded here so they aren't forgotten.

### 13.1 Audit-log retrofit on lifecycle transitions

**Why:** ADR-0073 says transitions are audit-logged (ADR-0018). The append-only audit-log table is E-01.F-03.S-03, currently unbuilt. Slice 5 ships transitions without audit; the retrofit is a thin `IAuditWriter.Record(...)` call inside `DeprecateApplicationHandler`/`DecommissionApplicationHandler`, plus an `Application.LifecycleChanged` domain event that an audit projection consumes asynchronously.

**Scope:**
- `Application.LifecycleChanged` domain event (Wolverine) emitted from each transition.
- Audit-projection handler subscribes and writes to the new audit table.
- Backfill is unnecessary — slice 5's transitions predate audit infra honestly.

**Trigger:** When E-01.F-03.S-03 ships the audit table.

**Effort estimate:** ~half-day on top of audit-log slice.

### 13.2 RBAC retrofit: backward transitions + admin override

**Why:** ADR-0073 allows backward transitions (`Deprecated → Active`, `Decommissioned → Deprecated`) for Org Admins, plus admin override on the "Decommissioned before sunsetDate" rule. RBAC is E-01.F-04.S-03, unbuilt today. Slice 5 forbids backward transitions and the override (returns 400 / 409 respectively).

**Scope:**
- Add `POST /applications/{id}/restore` (Org Admin only) — body `{ targetState: "Active" | "Deprecated" }`.
- Relax `Decommission` sunset-date check when caller has `platform-admin` (or future `org-admin`) claim.
- Audit each admin-action.

**Trigger:** When E-01.F-04.S-03 ships RBAC.

**Effort estimate:** ~half-day. Domain methods on `Application` (`Restore(target)`) added; per-route auth attribute applied.

### 13.3 Notifications retrofit: lifecycle transition events

**Why:** ADR-0073 says transitions trigger notifications to dependents (ADR-0047). Notification infra is E-06, post-slice-5.

**Scope:** Subscribe a notification dispatcher to `Application.LifecycleChanged` (already emitted per §13.1); fan out to dependents discovered via E-04 relationships.

**Trigger:** When E-06 notification infra and E-04 relationship graph are both available.

**Effort estimate:** ~half-day.

### 13.4 Successor reference on Deprecated transitions

**Why:** ADR-0073 says Deprecated entities "MUST include a sunset_date and a successor reference (where applicable)". Slice 5 honors sunset-date but defers successor entirely (no column, no DTO, no UI).

**Scope:**
- Add `successor_application_id UUID NULL` column with FK to `applications.id` and a same-tenant CHECK.
- Domain invariant: successor (when set) must be in the same tenant, must not be the application being deprecated, must not itself be Decommissioned.
- Cascade behavior when successor is later decommissioned (forbid? cascade-null? soft pointer?) — decided in this slice with an ADR addendum.
- Cross-entity successor (Service successor of an Application) — decided when E-04 relationships epic shapes the cross-aggregate model.
- `DeprecateApplicationRequest.SuccessorId?` field.
- SPA: `SuccessorPicker` component (search-select against `GET /applications` filtered by name, same tenant, excludes self, excludes Decommissioned).

**Trigger:** With the slice that gives the field a consumer (notification fan-out E-06 or relationship graph E-04 — whichever ships first). Both consume successor for migration guidance.

**Effort estimate:** ~1 day backend (column + invariant + cascade decision + tests) + ~half-day SPA picker.

### 13.5 TimeProvider retrofit on `Application.Create`

**Why:** Slice-3 §13.1 already registered this. Slice 5 adopts `TimeProvider` for `Deprecate`/`Decommission` because sunset-date arithmetic needs deterministic tests, but doesn't retrofit `Create`. The mixed state is honest only short-term.

**Trigger:** Same as slice-3 §13.1 — anytime after slice 5 merges.

**Effort estimate:** ~1 day. Mechanical migration via `dotnet-test:migrate-static-to-wrapper` skill.

### 13.6 Filter Decommissioned out of default list views

**Why:** ADR-0073 says Decommissioned entities "are filtered out of default views". Slice 5 ships gray pills only (visual cue, no filter). Filter UI lands when the search/filter slice arrives.

**Scope:**
- Default list view excludes `Lifecycle == Decommissioned` rows.
- Query string `?includeDecommissioned=true` opts in.
- Both `GET /applications` (list endpoint) and SPA `ApplicationsTable` adopt the change.

**Trigger:** With E-05.F-01.S-02 (search filters).

**Effort estimate:** ~half-day. Cursor-pagination contract already supports per-resource filter additions; just adds another filter parameter.

### 13.7 Cross-timezone sunset-date UX

**Why:** Sunset date sent as ISO-8601 with explicit timezone (UTC). User picks a calendar date in their browser's timezone; midnight-of-picked-date becomes UTC ISO. Cross-TZ users may experience off-by-one-day surprises if they expect "end of business day in tenant region" semantics.

**Trigger:** First user-reported off-by-one-day surprise. Or when MiFID II compliance flag (E-01.F-05.S-02) introduces tenant-region awareness.

**Effort estimate:** ~half-day. Likely "end of business day in tenant region" with a per-tenant `Region` setting.

---

## 14. Self-review

**Spec coverage check:** Every decision in §3 traces to a section in §4–§9. Every success criterion in §11 traces to a decision and a test.

**Placeholder scan:** No "TBD" or "TODO" tokens. Code blocks in §5 are illustrative; final code lands in writing-plans / executing-plans.

**Type / contract consistency:**

- `Lifecycle` enum (3 values, explicit ints) consistent across §4.2, §5.1, §5.2, §5.5, §9.1, §9.2.
- `InvalidLifecycleTransitionException` shape consistent across §4.3, §5.1, §6.4, §6.5, §9.4.
- `ApplicationResponse` shape consistent across §5.5, §6.1, §6.3, §8 (cache).
- `Version` wire encoding (base64 of `xmin` `uint`) consistent across §5.4 (`IfMatchEndpointFilter`), §5.5 (`ApplicationResponse`), §6.1 (happy path), §6.2 (412 path), §9.4 (filter test).
- `If-Match` / `ETag` semantics consistent: edit only, lifecycle endpoints don't take it (§3 #7, §6.3, §7).
- Sunset-date strict-greater-than (not ≥) consistent between §4.2, §5.1 (`Deprecate`), §9.1.
- Decommission sunset-date check uses `≥` (`now >= SunsetDate`) consistent between §4.2, §5.1, §9.1, §9.3.

**Scope check:** Single PR. ~25 new backend files (3 commands+handlers, 2 contracts DTOs, 4 exception handlers / filters, 1 migration, 1 enum, 1 domain exception, 4 test classes, 2 arch tests, 1 ADR) + ~12 new frontend files (5 components, 2 schemas, 5 test files) + ~13 modified files. Comparable to slice-3 + slice-4 combined; larger than either alone but covers backend + UI in one cohesive vertical slice. Not too large for one PR — slice 4 was a similar mixed-stack scope.

**Ambiguity check:**

- "match the existing convention" for endpoint delegate file layout (§4.4) — intentional, resolved at implementation time.
- "sensible default" for `sunsetDate` picker (today+30) (§8.4) — calibrated guess; will revisit if user testing shows otherwise.

**Internal consistency:**
- §3 Decision #7 says lifecycle endpoints don't emit ETag. §5.5 (`ApplicationResponse` carries `Version` field but only the edit endpoint sets the `ETag` response header) confirms it. §6.3 (deprecate flow doesn't read or emit ETag) confirms it. §7 error table lists `concurrency-conflict` only on edit. Consistent.
- §3 Decision #12 says edit on Decommissioned returns 409. §5.1 (`EditMetadata`) checks `Lifecycle == Decommissioned` and throws. §9.1 (`EditMetadata_on_Decommissioned_throws`) tests it. §9.3 (`PUT_on_Decommissioned_application_returns_409`) tests the wire path. Consistent.

**No issues found.**
