# Catalog API Entity (E-02.F-03.S-01) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the sync `Api` catalog entity end-to-end (POST/GET-by-id/cursor-list), as a first-class sibling aggregate to `Application`/`Service`.

**Architecture:** Faithful copy of the shipped Service slice inside the existing Catalog module — team-owned aggregate (ADR-0103), tenant-scoped DbContext (ADR-0090), direct-dispatch handlers (ADR-0093), cursor pagination (ADR-0095), fail-closed in-transaction audit, RLS-enforced table. API node only; all relational/derived layers are follow-ups (spec §11, ADR-0111).

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs, EF Core + Npgsql, PostgreSQL RLS, MSTest v4 + NSubstitute, Testcontainers real-seam integration tests.

**Spec:** `docs/superpowers/specs/2026-07-03-catalog-api-entity-design.md` · **ADR:** ADR-0111.

## Global Constraints

- **Build:** `dotnet build Kartova.slnx` with `TreatWarningsAsErrors=true` → 0 warnings / 0 errors. Windows: run `dotnet` via `cmd //c "..."` or PowerShell.
- **Line endings:** LF (repo `.gitattributes` normalizes; do not introduce CRLF).
- **Tests:** MSTest v4, native `Assert.*` (no FluentAssertions), NSubstitute. Integration tests hit the **real seam** — real `JwtBearer` + real Postgres/RLS via `KartovaApiFixtureBase` / `CatalogIntegrationTestBase` (`Fx`).
- **Contracts/DTOs** carry `[ExcludeFromCodeCoverage]` (enforced by `ContractsCoverageRules`).
- **Enums** serialize as camelCase strings on the wire (ADR-0109).
- **Tenant scope:** DbContext already registered via `AddModuleDbContext<CatalogDbContext>` (ADR-0090); handlers are direct-dispatch (ADR-0093); tenant id + created-by come from `ITenantContext`/`ICurrentUser`, never the payload; owning `TeamId` required + validated (ADR-0103).
- **RLS:** every catalog table gets `ENABLE` + `FORCE ROW LEVEL SECURITY` + a `tenant_isolation` policy on `current_setting('app.current_tenant_id')::uuid` (hand-added in the migration).
- **Pagination:** ADR-0095 — list endpoints expose `sortBy`/`sortOrder`/`cursor`/`limit` and return `CursorPage<T>`. **Filters are out of scope this slice** (FU-9).
- **Permissions:** adding one requires the 5-sync (C# const + `All`, C# role map, `permissions.snapshot.json`, `permissions.ts`, `usePermissions` OrgAdmin mock) — Task 2.
- **Naming:** aggregate type `Api`; the API version string property is `Version`; the Postgres `xmin` concurrency token maps to a property named **`Xmin`** (not `Version`).
- **Commits:** frequent, one per task minimum. Multi-line messages via PowerShell + multiple `-m` flags. End with the `Co-Authored-By` trailer.

---

## Impact Analysis (codelens/LSP)

**Method:** roslyn-codelens `find_references` (solution `Kartova.slnx` auto-loaded), run 2026-07-03. Arch-test project + the TypeScript frontend are outside the codelens solution graph, so those consumers are grep-supplemented (marked). This slice changes **no existing C# signature or behavior** — it adds new files + additive members; the table below is the fan-out of those additive members.

| Changed symbol | Change | Tool run | Callers / refs | Notable call sites | Covered by task |
|----------------|--------|----------|----------------|--------------------|-----------------|
| `KartovaPermissions.All` | add `CatalogApisRegister` member | `find_references` | 1 (codelens) + arch tests (grep) | `AuthorizationExtensions.cs:15` — iterates `All`, auto-registers one auth policy per permission (so `.RequireAuthorization("catalog.apis.register")` needs no extra code); `KartovaPermissionsRules.cs:22,26,88` — All-vs-declared exact-match + web-snapshot equivalence (grep) | Task 2 |
| `KartovaRolePermissions` (Member/OrgAdmin sets) | add `CatalogApisRegister` grant | `find_references` on `.ForRole` | 8 | prod: `TenantClaimsTransformation.cs:70` (JWT permission claims), `SessionStartHandler.cs:119` (/me/permissions); tests `TenantClaimsTransformationTests` / `SessionStartHandlerTests` derive expected **from `ForRole`** (equivalence) → self-track, no edits | Task 2 |
| `CatalogAuditActions` | add `ApiRegistered` const + `CatalogAuditTargetTypes.Api` | `find_references` | 9 | all existing usages are unrelated handlers (`RegisterServiceHandler.cs:38`, …) — additive, none break; only the new `RegisterApiHandler` consumes the new const | Tasks 3, 5 |
| `CatalogDbContext` | add `DbSet<Api> Apis` + `ApplyConfiguration` | n/a (additive) | — | `OnModelCreating` | Task 4 |
| `CatalogEndpointDelegates` / `CatalogModule` / `EndpointResultExtensions` | add delegates, routes, `ApiNotFound()` | n/a (additive) | — | — | Tasks 5, 6 |

**Blast-radius notes:** No consumer of an existing symbol changes behavior. `KartovaRolePermissions.ForRole` fan-out is the *intended* effect (Member/OrgAdmin JWTs + /me/permissions gain `catalog.apis.register`); its two production consumers need no edits, and the equivalence-style unit tests track the map automatically. The only *hardcoded* mirrors of the permission set — the arch-test snapshot check and the frontend `usePermissions` OrgAdmin mock — are covered by Task 2. `CatalogPermissionMatrixTests` enumerates endpoints in a hardcoded array (Task 8 adds the 3 API rows).

**Coverage check:** every reference above is handled — Task 2 (permission fan-out + arch/frontend mirrors), Tasks 3/5 (audit const), Task 8 (matrix). No gaps.

---

## Task 1: Domain — `ApiId`, `ApiStyle`, `Api` aggregate

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/ApiId.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/ApiStyle.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/Api.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ApiTests.cs`

**Interfaces:**
- Produces: `Api.Create(string displayName, string description, ApiStyle style, string version, string? specUrl, Guid createdByUserId, Guid teamId, TenantId tenantId, TimeProvider clock)` and an explicit-`DateTimeOffset createdAt` overload. Properties: `Id : ApiId`, `TenantId`, `DisplayName`, `Description`, `Style : ApiStyle`, `Version : string`, `SpecUrl : string?`, `TeamId`, `CreatedByUserId`, `CreatedAt`, `Xmin : uint`. `ApiStyle { Rest, Grpc, GraphQL }`. `readonly record struct ApiId(Guid Value)` with `New()`.

- [ ] **Step 1: Write the failing test** — `src/Modules/Catalog/Kartova.Catalog.Tests/ApiTests.cs`

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

[TestClass]
public class ApiTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());
    private static readonly Guid Creator = Guid.NewGuid();
    private static readonly Guid Team = Guid.NewGuid();
    private static readonly FakeTimeProvider Clock = new(DateTimeOffset.Parse("2026-07-03T10:00:00Z"));

    private static Api Create(
        string name = "orders-api", string desc = "Orders REST API.", ApiStyle style = ApiStyle.Rest,
        string version = "v1", string? specUrl = "https://specs.example.com/orders/openapi.json",
        Guid? creator = null, Guid? team = null)
        => Api.Create(name, desc, style, version, specUrl, creator ?? Creator, team ?? Team, Tenant, Clock);

    [TestMethod]
    public void Create_with_valid_args_sets_fields()
    {
        var a = Create();
        Assert.AreEqual("orders-api", a.DisplayName);
        Assert.AreEqual("Orders REST API.", a.Description);
        Assert.AreEqual(ApiStyle.Rest, a.Style);
        Assert.AreEqual("v1", a.Version);
        Assert.AreEqual("https://specs.example.com/orders/openapi.json", a.SpecUrl);
        Assert.AreEqual(Creator, a.CreatedByUserId);
        Assert.AreEqual(Team, a.TeamId);
        Assert.AreEqual(Tenant, a.TenantId);
        Assert.AreEqual(Clock.GetUtcNow(), a.CreatedAt);
        Assert.AreNotEqual(Guid.Empty, a.Id.Value);
    }

    [TestMethod]
    public void Create_allows_null_spec_url()
    {
        var a = Create(specUrl: null);
        Assert.IsNull(a.SpecUrl);
    }

    [TestMethod]
    public void Create_generates_fresh_id_each_call() =>
        Assert.AreNotEqual(Create().Id.Value, Create().Id.Value);

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_display_name(string name) =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(name: name));

    [TestMethod]
    public void Create_throws_on_display_name_over_128() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(name: new string('x', 129)));

    [TestMethod]
    public void Create_accepts_display_name_of_exactly_128() =>
        Assert.AreEqual(128, Create(name: new string('x', 128)).DisplayName.Length);

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_description(string desc) =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(desc: desc));

    [TestMethod]
    public void Create_throws_on_description_over_4096() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(desc: new string('x', 4097)));

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_throws_on_empty_version(string version) =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(version: version));

    [TestMethod]
    public void Create_throws_on_version_over_64() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(version: new string('9', 65)));

    [TestMethod]
    public void Create_throws_on_undefined_style() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(style: (ApiStyle)999));

    [TestMethod]
    public void Create_throws_on_relative_spec_url() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(specUrl: "/openapi.json"));

    [TestMethod]
    public void Create_throws_on_spec_url_over_2048() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(specUrl: "https://x.example.com/" + new string('a', 2048)));

    [TestMethod]
    public void Create_throws_on_empty_created_by() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(creator: Guid.Empty));

    [TestMethod]
    public void Create_throws_on_empty_team() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(team: Guid.Empty));

    [TestMethod]
    public void Create_with_null_TimeProvider_throws()
    {
        TimeProvider? nullClock = null;
        Assert.ThrowsExactly<ArgumentNullException>(
            () => Api.Create("a", "d", ApiStyle.Rest, "v1", null, Creator, Team, Tenant, nullClock!));
    }
}
```

- [ ] **Step 2: Run the test — verify it fails to compile** (`Api`/`ApiStyle` do not exist yet)

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -v q"`
Expected: build error — `Api`/`ApiStyle` not found.

- [ ] **Step 3: Create `ApiId.cs`**

```csharp
namespace Kartova.Catalog.Domain;

public readonly record struct ApiId(Guid Value)
{
    public static ApiId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
```

- [ ] **Step 4: Create `ApiStyle.cs`**

```csharp
namespace Kartova.Catalog.Domain;

/// <summary>Synchronous API style (ADR-0111). Async styles are a separate entity (E-02.F-03.S-02).</summary>
public enum ApiStyle
{
    Rest,
    Grpc,
    GraphQL,
}
```

- [ ] **Step 5: Create `Api.cs`**

```csharp
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

/// <summary>
/// Sync API catalog entity (ADR-0111). First-class, tenant-owned, team-owned. The
/// provider/instance links (implementedByApplicationId / Service.applicationId) and
/// derived exposure are follow-ups (spec §11) — this aggregate is the node only.
/// <c>Version</c> is the API version string (domain); the Postgres <c>xmin</c> concurrency
/// token maps to <c>Xmin</c> (renamed to avoid colliding with the domain Version field).
/// </summary>
public sealed class Api : ITenantOwned, ITeamScopedResource
{
    private Guid _id;   // plain-Guid PK backing field (same pattern as Application/Service)

    public ApiId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public ApiStyle Style { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public string? SpecUrl { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public uint Xmin { get; private set; }

    Guid? ITeamScopedResource.TeamId => TeamId;

    private Api() { }   // EF

    private Api(
        ApiId id, TenantId tenantId, string displayName, string description, ApiStyle style,
        string version, string? specUrl, Guid createdByUserId, Guid teamId, DateTimeOffset createdAt)
    {
        _id = id.Value;
        TenantId = tenantId;
        DisplayName = displayName;
        Description = description;
        Style = style;
        Version = version;
        SpecUrl = specUrl;
        CreatedByUserId = createdByUserId;
        TeamId = teamId;
        CreatedAt = createdAt;
    }

    public static Api Create(
        string displayName, string description, ApiStyle style, string version, string? specUrl,
        Guid createdByUserId, Guid teamId, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        return Create(displayName, description, style, version, specUrl, createdByUserId, teamId, tenantId, clock.GetUtcNow());
    }

    /// <summary>Overload taking an explicit <paramref name="createdAt"/> — for seed/test fixtures.</summary>
    public static Api Create(
        string displayName, string description, ApiStyle style, string version, string? specUrl,
        Guid createdByUserId, Guid teamId, TenantId tenantId, DateTimeOffset createdAt)
    {
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        if (!Enum.IsDefined(style))
            throw new ArgumentException("Unknown API style.", nameof(style));
        ValidateVersion(version);
        ValidateSpecUrl(specUrl);
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId is required.", nameof(createdByUserId));
        if (teamId == Guid.Empty)
            throw new ArgumentException("teamId is required.", nameof(teamId));

        return new Api(ApiId.New(), tenantId, displayName, description, style, version, specUrl, createdByUserId, teamId, createdAt);
    }

    private static void ValidateDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("API display name must not be empty.", nameof(displayName));
        if (displayName.Length > 128)
            throw new ArgumentException("API display name must be <= 128 characters.", nameof(displayName));
    }

    private static void ValidateDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("API description must not be empty.", nameof(description));
        if (description.Length > 4096)
            throw new ArgumentException("API description must be <= 4096 characters.", nameof(description));
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("API version must not be empty.", nameof(version));
        if (version.Length > 64)
            throw new ArgumentException("API version must be <= 64 characters.", nameof(version));
    }

    private static void ValidateSpecUrl(string? specUrl)
    {
        if (specUrl is null) return;
        if (specUrl.Length > 2048)
            throw new ArgumentException("API spec URL must be <= 2048 characters.", nameof(specUrl));
        // Strict absolute URI with host (spec URLs are real fetchable links — unlike the
        // relaxed ServiceEndpoint address rule granted by ADR-0111 §Decision 6, FU-4).
        if (!Uri.TryCreate(specUrl, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Authority))
            throw new ArgumentException("API spec URL must be an absolute URI with a host.", nameof(specUrl));
    }
}
```

- [ ] **Step 6: Run the test — verify it passes**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -v q"`
Expected: PASS (all `ApiTests` green).

- [ ] **Step 7: Commit**

```
git add src/Modules/Catalog/Kartova.Catalog.Domain/ApiId.cs src/Modules/Catalog/Kartova.Catalog.Domain/ApiStyle.cs src/Modules/Catalog/Kartova.Catalog.Domain/Api.cs src/Modules/Catalog/Kartova.Catalog.Tests/ApiTests.cs
git commit -m "feat(catalog): add Api domain aggregate (E-02.F-03.S-01)"
```

---

## Task 2: Permission `catalog.apis.register` + role map + 5-sync

**Files:**
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs`
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs`
- Modify: `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs`
- Modify: `web/src/shared/auth/permissions.snapshot.json`
- Modify: `web/src/shared/auth/permissions.ts`
- Modify: `web/src/shared/auth/__tests__/usePermissions.test.tsx`

**Interfaces:**
- Produces: `KartovaPermissions.CatalogApisRegister = "catalog.apis.register"`, granted to Member + OrgAdmin. Consumed by Task 6 route (`.RequireAuthorization(KartovaPermissions.CatalogApisRegister)`) and Task 8 matrix.

- [ ] **Step 1: Write failing role tests** — append to `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs` (before the closing brace)

```csharp
    [TestMethod]
    public void Member_and_OrgAdmin_can_register_apis_but_Viewer_cannot()
    {
        Assert.IsTrue(KartovaRolePermissions.ForRole(KartovaRoles.Member).Contains(KartovaPermissions.CatalogApisRegister));
        Assert.IsTrue(KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin).Contains(KartovaPermissions.CatalogApisRegister));
        Assert.IsFalse(KartovaRolePermissions.ForRole(KartovaRoles.Viewer).Contains(KartovaPermissions.CatalogApisRegister));
    }

    [TestMethod]
    public void CatalogApisRegister_is_in_the_All_set() =>
        Assert.IsTrue(KartovaPermissions.All.Contains(KartovaPermissions.CatalogApisRegister));
```

- [ ] **Step 2: Run — verify compile failure** (`CatalogApisRegister` undefined)

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Tests -v q"`
Expected: build error — `CatalogApisRegister` not found.

- [ ] **Step 3: Add the const + `All` entry** — `KartovaPermissions.cs`

Add after the `CatalogServicesRegister` const (line 13):
```csharp
    public const string CatalogApisRegister = "catalog.apis.register";
```
Add to the `All` initializer, immediately after `CatalogServicesRegister,`:
```csharp
        CatalogApisRegister,
```

- [ ] **Step 4: Grant to Member + OrgAdmin** — `KartovaRolePermissions.cs`

In the `[KartovaRoles.Member]` array, after `KartovaPermissions.CatalogServicesRegister,`:
```csharp
                KartovaPermissions.CatalogApisRegister,
```
In the `[KartovaRoles.OrgAdmin]` array, after `KartovaPermissions.CatalogServicesRegister,`:
```csharp
                KartovaPermissions.CatalogApisRegister,
```

- [ ] **Step 5: Sync the frontend mirrors**

`web/src/shared/auth/permissions.snapshot.json` — add after `"catalog.services.register",`:
```json
  "catalog.apis.register",
```

`web/src/shared/auth/permissions.ts` — add after the `CatalogServicesRegister` line:
```ts
  CatalogApisRegister: "catalog.apis.register",
```

`web/src/shared/auth/__tests__/usePermissions.test.tsx` — in the OrgAdmin mock `permissions` array (the "returns OrgAdmin set with all permissions" test), add after `"catalog.services.register",`:
```ts
          "catalog.apis.register",
```

- [ ] **Step 6: Run C# tests — verify pass**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Tests tests/Kartova.ArchitectureTests -v q"`
Expected: PASS — new role tests + `KartovaPermissionsRules` (All-vs-declared, snapshot equivalence, orphan-permission-in-a-role) all green.

- [ ] **Step 7: Run frontend permission tests — verify pass**

Run: `cmd //c "cd web && npm run test -- usePermissions permissions"` (or the repo's vitest invocation)
Expected: PASS — `usePermissions` OrgAdmin loop + the `permissions.ts` snapshot drift guard green.

- [ ] **Step 8: Commit**

```
git add src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs web/src/shared/auth/permissions.snapshot.json web/src/shared/auth/permissions.ts web/src/shared/auth/__tests__/usePermissions.test.tsx
git commit -m "feat(catalog): add catalog.apis.register permission (Member+OrgAdmin) + 5-sync"
```

---

## Task 3: Contracts + Application layer + audit action

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/RegisterApiRequest.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/ApiResponse.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/ApiSortField.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/RegisterApiCommand.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/GetApiByIdQuery.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/ListApisQuery.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/ApiResponseExtensions.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs`

**Interfaces:**
- Produces: `RegisterApiRequest`, `ApiResponse` (+ `CreatedBy`), `ApiSortField { DisplayName, Style, Version, CreatedAt }`, `RegisterApiCommand`, `GetApiByIdQuery(Guid Id)`, `ListApisQuery(ApiSortField, SortOrder, string? Cursor, int Limit)`, `Api.ToResponse()`, `CatalogAuditActions.ApiRegistered = "api.registered"`, `CatalogAuditTargetTypes.Api = "Api"`. Consumed by Tasks 5, 6, 7, 8.

- [ ] **Step 1: Create `ApiSortField.cs`**

```csharp
namespace Kartova.Catalog.Contracts;

/// <summary>Public sort-field allowlist for <c>GET /api/v1/catalog/apis</c> (ADR-0095).
/// Sortable on every displayed column (spec §3 #14).</summary>
public enum ApiSortField
{
    DisplayName,
    Style,
    Version,
    CreatedAt,
}
```

- [ ] **Step 2: Create `RegisterApiRequest.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record RegisterApiRequest(
    string DisplayName,
    string Description,
    ApiStyle Style,
    string Version,
    string? SpecUrl,
    Guid TeamId);
```

- [ ] **Step 3: Create `ApiResponse.cs`**

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel;

namespace Kartova.Catalog.Contracts;

/// <summary>API response for a single catalog API entity. <see cref="CreatedBy"/> is
/// enriched by the read handlers via <c>IUserDirectory</c> (mirrors ServiceResponse);
/// write-path handlers leave it null. No concurrency-token field this slice (no edit
/// endpoint) — <see cref="Version"/> is the API version string, not the xmin token.</summary>
[ExcludeFromCodeCoverage]
public sealed record ApiResponse(
    Guid Id,
    Guid TenantId,
    string DisplayName,
    string Description,
    ApiStyle Style,
    string Version,
    string? SpecUrl,
    Guid TeamId,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAt)
{
    public UserDisplayInfo? CreatedBy { get; init; }
}
```

- [ ] **Step 4: Create `RegisterApiCommand.cs`**

```csharp
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Register a new <see cref="Api"/> in the current tenant. Tenant id + created-by
/// come from request context (ADR-0090); <c>TeamId</c> is the required owning team (ADR-0103),
/// validated by the delegate before dispatch.</summary>
public sealed record RegisterApiCommand(
    string DisplayName,
    string Description,
    ApiStyle Style,
    string Version,
    string? SpecUrl,
    Guid TeamId);
```

- [ ] **Step 5: Create `GetApiByIdQuery.cs`**

```csharp
namespace Kartova.Catalog.Application;

/// <summary>Fetch one Api by id within the current tenant scope (RLS-filtered).</summary>
public sealed record GetApiByIdQuery(Guid Id);
```

- [ ] **Step 6: Create `ListApisQuery.cs`** (filter-free this slice — filters are FU-9)

```csharp
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Application;

/// <summary>List APIs visible to the current tenant (RLS-filtered), cursor-paginated
/// (ADR-0095). No attribute filters this slice — style/team filtering is deferred to the
/// API-UI slice (spec §11 FU-9).</summary>
public sealed record ListApisQuery(
    ApiSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit);
```

- [ ] **Step 7: Create `ApiResponseExtensions.cs`**

```csharp
using Kartova.Catalog.Contracts;

namespace Kartova.Catalog.Application;

public static class ApiResponseExtensions
{
    public static ApiResponse ToResponse(this Kartova.Catalog.Domain.Api api) =>
        new(
            api.Id.Value,
            api.TenantId.Value,
            api.DisplayName,
            api.Description,
            api.Style,
            api.Version,
            api.SpecUrl,
            api.TeamId,
            api.CreatedByUserId,
            api.CreatedAt);
}
```

- [ ] **Step 8: Add the audit constants** — `CatalogAuditActions.cs`

Add after `ServiceRegistered`:
```csharp
    public const string ApiRegistered = "api.registered";
```
Add to `CatalogAuditTargetTypes`, after `Service`:
```csharp
    public const string Api = "Api";
```

- [ ] **Step 9: Build — verify compile**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Application -v q"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 10: Commit**

```
git add src/Modules/Catalog/Kartova.Catalog.Contracts/RegisterApiRequest.cs src/Modules/Catalog/Kartova.Catalog.Contracts/ApiResponse.cs src/Modules/Catalog/Kartova.Catalog.Contracts/ApiSortField.cs src/Modules/Catalog/Kartova.Catalog.Application/RegisterApiCommand.cs src/Modules/Catalog/Kartova.Catalog.Application/GetApiByIdQuery.cs src/Modules/Catalog/Kartova.Catalog.Application/ListApisQuery.cs src/Modules/Catalog/Kartova.Catalog.Application/ApiResponseExtensions.cs src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs
git commit -m "feat(catalog): Api contracts, application queries, and api.registered audit action"
```

---

## Task 4: EF configuration + DbContext + migration (RLS)

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApiConfiguration.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<timestamp>_AddApis.cs` (+ `.Designer.cs` + updated `CatalogDbContextModelSnapshot.cs`)

**Interfaces:**
- Produces: `catalog_apis` table (RLS-forced), `CatalogDbContext.Apis`, `EfApiConfiguration.IdFieldName`. Consumed by Task 5.

- [ ] **Step 1: Create `EfApiConfiguration.cs`**

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

public sealed class EfApiConfiguration : IEntityTypeConfiguration<Api>
{
    internal const string IdFieldName = "_id";

    public void Configure(EntityTypeBuilder<Api> b)
    {
        b.ToTable("catalog_apis");

        b.Property<Guid>(IdFieldName)
            .HasField(IdFieldName)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasKey(IdFieldName);
        b.Ignore(x => x.Id);

        b.Property(x => x.TenantId)
            .HasConversion(v => v.Value, v => new TenantId(v))
            .HasColumnName("tenant_id")
            .IsRequired();
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_catalog_apis_tenant_id");

        b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.DisplayName })
            .HasDatabaseName("ix_catalog_apis_tenant_id_display_name");

        b.Property(x => x.Description).HasColumnName("description").HasMaxLength(4096).IsRequired();

        b.Property(x => x.Style)
            .HasColumnName("style")
            .HasColumnType("smallint")
            .HasConversion<short>()
            .IsRequired();

        b.Property(x => x.Version).HasColumnName("version").HasMaxLength(64).IsRequired();
        b.Property(x => x.SpecUrl).HasColumnName("spec_url").HasMaxLength(2048);

        b.Property(x => x.TeamId).HasColumnName("team_id").IsRequired();
        b.HasIndex(x => x.TeamId).HasDatabaseName("idx_catalog_apis_team");
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.Property(x => x.Xmin)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsRowVersion()
            .IsConcurrencyToken();
    }
}
```

- [ ] **Step 2: Register in `CatalogDbContext.cs`**

Add the DbSet after `Services`:
```csharp
    public DbSet<Kartova.Catalog.Domain.Api> Apis => Set<Kartova.Catalog.Domain.Api>();
```
Add in `OnModelCreating`, after `ApplyConfiguration(new EfServiceConfiguration())`:
```csharp
        modelBuilder.ApplyConfiguration(new EfApiConfiguration());
```

- [ ] **Step 3: Generate the migration**

Generate it the same way the `AddServices` catalog migration was created for this repo (EF tools with `Kartova.Catalog.Infrastructure` as the migrations project). Typical:
```
cmd //c "dotnet ef migrations add AddApis --project src/Modules/Catalog/Kartova.Catalog.Infrastructure --startup-project src/Kartova.Migrator"
```
If the EF tooling isn't wired locally, hand-author the three files mirroring `Migrations/20260620083703_AddServices*.cs` (copy the Designer + update `CatalogDbContextModelSnapshot.cs` for the `Api` entity).

- [ ] **Step 4: Add the RLS SQL to the generated `Up`/`Down`** (EF does not emit RLS — same as `AddServices`)

The `Up` table create should produce columns `id, tenant_id, display_name, description, style (smallint), version, spec_url (nullable), team_id, created_by_user_id, created_at, xmin` + the three indexes. Then append inside `Up` (after `CreateIndex` calls):
```csharp
            migrationBuilder.Sql(@"
ALTER TABLE catalog_apis ENABLE ROW LEVEL SECURITY;
ALTER TABLE catalog_apis FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON catalog_apis
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
");
```
And at the **start** of `Down` (before `DropTable`):
```csharp
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON catalog_apis;
ALTER TABLE catalog_apis DISABLE ROW LEVEL SECURITY;
");
```

- [ ] **Step 5: Build — verify migration compiles + snapshot updated**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure -v q"`
Expected: 0 warnings, 0 errors; `CatalogDbContextModelSnapshot.cs` now contains the `Api` entity.

- [ ] **Step 6: Commit**

```
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApiConfiguration.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs "src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/*AddApis*" src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/CatalogDbContextModelSnapshot.cs
git commit -m "feat(catalog): catalog_apis table + EF mapping + RLS migration"
```

---

## Task 5: Handlers + sort specs + DI registration

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApiSortSpecs.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterApiHandler.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetApiByIdHandler.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApisHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (DI only in this task)

**Interfaces:**
- Consumes: Task 3 contracts/commands, Task 4 `CatalogDbContext.Apis`.
- Produces: `RegisterApiHandler.Handle(cmd, db, tenant, user, audit, ct) → ApiResponse`; `GetApiByIdHandler.Handle(query, db, ct) → ApiResponse?`; `ListApisHandler.Handle(query, db, ct) → CursorPage<ApiResponse>`; `ApiSortSpecs.{Resolve, AllowedFieldNames, IdSelector, IdEquals}`. Consumed by Task 6.

- [ ] **Step 1: Create `ApiSortSpecs.cs`**

```csharp
using System.Linq.Expressions;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Pagination;
using Microsoft.EntityFrameworkCore;
using DomainApi = Kartova.Catalog.Domain.Api;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Per-resource sort allowlist for the APIs list endpoint (ADR-0095 §5).
/// Sortable on every displayed column (spec §3 #14).</summary>
internal static class ApiSortSpecs
{
    public static readonly Expression<Func<DomainApi, Guid>> IdSelector =
        x => EF.Property<Guid>(x, EfApiConfiguration.IdFieldName);

    public static readonly SortSpec<DomainApi> DisplayName = new("displayName", x => x.DisplayName);
    public static readonly SortSpec<DomainApi> Style = new("style", x => x.Style);
    public static readonly SortSpec<DomainApi> Version = new("version", x => x.Version);
    public static readonly SortSpec<DomainApi> CreatedAt = new("createdAt", x => x.CreatedAt);

    public static readonly IReadOnlyList<string> AllowedFieldNames =
        [DisplayName.FieldName, Style.FieldName, Version.FieldName, CreatedAt.FieldName];

    public static Expression<Func<DomainApi, bool>> IdEquals(Guid id) =>
        x => EF.Property<Guid>(x, EfApiConfiguration.IdFieldName) == id;

    public static SortSpec<DomainApi> Resolve(ApiSortField field) => field switch
    {
        ApiSortField.DisplayName => DisplayName,
        ApiSortField.Style => Style,
        ApiSortField.Version => Version,
        ApiSortField.CreatedAt => CreatedAt,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
```

> If `SortSpec<T>` requires a uniform key type, both `x => x.Style` (enum) and `x => x.Version` (string) must be expressible under the same `SortSpec<DomainApi>`. `ServiceSortSpecs` already mixes `string` (DisplayName) and `DateTimeOffset` (CreatedAt) in one `SortSpec<DomainService>`, so the key is boxed/`object` — enum + string are fine. If a compile error appears, mirror the exact `SortSpec` construction used in `ServiceSortSpecs.cs`.

- [ ] **Step 2: Create `RegisterApiHandler.cs`**

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Direct-dispatch handler for <see cref="RegisterApiCommand"/> (ADR-0093).
/// Tenant id + created-by come from context; the owning team id is validated by the
/// delegate before dispatch. Audit row written in-transaction (fail-closed).</summary>
public sealed class RegisterApiHandler
{
    private readonly TimeProvider _clock;

    public RegisterApiHandler(TimeProvider clock) => _clock = clock;

    public async Task<ApiResponse> Handle(
        RegisterApiCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var api = Api.Create(
            cmd.DisplayName, cmd.Description, cmd.Style, cmd.Version, cmd.SpecUrl,
            user.UserId, cmd.TeamId, tenant.Id, _clock);

        db.Apis.Add(api);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.ApiRegistered,
            CatalogAuditTargetTypes.Api,
            api.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = api.DisplayName,
                ["style"] = api.Style.ToString(),
                ["version"] = api.Version,
                ["teamId"] = api.TeamId.ToString(),
            }), ct);

        return api.ToResponse();
    }
}
```

- [ ] **Step 3: Create `GetApiByIdHandler.cs`**

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="GetApiByIdQuery"/>. Returns null when the row is
/// invisible in the current tenant scope (RLS auto-filters). Enriches <c>CreatedBy</c>
/// via <see cref="IUserDirectory"/> (mirrors GetServiceByIdHandler).</summary>
public sealed class GetApiByIdHandler(IUserDirectory directory)
{
    public async Task<ApiResponse?> Handle(GetApiByIdQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var api = await db.Apis.FirstOrDefaultAsync(ApiSortSpecs.IdEquals(q.Id), ct);
        if (api is null) return null;

        var creator = await directory.GetAsync(api.CreatedByUserId, ct);
        return api.ToResponse() with { CreatedBy = creator };
    }
}
```

