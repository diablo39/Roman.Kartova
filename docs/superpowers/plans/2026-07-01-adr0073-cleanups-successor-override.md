# ADR-0073 Cleanups (Sunset Override + Successor) + FU-1 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the two open ADR-0073 gaps on `Application` — an OrgAdmin-gated sunset-date override on decommission (§15.1) and an optional App→App successor reference (§15.7, ADR-0110) — and fix FU-1 (a tampered cursor returns 400, not 500).

**Architecture:** Backend is a modular monolith (ADR-0082), Catalog module, Clean Architecture layers (Domain / Application / Infrastructure / Contracts), direct-dispatch handlers (ADR-0093), tenant scope via `ITenantScope` + RLS (ADR-0090). Frontend is React 19 + TS strict, TanStack Query, Untitled UI primitives (ADR-0094), generated OpenAPI client. Three independently shippable sub-slices, sequenced A→B→C.

**Tech Stack:** .NET 10 / EF Core / Npgsql / MSTest v4 + NSubstitute + Testcontainers · React 19 / Vite / Vitest / react-hook-form + zod.

**Spec:** `docs/superpowers/specs/2026-07-01-adr0073-cleanups-successor-override-design.md` (authoritative — read it; this plan operationalizes it). **ADR:** ADR-0110.

## Global Constraints

- `TreatWarningsAsErrors=true` — 0 warnings, 0 errors, whole solution (`Kartova.slnx`).
- Windows shell: `cmd //c` (double-slash) or PowerShell wrappers for `dotnet`. Git Bash lacks `grep -P`.
- Enums serialize camelCase on the wire (ADR-0109); FE derives enum/DTO types from the **generated client**, never hand-authored literals. `tsc -b` (`npm run build`) is the binding FE type gate.
- `[ExcludeFromCodeCoverage]` on every `*Request`/`*Response`/DTO (enforced by `ContractsCoverageRules`).
- Coverage/mutation target ≥80% on changed Domain/Application (`stryker-config.json`).
- LF line endings (`.gitattributes eol=lf`); don't introduce CRLF.
- Lifecycle `enum` numeric values are load-bearing — never renumber (`LifecycleEnumRules`).
- REST verbs (ADR-0096): `POST /<action>` for commands, `PUT` for idempotent replacement. ProblemDetails (ADR-0091) for all errors.
- Every mutation endpoint runs `LoadAndAuthorizeApplicationAsync` (team-scoped gate) before the handler.

## Environment preflight (run before Task A1)

- [ ] `cmd //c "dotnet restore Kartova.slnx"` — confirm NuGet auth to private feeds works. **If restore fails on auth, STOP and ask the user how they authenticate (PAT / credential provider) — do NOT edit nuget.config with placeholders.**
- [ ] `cmd //c "dotnet build Kartova.slnx -c Debug"` — baseline green before any change.
- [ ] `docker ps` — confirm Docker is up (Testcontainers integration tests + gate 4 need it). If Docker is unavailable, integration/container gates are flagged **pending user verification**, not skipped-as-green.
- [ ] Ensure the Vite dev server (5173) is **not** running before any `npm ci` / `ci-local.sh frontend` (EPERM on lightningcss otherwise).

---

## Sub-slice A — FU-1 cursor hardening (do first; backend-only, no domain coupling)

### Task A1: Map tampered-cursor parse failures to 400

