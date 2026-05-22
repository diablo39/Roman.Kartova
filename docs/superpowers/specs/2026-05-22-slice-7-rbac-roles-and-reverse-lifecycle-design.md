# Slice 7 — RBAC permission model + reverse lifecycle (design)

**Date:** 2026-05-22
**Author:** Roman Głogowski (AI-assisted)
**Status:** Draft, pending user review
**Stories closed:** E-01.F-04.S-03 (RBAC with five roles), E-01.F-04.S-04 (SSO login via web UI), slice-5 §13.6 *partial* (backward lifecycle transitions — sunset-date override stays as residual follow-up).
**ADRs touched:** ADR-0073 gets a one-paragraph addendum documenting the reverse endpoints and the still-deferred admin override. No new ADRs.

---

## 1. Why this slice

Three slices into Phase 1 (slices 3–6) and an MSTest migration tooling slice (PR #23), the Application aggregate has full forward lifecycle (Active → Deprecated → Decommissioned, slice 5) but only the lightest authorization: any authenticated user can register, edit, deprecate, or decommission an Application. ADR-0008 promises five fixed roles with real authorization; ADR-0073 promises backward transitions for Org Admins. Neither is enforced today.

Slice 5 §13.6 registered "RBAC retrofit: backward transitions + admin override — Trigger: when E-01.F-04.S-03 ships". That's now.

This slice lands:

1. **Five-role realm.** Add `Viewer` and `TeamAdmin` to the KeyCloak realm (existing: `OrgAdmin`, `Member`, `platform-admin`). Seed dev users for the two new roles. `ServiceAccount` is a forward-compat constant only — ADR-0009 defers CLI/automation principals to Phase 5.
2. **Granular permission model.** Roles map to sets of granular permissions; ASP.NET policies check for the presence of an exact *permission* claim, not a *role*. The role→permission map lives in C# as the single source of truth.
3. **Permission-claim expansion** at JWT-validation time in `TenantClaimsTransformation` (already flattens `realm_access.roles[]` per slice 2 — we extend it).
4. **Catalog endpoints gated by permission**, replacing the slice-2 default `RequireAuthorization()` (authenticated-only) with named-permission policies.
5. **Two new OrgAdmin-only endpoints** for backward lifecycle transitions: `POST /applications/{id}/reactivate` and `POST /applications/{id}/un-decommission`, with matching domain methods on `Application`.
6. **SPA permission hook + UI gating.** `usePermissions()` hits `GET /me/permissions`; buttons/menus hide when the permission is absent. Reverse-lifecycle dialogs added for OrgAdmin.

This slice ships **no new entity surface** — the work is foundational. It is the last big Phase-0 foundation block before the entity rollout resumes (Service entity in a subsequent slice).

## 2. Context

- Slices 0–6 merged. Last functional slice: slice 6 (PR #22, 2026-05-07). Tooling slice: xUnit→MSTest v4 migration (PR #23, 2026-05-13). Today: 2026-05-22.
- KeyCloak realm has 3 of 5 ADR-0008 roles. 4 dev users (Alice/OrgAdmin, Mike/Member, Bob/OrgAdmin-orgb, platform-admin).
- `Kartova.SharedKernel.AspNetCore.TenantClaimsTransformation` already flattens `realm_access.roles[]` → `ClaimTypes.Role` and populates `ITenantContext`. Extension target is well-isolated (4 unit-test callers via codelens, no other code-path direct callers — production wires it via ASP.NET's `IClaimsTransformation` pipeline).
- `KartovaRoles` static class has 3 constants: `PlatformAdmin`, `OrgAdmin`, `Member`. Codelens reports exactly 2 in-code usages (`OrganizationModule.cs:49` for the `/me/admin-only` smoke, `ModuleRouteExtensions.cs:34` for the `/admin/*` group). Both stay outside the new permission model.
- Catalog endpoints (`POST /applications`, `PUT /applications/{id}`, `POST /applications/{id}/deprecate`, `POST /applications/{id}/decommission`, `GET /applications`, `GET /applications/{id:guid}`) inherit `RequireAuthorization()` from `MapTenantScopedModule(...)` — authenticated-only, no role/permission check.
- `Application` aggregate (codelens: implements only `ITenantOwned`, no siblings, no derived types) carries `Lifecycle`, `SunsetDate`, `Version`. Forward methods `Deprecate(sunsetDate, clock)` + `Decommission(clock)` exist; reverse methods do not.
- SPA OIDC flow (PKCE + sessionStorage) works end-to-end. `RequireAuth` redirects unauthenticated users to KeyCloak. `useCurrentUser()` exposes `{ userId, displayName, email, tenantId, accessToken }` — no role field.
- `IModule` implementers (codelens): `CatalogModule`, `OrganizationModule`. `OrganizationAdminModule` is `IModuleEndpoints`-only (platform-admin admin routes — outside the tenant scope, orthogonal to the permission model).

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | Five realm roles in KeyCloak: `PlatformAdmin`, `OrgAdmin`, `TeamAdmin`, `Member`, `Viewer`. `ServiceAccount` is a forward-compat C# constant only — no realm role yet (ADR-0009 defers to Phase 5). | Matches ADR-0008. Avoids realm churn when CLI auth ships. |
| 2 | Single source of truth for the role↔permission relationship is a static C# map `KartovaRolePermissions.Map: Role → Set<Permission>`. Each user has exactly one realm role; the map expands it into a permission set at JWT-validation time. `KartovaRoles.AtLeast(...)` does **not** exist — there is no role-hierarchy helper, only the explicit per-role map. | Hierarchy is data, not behavior. KeyCloak realm config stays role-only. Adding a new role = one map entry. |
| 3 | Authorization expressed as named *permissions* — string constants (`catalog.applications.register`, ...). Each permission is also the name of an ASP.NET policy whose body is `RequireClaim(KartovaClaims.Permission, "<permission>")`. Endpoints reference permissions by name (`RequireAuthorization(KartovaPermissions.CatalogApplicationsRegister)`). Policy name == permission name. | One concept, not two. Policies are uniform plumbing — adding a permission = one map-entry update + one endpoint binding. |
| 4 | `TenantClaimsTransformation` expands role claims → permission claims server-side at validation time. JWT stays role-only; the principal carries derived permission claims after the transformation. KeyCloak realm config never sees the permission catalog. | Future-proof — when permissions become per-entity (ownership/team-scoped), the expansion logic stays in C# and the JWT shape doesn't change. |
| 5 | SPA learns its permission set from `GET /api/v1/organization/me/permissions` (React Query cached for the active token's lifetime). SPA mirrors only the permission-name string constants — never the role→permission map. UI is hide-by-default (render only after `usePermissions()` resolves). | Single source of truth; no SPA drift on the *map*. The constant mirror is ~5 string literals + a committed JSON snapshot acting as drift sentinel. |
| 6 | TeamAdmin is forward-compat in this slice: realm role + dev user + C# constant + included in `KartovaRolePermissions.Map` with the same permission set as Member. No team-scoped enforcement (teams don't exist — E-03.F-02). | Avoids re-touching realm/JWT/policies when teams ship. TeamAdmin diverges from Member only when team-aggregate authorization lands. |
| 7 | Two new OrgAdmin-only endpoints: `POST /applications/{id}/reactivate` (empty body) and `POST /applications/{id}/un-decommission` (`{ sunsetDate }` body). `Reactivate` accepts Deprecated→Active and Decommissioned→Active. `UnDecommission` accepts only Decommissioned→Deprecated. | Endpoint-per-transition convention from slice 5 §3 Decision #7. Reverse skipping allowed (no migration window needed backward). |
| 8 | `Reactivate` clears `SunsetDate`. `UnDecommission` requires a strictly-future `sunsetDate` (mirrors `/deprecate` invariant). Past sunsetDate → 400. | Active never carries sunsetDate; Deprecated must always carry a future one (ADR-0073). |
| 9 | All lifecycle methods (forward + reverse) bump `Version`. No `If-Match` on lifecycle endpoints — the state precondition (`Lifecycle == X`) is the implicit version (preserves slice-5 §3 Decision #7). | Reverse transition invalidates any cached version held by other clients. |
| 10 | Sunset-date admin override on `/deprecate` is **not in scope**. `/deprecate` and `/decommission` keep strict-future / pre-sunset checks for everyone. Slice-5 §13.6 residual: "sunset override only" remains a registered follow-up. | Per user choice. Cleaner — no role-conditional branching inside handlers. |
| 11 | `KartovaRoles.PlatformAdmin` endpoints (`/admin/organizations` POST + `/organization/me/admin-only` GET) stay on `RequireRole(KartovaRoles.PlatformAdmin/OrgAdmin)`. They are not migrated to the permission model in this slice. | PlatformAdmin operates outside tenant scope (no `tenant_id` claim) and the `/me/admin-only` smoke is slice-2 infrastructure with intentional minimal surface. Tracked as §15.3 follow-up. |
| 12 | SPA `useCurrentUser()` stays as-is. Permissions come only from `usePermissions()` (separate hook calling `/me/permissions`). | One axis of authorization information per hook; smaller blast radius if the hook signature changes later. |
| 13 | Hide-by-default UI: action buttons render only after `usePermissions()` resolves AND the permission is present. On unexpected 403 from the API, surface a toast via the existing problem-details handler. Dialog stays open with inline alert so the user sees the failure. | No flash-of-forbidden content. Toast covers token-drift races. |
| 14 | One PR, sequenced commits per task. Closes the two stories (S-03 + S-04) and partially closes slice-5 §13.6. | Comparable scope to slice 5 (~25 files). |
| 15 | ADR-0073 gets a one-paragraph addendum documenting the reverse endpoints. No new ADR. | Reverse transitions are concrete implementation of the ADR's existing "backward transitions require Org Admin" rule. |

## 4. Permission catalog and role map

### 4.1 Permission constants

```csharp
namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaPermissions
{
    public const string CatalogRead                         = "catalog.read";
    public const string CatalogApplicationsRegister         = "catalog.applications.register";
    public const string CatalogApplicationsEditMetadata     = "catalog.applications.edit-metadata";
    public const string CatalogApplicationsLifecycleForward = "catalog.applications.lifecycle.forward";
    public const string CatalogApplicationsLifecycleReverse = "catalog.applications.lifecycle.reverse";

    public static IReadOnlyCollection<string> All { get; } = new[]
    {
        CatalogRead,
        CatalogApplicationsRegister,
        CatalogApplicationsEditMetadata,
        CatalogApplicationsLifecycleForward,
        CatalogApplicationsLifecycleReverse,
    };
}
```

### 4.2 Role → permission map

```csharp
public static class KartovaRolePermissions
{
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [KartovaRoles.Viewer]    = new HashSet<string>(StringComparer.Ordinal)
            {
                KartovaPermissions.CatalogRead,
            },
            [KartovaRoles.Member]    = new HashSet<string>(StringComparer.Ordinal)
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
            },
            [KartovaRoles.TeamAdmin] = new HashSet<string>(StringComparer.Ordinal)
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
            },
            [KartovaRoles.OrgAdmin]  = new HashSet<string>(StringComparer.Ordinal)
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
                KartovaPermissions.CatalogApplicationsLifecycleReverse,
            },
            // PlatformAdmin is orthogonal — no catalog permissions.
            // ServiceAccount has no realm role yet — no entry.
        };

    public static IReadOnlySet<string> ForRole(string role) =>
        Map.TryGetValue(role, out var perms) ? perms : EmptySet;

    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.Ordinal);
}
```

### 4.3 `KartovaRoles` constants

```csharp
public static class KartovaRoles
{
    public const string PlatformAdmin  = "platform-admin";
    public const string OrgAdmin       = "OrgAdmin";
    public const string TeamAdmin      = "TeamAdmin";       // NEW
    public const string Member         = "Member";
    public const string Viewer         = "Viewer";          // NEW
    public const string ServiceAccount = "ServiceAccount";  // NEW — forward-compat only, no realm role yet
}
```

### 4.4 Claim constant

```csharp
public static class KartovaClaims
{
    public const string TenantId    = "tenant_id";
    public const string RealmAccess = "realm_access";
    public const string Permission  = "kartova.permission";  // NEW
}
```

## 5. Authorization plumbing

### 5.1 Permission-claim expansion in `TenantClaimsTransformation`

```csharp
// After the existing realm-role flatten loop:
if (principal.Identity is ClaimsIdentity id)
{
    foreach (var role in roles)
    {
        foreach (var perm in KartovaRolePermissions.ForRole(role))
        {
            if (!id.HasClaim(KartovaClaims.Permission, perm))
                id.AddClaim(new Claim(KartovaClaims.Permission, perm));
        }
    }
}
```

The existing role-claim flatten stays unchanged. Production callers (ASP.NET's `IClaimsTransformation` pipeline) are unaffected; the 4 unit tests in `TenantClaimsTransformationTests` get new assertions per role.

### 5.2 Policy registration

```csharp
// src/Kartova.SharedKernel.AspNetCore/AuthorizationExtensions.cs
public static class AuthorizationExtensions
{
    public static AuthorizationBuilder AddKartovaPermissionPolicies(this AuthorizationBuilder b)
    {
        foreach (var perm in KartovaPermissions.All)
        {
            b.AddPolicy(perm, p => p.RequireClaim(KartovaClaims.Permission, perm));
        }
        return b;
    }
}
```

Wired from `JwtAuthenticationExtensions.AddKartovaJwtAuth` so every API host (currently `Kartova.Api`; future hosts inherit automatically):

```csharp
services.AddAuthorizationBuilder()
        .AddKartovaPermissionPolicies();
```

### 5.3 Catalog endpoint topology

```
POST   /api/v1/catalog/applications                        catalog.applications.register
GET    /api/v1/catalog/applications                        catalog.read
GET    /api/v1/catalog/applications/{id:guid}              catalog.read
PUT    /api/v1/catalog/applications/{id:guid}              catalog.applications.edit-metadata
POST   /api/v1/catalog/applications/{id:guid}/deprecate            catalog.applications.lifecycle.forward
POST   /api/v1/catalog/applications/{id:guid}/decommission         catalog.applications.lifecycle.forward
POST   /api/v1/catalog/applications/{id:guid}/reactivate           catalog.applications.lifecycle.reverse    [NEW]
POST   /api/v1/catalog/applications/{id:guid}/un-decommission      catalog.applications.lifecycle.reverse    [NEW]
```

Each `MapPost/Get/Put` call appends `.RequireAuthorization(KartovaPermissions.<...>)`. The previous default `.RequireAuthorization()` (from `MapTenantScopedModule`) is superseded — every catalog endpoint now requires an explicit permission claim.

### 5.4 Organization endpoint topology

```
GET    /api/v1/organization/me                  (authenticated-only — unchanged)
GET    /api/v1/organization/me/permissions      (authenticated-only — NEW)
GET    /api/v1/organization/me/admin-only       RequireRole(KartovaRoles.OrgAdmin)  (unchanged)
```

`GET /me/permissions` returns `MePermissionsResponse { string Role, IReadOnlyCollection<string> Permissions }` derived from the calling principal's claims (the principal already carries the expanded permission set after `TenantClaimsTransformation`).

### 5.5 PlatformAdmin endpoints (unchanged)

`POST /api/v1/admin/organizations` (`OrganizationAdminModule.MapPost`) stays on `RequireRole(KartovaRoles.PlatformAdmin)` — outside tenant scope, no permission-claim equivalent yet. Tracked as follow-up §15.3.

### 5.6 ADR-0073 addendum

One paragraph appended to ADR-0073's "Consequences" section:

> **Implementation note (slice 7, 2026-05-22, PR #XX):** Backward transitions land as two OrgAdmin-only endpoints: `POST /api/v1/catalog/applications/{id}/reactivate` (empty body; Deprecated→Active OR Decommissioned→Active; clears `sunsetDate`) and `POST /api/v1/catalog/applications/{id}/un-decommission` (`{sunsetDate}` body; Decommissioned→Deprecated; requires strictly-future sunsetDate). Authorization is enforced by the named permission `catalog.applications.lifecycle.reverse` (granted to OrgAdmin only via `KartovaRolePermissions.Map`). Forward transitions remain Member-or-higher. The "may not occur before sunset_date unless an admin overrides" exception on forward `Decommission` is **not** implemented in this slice — sunset-date admin override stays as a registered follow-up (slice 7 §15.1).

No status banner change.

## 6. Domain — reverse lifecycle

### 6.1 New domain methods on `Application`

```csharp
public void Reactivate()
{
    if (Lifecycle != Lifecycle.Deprecated && Lifecycle != Lifecycle.Decommissioned)
        throw new InvalidLifecycleTransitionException(Lifecycle, "Reactivate");

    Lifecycle  = Lifecycle.Active;
    SunsetDate = null;
    Version++;
}

public void UnDecommission(DateTimeOffset newSunsetDate, TimeProvider clock)
{
    if (Lifecycle != Lifecycle.Decommissioned)
        throw new InvalidLifecycleTransitionException(Lifecycle, "UnDecommission");

    if (newSunsetDate <= clock.GetUtcNow())
        throw new ArgumentException("sunsetDate must be strictly in the future", nameof(newSunsetDate));

    Lifecycle  = Lifecycle.Deprecated;
    SunsetDate = newSunsetDate;
    Version++;
}
```

`Application` carries `CreatedAt` but **no `UpdatedAt` field** (verified by codelens against the current aggregate). The `Version` bump is the only mutation marker; an `UpdatedAt` field is registered as §15.9 if it ever becomes necessary. `Reactivate` takes no parameters because the operation has no time-dependent check.

### 6.2 Invariant table (extends slice-5 §4.2)

| Operation | Pre-condition | Failure mode |
|---|---|---|
| `Reactivate` | `Lifecycle ∈ { Deprecated, Decommissioned }` | `InvalidLifecycleTransitionException(Lifecycle, "Reactivate")` → 409 |
| `UnDecommission` | `Lifecycle == Decommissioned` | `InvalidLifecycleTransitionException(Lifecycle, "UnDecommission")` → 409 |
| `UnDecommission` | `newSunsetDate > clock.GetUtcNow()` (strict) | `ArgumentException(nameof(newSunsetDate))` → 400 |

### 6.3 Lifecycle state machine after this slice

```
        ┌──────────── reactivate ─────────────┐  (OrgAdmin)
        │                                     │
        ▼                                     │
     Active ──deprecate──▶ Deprecated ──decommission──▶ Decommissioned
        ▲                       ▲                            │
        │                       └────── un-decommission ─────┘  (OrgAdmin, new sunsetDate)
        │                                                    │
        └──────────────────── reactivate ────────────────────┘  (OrgAdmin)
```

### 6.4 Commands / handlers / DTOs

| File | Shape |
|---|---|
| `src/Modules/Catalog/Kartova.Catalog.Application/ReactivateApplicationCommand.cs` + handler | `(ApplicationId Id)` |
| `src/Modules/Catalog/Kartova.Catalog.Application/UnDecommissionApplicationCommand.cs` + handler | `(ApplicationId Id, DateTimeOffset SunsetDate)` |
| `src/Modules/Catalog/Kartova.Catalog.Contracts/UnDecommissionApplicationRequest.cs` | `{ SunsetDate }` with `[ExcludeFromCodeCoverage]` |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | adds `ReactivateApplicationAsync`, `UnDecommissionApplicationAsync` |

No migration — schema is unchanged. Reverse transitions only flip the existing `lifecycle smallint` column and the existing `sunset_date timestamptz` column.

`InvalidLifecycleTransitionException` is reused as-is — it already carries `currentLifecycle` + `attemptedTransition` + optional `sunsetDate` + `reason`.

## 7. KeyCloak realm seed

### 7.1 Roles

`deploy/keycloak/kartova-realm.json` `roles.realm` becomes:

```json
[
  { "name": "OrgAdmin" },
  { "name": "TeamAdmin" },
  { "name": "Member" },
  { "name": "Viewer" },
  { "name": "platform-admin" }
]
```

### 7.2 Dev users (additions)

| Username | First / Last | tenant_id | realm role |
|---|---|---|---|
| `team-admin@orga.kartova.local` | Tanya TeamAdmin | `11111…` | `TeamAdmin` |
| `viewer@orga.kartova.local`      | Vera Viewer    | `11111…` | `Viewer`    |

Both share `tenant_id = 11111…` with Alice (OrgAdmin) and Mike (Member) so role-based authorization tests don't tangle with tenant-isolation tests.

### 7.3 Client config

`kartova-web` (SPA, PKCE) and `kartova-api` (audience mapper) are unchanged. Existing `tenant_id` user-attribute mapper and `audience-kartova-api` mapper flow into JWTs as before.

### 7.4 Realm-seed architecture rules

`tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs` gains:

- For each `KartovaRoles.*` constant except `ServiceAccount`, assert the role appears in `roles.realm[]`.
- For each role appearing in `KartovaRolePermissions.Map`, assert ≥1 dev user with that `realmRoles` entry exists in `users[]`.

## 8. SPA

### 8.1 New files

| File | Purpose |
|---|---|
| `web/src/shared/auth/permissions.ts` | String-literal constants mirroring `KartovaPermissions`. Type `KartovaPermission = typeof KartovaPermissions[keyof typeof KartovaPermissions]`. Imports drift-snapshot. |
| `web/src/shared/auth/permissions.snapshot.json` | Committed JSON list of permission names — drift sentinel. |
| `web/src/shared/auth/usePermissions.ts` | React Query hook calling `GET /api/v1/organization/me/permissions`. Returns `{ role: string \| null, hasPermission: (p: KartovaPermission) => boolean, isLoading: boolean }`. |
| `web/src/shared/auth/__tests__/usePermissions.test.tsx` | Per-role behavior, loading state, 401 handling. |
| `web/src/features/catalog/components/ReactivateConfirmDialog.tsx` | Plain confirm (empty body). |
| `web/src/features/catalog/components/UnDecommissionConfirmDialog.tsx` | Sunset-date picker (mirrors `DeprecateConfirmDialog`). |
| `web/src/features/catalog/schemas/unDecommissionApplication.ts` | zod schema (sunsetDate, future-only). |

### 8.2 Hook surface

```ts
const { hasPermission, isLoading, role } = usePermissions();

if (isLoading) return <Skeleton />;
return (
  <>
    {hasPermission(KartovaPermissions.CatalogApplicationsRegister) && <RegisterButton />}
    {hasPermission(KartovaPermissions.CatalogApplicationsLifecycleReverse) && <ReactivateMenuItem />}
  </>
);
```

`useCurrentUser()` is **not** extended — permissions are a separate concern with their own hook.

### 8.3 Gated UI sites

| File | Gating change |
|---|---|
| `web/src/features/catalog/pages/CatalogListPage.tsx` | "Register Application" button hidden unless `CatalogApplicationsRegister`. Toolbar checkbox + table itself remain visible (page is gated by `CatalogRead`). |
| `web/src/features/catalog/pages/ApplicationDetailPage.tsx` | Edit button hidden unless `CatalogApplicationsEditMetadata`. `LifecycleMenu` mounted only if `CatalogApplicationsLifecycleForward` OR `CatalogApplicationsLifecycleReverse`. |
| `web/src/features/catalog/components/LifecycleMenu.tsx` | Existing component gains two items: "Reactivate…" (visible iff `CatalogApplicationsLifecycleReverse` AND state ∈ {Deprecated, Decommissioned}) and "Restore to Deprecated…" (visible iff `CatalogApplicationsLifecycleReverse` AND state == Decommissioned). |
| `web/src/components/layout/AppLayout.tsx` (or `RequireAuth`) | After auth resolves, gate the protected shell on `CatalogRead`. Zero-permission users land on a "no access" page instead of an empty catalog. |
| `web/src/features/catalog/api/applications.ts` | Adds `useReactivateApplication`, `useUnDecommissionApplication` mutations. |

### 8.4 Hide-by-default semantics

- Before `usePermissions()` resolves: hide action buttons; show a skeleton on the page.
- After it resolves: show only what permission allows.
- On a 403 from the API (race / token drift): existing problem-details handler surfaces a toast "You don't have permission for this action". Mutation result reflects the error so the dialog stays open with inline alert.

### 8.5 Sign-in / sign-out

Unchanged from slice 5. `RequireAuth` redirects unauthenticated users to KeyCloak; `TopBar` dropdown calls `signoutRedirect()`. No signed-out landing page in this slice — S-04 narrative is satisfied by the existing OIDC redirect flow.

## 9. Implementation order (rough — finalised by writing-plans)

1. **`KartovaRoles` constants + `KartovaPermissions` constants + `KartovaClaims.Permission` constant.**
2. **`KartovaRolePermissions.Map`** + unit test asserting per-role expansion.
3. **`TenantClaimsTransformation`** permission-claim expansion + extended unit tests.
4. **`AuthorizationExtensions.AddKartovaPermissionPolicies`** + wired into `AddKartovaJwtAuth`.
5. **Catalog endpoint policy attachments** (six existing endpoints) + integration-test fixture support for issuing tokens as Viewer / TeamAdmin.
6. **`GET /me/permissions`** endpoint + integration test.
7. **Domain methods** `Application.Reactivate` and `Application.UnDecommission` + unit tests.
8. **Commands / handlers / DTOs / endpoint delegates** for reverse lifecycle.
9. **Two new reverse endpoints** with `RequireAuthorization(CatalogApplicationsLifecycleReverse)` + integration tests (happy + 409 + 400 + 403 + 401).
10. **KeyCloak realm seed** — add `Viewer`, `TeamAdmin` roles + two new dev users + arch-test rule updates.
11. **SPA `permissions.ts` constants + drift snapshot + `usePermissions` hook + Vitest.**
12. **SPA gated UI sites** (CatalogListPage, ApplicationDetailPage, LifecycleMenu, AppLayout).
13. **SPA reverse dialogs** + mutation hooks + Vitest.
14. **ADR-0073 addendum** referencing reverse endpoints.
15. **`CatalogPermissionMatrixTests`** — role × endpoint matrix driven off `KartovaRolePermissions.Map`.
16. **DoD pipeline** — full build, full tests, `docker compose up` real-HTTP path per-role, `/simplify`, mutation-sentinel + test-generator, requesting-code-review, `/pr-review-toolkit:review-pr`, `/deep-review`.
17. **Update `CHECKLIST.md`** for E-01.F-04.S-03 + S-04.

## 10. Tests inventory

| Layer | Project | File / scope |
|---|---|---|
| Unit (domain) | `Kartova.Catalog.Tests` | `ApplicationReactivateTests.cs` — Deprecated→Active clears SunsetDate; Decommissioned→Active clears SunsetDate; Active→Active 409; Version bumps. |
| Unit (domain) | `Kartova.Catalog.Tests` | `ApplicationUnDecommissionTests.cs` — Decommissioned→Deprecated sets SunsetDate; non-Decommissioned 409; past sunsetDate 400; strict-greater-than boundary; Version bumps. |
| Unit (claims) | `Kartova.SharedKernel.AspNetCore.Tests` | `TenantClaimsTransformationTests.cs` — extend 4 existing tests + add `Expands_role_claims_into_permission_claims` per role. |
| Unit (map) | `Kartova.SharedKernel.Tests` (new project iff absent, else colocate in `Kartova.SharedKernel.AspNetCore.Tests`) | `KartovaRolePermissionsTests.cs` — every role's expansion is non-empty (except PlatformAdmin/ServiceAccount); Viewer ⊂ Member; Member set equals TeamAdmin set; OrgAdmin uniquely owns `CatalogApplicationsLifecycleReverse`. |
| Architecture | `Kartova.ArchitectureTests` | `KeycloakRealmSeedRules.cs` — every `KartovaRoles.*` (except `ServiceAccount`) appears in `kartova-realm.json`; every role has ≥1 dev user. |
| Architecture | `Kartova.ArchitectureTests` | `KartovaPermissionsRules.cs` (new) — every `KartovaPermissions.*` is a value in some `KartovaRolePermissions.Map` entry (no orphan permissions); every map value ⊆ `KartovaPermissions.All`; TS snapshot equals C# `KartovaPermissions.All`. |
| Integration | `Kartova.Catalog.IntegrationTests` | `ReactivateApplicationTests.cs` — happy paths (Deprecated→Active, Decommissioned→Active); 409 wrong state; 403 for Member; 401 for unauthenticated. |
| Integration | `Kartova.Catalog.IntegrationTests` | `UnDecommissionApplicationTests.cs` — happy path; 409 wrong state; 400 past sunsetDate; 403 for Member; 401 for unauthenticated. |
| Integration | `Kartova.Catalog.IntegrationTests` | `CatalogPermissionMatrixTests.cs` — role × endpoint matrix (4 roles × 8 catalog endpoints = 32 cells) driven off `KartovaRolePermissions.Map` so adding a role/permission updates the matrix automatically. Each cell asserts allowed (any 2xx/4xx ≠ 403) or forbidden (403). |
| Integration | `Kartova.Organization.IntegrationTests` | `GetMePermissionsTests.cs` — `/me/permissions` returns the right set per role; 401 for unauthenticated. |
| SPA component | `web/src/shared/auth/__tests__/usePermissions.test.tsx` | per-role permission set; loading state; 401 transition. |
| SPA component | `web/src/features/catalog/components/__tests__/LifecycleMenu.test.tsx` | state × role matrix (existing tests + reverse-item visibility). |
| SPA component | `web/src/features/catalog/pages/__tests__/{ApplicationDetailPage,CatalogListPage}.test.tsx` | Viewer / Member / OrgAdmin gating. |

### 10.1 Token-issuing test helpers

The existing Testcontainers KeyCloak fixture issues tokens for `admin@orga` (OrgAdmin) and `member@orga` (Member). It gains two new helpers for `viewer@orga` and `team-admin@orga`. Helpers are co-located with the existing ones in `KeycloakContainerTestBase` (slice-2 infrastructure).

### 10.2 Drift sentinel (TS ↔ C# permission name constants)

`KartovaPermissionsRules.cs` asserts the C# `KartovaPermissions.All` list equals the JSON snapshot at `web/src/shared/auth/permissions.snapshot.json` (committed). The TypeScript file imports the snapshot. Single source of truth. Fails CI if either side drifts.

## 11. Definition of Done

CLAUDE.md-numbered, evidence to capture:

1. **Solution build with `TreatWarningsAsErrors=true`** — 0 warnings, 0 errors. Capture `dotnet build` output.
2. **Per-task subagent reviews** (spec-compliance + code-quality) — invoked on each task, never skipped.
3. **`superpowers:requesting-code-review`** at slice boundary against full branch diff with this spec + plan as context.
4. **Full test suite green** — unit + architecture + integration (Testcontainers + KeyCloak). Capture `dotnet test` summary + Vitest summary.
5. **`docker compose up` + real HTTP** — qualifies because middleware/policy changes touch every endpoint. Capture per-role login + `GET /me/permissions` + happy and forbidden mutations for at least Viewer / Member / OrgAdmin.
6. **`/simplify`** against branch diff — reuse / quality / efficiency lenses. Should-fix items addressed or explicitly skipped with rationale.
7. **Mutation feedback loop** — `mutation-sentinel` against changed files, `test-generator` until survivors are killed or accepted. Score ≥80%.
8. **`/pr-review-toolkit:review-pr`** skill.
9. **`/deep-review`** against branch diff with spec / plan / ADRs / tests. Blocking + Should-fix addressed; nits triaged.

Until all nine green, status is "implementation staged, verification pending" — not "slice 7 complete".

## 12. Success criteria

- ✅ KeyCloak realm carries all five ADR-0008 roles (minus ServiceAccount, intentionally deferred). Dev users exist for each org-A role.
- ✅ `KartovaPermissions` defines 5 constants; `KartovaRolePermissions.Map` covers Viewer / Member / TeamAdmin / OrgAdmin with correct sets.
- ✅ `TenantClaimsTransformation` expands role claims into permission claims; existing tests pass; new per-role test cases assert exact expansion.
- ✅ `GET /api/v1/organization/me/permissions` returns the right shape per role.
- ✅ Catalog endpoints require named-permission policies; Viewer gets 403 on POST / PUT / lifecycle; Member gets 200/201 on forward + 403 on reverse; OrgAdmin gets 200 on all.
- ✅ `POST /applications/{id}/reactivate` and `POST /applications/{id}/un-decommission` exist and obey the §6.2 invariants.
- ✅ SPA hides Register/Edit/Lifecycle buttons by default; renders them only after `usePermissions()` resolves and the permission is present. 403s surface as toast + inline alert.
- ✅ ADR-0073 has the §5.6 addendum.
- ✅ Solution builds with `TreatWarningsAsErrors=true`. Full test suite green.
- ✅ Mutation score ≥80%.
- ✅ `CHECKLIST.md` updated for E-01.F-04.S-03 and E-01.F-04.S-04. Slice-5 §13.6 note: "backward transitions landed in slice 7 — PR #XX; sunset-date admin override remains follow-up".

## 13. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Permission-claim expansion bloats principal claims (5 per Member, 5 per OrgAdmin) on every request | Token-level claims unchanged (JWT remains role-only). Principal claims are in-memory after `TenantClaimsTransformation` — no wire-format impact. |
| Adding a new permission requires touching the map in ≥4 places (constant, `All`, each role's set) | Codified in `KartovaPermissionsRules.cs` (arch test): every constant must appear in `All` and in at least one role's set. Test fails fast on orphan constant. |
| SPA mirror table drifts from C# constants | Drift sentinel arch test (§10.2) compares `KartovaPermissions.All` to the JSON snapshot. CI fails on drift. |
| `usePermissions` Vitest pass while production fails because of unseeded `/me/permissions` mock | Integration test `GetMePermissionsTests` against real API + KeyCloak Testcontainers covers the production path. |
| Backward transitions break domain assumptions in downstream code (e.g., audit-log slice assumes one-way state machine) | Audit-log slice (E-01.F-03.S-03) is not built yet — no consumer to break. Forward consumers (graphs / scorecards / notifications, all unbuilt) will design around the four-direction state machine when they ship. |
| TeamAdmin forward-compat creates false sense of "team admin works today" | Spec §3 Decision #6 documents the forward-compat status explicitly. SPA UX is identical to Member — no team-specific surface yet. |

## 14. Self-review

**Placeholder scan:** No "TBD" or "TODO" tokens. The `Kartova.SharedKernel.Tests` project location is intentionally provisional ("iff absent, else colocate") — resolved at implementation time without driving design.

**Type / contract consistency:**

- `KartovaPermissions.*` names consistent across §4.1 (constants), §4.2 (map keys), §5.3 + §5.4 (endpoint bindings), §10 (test names), §12 (success criteria).
- `MePermissionsResponse` shape consistent across §5.4 (endpoint), §8.2 (SPA hook), §10 (test target).
- `Reactivate` / `UnDecommission` parameter shapes consistent across §6.1 (domain), §6.4 (command), §8.3 (SPA mutation).
- `KartovaClaims.Permission = "kartova.permission"` consistent across §4.4 (constant), §5.1 (transformation), §5.2 (policy body).

**Scope check:** ~25 files modified/added — comparable to slice 5 (37). Single PR is the right shape. Permission model + reverse lifecycle bundle is justified because they share dependencies: reverse endpoints exist *because* OrgAdmin has a permission to call them; without the permission model, the endpoints are unreachable to authorize correctly.

**Ambiguity check:**

- "Hide-by-default UI" semantics fully specified in §8.4 (no flash + toast on race).
- "Per-role" tests for permission expansion are enumerated exactly in §10 (Viewer / Member / TeamAdmin / OrgAdmin — 4 cases, not "per role").
- Reverse-transition allowed sources for `Reactivate` (both Deprecated and Decommissioned) explicitly stated in §3 Decision #7 and §6.1; not left implicit.

**Internal consistency:**

- Decision #2 (no `AtLeast` helper) is consistent with §4.2 (explicit per-role sets, no programmatic union).
- Decision #6 (TeamAdmin forward-compat) is consistent with §4.2 (`TeamAdmin` map entry equals `Member`).
- Decision #10 (no sunset override) is consistent with §5.3 (only two new reverse endpoints; existing forward endpoints unchanged) and §13 risks + §15.1 (admin-override remains follow-up).
- Decision #11 (PlatformAdmin endpoints stay on `RequireRole`) is consistent with §5.5 (explicit no-change call-out).
- Decision #12 (`useCurrentUser` unchanged) is consistent with §8.2 (`usePermissions` is a separate hook).

**Scope compared to other slices:** between slice 5 (~37 files) and slice 6 (~15 files). Closer to slice 5 in shape (mixed-stack: backend + SPA + realm seed + tests). Justified bundle — permission model and reverse-endpoint policy share the same authorization layer.

## 15. Follow-ups (registered for future planning, not in scope)

### 15.1 Sunset-date admin override

**Why:** ADR-0073 says "may not occur before sunset_date unless an admin overrides (logged in audit)". Slice 7 lands backward transitions but leaves the override on `Deprecate`/`Decommission` strict-future for everyone.

**Trigger:** When MiFID II audit-log (E-01.F-03.S-03) ships — the override needs an audit sink. Standalone earlier if a real user reports needing to deprecate with a past sunsetDate.

**Effort:** ~half-day backend + ~quarter-day SPA.

**Carry-forward from:** slice-5 §13.6 (this slice closes the "backward transitions" half of that follow-up).

### 15.2 ServiceAccount role wiring

**Why:** ADR-0009 promises CLI/automation principals as a first-class role. `KartovaRoles.ServiceAccount` is a forward-compat constant; realm has no role yet.

**Trigger:** Phase 5 — E-13.F-01.S-02 (CLI authentication with service account JWT).

**Effort:** ~half-day to seed role + permission map entry + KeyCloak client config.

### 15.3 PlatformAdmin endpoints to permission model

**Why:** `POST /admin/organizations` and `/me/admin-only` stay on `RequireRole`. The permission model would handle them uniformly with named permissions like `platform.organizations.create`.

**Trigger:** When platform-admin surface grows beyond two endpoints (likely with a platform-admin dashboard slice).

**Effort:** ~quarter-day per endpoint.

### 15.4 Team-scoped permissions for TeamAdmin

**Why:** TeamAdmin is forward-compat in slice 7 — same permission set as Member. ADR-0008 promises TeamAdmin "manages their team's entities and members" which requires team-scoped authorization (per-entity ownership check).

**Trigger:** E-03.F-02 (Team Management) ships.

**Effort:** ~1 day — adds team-ownership claim expansion + per-entity authorization in handlers.

### 15.5 Audit log on lifecycle transitions

**Why:** ADR-0073 says transitions are audit-logged. Audit table is E-01.F-03.S-03, unbuilt. Reverse transitions are particularly audit-sensitive.

**Trigger:** When E-01.F-03.S-03 ships.

**Effort:** ~half-day per transition (forward + reverse).

**Carry-forward from:** slice-5 §13.5.

### 15.6 Notifications on lifecycle transitions

**Why:** ADR-0073 + ADR-0047 says transitions notify dependents. Notification infra is E-06, unbuilt.

**Trigger:** When E-06 + E-04 are both available.

**Effort:** ~half-day.

**Carry-forward from:** slice-5 §13.7.

### 15.7 Successor reference on Deprecated transitions

**Why:** ADR-0073 says Deprecated entities "MUST include a sunset_date and a successor reference (where applicable)". Slice 5 honored sunset; successor entirely deferred.

**Trigger:** With slice giving the field a consumer (notification fan-out E-06 or relationship graph E-04 — whichever ships first).

**Effort:** ~1 day backend + ~half-day SPA picker.

**Carry-forward from:** slice-5 §13.4.

### 15.8 `Application.UpdatedAt` field

**Why:** The aggregate currently tracks `CreatedAt` + `Version` only. A mutation timestamp ("last modified") becomes useful when displaying lifecycle history or sorting catalog by recency.

**Trigger:** First feature that needs to surface "last changed" in UI or sort — likely the activity feed (E-06.F-01).

**Effort:** ~quarter-day — add field, migration, set on every mutating domain method.

### 15.9 Signed-out landing page

**Why:** Today, unauthenticated users redirect immediately to KeyCloak via `RequireAuth`. A signed-out landing page with "Sign in" CTA, product framing, and pricing/CTA links is standard SaaS UX.

**Trigger:** Marketing-ready milestone (likely E-09 onboarding wizard, which already needs an entry surface).

**Effort:** ~half-day design + ~half-day implementation.

---

**End of design.**