- [ ] **Step 4: Create `ListApisHandler.cs`** (no filter block — FU-9)

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using DomainApi = Kartova.Catalog.Domain.Api;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Handler for <see cref="ListApisQuery"/>. RLS scopes the result set; keyset
/// pagination via ToCursorPagedAsync (ADR-0095). Each page row is enriched with the creator
/// display name in one batched IUserDirectory round trip (mirrors ListServicesHandler).
/// No attribute filters this slice (FU-9).</summary>
public sealed class ListApisHandler(IUserDirectory directory)
{
    private static readonly Func<DomainApi, Guid> IdExtractor = x => x.Id.Value;

    public async Task<CursorPage<ApiResponse>> Handle(ListApisQuery q, CatalogDbContext db, CancellationToken ct)
    {
        var spec = ApiSortSpecs.Resolve(q.SortBy);

        var page = await db.Apis
            .ToCursorPagedAsync(
                spec, q.SortOrder, q.Cursor, q.Limit,
                ApiSortSpecs.IdSelector, IdExtractor, ct);

        var creatorIds = new HashSet<Guid>(page.Items.Select(a => a.CreatedByUserId));
        var creators = await directory.GetManyAsync(creatorIds, ct);

        var items = page.Items
            .Select(a =>
            {
                var resp = a.ToResponse();
                return creators.TryGetValue(a.CreatedByUserId, out var creator)
                    ? resp with { CreatedBy = creator }
                    : resp;
            })
            .ToList();
        return new CursorPage<ApiResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
```

> If `ToCursorPagedAsync` has no overload without `expectedFilters`, pass `expectedFilters: null` explicitly (mirror the `ListServicesHandler` call site signature).

- [ ] **Step 5: Register handlers in `CatalogModule.RegisterServices`**

After `services.AddScoped<ListServicesHandler>();`:
```csharp
        services.AddScoped<RegisterApiHandler>();
        services.AddScoped<GetApiByIdHandler>();
        services.AddScoped<ListApisHandler>();
```

- [ ] **Step 6: Build — verify compile**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure -v q"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 7: Commit**

```
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApiSortSpecs.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterApiHandler.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetApiByIdHandler.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/ListApisHandler.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs
git commit -m "feat(catalog): Api register/get/list handlers + sort specs + DI"
```

---

## Task 6: Endpoint delegates + routes + `ApiNotFound`

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EndpointResultExtensions.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (routes)

**Interfaces:**
- Consumes: Task 5 handlers, Task 2 permission.
- Produces: `POST /api/v1/catalog/apis`, `GET /api/v1/catalog/apis/{id:guid}`, `GET /api/v1/catalog/apis`. Consumed by Tasks 7, 8.

- [ ] **Step 1: Add `ApiNotFound()`** — `EndpointResultExtensions.cs`, after `ServiceNotFound()`

```csharp
    /// <inheritdoc cref="ApplicationNotFound"/>
    internal static IResult ApiNotFound() =>
        ResourceNotFound("API", "No API with that id is visible in the current tenant.");
```

- [ ] **Step 2: Add the three delegates** — `CatalogEndpointDelegates.cs`, after `ListServicesAsync` (before the relationship delegates). GET-by-id does **not** emit an ETag (no concurrency token exposed this slice).

```csharp
    internal static async Task<IResult> RegisterApiAsync(
        [FromBody] RegisterApiRequest request,
        RegisterApiHandler handler,
        CatalogDbContext db,
        ITenantContext tenant,
        ClaimsPrincipal caller,
        ICurrentUser currentUser,
        IAuthorizationService auth,
        IOrganizationTeamExistenceChecker teamChecker,
        IAuditWriter audit,
        CancellationToken ct)
    {
        // ADR-0103: a new API requires an existing owning team in the tenant.
        // RLS-scoped checker → a cross-tenant id resolves as "not found" (same 422 branch).
        if (!await teamChecker.ExistsAsync(request.TeamId, ct))
        {
            return Results.Problem(
                type: ProblemTypes.InvalidTeam,
                title: "Invalid team",
                detail: "The supplied teamId does not resolve to a team in the current tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        // Target-team membership gate (reuses the shared ApplicationTeamScoped policy).
        if (await AuthorizeTargetTeamAsync(auth, caller, request.TeamId) is { } forbidden)
            return forbidden;

        var response = await handler.Handle(
            new RegisterApiCommand(
                request.DisplayName, request.Description, request.Style, request.Version, request.SpecUrl, request.TeamId),
            db, tenant, currentUser, audit, ct);

        return Results.Created($"/api/v1/catalog/apis/{response.Id}", response);
    }

    internal static async Task<IResult> GetApiByIdAsync(
        Guid id,
        GetApiByIdHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var resp = await handler.Handle(new GetApiByIdQuery(id), db, ct);
        if (resp is null) return EndpointResultExtensions.ApiNotFound();
        return Results.Ok(resp);
    }

    internal static async Task<IResult> ListApisAsync(
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? cursor,
        [FromQuery] string? limit,
        ListApisHandler handler,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var (parsedSortBy, parsedSortOrder, effectiveLimit) =
            CursorListBinding.Bind<ApiSortField>(sortBy, sortOrder, limit, ApiSortSpecs.AllowedFieldNames);

        var query = new ListApisQuery(
            SortBy: parsedSortBy ?? ApiSortField.DisplayName,
            SortOrder: parsedSortOrder ?? SortOrder.Asc,
            Cursor: cursor,
            Limit: effectiveLimit);

        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }
```

- [ ] **Step 3: Map the routes** — `CatalogModule.MapEndpoints`, after the `ListServices` mapping

```csharp
        tenant.MapPost("/apis", CatalogEndpointDelegates.RegisterApiAsync)
              .RequireAuthorization(KartovaPermissions.CatalogApisRegister)
              .WithName("RegisterApi")
              .Produces<ApiResponse>(StatusCodes.Status201Created)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status403Forbidden)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        tenant.MapGet("/apis/{id:guid}", CatalogEndpointDelegates.GetApiByIdAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("GetApiById")
              .Produces<ApiResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status404NotFound);
        tenant.MapGet("/apis", CatalogEndpointDelegates.ListApisAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("ListApis")
              .Produces<CursorPage<ApiResponse>>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
```

- [ ] **Step 4: Build the whole solution — verify compile**

Run: `cmd //c "dotnet build Kartova.slnx -v q"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/EndpointResultExtensions.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs
git commit -m "feat(catalog): wire /api/v1/catalog/apis POST/GET/list endpoints"
```

---

## Task 7: Register integration tests (real seam)

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApiTests.cs`

**Interfaces:**
- Consumes: the live endpoints from Task 6 via `CatalogIntegrationTestBase` (`Fx`, `KartovaApiFixtureBase.WireJson`).

- [ ] **Step 1: Write the tests**

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class RegisterApiTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static object Body(Guid teamId, ApiStyle style = ApiStyle.Rest, string version = "v1",
        string? specUrl = "https://specs.example.com/openapi.json") => new
    {
        displayName = "orders-api",
        description = "Orders REST API.",
        style,
        version,
        specUrl,
        teamId,
    };

    [TestMethod]
    public async Task POST_with_valid_payload_returns_201_and_roundtrips()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId, ApiStyle.Grpc, "2.0"));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("orders-api", body!.DisplayName);
        Assert.AreEqual(ApiStyle.Grpc, body.Style);
        Assert.AreEqual("2.0", body.Version);
        Assert.AreEqual(teamId, body.TeamId);

        // Round-trips through GET-by-id.
        var get = await client.GetAsync($"/api/v1/catalog/apis/{body.Id}");
        Assert.AreEqual(HttpStatusCode.OK, get.StatusCode);
    }