**Files:**
- Modify: `src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs` (`ConvertCursorValue`, ~line 206)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs` (add a case)

**Interfaces:**
- Consumes: `InvalidCursorException` (`Kartova.SharedKernel.Pagination`) — already mapped to 400 `invalid-cursor` by `PagingExceptionHandler`.
- Produces: no signature change; behavior change only (wrong-typed cursor sort value → 400).

- [ ] **Step 1: Write the failing real-seam test.** In `ListApplicationsPaginationTests`, add a test that issues a valid first page (`GET /api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=1`), then takes the returned `nextCursor`, base64url-decodes it, replaces the sort value `s` with a non-date string (e.g. `"not-a-date"`), re-encodes, and replays it. Follow the existing base64url + `System.Text.Json` cursor manipulation already used in this file's tamper/mismatch tests.

```csharp
[TestMethod]
public async Task ListApplications_CursorWithWrongTypedSortValue_Returns400InvalidCursor()
{
    // arrange: get a real nextCursor for a createdAt-sorted page
    var first = await Client.GetAsync("/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=1");
    first.EnsureSuccessStatusCode();
    var page = await first.Content.ReadFromJsonAsync<CursorPage<ApplicationResponse>>();
    var tampered = TamperSortValue(page!.NextCursor!, "not-a-date"); // helper mirrors existing tamper tests

    // act
    var resp = await Client.GetAsync($"/api/v1/catalog/applications?sortBy=createdAt&sortOrder=asc&limit=1&cursor={Uri.EscapeDataString(tampered)}");

    // assert
    Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    var problem = await resp.Content.ReadFromJsonAsync<ProblemDetails>();
    StringAssert.Contains(problem!.Type ?? problem.Title, "invalid-cursor");
}
```
If a `TamperSortValue` helper does not already exist in the file, add a private one that decodes the cursor JSON, sets the `s` field, and re-encodes (reuse the file's existing `CursorCodec`-shaped helpers).

- [ ] **Step 2: Run it; verify it fails with 500 (not 400).** `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~ListApplications_CursorWithWrongTypedSortValue"` → Expected: FAIL (returns 500). (Requires Docker; if unavailable, mark this test *pending user verification* and proceed to the unit-level guard below.)

- [ ] **Step 3: Fix `ConvertCursorValue`.** Wrap the existing body:

```csharp
private static object ConvertCursorValue(object value, Type targetType)
{
    try
    {
        if (targetType == typeof(DateTimeOffset) && value is string s)
            return DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        if (targetType == typeof(DateTime) && value is string s2)
            return DateTime.Parse(s2, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime();
        if (targetType == typeof(Guid) && value is string s3)
            return Guid.Parse(s3);
        return Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture)!;
    }
    catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
    {
        throw new InvalidCursorException(
            $"Cursor sort value '{value}' is not compatible with expected type {targetType.Name}.", ex);
    }
}
```

- [ ] **Step 4: Run the test; verify 400.** Same command → Expected: PASS.
- [ ] **Step 5: Full build + affected suites green.** `cmd //c "dotnet build Kartova.slnx -c Debug"` (0 warn/err), then the pagination integration test + `Kartova.SharedKernel.Postgres` unit tests if any.
- [ ] **Step 6: Commit.**
```bash
git add src/Kartova.SharedKernel.Postgres/Pagination/QueryablePagingExtensions.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListApplicationsPaginationTests.cs
git commit -m "fix(pagination): map tampered cursor sort-value to 400 invalid-cursor (FU-1)"
```

---

## Sub-slice B — §15.1 sunset-date admin override

### Task B1: Add `catalog.applications.lifecycle.override` permission (5-sync + matrix)