    [TestMethod]
    public async Task POST_allows_null_spec_url()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team Null");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId, specUrl: null));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNull(body!.SpecUrl);
    }

    [TestMethod]
    public async Task POST_without_token_returns_401()
    {
        using var anon = Fx.CreateAnonymousClient();
        var resp = await anon.PostAsJsonAsync("/api/v1/catalog/apis", Body(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_empty_display_name_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team 400");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis",
            new { displayName = "", description = "d", style = ApiStyle.Rest, version = "v1", specUrl = (string?)null, teamId });
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_relative_spec_url_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team Url");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId, specUrl: "/relative/openapi.json"));
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_with_unknown_team_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", Body(Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_by_member_not_in_target_team_returns_403()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(tenantId, "Api Team Restricted");
        var memberClient = await Fx.CreateAuthenticatedClientAsync(
            "member@orga.kartova.local", new[] { KartovaRoles.Member });

        var resp = await memberClient.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId));
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_sets_CreatedByUserId_to_caller_sub()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team Identity");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        var expectedSub = await Fx.GetSubClaimAsync(OrgAUser);
        Assert.AreEqual(expectedSub, body!.CreatedByUserId);
    }

    [TestMethod]
    public async Task GET_by_id_unknown_returns_404()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/apis/{Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [TestMethod]
    public async Task GET_by_id_from_other_tenant_returns_404()
    {
        // Register as OrgA, then attempt to read it as OrgB (RLS must hide it).
        var clientA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Team XT");
        var created = await clientA.PostAsJsonAsync("/api/v1/catalog/apis", Body(teamId));
        var body = await created.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);

        var clientB = await Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var resp = await clientB.GetAsync($"/api/v1/catalog/apis/{body!.Id}");
        Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
```

> Verify the cross-tenant helper (`admin@orgb.kartova.local`) matches the fixture's second-org seed used by `RegisterApplicationTests`/`RegisterServiceTests`; if the fixture exposes a different second-tenant accessor, mirror that call exactly.

- [ ] **Step 2: Run the tests — verify pass** (this is the RED→GREEN driver for Tasks 3–6 wiring)

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~RegisterApiTests -v q"`
Expected: PASS — all `RegisterApiTests` green (Testcontainers Postgres + real JWT).

- [ ] **Step 3: Commit**

```
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RegisterApiTests.cs
git commit -m "test(catalog): real-seam register/get Api integration tests"
```

---

## Task 8: List/pagination integration tests + permission-matrix rows

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApisPaginationTests.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs`

- [ ] **Step 1: Write `ListApisPaginationTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class ListApisPaginationTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    private static async Task Seed(HttpClient client, Guid teamId, string name, string version = "v1")
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", new
        {
            displayName = name, description = "seed.", style = ApiStyle.Rest, version, specUrl = (string?)null, teamId,
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
    }

    [TestMethod]
    public async Task List_returns_cursor_page_sorted_by_displayName_asc_by_default()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api List Team");
        await Seed(client, teamId, "alpha-api");
        await Seed(client, teamId, "bravo-api");
        await Seed(client, teamId, "charlie-api");

        var page = await client.GetFromJsonAsync<CursorPage<ApiResponse>>(
            "/api/v1/catalog/apis?limit=2", KartovaApiFixtureBase.WireJson);

        Assert.AreEqual(2, page!.Items.Count);
        Assert.AreEqual("alpha-api", page.Items[0].DisplayName);
        Assert.AreEqual("bravo-api", page.Items[1].DisplayName);
        Assert.IsNotNull(page.NextCursor);

        var page2 = await client.GetFromJsonAsync<CursorPage<ApiResponse>>(
            $"/api/v1/catalog/apis?limit=2&cursor={Uri.EscapeDataString(page.NextCursor!)}",
            KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("charlie-api", page2!.Items[0].DisplayName);
    }

    [TestMethod]
    public async Task List_honors_sortBy_version_and_style()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Sort Team");
        await Seed(client, teamId, "z-api", version: "1.0");
        await Seed(client, teamId, "a-api", version: "2.0");

        var byVersion = await client.GetAsync("/api/v1/catalog/apis?sortBy=version&sortOrder=asc");
        Assert.AreEqual(HttpStatusCode.OK, byVersion.StatusCode);
        var byStyle = await client.GetAsync("/api/v1/catalog/apis?sortBy=style&sortOrder=desc");
        Assert.AreEqual(HttpStatusCode.OK, byStyle.StatusCode);
    }

    [TestMethod]
    public async Task List_rejects_unknown_sortBy_with_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync("/api/v1/catalog/apis?sortBy=bogus");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task List_rejects_out_of_range_limit_with_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync("/api/v1/catalog/apis?limit=99999");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task List_is_tenant_isolated()
    {
        var clientA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Api Iso Team");
        await Seed(clientA, teamId, "orga-only-api");

        var clientB = await Fx.CreateAuthenticatedClientAsync("admin@orgb.kartova.local");
        var pageB = await clientB.GetFromJsonAsync<CursorPage<ApiResponse>>(
            "/api/v1/catalog/apis?limit=100", KartovaApiFixtureBase.WireJson);
        Assert.IsFalse(pageB!.Items.Any(a => a.DisplayName == "orga-only-api"));
    }
}
```

> Match the `sortBy=bogus`→400 and `limit=99999`→422 expectations to `ListServicesPaginationTests`; if that suite asserts different envelopes, mirror it exactly (the binding is shared `CursorListBinding`).

- [ ] **Step 2: Run — verify pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~ListApisPaginationTests -v q"`
Expected: PASS.

- [ ] **Step 3: Add the 3 API rows to the permission matrix** — `CatalogPermissionMatrixTests.cs`

In the `Endpoints` array, after the services rows:
```csharp
        (HttpMethod.Post, "/api/v1/catalog/apis",                          KartovaPermissions.CatalogApisRegister),
        (HttpMethod.Get,  "/api/v1/catalog/apis",                          KartovaPermissions.CatalogRead),
        (HttpMethod.Get,  "/api/v1/catalog/apis/{apiId}",                  KartovaPermissions.CatalogRead),
```

In the arrange block, after the service seed (`svcId`), seed an API so `{apiId}` substitutes:
```csharp
        var registerApiResp = await seederClient.PostAsJsonAsync(
            "/api/v1/catalog/apis",
            new
            {
                displayName = "Matrix Api",
                description = "Seed api for permission matrix test.",
                style = Kartova.Catalog.Domain.ApiStyle.Rest,
                version = "v1",
                specUrl = (string?)null,
                teamId,
            });
        Assert.IsTrue(registerApiResp.IsSuccessStatusCode,
            $"Seed api registration must succeed (was {registerApiResp.StatusCode}).");
        var seededApi = await registerApiResp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        var apiId = seededApi!.Id;
```

In the URL-substitution line, add `.Replace("{apiId}", apiId.ToString())`.