**Files:**
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` (const + `All`)
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` (OrgAdmin set only)
- Modify: `web/src/shared/auth/permissions.ts` (TS constant + any `ALL`/snapshot-derived list)
- Modify: `web/permissions.snapshot.json`
- Modify: `web/src/shared/auth/usePermissions.*` OrgAdmin mock (the test/mock list — grep `usePermissions` for the OrgAdmin permission array)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs`

**Interfaces:**
- Produces: `KartovaPermissions.CatalogApplicationsLifecycleOverride = "catalog.applications.lifecycle.override"` (consumed by B4 endpoint authz + B5 FE).

- [ ] **Step 1: Add the failing matrix assertion.** In `CatalogPermissionMatrixTests`, extend the OrgAdmin-has / Member-lacks / Viewer-lacks assertions to include the new permission (follow the file's existing per-permission assertion shape).
- [ ] **Step 2: Run it; verify fail** (const doesn't exist / not in map). `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~CatalogPermissionMatrix"` → FAIL.
- [ ] **Step 3: Add the C# const + `All` entry.**
```csharp
public const string CatalogApplicationsLifecycleOverride = "catalog.applications.lifecycle.override";
// ...add CatalogApplicationsLifecycleOverride to the All initializer array (near the other lifecycle perms)
```
- [ ] **Step 4: Grant to OrgAdmin only** in `KartovaRolePermissions.Map` (add to the OrgAdmin set array; do NOT add to Member/Viewer).
- [ ] **Step 5: Mirror to FE.** Add the TS constant in `permissions.ts` (match the existing `CatalogApplicationsLifecycle*` naming); add the string to `permissions.snapshot.json`; add it to the `usePermissions` OrgAdmin mock array. (Backend CI guards C#↔snapshot only — the `permissions.ts` + mock edits are what keep the Frontend job green.)
- [ ] **Step 6: Run matrix test + FE typecheck.** Backend test → PASS. `cd web && npm run typecheck` (or `npx tsc -b`) → 0 errors. (Ensure dev server not running.)
- [ ] **Step 7: Commit.**
```bash
git add src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs web/src/shared/auth/permissions.ts web/permissions.snapshot.json web/src/shared/auth/usePermissions.* src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs
git commit -m "feat(auth): add catalog.applications.lifecycle.override permission (OrgAdmin-only)"
```

### Task B2: Domain — `Decommission` override flag

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationLifecycleTests.cs`

**Interfaces:**
- Produces: `Application.Decommission(TimeProvider clock, bool allowBeforeSunset = false)` (consumed by B3 handler).

- [ ] **Step 1: Failing domain tests.**
```csharp
[TestMethod]
public void Decommission_BeforeSunset_WithOverride_Succeeds()
{
    var app = DeprecatedAppWithFutureSunset(out var clock); // helper: Active→Deprecate(future), clock < sunset
    app.Decommission(clock, allowBeforeSunset: true);
    Assert.AreEqual(Lifecycle.Decommissioned, app.Lifecycle);
}

[TestMethod]
public void Decommission_BeforeSunset_WithoutOverride_Throws()
{
    var app = DeprecatedAppWithFutureSunset(out var clock);
    var ex = Assert.ThrowsException<InvalidLifecycleTransitionException>(() => app.Decommission(clock));
    StringAssert.Contains(ex.Message + ex.Reason, "before-sunset-date"); // match the existing reason surface
}
```
(Reuse existing deprecate/decommission helpers in the file; `FakeTimeProvider` for `clock`.)
- [ ] **Step 2: Run; verify fail** (overload doesn't exist). `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter FullyQualifiedName~ApplicationLifecycle"` → FAIL.
- [ ] **Step 3: Add the overload.**
```csharp
public void Decommission(TimeProvider clock, bool allowBeforeSunset = false)
{
    if (Lifecycle != Lifecycle.Deprecated)
        throw new InvalidLifecycleTransitionException(Lifecycle, nameof(Decommission), SunsetDate);
    if (!allowBeforeSunset && clock.GetUtcNow() < SunsetDate!.Value)
        throw new InvalidLifecycleTransitionException(Lifecycle, nameof(Decommission), SunsetDate, reason: "before-sunset-date");
    Lifecycle = Lifecycle.Decommissioned;
}
```
The default `false` preserves the current caller (`DecommissionApplicationHandler` until B3) and all existing tests.
- [ ] **Step 4: Run; verify pass** (new + existing lifecycle tests). → PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(catalog): Application.Decommission supports admin sunset override"`

### Task B3: Contracts + command + handler (override + audit)

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/DecommissionApplicationRequest.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/DecommissionApplicationCommand.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/DecommissionApplicationHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogAuditEntries.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/CatalogAuditEntriesTests.cs`

**Interfaces:**
- Produces: `DecommissionApplicationRequest(bool OverrideSunset = false)`; `DecommissionApplicationCommand(ApplicationId Id, bool OverrideSunset)`; audit entry carries `overrodeSunset` + bypassed `sunsetDate` when an override actually bypassed the check.

- [ ] **Step 1: Contract.**
```csharp
using System.Diagnostics.CodeAnalysis;
namespace Kartova.Catalog.Contracts;
[ExcludeFromCodeCoverage]
public sealed record DecommissionApplicationRequest(bool OverrideSunset = false);
```
- [ ] **Step 2: Command** — `DecommissionApplicationCommand(Kartova.Catalog.Domain.ApplicationId Id, bool OverrideSunset)`. Update the XML doc to note the override.
- [ ] **Step 3: Failing audit test.** In `CatalogAuditEntriesTests`, assert `LifecycleChanged` produces `overrodeSunset: true` + a `sunsetDate` data member when passed the override context (match the factory's existing data-bag shape). Decide the factory signature: extend `LifecycleChanged(app, from, bool overrodeSunset = false, DateTimeOffset? bypassedSunset = null)`.
- [ ] **Step 4: Handler.** Capture the sunset before the transition; call the overload; audit with override flags when a bypass occurred:
```csharp
var from = app.Lifecycle;
var priorSunset = app.SunsetDate;
var bypassed = cmd.OverrideSunset && priorSunset is { } s && _clock.GetUtcNow() < s;
app.Decommission(_clock, allowBeforeSunset: cmd.OverrideSunset);
await db.SaveChangesAsync(ct);
await audit.AppendAsync(CatalogAuditEntries.LifecycleChanged(app, from, overrodeSunset: bypassed, bypassedSunset: bypassed ? priorSunset : null), ct);
return app.ToResponse();
```
- [ ] **Step 5: Run audit unit test + domain suite.** → PASS. `cmd //c "dotnet build Kartova.slnx -c Debug"` → 0 warn/err.
- [ ] **Step 6: Commit.** `git commit -m "feat(catalog): decommission command carries OverrideSunset + audits the bypass"`

### Task B4: Endpoint override authz + real-seam integration tests

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (`DecommissionApplicationAsync`)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (bind body; ProducesProblem already has 403)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DecommissionApplicationTests.cs`

**Interfaces:**
- Consumes: `KartovaPermissions.CatalogApplicationsLifecycleOverride` (B1); `DecommissionApplicationRequest` (B3).

- [ ] **Step 1: Failing real-seam tests** (real JWT + Postgres via `KartovaApiFixtureBase`; follow the file's existing seeding of a Deprecated app with a **future** sunset):
  - OrgAdmin `POST /decommission {overrideSunset:true}` before sunset → **200**, body `lifecycle == "decommissioned"`.
  - Member (of the app's team) `{overrideSunset:true}` before sunset → **403** (lacks the override permission).
  - Member `{}` (no override) before sunset → **409** `reason=before-sunset-date` (regression, unchanged).
  - OrgAdmin `{}` after sunset → **200** (regression).
  - After the override success, assert an `audit_log` row with `overrodeSunset` (query via the test's audit accessor if present; else assert 200 + leave audit to the unit test).
- [ ] **Step 2: Run; verify fail** (override currently ignored / 409). → FAIL.
- [ ] **Step 3: Delegate.** Bind `[FromBody] DecommissionApplicationRequest? request` (null → `OverrideSunset=false`). After `LoadAndAuthorizeApplicationAsync`, gate the override:
```csharp
var overrideSunset = request?.OverrideSunset ?? false;
if (overrideSunset)
{
    var ovr = await auth.AuthorizeAsync(user, KartovaPermissions.CatalogApplicationsLifecycleOverride);
    if (!ovr.Succeeded) return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "forbidden");
}
var result = await handler.Handle(new DecommissionApplicationCommand(new ApplicationId(id), overrideSunset), db, audit, ct);
```
(Match the surrounding delegate's `IAuthorizationService auth` / `ClaimsPrincipal user` parameter names + the file's existing forbidden-result helper. The permission string is the policy name — confirm policies auto-register from `KartovaPermissions.All`; if not, register a policy per permission where the others are registered.)
- [ ] **Step 4: Endpoint binding.** In `CatalogModule`, the decommission `MapPost` stays on `.RequireAuthorization(CatalogApplicationsLifecycleForward)`; confirm the delegate now accepts the optional body (minimal API binds `[FromBody]` optional). Keep `.ProducesProblem(403)`.
- [ ] **Step 5: Run integration tests.** (Docker) → PASS. If Docker unavailable → mark *pending user verification*; still run `dotnet build` green.
- [ ] **Step 6: Commit.** `git commit -m "feat(catalog): OrgAdmin sunset override on decommission endpoint (15.1)"`

### Task B5: Codegen + FE override checkbox

**Files:**
- Modify: `web/src/features/catalog/api/applications.ts` (`useDecommissionApplication` payload)
- Modify: `web/src/features/catalog/components/DecommissionConfirmDialog.tsx`
- Modify: `web/src/generated/openapi.ts` + `web/openapi-snapshot.json` (regenerated)
- Test: `web/src/features/catalog/components/__tests__/DecommissionConfirmDialog.test.tsx`

- [ ] **Step 1: Regenerate the client.** Run the API, `cd web && npm run codegen`; confirm `DecommissionApplicationRequest` (with `overrideSunset`) appears in `openapi.ts`; commit the snapshot. (If the API can't run in-session, flag *pending user verification* and hand-extend the mutation typing minimally against the committed snapshot per the OpenAPI-snapshot memory.)
- [ ] **Step 2: Failing component tests.** In `DecommissionConfirmDialog.test.tsx`:
  - Given `usePermissions` returns the override permission AND `sunsetDate` is in the future → the "Override sunset date" checkbox renders; confirming posts `{ overrideSunset: true }`.
  - Given no override permission → checkbox absent; confirming posts no override (or `false`).
  (Stub `usePermissions` + the mutation as the sibling dialog tests do.)
- [ ] **Step 3: Implement.** Add `overrideSunset` optional to `useDecommissionApplication`'s payload; in the dialog compute `canOverride = hasPermission(CatalogApplicationsLifecycleOverride) && now < new Date(application.sunsetDate ?? 0)`; render a checkbox (Untitled UI) when `canOverride`; pass its state on confirm.
- [ ] **Step 4: Run FE tests + typecheck.** `cd web && npm test -- DecommissionConfirmDialog` and `npm run build` → green.
- [ ] **Step 5: Commit.** `git add web/... && git commit -m "feat(web): sunset-override checkbox on decommission dialog (OrgAdmin)"`

---

## Sub-slice C — §15.7 successor reference (ADR-0110)

### Task C1: Domain — successor field + Deprecate/SetSuccessor/Reactivate

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationSuccessorTests.cs` (new) + `ApplicationLifecycleTests.cs` (Deprecate signature)