In `AttachShapeValidBody`, add a branch:
```csharp
        else if (method == HttpMethod.Post && pathTemplate == "/api/v1/catalog/apis")
        {
            req.Content = JsonContent.Create(new
            {
                displayName = "Matrix Api",
                description = "Matrix shape body.",
                style = Kartova.Catalog.Domain.ApiStyle.Rest,
                version = "v1",
                specUrl = (string?)null,
                teamId,
            });
        }
```

- [ ] **Step 4: Run the full Catalog integration suite — verify pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests -v q"`
Expected: PASS (matrix now covers the 3 API rows; per-role 403/allow correct).

> Docker flake note: if a single integration assembly transiently fails with a Testcontainers named-pipe/Docker timeout under saturation, re-run that assembly in isolation before treating it as red.

- [ ] **Step 5: Commit**

```
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApisPaginationTests.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs
git commit -m "test(catalog): Api list/pagination real-seam tests + permission-matrix rows"
```

---

## Task 9: OpenAPI snapshot regen + terminal Definition-of-Done

**Files:**
- Modify (regenerated): `web/openapi-snapshot.json` (+ committed generated-client snapshot, if the repo commits one)
- Create: `docs/superpowers/verification/2026-07-03-catalog-api-entity/dod.md` (copy `docs/superpowers/templates/dod-ledger-template.md`)
- Create: `docs/superpowers/verification/2026-07-03-catalog-api-entity/gate-findings.yaml` (copy `docs/superpowers/templates/gate-findings-template.yaml`)