**Interfaces:**
- Produces: `Application.SuccessorApplicationId : Guid?`; `Deprecate(DateTimeOffset sunsetDate, Guid? successorApplicationId, TimeProvider clock)`; `SetSuccessor(Guid? successorApplicationId)`; `Reactivate()` clears successor.

- [ ] **Step 1: Failing tests** (`ApplicationSuccessorTests`):
```csharp
[TestMethod] public void Deprecate_WithSuccessor_SetsIt() { /* Active app; Deprecate(futureSunset, otherId, clock); assert SuccessorApplicationId == otherId */ }
[TestMethod] public void Deprecate_WithSelfSuccessor_Throws() { /* Deprecate(futureSunset, app.Id.Value, clock) → ArgumentException */ }
[TestMethod] public void SetSuccessor_WhileDeprecated_Updates() { /* Deprecate then SetSuccessor(other) → updates */ }
[TestMethod] public void SetSuccessor_WhileActive_Throws() { /* Active app → InvalidLifecycleTransitionException */ }
[TestMethod] public void SetSuccessor_Null_Clears() { /* Deprecated w/ successor; SetSuccessor(null) → null */ }
[TestMethod] public void Reactivate_ClearsSuccessorAndSunset() { /* Deprecated w/ successor+sunset; Reactivate() → both null */ }
```
Also update existing `Deprecate` call sites in `ApplicationLifecycleTests` to the new 3-arg signature (pass `null` successor).
- [ ] **Step 2: Run; verify fail.** → FAIL (members/overload missing).
- [ ] **Step 3: Implement** in `Application.cs`:
```csharp
public Guid? SuccessorApplicationId { get; private set; }

public void Deprecate(DateTimeOffset sunsetDate, Guid? successorApplicationId, TimeProvider clock)
{
    if (Lifecycle != Lifecycle.Active)
        throw new InvalidLifecycleTransitionException(Lifecycle, nameof(Deprecate), SunsetDate);
    if (sunsetDate <= clock.GetUtcNow())
        throw new ArgumentException("sunsetDate must be in the future.", nameof(sunsetDate));
    RejectSelfSuccessor(successorApplicationId);
    Lifecycle = Lifecycle.Deprecated;
    SunsetDate = sunsetDate;
    SuccessorApplicationId = successorApplicationId;
}

public void SetSuccessor(Guid? successorApplicationId)
{
    if (Lifecycle != Lifecycle.Deprecated)
        throw new InvalidLifecycleTransitionException(Lifecycle, nameof(SetSuccessor), SunsetDate);
    RejectSelfSuccessor(successorApplicationId);
    SuccessorApplicationId = successorApplicationId;
}

private void RejectSelfSuccessor(Guid? successorApplicationId)
{
    if (successorApplicationId == _id)
        throw new ArgumentException("An application cannot be its own successor.", nameof(successorApplicationId));
}
```
In `Reactivate()` add `SuccessorApplicationId = null;` alongside `SunsetDate = null;`. (The old 2-arg `Deprecate` is replaced by the 3-arg — update the handler in C3.)
- [ ] **Step 4: Run; verify pass** (successor + lifecycle suites). → PASS.
- [ ] **Step 5: Commit.** `git commit -m "feat(catalog): Application successor reference (set on deprecate, editable, cleared on reactivate)"`

### Task C2: Migration + EF self-FK

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs`
- Create: migration `*_AddApplicationSuccessor.cs` (+ Designer) via `dotnet ef`

**Interfaces:** column `successor_application_id uuid null` + self-FK (`OnDelete Restrict`) + index on `catalog.applications`.

- [ ] **Step 1: EF config.** Map the scalar + self-FK (shadow nav — no navigation property):
```csharp
builder.Property(a => a.SuccessorApplicationId).HasColumnName("successor_application_id");
builder.HasOne<Application>()
       .WithMany()
       .HasForeignKey(a => a.SuccessorApplicationId)
       .OnDelete(DeleteBehavior.Restrict);
builder.HasIndex(a => a.SuccessorApplicationId);
```
- [ ] **Step 2: Generate migration.** `cmd //c "dotnet ef migrations add AddApplicationSuccessor --project src/Modules/Catalog/Kartova.Catalog.Infrastructure --startup-project src/Kartova.Api --context CatalogDbContext"`. Inspect: nullable column, FK to `catalog.applications(id)`, index. Ensure it does NOT alter RLS.
- [ ] **Step 3: Build + snapshot check.** `cmd //c "dotnet build Kartova.slnx -c Debug"` → 0 warn/err; `CatalogDbContextModelSnapshot` updated.
- [ ] **Step 4: Commit.** `git commit -m "feat(catalog): migration + EF self-FK for successor_application_id"`