- [ ] **Step 1: Regenerate the OpenAPI snapshot** (three new endpoints must land in the wire contract)

The new endpoints change the generated OpenAPI doc. Regenerate the committed snapshot the way the repo does (predev/prebuild against the live API), then run the binding type-gate:
```
cmd //c "cd web && npm run build"
```
Expected: `web/openapi-snapshot.json` updated with `/api/v1/catalog/apis`; `tsc -b` passes (no binding type errors). Commit the regenerated snapshot (do not hand-edit).

> `OpenApiTests` is name-keyed/live — a param-order diff in the snapshot is cosmetic; regenerate+commit rather than revert.

- [ ] **Step 2: Start the DoD ledger**

Copy the ledger + findings templates into `docs/superpowers/verification/2026-07-03-catalog-api-entity/`, fill the header (slice, spec/plan/ADR-0111 links), and mark gates as they run.

- [ ] **Step 3: Gate 1 — full solution build, warnings-as-errors**

Run: `cmd //c "dotnet build Kartova.slnx -v q"`
Expected: 0 warnings, 0 errors. Record in ledger.

- [ ] **Step 4: Gate 3 — full test suite**

Run: `cmd //c "dotnet test Kartova.slnx -v q"` (+ `cd web && npm run test` for frontend)
Expected: all green — unit (`ApiTests`, role tests), architecture (`KartovaPermissionsRules`, `ContractsCoverageRules`), integration (`RegisterApiTests`, `ListApisPaginationTests`, `CatalogPermissionMatrixTests`), frontend (`usePermissions`). Record.