### Task C3: Contracts + Deprecate command/handler successor + existence validation

**Files:**
- Modify: `Kartova.Catalog.Contracts/DeprecateApplicationRequest.cs`, `ApplicationResponse.cs`
- Modify: `Kartova.Catalog.Application/DeprecateApplicationCommand.cs`, `ApplicationResponseExtensions.cs`
- Modify: `Kartova.Catalog.Infrastructure/DeprecateApplicationHandler.cs`
- Test: `Kartova.Catalog.IntegrationTests/DeprecateApplicationTests.cs`

**Interfaces:**
- Produces: `DeprecateApplicationRequest(DateTimeOffset SunsetDate, Guid? SuccessorApplicationId = null)`; `DeprecateApplicationCommand(Id, SunsetDate, Guid? SuccessorApplicationId)`; `ApplicationResponse` += init-only `Guid? SuccessorApplicationId`, `string? SuccessorDisplayName`; 422 `invalid-successor` wire type.

- [ ] **Step 1: Contracts.** `DeprecateApplicationRequest(DateTimeOffset SunsetDate, Guid? SuccessorApplicationId = null)`. On `ApplicationResponse`, append **init-only** props (keep positional arity stable, mirror `CreatedBy`):
```csharp
public Guid? SuccessorApplicationId { get; init; }
public string? SuccessorDisplayName { get; init; }
```
`ApplicationResponseExtensions.ToResponse` sets `SuccessorApplicationId = app.SuccessorApplicationId` (via `with` or ctor — keep `SuccessorDisplayName` null on the write path).
- [ ] **Step 2: Command** — add `Guid? SuccessorApplicationId`.
- [ ] **Step 3: Failing real-seam tests** (`DeprecateApplicationTests`):
  - Deprecate with a valid same-tenant successor id → 200, body `successorApplicationId` set.
  - Deprecate with a non-existent (or cross-tenant) successor id → **422 `invalid-successor`**.
  - Deprecate with `successorApplicationId == this app id` → **400** (`successor-self-reference`).
  - Existing no-successor deprecate still 200.
- [ ] **Step 4: Handler** — validate existence (RLS-scoped) before the domain call:
```csharp
if (cmd.SuccessorApplicationId is { } sid && !await db.Applications.AnyAsync(ApplicationSortSpecs.IdEquals(sid), ct))
    return /* 422 invalid-successor */; // follow the handler's result convention (null-for-404 vs a result type); see below
app.Deprecate(cmd.SunsetDate, cmd.SuccessorApplicationId, _clock);
```
The handler currently returns `ApplicationResponse?` (null → 404). For the 422, either throw a mapped exception (preferred — add `InvalidSuccessorException` + a ProblemDetails mapping mirroring the existing catalog 422 `invalid-team`) or return a result type; **follow the pattern used by register/assign-team's `invalid-team` 422** (grep `invalid-team`). Self-reference `ArgumentException` from the domain must surface as **400** — confirm the existing `ArgumentException`→400 mapping covers `Deprecate` (the current 2-arg path already relied on it for past-sunset); if not, map it.
- [ ] **Step 5: Run integration + build.** (Docker) → PASS; `dotnet build` 0 warn/err.
- [ ] **Step 6: Commit.** `git commit -m "feat(catalog): deprecate accepts + validates successor (422 invalid-successor, 400 self-ref)"`

### Task C4: Set-successor endpoint (edit while Deprecated)

**Files:**
- Create: `Kartova.Catalog.Contracts/SetApplicationSuccessorRequest.cs`, `Kartova.Catalog.Application/SetApplicationSuccessorCommand.cs`, `Kartova.Catalog.Infrastructure/SetApplicationSuccessorHandler.cs`
- Modify: `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`, `CatalogModule.cs`, `CatalogAuditEntries.cs`
- Test: `Kartova.Catalog.IntegrationTests/SetApplicationSuccessorTests.cs` (new)

**Interfaces:**
- Produces: `PUT /api/v1/catalog/applications/{id}/successor` `{ Guid? SuccessorApplicationId }` → `ApplicationResponse`; audit `application.successor_changed`.

- [ ] **Step 1: Contract + command.** `SetApplicationSuccessorRequest(Guid? SuccessorApplicationId)` `[ExcludeFromCodeCoverage]`; `SetApplicationSuccessorCommand(ApplicationId Id, Guid? SuccessorApplicationId)`.
- [ ] **Step 2: Audit entry.** Add `CatalogAuditEntries.SuccessorChanged(app, Guid? from)` → action `application.successor_changed`, data `{ from, to }`.
- [ ] **Step 3: Failing real-seam tests** (`SetApplicationSuccessorTests`): set while Deprecated → 200; clear (`null`) → 200 (successor null); on Active/Decommissioned → 409; invalid successor id → 422; self → 400; non-member (not OrgAdmin, not team) → 403.
- [ ] **Step 4: Handler.** Load (RLS) → null→404; existence-validate successor → 422; capture `from = app.SuccessorApplicationId`; `app.SetSuccessor(cmd.SuccessorApplicationId)`; `SaveChanges`; `audit.AppendAsync(SuccessorChanged(app, from))`; return `ToResponse()`.
- [ ] **Step 5: Delegate + route.** Add `SetApplicationSuccessorAsync` (mirror `AssignApplicationTeamAsync`: `LoadAndAuthorizeApplicationAsync` team gate, `[FromBody] SetApplicationSuccessorRequest`). In `CatalogModule`: `tenant.MapPut("/applications/{id:guid}/successor", ...).RequireAuthorization(CatalogApplicationsLifecycleForward).WithName("SetApplicationSuccessor").Produces<ApplicationResponse>(200).ProducesProblem(400/403/404/409/422)`. Register `SetApplicationSuccessorHandler` in `RegisterServices`.
- [ ] **Step 6: Run + build.** (Docker) → PASS; `dotnet build` 0 warn/err.
- [ ] **Step 7: Commit.** `git commit -m "feat(catalog): PUT /applications/{id}/successor to set/clear successor while deprecated"`