- [ ] **Step 5: Commit the snapshot + ledger**

```
git add web/openapi-snapshot.json docs/superpowers/verification/2026-07-03-catalog-api-entity/
git commit -m "chore(catalog): regenerate OpenAPI snapshot for /apis + DoD ledger"
```

- [ ] **Step 6: Remaining DoD gates (per CLAUDE.md — not restated here)**

Run in fail-fast order and record each in the ledger + `gate-findings.yaml`:
- Gate 2 — per-task subagent reviews (interleaved during dev).
- Gate 4 — container build (`images` CI job / `docker compose build`).
- Gate 5 — `/simplify` against the branch diff.
- **Gate 6 — mutation loop (`/misc:mutation-sentinel` → `/misc:test-generator`), BLOCKING** — the diff touches Domain (`Api`) + Application/Infrastructure handler logic. Target ≥80%; document survivors.
- Gate 7 — `/superpowers:requesting-code-review` (full branch diff).
- Gate 8 — `/pr-review-toolkit:review-pr`.
- Gate 9 — `/deep-review` (spec/plan/ADR-0111/tests).
- **Terminal re-verify:** re-run Gate 1 + Gate 3 after 5–9 (they may have applied fixes). Then run `scripts/ci-local.sh` (Release mirror) green before push.

- [ ] **Step 7: Update the CHECKLIST + open the PR**

Mark `E-02.F-03.S-01` complete in `docs/product/CHECKLIST.md` (keep the FU-1..FU-11 note), commit, push the branch, open the PR citing the DoD ledger path.

---

## Self-Review

**1. Spec coverage** — every spec section maps to a task:
- §2 domain model → Task 1. §3 #10 Version/Xmin split → Task 1 (`Xmin`) + Task 3 (`ApiResponse.Version`). §3 #5–8 field rules → Task 1 validators + tests. §4.4 permission 5-sync → Task 2. §5.3 contracts → Task 3. §3 persistence/RLS → Task 4. §4.1 endpoints + §4.2 flow → Tasks 5–6. §6 errors → Tasks 6–8 (validation 400, invalid-team 422, membership 403, cross-tenant 404, bad sort 400, bad limit 422). §7 gate-5 artifacts (ApiTests, RegisterApiTests, ListApisPaginationTests, matrix rows, role tests) → Tasks 1,2,7,8. §8 impact/touchpoints → Impact Analysis section. §9 list surface (sort all cols, filters deferred) → Task 3/5/6 (sort allowlist) + filter-registry (already committed). §10 mutation-blocking DoD → Task 9. §11 follow-ups → registered (spec + ADR + CHECKLIST, already committed).

**2. Placeholder scan** — no TBD/TODO. Every code step shows complete code; the two "mirror the existing file" notes (SortSpec key type, cross-tenant fixture accessor, list-envelope expectations) point at named existing files with the fallback shown, not hand-waves.

**3. Type consistency** — `Api.Create(displayName, description, style, version, specUrl, createdByUserId, teamId, tenantId, clock)` identical across Tasks 1, 5, 7. `ApiResponse` fields identical across Task 3 (def), Task 5 (ToResponse), Tasks 7–8 (deserialize). `ApiSortField {DisplayName,Style,Version,CreatedAt}` identical across Tasks 3, 5, 6. `RegisterApiCommand`/`RegisterApiRequest` field order matches. `Xmin` (not `Version`) used consistently in Tasks 1 + 4. `CatalogApisRegister` string `"catalog.apis.register"` identical across Task 2 (C#), Task 2 (TS/snapshot), Task 6 (route), Task 8 (matrix).

**No blocking issues found.**