### Task C5: Detail read enrichment (successor display name)

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetApplicationByIdHandler.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/` (the GetApplicationById test file; grep `GetApplicationById`)

- [ ] **Step 1: Failing test.** Deprecated app with a successor → `GET /applications/{id}` response carries `successorDisplayName` == the successor's display name; app without a successor → `successorApplicationId` null, `successorDisplayName` null.
- [ ] **Step 2: Implement.** After loading the app, if `SuccessorApplicationId is { } sid`, resolve the name (RLS-scoped) and attach:
```csharp
var resp = app.ToResponse();
if (app.SuccessorApplicationId is { } sid)
{
    var name = await db.Applications.Where(ApplicationSortSpecs.IdEquals(sid))
        .Select(a => a.DisplayName).FirstOrDefaultAsync(ct);
    resp = resp with { SuccessorDisplayName = name };
}
return resp;
```
(List handler unchanged — successor is not a list field, ADR-0107.)
- [ ] **Step 3: Run + build.** (Docker) → PASS; build green.
- [ ] **Step 4: Commit.** `git commit -m "feat(catalog): enrich successor display name on application detail read"`

### Task C6: Codegen + FE (picker, set-successor dialog, detail link)

**Files:**
- Modify: `web/src/features/catalog/schemas/deprecateApplication.ts`, `components/DeprecateConfirmDialog.tsx`, `pages/ApplicationDetailPage.tsx`, `api/applications.ts`, generated client + snapshot
- Create: `web/src/features/catalog/components/SetSuccessorDialog.tsx` (+ `__tests__/SetSuccessorDialog.test.tsx`)
- Test: `components/__tests__/DeprecateConfirmDialog.test.tsx`, `pages/__tests__/ApplicationDetailPage.test.tsx`

- [ ] **Step 1: Regenerate client** (API running) → `npm run codegen`; confirm `SetApplicationSuccessor`, `DeprecateApplicationRequest.successorApplicationId`, `ApplicationResponse.successorApplicationId/successorDisplayName`. Commit snapshot. (Flag *pending user verification* if API can't run.)
- [ ] **Step 2: Schema + deprecate picker.** `deprecateApplicationSchema` += `successorApplicationId: z.string().uuid().optional()`. In `DeprecateConfirmDialog`, add an optional `<EntitySearchCombobox kind="application" excludeId={application.id} onSelect={id => form.setValue("successorApplicationId", id)} />` (clearable); include the value in the deprecate payload. Test: selecting an app puts its id in the payload; omitting → absent.
- [ ] **Step 3: `useSetApplicationSuccessor` + `SetSuccessorDialog`.** New mutation → `PUT /applications/{id}/successor`, invalidates the app detail query. New dialog reuses `EntitySearchCombobox` (set) + a Clear action (submits `null`); toasts on 422/409. Test: set + clear call PUT with the right body; 422 → toast.
- [ ] **Step 4: Detail page.** When `application.successorApplicationId`, render a "Successor →" row linking `/catalog/applications/{successorApplicationId}` showing `successorDisplayName ?? "—"`; when Deprecated + `canManage`, a "Set/Change successor" button opening `SetSuccessorDialog`. Test (`ApplicationDetailPage.test`): renders the link when present; absent otherwise.
- [ ] **Step 5: Run FE tests + build.** `cd web && npm test` (catalog) + `npm run build` → green.
- [ ] **Step 6: Commit.** `git commit -m "feat(web): successor picker on deprecate, set-successor dialog, detail-page successor link"`

### Task C7: Docs — list-filter-registry + CHECKLIST

**Files:**
- Modify: `docs/design/list-filter-registry.md`, `docs/product/CHECKLIST.md`

- [ ] **Step 1: Registry opt-out row.** In the Applications list record, add a row for `successorApplicationId`: **column no / sort no / filter no** — "deprecation detail, surfaced on detail page only (ADR-0107 field-addition trigger; ADR-0110)."
- [ ] **Step 2: CHECKLIST.** Note §15.1 + §15.7 closed under E-02.F-01.S-04's follow-ups (cite this slice + ADR-0110). Do not fabricate summary-table counts.
- [ ] **Step 3: Commit.** `git commit -m "docs: list-filter-registry successor opt-out + CHECKLIST 15.1/15.7 closed"`

---

## Self-Review

**Spec coverage:** §15.1 → B1–B5; §15.7 → C1–C7 (+ ADR-0110 already committed); FU-1 → A1. Response fields (init-only) → C3. Field-addition trigger opt-out → C7. Detail enrichment → C5. Every §8 spec test artifact maps to a task's tests.

**Placeholder scan:** No TBD/TODO. Two deliberate "follow the existing pattern" pointers (integration-test seeding in `DecommissionApplicationTests`/`DeprecateApplicationTests`; the `invalid-team`→422 mapping to mirror for `invalid-successor`) — each names the exact file/precedent to copy, with the concrete assertions/behavior listed. Not placeholders (the novel logic carries full code).

**Type consistency:** `Decommission(clock, allowBeforeSunset)` (B2) used by B3 handler + B4 command. `Deprecate(sunsetDate, successorApplicationId, clock)` (C1) used by C3 handler; the old 2-arg call sites are updated in C1/C3. `SetSuccessor(Guid?)` (C1) used by C4. `SuccessorApplicationId`/`SuccessorDisplayName` init-only props (C3) consumed by C5 enrichment + C6 FE. Permission const `CatalogApplicationsLifecycleOverride` (B1) consumed by B4 + B5.

**Sequencing/independence:** A ships alone. B ships after A (or standalone). C depends on nothing in B except a green tree. Migration (C2) precedes any handler that reads/writes the column.

**Known-unknowns flagged for the implementer:** (1) whether permission policies auto-register from `All` (B4 step 3) — verify, register if not; (2) the `invalid-successor` 422 mapping mechanism — mirror `invalid-team` (C3 step 4); (3) `ArgumentException`→400 mapping already covers `Deprecate` — confirm (C3). These are confirm-or-mirror, not open design.
