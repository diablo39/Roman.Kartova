# Slice 7 — RBAC permission model + reverse lifecycle (implementation plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land a granular permission model (role → permission set) with named ASP.NET policies, gate every Catalog endpoint by permission, add two OrgAdmin-only reverse-lifecycle endpoints (`/reactivate`, `/un-decommission`), and surface the model in the SPA via `usePermissions()` + hide-by-default UI gating. Closes E-01.F-04.S-03, E-01.F-04.S-04, and the "backward transitions" half of slice-5 §13.6.

**Architecture:** Permissions are string constants (`catalog.applications.register`, ...) with a single C# `KartovaRolePermissions.Map: Role → Set<Permission>` as the source of truth. `TenantClaimsTransformation` expands role claims into permission claims at JWT-validation time. ASP.NET policies are registered with one entry per permission (policy name == permission name) and bound via `RequireAuthorization(<permission>)` on endpoints. The SPA learns its permission set from `GET /me/permissions` (React Query cached), renders hide-by-default, and the TS permission-name constants are kept in sync with C# via a committed JSON snapshot acting as a drift sentinel.

**Tech Stack:** .NET 10 / ASP.NET Core / EF Core / Wolverine · KeyCloak (realm seed JSON) · PostgreSQL (RLS, `xmin` rowversion) · React 19 / TypeScript / Vite / React Query / Untitled UI / react-aria-components · MSTest v4 (native asserts) + NSubstitute + Testcontainers / Microsoft.Extensions.TimeProvider.Testing · Vitest.

**Spec:** `docs/superpowers/specs/2026-05-22-slice-7-rbac-roles-and-reverse-lifecycle-design.md`

---

## File Structure

### Backend — new files

| Path | Responsibility |
|---|---|
| `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` | Permission name constants + `All` collection. |
| `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` | `Map: Role → IReadOnlySet<Permission>` + `ForRole(role)` accessor. Single source of truth. |
| `src/Kartova.SharedKernel.AspNetCore/AuthorizationExtensions.cs` | `AddKartovaPermissionPolicies(this AuthorizationBuilder)` — registers one policy per permission. |
| `src/Modules/Organization/Kartova.Organization.Contracts/MePermissionsResponse.cs` | DTO `{ Role, Permissions }` for `GET /me/permissions`. |
| `src/Modules/Catalog/Kartova.Catalog.Application/ReactivateApplicationCommand.cs` | Wolverine command record `(ApplicationId Id)`. |
| `src/Modules/Catalog/Kartova.Catalog.Application/ReactivateApplicationHandler.cs` | Loads `Application`, calls `Reactivate()`, saves. |
| `src/Modules/Catalog/Kartova.Catalog.Application/UnDecommissionApplicationCommand.cs` | Wolverine command record `(ApplicationId Id, DateTimeOffset SunsetDate)`. |
| `src/Modules/Catalog/Kartova.Catalog.Application/UnDecommissionApplicationHandler.cs` | Loads `Application`, calls `UnDecommission(...)`, saves. |
| `src/Modules/Catalog/Kartova.Catalog.Contracts/UnDecommissionApplicationRequest.cs` | DTO `{ SunsetDate }` with `[ExcludeFromCodeCoverage]`. |
| `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationReactivateTests.cs` | Domain unit tests for `Reactivate()`. |
| `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationUnDecommissionTests.cs` | Domain unit tests for `UnDecommission(...)`. |
| `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ReactivateApplicationTests.cs` | HTTP integration tests for `POST /reactivate`. |
| `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/UnDecommissionApplicationTests.cs` | HTTP integration tests for `POST /un-decommission`. |
| `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs` | 4 roles × 8 endpoints matrix; data-driven from `KartovaRolePermissions.Map`. |
| `src/Modules/Organization/Kartova.Organization.IntegrationTests/GetMePermissionsTests.cs` | Integration tests for `GET /me/permissions`. |
| `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs` | Unit tests for the `Map` shape (Viewer ⊂ Member, OrgAdmin owns reverse, etc.). New project iff absent — see Task 4. |
| `tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs` | Arch tests: every permission appears in ≥1 role's set; TS snapshot equals C# `KartovaPermissions.All`. |

### Backend — modified files

| Path | Change |
|---|---|
| `src/Kartova.SharedKernel/Multitenancy/KartovaRoles.cs` | Add `TeamAdmin`, `Viewer`, `ServiceAccount` constants. |
| `src/Kartova.SharedKernel/Multitenancy/KartovaClaims.cs` | Add `Permission` constant. |
| `src/Kartova.SharedKernel.AspNetCore/TenantClaimsTransformation.cs` | After role-claim flatten, expand role → permission claims via `KartovaRolePermissions.ForRole(...)`. |
| `src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs` | After `services.AddAuthorization()`, call `services.AddAuthorizationBuilder().AddKartovaPermissionPolicies()`. |
| `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs` | Add `Reactivate()` and `UnDecommission(DateTimeOffset, TimeProvider)` methods. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` | Attach `RequireAuthorization(KartovaPermissions.X)` to all 6 existing catalog endpoints + register 2 new endpoints. Register two new handlers in DI. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | Add `ReactivateApplicationAsync`, `UnDecommissionApplicationAsync` delegates. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs` | Map `GET /me/permissions` endpoint. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationEndpointDelegates.cs` | Add `GetMePermissionsAsync` delegate. |
| `tests/Kartova.SharedKernel.AspNetCore.Tests/TenantClaimsTransformationTests.cs` | Extend 4 existing tests for permission-claim expansion + add per-role expansion tests. |
| `tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs` | Add: every `KartovaRoles.*` (except `ServiceAccount`) appears in realm JSON; every role in `KartovaRolePermissions.Map` has ≥1 dev user. |
| `docs/architecture/decisions/ADR-0073-enforced-entity-lifecycle-states.md` | Append §5.6 addendum text from spec. |
| `docs/product/CHECKLIST.md` | Mark E-01.F-04.S-03 + S-04 complete. Update slice-5 §13.6 note. |
| `deploy/keycloak/kartova-realm.json` | Add `TeamAdmin` + `Viewer` realm roles + two dev users. |

### Frontend — new files

| Path | Responsibility |
|---|---|
| `web/src/shared/auth/permissions.snapshot.json` | Committed list of permission names — drift sentinel. |
| `web/src/shared/auth/permissions.ts` | `KartovaPermissions` constants typed from snapshot. |
| `web/src/shared/auth/usePermissions.ts` | React Query hook calling `GET /api/v1/organizations/me/permissions`. |
| `web/src/shared/auth/__tests__/usePermissions.test.tsx` | Per-role permission set; loading; 401 handling. |
| `web/src/features/catalog/components/ReactivateConfirmDialog.tsx` | Empty-body confirm. |
| `web/src/features/catalog/components/UnDecommissionConfirmDialog.tsx` | Sunset-date picker (mirrors `DeprecateConfirmDialog`). |
| `web/src/features/catalog/components/__tests__/ReactivateConfirmDialog.test.tsx` | Confirm path + cancel + mutation error. |
| `web/src/features/catalog/components/__tests__/UnDecommissionConfirmDialog.test.tsx` | Sunset-date validation + confirm + cancel + mutation error. |
| `web/src/features/catalog/schemas/unDecommissionApplication.ts` | zod schema (sunsetDate, future-only). |

### Frontend — modified files

| Path | Change |
|---|---|
| `web/src/features/catalog/api/applications.ts` | Add `useReactivateApplication`, `useUnDecommissionApplication` mutation hooks. |
| `web/src/features/catalog/components/LifecycleMenu.tsx` | Add "Reactivate…" + "Restore to Deprecated…" items gated by reverse permission. |
| `web/src/features/catalog/pages/CatalogListPage.tsx` | Gate "Register Application" button by `CatalogApplicationsRegister`. |
| `web/src/features/catalog/pages/ApplicationDetailPage.tsx` | Gate Edit button + LifecycleMenu mounting by relevant permissions. |
| `web/src/components/layout/AppLayout.tsx` | Gate the protected shell on `CatalogRead`; render "no access" placeholder for zero-permission users. |
| `web/src/components/layout/__tests__/AppLayout.test.tsx` (new, if absent) | Permission-gating tests. |

---

## Tasks

### Task 1: KeyCloak realm seed — add Viewer + TeamAdmin roles and dev users

**Files:**
- Modify: `deploy/keycloak/kartova-realm.json`
- Modify: `tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs`

**Why first:** Subsequent integration tests need tokens issued for `viewer@orga` and `team-admin@orga`. Seeding the realm first means the KeyCloak Testcontainer comes up with all five org-A identities available.

- [ ] **Step 1: Write the failing arch test for new realm roles**

Open `tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs` and add a test method that loads the realm JSON and asserts each new role + each new dev user:

```csharp
[TestMethod]
public void Realm_seed_includes_Viewer_and_TeamAdmin_roles_and_dev_users()
{
    var realm = LoadRealmJson();   // existing helper

    var roles = realm.RootElement
        .GetProperty("roles")
        .GetProperty("realm")
        .EnumerateArray()
        .Select(r => r.GetProperty("name").GetString())
        .ToHashSet(StringComparer.Ordinal);

    Assert.IsTrue(roles.Contains("Viewer"), "Realm must include 'Viewer' role.");
    Assert.IsTrue(roles.Contains("TeamAdmin"), "Realm must include 'TeamAdmin' role.");

    var usernames = realm.RootElement
        .GetProperty("users")
        .EnumerateArray()
        .Select(u => u.GetProperty("username").GetString())
        .ToHashSet(StringComparer.Ordinal);

    Assert.IsTrue(usernames.Contains("viewer@orga.kartova.local"),
        "Realm must include a 'viewer@orga' dev user.");
    Assert.IsTrue(usernames.Contains("team-admin@orga.kartova.local"),
        "Realm must include a 'team-admin@orga' dev user.");
}
```

If `LoadRealmJson()` does not exist, write a small helper that reads from `deploy/keycloak/kartova-realm.json` relative to `Directory.GetCurrentDirectory()` walked up to repo root.

- [ ] **Step 2: Run the new arch test and verify it fails**

```bash
dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter "FullyQualifiedName~Realm_seed_includes_Viewer_and_TeamAdmin"
```

Expected: FAIL — realm currently has only `OrgAdmin`, `Member`, `platform-admin` and no `viewer@orga` or `team-admin@orga` users.

- [ ] **Step 3: Update `deploy/keycloak/kartova-realm.json`**

In the `roles.realm` array, after the `Member` entry, add:

```json
{ "name": "TeamAdmin" },
{ "name": "Viewer" },
```

In the `users` array, after the existing `admin@orgb.kartova.local` entry and before `platform-admin@kartova.local`, add:

```json
{
  "username": "team-admin@orga.kartova.local",
  "enabled": true,
  "emailVerified": true,
  "email": "team-admin@orga.kartova.local",
  "firstName": "Tanya",
  "lastName": "TeamAdmin",
  "attributes": {
    "tenant_id": ["11111111-1111-1111-1111-111111111111"]
  },
  "credentials": [
    { "type": "password", "value": "dev_pass", "temporary": false }
  ],
  "realmRoles": ["TeamAdmin"]
},
{
  "username": "viewer@orga.kartova.local",
  "enabled": true,
  "emailVerified": true,
  "email": "viewer@orga.kartova.local",
  "firstName": "Vera",
  "lastName": "Viewer",
  "attributes": {
    "tenant_id": ["11111111-1111-1111-1111-111111111111"]
  },
  "credentials": [
    { "type": "password", "value": "dev_pass", "temporary": false }
  ],
  "realmRoles": ["Viewer"]
},
```

- [ ] **Step 4: Re-run the arch test and verify it passes**

```bash
dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter "FullyQualifiedName~Realm_seed_includes_Viewer_and_TeamAdmin"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add deploy/keycloak/kartova-realm.json tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs
git commit -m "feat(realm): seed Viewer and TeamAdmin dev users (slice 7)"
```

---

### Task 2: KartovaRoles + KartovaClaims constant additions

**Files:**
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaRoles.cs`
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaClaims.cs`
- Test: `tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs` (extend the existing "every C# role appears in realm" rule)

- [ ] **Step 1: Write a failing arch test that links C# `KartovaRoles` constants to the realm JSON**

Add a method to `KeycloakRealmSeedRules.cs`:

```csharp
[TestMethod]
public void Every_KartovaRoles_constant_except_ServiceAccount_appears_in_realm_seed()
{
    var realmRoles = LoadRealmJson()
        .RootElement.GetProperty("roles").GetProperty("realm")
        .EnumerateArray()
        .Select(r => r.GetProperty("name").GetString()!)
        .ToHashSet(StringComparer.Ordinal);

    var constantValues = typeof(KartovaRoles)
        .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlatHierarchy)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (Name: f.Name, Value: (string)f.GetRawConstantValue()!))
        .Where(t => t.Name != nameof(KartovaRoles.ServiceAccount))
        .ToArray();

    foreach (var (name, value) in constantValues)
    {
        Assert.IsTrue(realmRoles.Contains(value),
            $"KartovaRoles.{name} = '{value}' has no matching entry in kartova-realm.json roles.realm.");
    }
}
```

- [ ] **Step 2: Run the new arch test, verify it fails with a compile error**

Expected: BUILD FAIL — `KartovaRoles.ServiceAccount`, `TeamAdmin`, `Viewer` don't exist yet.

- [ ] **Step 3: Add the missing constants**

Replace `src/Kartova.SharedKernel/Multitenancy/KartovaRoles.cs` with:

```csharp
namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaRoles
{
    public const string PlatformAdmin  = "platform-admin";
    public const string OrgAdmin       = "OrgAdmin";
    public const string TeamAdmin      = "TeamAdmin";
    public const string Member         = "Member";
    public const string Viewer         = "Viewer";
    public const string ServiceAccount = "ServiceAccount";  // forward-compat — no realm role yet (ADR-0009)
}
```

In `src/Kartova.SharedKernel/Multitenancy/KartovaClaims.cs`, add a `Permission` constant alongside the existing ones:

```csharp
public const string Permission = "kartova.permission";
```

- [ ] **Step 4: Re-run the arch test, verify PASS**

```bash
dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter "FullyQualifiedName~Every_KartovaRoles_constant"
```

Expected: PASS — all five non-ServiceAccount constants now match realm entries.

- [ ] **Step 5: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/KartovaRoles.cs src/Kartova.SharedKernel/Multitenancy/KartovaClaims.cs tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs
git commit -m "feat(roles): add TeamAdmin, Viewer, ServiceAccount + Permission claim constant"
```

---

### Task 3: KartovaPermissions name constants

**Files:**
- Create: `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs`
- Test: `tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs` (new file)

- [ ] **Step 1: Create the architecture-rule test file with a failing test**

Create `tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs`:

```csharp
using System.Reflection;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.ArchitectureTests;

[TestClass]
public sealed class KartovaPermissionsRules
{
    [TestMethod]
    public void All_collection_contains_every_public_string_constant()
    {
        var declared = typeof(KartovaPermissions)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlatHierarchy)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var v in declared)
        {
            Assert.IsTrue(KartovaPermissions.All.Contains(v),
                $"KartovaPermissions.All must list every declared constant — missing '{v}'.");
        }

        Assert.AreEqual(declared.Count, KartovaPermissions.All.Count,
            "KartovaPermissions.All must not contain entries that are not declared as constants.");
    }
}
```

- [ ] **Step 2: Run, verify FAIL**

```bash
dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter "FullyQualifiedName~KartovaPermissionsRules.All_collection"
```

Expected: BUILD FAIL — `KartovaPermissions` does not exist yet.

- [ ] **Step 3: Create `KartovaPermissions.cs`**

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

- [ ] **Step 4: Re-run, verify PASS**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs
git commit -m "feat(perm): add KartovaPermissions name constants + arch rule"
```

---

### Task 4: KartovaRolePermissions map + unit tests

**Files:**
- Create: `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs`
- Test: `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs` (new test project) OR colocate in `tests/Kartova.SharedKernel.AspNetCore.Tests` if `Kartova.SharedKernel.Tests` does not exist.

> **Project decision:** run `ls tests/` first. If `Kartova.SharedKernel.Tests` exists, create the file there. Otherwise create the file in `tests/Kartova.SharedKernel.AspNetCore.Tests/` (which already exists and references the same SharedKernel assembly). Do not create a new project for this slice.

- [ ] **Step 1: Write the failing unit tests**

```csharp
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.SharedKernel.AspNetCore.Tests;   // or .SharedKernel.Tests if that project exists

[TestClass]
public sealed class KartovaRolePermissionsTests
{
    [TestMethod]
    public void Viewer_can_read_catalog_only()
    {
        var perms = KartovaRolePermissions.ForRole(KartovaRoles.Viewer);
        Assert.AreEqual(1, perms.Count);
        Assert.IsTrue(perms.Contains(KartovaPermissions.CatalogRead));
    }

    [TestMethod]
    public void Member_can_read_register_edit_forward_lifecycle()
    {
        var perms = KartovaRolePermissions.ForRole(KartovaRoles.Member);
        Assert.IsTrue(perms.Contains(KartovaPermissions.CatalogRead));
        Assert.IsTrue(perms.Contains(KartovaPermissions.CatalogApplicationsRegister));
        Assert.IsTrue(perms.Contains(KartovaPermissions.CatalogApplicationsEditMetadata));
        Assert.IsTrue(perms.Contains(KartovaPermissions.CatalogApplicationsLifecycleForward));
        Assert.IsFalse(perms.Contains(KartovaPermissions.CatalogApplicationsLifecycleReverse));
    }

    [TestMethod]
    public void TeamAdmin_set_equals_Member_set_in_slice_7()
    {
        var member    = KartovaRolePermissions.ForRole(KartovaRoles.Member);
        var teamAdmin = KartovaRolePermissions.ForRole(KartovaRoles.TeamAdmin);
        CollectionAssert.AreEquivalent(member.ToList(), teamAdmin.ToList(),
            "TeamAdmin is forward-compat in slice 7; should match Member.");
    }

    [TestMethod]
    public void OrgAdmin_uniquely_owns_reverse_lifecycle()
    {
        var orgAdmin = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
        Assert.IsTrue(orgAdmin.Contains(KartovaPermissions.CatalogApplicationsLifecycleReverse));

        foreach (var role in new[] { KartovaRoles.Viewer, KartovaRoles.Member, KartovaRoles.TeamAdmin })
        {
            var perms = KartovaRolePermissions.ForRole(role);
            Assert.IsFalse(perms.Contains(KartovaPermissions.CatalogApplicationsLifecycleReverse),
                $"{role} must not have reverse-lifecycle permission.");
        }
    }

    [TestMethod]
    public void Viewer_subset_of_Member()
    {
        var viewer = KartovaRolePermissions.ForRole(KartovaRoles.Viewer);
        var member = KartovaRolePermissions.ForRole(KartovaRoles.Member);
        Assert.IsTrue(viewer.IsSubsetOf(member));
    }

    [TestMethod]
    public void Member_subset_of_OrgAdmin()
    {
        var member   = KartovaRolePermissions.ForRole(KartovaRoles.Member);
        var orgAdmin = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
        Assert.IsTrue(member.IsSubsetOf(orgAdmin));
    }

    [TestMethod]
    public void PlatformAdmin_has_no_catalog_permissions()
    {
        var perms = KartovaRolePermissions.ForRole(KartovaRoles.PlatformAdmin);
        Assert.AreEqual(0, perms.Count);
    }

    [TestMethod]
    public void ServiceAccount_has_no_realm_role_yet_returns_empty_set()
    {
        var perms = KartovaRolePermissions.ForRole(KartovaRoles.ServiceAccount);
        Assert.AreEqual(0, perms.Count);
    }

    [TestMethod]
    public void Unknown_role_returns_empty_set()
    {
        var perms = KartovaRolePermissions.ForRole("not-a-real-role");
        Assert.AreEqual(0, perms.Count);
    }
}
```

- [ ] **Step 2: Run, verify BUILD FAIL**

```bash
dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --filter "FullyQualifiedName~KartovaRolePermissionsTests"
```

Expected: BUILD FAIL — `KartovaRolePermissions` does not exist.

- [ ] **Step 3: Create `KartovaRolePermissions.cs`**

```csharp
namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaRolePermissions
{
    private static readonly IReadOnlySet<string> EmptySet =
        new HashSet<string>(StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> Map =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            [KartovaRoles.Viewer] = new HashSet<string>(StringComparer.Ordinal)
            {
                KartovaPermissions.CatalogRead,
            },
            [KartovaRoles.Member] = new HashSet<string>(StringComparer.Ordinal)
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
            },
            [KartovaRoles.TeamAdmin] = new HashSet<string>(StringComparer.Ordinal)
            {
                // Forward-compat in slice 7: same set as Member. Diverges when teams ship (E-03.F-02).
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
            },
            [KartovaRoles.OrgAdmin] = new HashSet<string>(StringComparer.Ordinal)
            {
                KartovaPermissions.CatalogRead,
                KartovaPermissions.CatalogApplicationsRegister,
                KartovaPermissions.CatalogApplicationsEditMetadata,
                KartovaPermissions.CatalogApplicationsLifecycleForward,
                KartovaPermissions.CatalogApplicationsLifecycleReverse,
            },
            // PlatformAdmin: orthogonal — operates outside tenant scope. No entry.
            // ServiceAccount: no realm role yet (ADR-0009). No entry.
        };

    public static IReadOnlySet<string> ForRole(string role) =>
        Map.TryGetValue(role, out var perms) ? perms : EmptySet;
}
```

- [ ] **Step 4: Run unit tests, verify all 9 PASS**

```bash
dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --filter "FullyQualifiedName~KartovaRolePermissionsTests"
```

Expected: 9 passed, 0 failed.

- [ ] **Step 5: Add architecture rule asserting every permission appears in at least one role's set**

Append to `tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs`:

```csharp
[TestMethod]
public void Every_permission_appears_in_at_least_one_role_set()
{
    var permissionsInUse = KartovaRolePermissions.Map
        .SelectMany(kvp => kvp.Value)
        .ToHashSet(StringComparer.Ordinal);

    foreach (var perm in KartovaPermissions.All)
    {
        Assert.IsTrue(permissionsInUse.Contains(perm),
            $"Orphan permission '{perm}' — not granted to any role in KartovaRolePermissions.Map.");
    }
}

[TestMethod]
public void Every_mapped_value_is_a_known_permission()
{
    var declared = new HashSet<string>(KartovaPermissions.All, StringComparer.Ordinal);

    foreach (var (role, perms) in KartovaRolePermissions.Map)
    {
        foreach (var perm in perms)
        {
            Assert.IsTrue(declared.Contains(perm),
                $"Role {role} grants unknown permission '{perm}' — not declared in KartovaPermissions.");
        }
    }
}
```

- [ ] **Step 6: Run the new arch tests, verify PASS**

```bash
dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter "FullyQualifiedName~KartovaPermissionsRules"
```

Expected: PASS (all 3 arch tests).

- [ ] **Step 7: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs tests/Kartova.SharedKernel.AspNetCore.Tests/KartovaRolePermissionsTests.cs tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs
git commit -m "feat(perm): add KartovaRolePermissions.Map (single source of truth)"
```

---

### Task 5: Extend TenantClaimsTransformation to expand role → permission claims

**Files:**
- Modify: `src/Kartova.SharedKernel.AspNetCore/TenantClaimsTransformation.cs`
- Modify: `tests/Kartova.SharedKernel.AspNetCore.Tests/TenantClaimsTransformationTests.cs`

- [ ] **Step 1: Write a failing test asserting permission-claim expansion for Member**

Append to `tests/Kartova.SharedKernel.AspNetCore.Tests/TenantClaimsTransformationTests.cs`:

```csharp
[TestMethod]
public async Task Expands_role_claims_into_permission_claims_for_Member()
{
    var realmAccess = JsonSerializer.Serialize(new { roles = new[] { KartovaRoles.Member } });
    var principal = BuildPrincipal(
        tenantId: TenantA,
        claims: new[]
        {
            new Claim(KartovaClaims.TenantId, TenantA.ToString()),
            new Claim(KartovaClaims.RealmAccess, realmAccess),
        });

    var sut = NewSut();
    var transformed = await sut.TransformAsync(principal);

    var permClaims = transformed.FindAll(KartovaClaims.Permission)
                                .Select(c => c.Value).ToHashSet(StringComparer.Ordinal);

    foreach (var perm in KartovaRolePermissions.ForRole(KartovaRoles.Member))
    {
        Assert.IsTrue(permClaims.Contains(perm),
            $"Permission '{perm}' must be present on principal after transformation.");
    }

    Assert.IsFalse(permClaims.Contains(KartovaPermissions.CatalogApplicationsLifecycleReverse),
        "Member must not get the reverse-lifecycle permission.");
}
```

If helpers `BuildPrincipal` and `NewSut` already exist in this test file, reuse them; otherwise refer to the existing four tests (lines 31, 48, 61, 75 from codelens) for the construction pattern and add helpers that mirror them.

- [ ] **Step 2: Run, verify FAIL**

```bash
dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --filter "FullyQualifiedName~Expands_role_claims_into_permission_claims_for_Member"
```

Expected: FAIL — no permission claims present.

- [ ] **Step 3: Extend `TenantClaimsTransformation`**

In `TransformAsync`, immediately after the existing block that adds `ClaimTypes.Role` claims (around lines 43–52 in the current file), add:

```csharp
if (principal.Identity is ClaimsIdentity permId)
{
    foreach (var role in roles)
    {
        foreach (var perm in KartovaRolePermissions.ForRole(role))
        {
            if (!permId.HasClaim(KartovaClaims.Permission, perm))
            {
                permId.AddClaim(new Claim(KartovaClaims.Permission, perm));
            }
        }
    }
}
```

Note: the outer `if (principal.Identity is ClaimsIdentity id)` already exists for role-claim flattening — you may reuse that block rather than nesting a second cast. Keep both populations inside one cast for readability.

- [ ] **Step 4: Re-run, verify PASS**

Expected: PASS.

- [ ] **Step 5: Add parallel tests for Viewer, TeamAdmin, OrgAdmin, and an unknown role**

For each role, repeat the Step-1 pattern, asserting the exact set returned by `KartovaRolePermissions.ForRole(role)` is present on the principal.

For an "unknown role" case (e.g., `realm_access.roles = ["not-a-real-role"]`), assert `FindAll(KartovaClaims.Permission)` is empty.

- [ ] **Step 6: Verify existing 4 tests still pass**

```bash
dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --filter "FullyQualifiedName~TenantClaimsTransformationTests"
```

Expected: all tests (existing 4 + new ~5) PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Kartova.SharedKernel.AspNetCore/TenantClaimsTransformation.cs tests/Kartova.SharedKernel.AspNetCore.Tests/TenantClaimsTransformationTests.cs
git commit -m "feat(claims): expand role claims into permission claims on validated principals"
```

---

### Task 6: AuthorizationExtensions + wire policy registration into JwtAuthenticationExtensions

**Files:**
- Create: `src/Kartova.SharedKernel.AspNetCore/AuthorizationExtensions.cs`
- Modify: `src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs`
- Test: extend `tests/Kartova.SharedKernel.AspNetCore.Tests/JwtAuthenticationExtensionsTests.cs` if it exists; otherwise verify via integration tests in later tasks. (No new file in this task.)

- [ ] **Step 1: Create `AuthorizationExtensions.cs`**

```csharp
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Authorization;

namespace Kartova.SharedKernel.AspNetCore;

public static class AuthorizationExtensions
{
    /// <summary>
    /// Registers one ASP.NET authorization policy per <see cref="KartovaPermissions"/> entry.
    /// Policy name equals the permission name; body is <c>RequireClaim(KartovaClaims.Permission, &lt;perm&gt;)</c>.
    /// </summary>
    public static AuthorizationBuilder AddKartovaPermissionPolicies(this AuthorizationBuilder builder)
    {
        foreach (var perm in KartovaPermissions.All)
        {
            builder.AddPolicy(perm, p => p.RequireClaim(KartovaClaims.Permission, perm));
        }
        return builder;
    }
}
```

- [ ] **Step 2: Wire into `AddKartovaJwtAuth`**

Edit `src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs`. Replace the existing line:

```csharp
services.AddAuthorization();
```

with:

```csharp
services.AddAuthorization();
services.AddAuthorizationBuilder().AddKartovaPermissionPolicies();
```

- [ ] **Step 3: Build to confirm no compile errors**

```bash
dotnet build Kartova.slnx
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/Kartova.SharedKernel.AspNetCore/AuthorizationExtensions.cs src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs
git commit -m "feat(auth): register one ASP.NET policy per KartovaPermissions entry"
```

---

### Task 7: GET /me/permissions endpoint

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Contracts/MePermissionsResponse.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationEndpointDelegates.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs`
- Test: `src/Modules/Organization/Kartova.Organization.IntegrationTests/GetMePermissionsTests.cs` (new file)

- [ ] **Step 1: Write the failing integration test**

Create `src/Modules/Organization/Kartova.Organization.IntegrationTests/GetMePermissionsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.IntegrationTests;

[TestClass]
public sealed class GetMePermissionsTests : OrganizationIntegrationTestBase
{
    private const string OrgAUser   = "admin@orga.kartova.local";        // OrgAdmin
    private const string OrgAMember = "member@orga.kartova.local";       // Member
    private const string OrgAViewer = "viewer@orga.kartova.local";       // Viewer
    private const string OrgATeam   = "team-admin@orga.kartova.local";   // TeamAdmin

    [TestMethod]
    public async Task GET_me_permissions_returns_OrgAdmin_set()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync("/api/v1/organizations/me/permissions");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MePermissionsResponse>();

        Assert.IsNotNull(body);
        Assert.AreEqual(KartovaRoles.OrgAdmin, body!.Role);
        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin).ToList(),
            body.Permissions.ToList());
    }

    [TestMethod]
    public async Task GET_me_permissions_returns_Member_set()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAMember);
        var resp = await client.GetAsync("/api/v1/organizations/me/permissions");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MePermissionsResponse>();
        Assert.AreEqual(KartovaRoles.Member, body!.Role);
        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.Member).ToList(),
            body.Permissions.ToList());
    }

    [TestMethod]
    public async Task GET_me_permissions_returns_Viewer_set()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAViewer);
        var resp = await client.GetAsync("/api/v1/organizations/me/permissions");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MePermissionsResponse>();
        Assert.AreEqual(KartovaRoles.Viewer, body!.Role);
        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.Viewer).ToList(),
            body.Permissions.ToList());
    }

    [TestMethod]
    public async Task GET_me_permissions_returns_TeamAdmin_set()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgATeam);
        var resp = await client.GetAsync("/api/v1/organizations/me/permissions");

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<MePermissionsResponse>();
        Assert.AreEqual(KartovaRoles.TeamAdmin, body!.Role);
        CollectionAssert.AreEquivalent(
            KartovaRolePermissions.ForRole(KartovaRoles.TeamAdmin).ToList(),
            body.Permissions.ToList());
    }

    [TestMethod]
    public async Task GET_me_permissions_returns_401_when_unauthenticated()
    {
        var client = Fx.CreateUnauthenticatedClient();
        var resp = await client.GetAsync("/api/v1/organizations/me/permissions");

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
```

If `OrganizationIntegrationTestBase` does not exist, mirror `CatalogIntegrationTestBase` exactly — it's an 11-line file exposing `Fx`. If `CreateUnauthenticatedClient` does not exist on the fixture, drop the 401 test in this task and add it in a follow-up after extending the fixture.

- [ ] **Step 2: Run, verify FAIL**

```bash
dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --filter "FullyQualifiedName~GetMePermissionsTests"
```

Expected: BUILD FAIL — `MePermissionsResponse` and the endpoint don't exist.

- [ ] **Step 3: Create the DTO**

Create `src/Modules/Organization/Kartova.Organization.Contracts/MePermissionsResponse.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record MePermissionsResponse(string Role, IReadOnlyCollection<string> Permissions);
```

- [ ] **Step 4: Add the endpoint delegate**

In `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationEndpointDelegates.cs`, add:

```csharp
internal static IResult GetMePermissionsAsync(ClaimsPrincipal user)
{
    var role = user.FindAll(ClaimTypes.Role)
                   .Select(c => c.Value)
                   .FirstOrDefault() ?? string.Empty;

    var permissions = user.FindAll(KartovaClaims.Permission)
                          .Select(c => c.Value)
                          .ToArray();

    return Results.Ok(new MePermissionsResponse(role, permissions));
}
```

Add the required `using` directives: `using System.Security.Claims;`, `using Kartova.Organization.Contracts;`, `using Kartova.SharedKernel.Multitenancy;`.

- [ ] **Step 5: Map the endpoint**

In `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs`, alongside the existing `/me` mapping (line 44), add:

```csharp
tenant.MapGet("/me/permissions", OrganizationEndpointDelegates.GetMePermissionsAsync)
      .WithName("GetMePermissions")
      .Produces<MePermissionsResponse>(StatusCodes.Status200OK);
```

- [ ] **Step 6: Re-run, verify all five tests PASS**

```bash
dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --filter "FullyQualifiedName~GetMePermissionsTests"
```

Expected: 5 passed (or 4 if the 401 case was dropped due to fixture limits).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Contracts/MePermissionsResponse.cs src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationEndpointDelegates.cs src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/GetMePermissionsTests.cs
git commit -m "feat(api): GET /organizations/me/permissions returns role and permission set"
```

---

### Task 8: Attach permission policies to existing Catalog endpoints

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs` (new file)

- [ ] **Step 1: Write the failing matrix test (existing 6 endpoints × 4 roles = 24 cells)**

Create `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.IntegrationTests;

/// <summary>
/// Asserts the (role × catalog-endpoint) authorization matrix. Data-driven: each cell looks up the
/// required permission for the endpoint and checks the role's permission set in
/// <see cref="KartovaRolePermissions"/>. Reverse endpoints are added in Task 13/14.
/// </summary>
[TestClass]
public sealed class CatalogPermissionMatrixTests : CatalogIntegrationTestBase
{
    private const string OrgAdmin  = "admin@orga.kartova.local";
    private const string Member    = "member@orga.kartova.local";
    private const string TeamAdmin = "team-admin@orga.kartova.local";
    private const string Viewer    = "viewer@orga.kartova.local";

    private static readonly (string Role, string Username)[] Roles =
    {
        (KartovaRoles.OrgAdmin,  OrgAdmin),
        (KartovaRoles.Member,    Member),
        (KartovaRoles.TeamAdmin, TeamAdmin),
        (KartovaRoles.Viewer,    Viewer),
    };

    private static readonly (HttpMethod Method, string Path, string Permission)[] Endpoints =
    {
        (HttpMethod.Post, "/api/v1/catalog/applications",                   KartovaPermissions.CatalogApplicationsRegister),
        (HttpMethod.Get,  "/api/v1/catalog/applications",                   KartovaPermissions.CatalogRead),
        (HttpMethod.Get,  "/api/v1/catalog/applications/{id}",              KartovaPermissions.CatalogRead),
        (HttpMethod.Put,  "/api/v1/catalog/applications/{id}",              KartovaPermissions.CatalogApplicationsEditMetadata),
        (HttpMethod.Post, "/api/v1/catalog/applications/{id}/deprecate",    KartovaPermissions.CatalogApplicationsLifecycleForward),
        (HttpMethod.Post, "/api/v1/catalog/applications/{id}/decommission", KartovaPermissions.CatalogApplicationsLifecycleForward),
    };

    [TestMethod]
    public async Task Every_role_endpoint_cell_matches_KartovaRolePermissions_Map()
    {
        // Seed a fixture Application as OrgAdmin so {id} substitution works on per-role calls.
        var seederClient = await Fx.CreateAuthenticatedClientAsync(OrgAdmin);
        var seeded = await Fx.RegisterApplicationAsync(seederClient,
            name: "matrix-app", displayName: "Matrix App", description: "Seed.");
        var appId = seeded.Id;

        foreach (var (role, user) in Roles)
        {
            var client = await Fx.CreateAuthenticatedClientAsync(user);
            var grants = KartovaRolePermissions.ForRole(role);

            foreach (var (method, path, perm) in Endpoints)
            {
                var url = path.Replace("{id}", appId.ToString());
                using var req = new HttpRequestMessage(method, url);

                // Body needs to be valid for the endpoint binding to even reach the policy.
                // We use a "shape-valid" body so non-403 responses can still bubble through to the
                // domain handler — those will fail with 4xx for other reasons (e.g., 409 on lifecycle).
                AttachShapeValidBody(req, method, path);

                var resp = await client.SendAsync(req);
                var expectedForbidden = !grants.Contains(perm);

                if (expectedForbidden)
                {
                    Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode,
                        $"{role} calling {method} {path} should be 403 (lacks {perm}).");
                }
                else
                {
                    Assert.AreNotEqual(HttpStatusCode.Forbidden, resp.StatusCode,
                        $"{role} calling {method} {path} should NOT be 403 (has {perm}). Actual: {resp.StatusCode}.");
                    Assert.AreNotEqual(HttpStatusCode.Unauthorized, resp.StatusCode,
                        $"{role} calling {method} {path} should NOT be 401. Actual: {resp.StatusCode}.");
                }
            }
        }
    }

    private static void AttachShapeValidBody(HttpRequestMessage req, HttpMethod method, string path)
    {
        if (method == HttpMethod.Post && path == "/api/v1/catalog/applications")
        {
            // Use a unique kebab name per request to avoid collisions across cells.
            var unique = $"matrix-write-{Guid.NewGuid():N}";
            req.Content = JsonContent.Create(new
            {
                name = unique,
                displayName = "Matrix Write",
                description = "Matrix shape body.",
            });
        }
        else if (method == HttpMethod.Put)
        {
            req.Headers.IfMatch.Add(new System.Net.Http.Headers.EntityTagHeaderValue("\"AAAAAA==\""));
            req.Content = JsonContent.Create(new
            {
                displayName = "Matrix Edit",
                description = "Matrix edit body.",
            });
        }
        else if (path.EndsWith("/deprecate"))
        {
            req.Content = JsonContent.Create(new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });
        }
    }
}
```

Note: this test asserts 4 × 6 = **24 cells** of (role × endpoint). Reverse endpoints (8 → 32 cells) are added in Tasks 13 and 14.

- [ ] **Step 2: Run, verify FAIL**

```bash
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter "FullyQualifiedName~CatalogPermissionMatrixTests"
```

Expected: FAIL — endpoints currently allow any authenticated user (no 403 for Viewer).

- [ ] **Step 3: Attach `RequireAuthorization(...)` to all six endpoints**

Edit `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs`. For each `tenant.MapX(...)` chain, append `.RequireAuthorization(KartovaPermissions.<X>)`. The complete `MapEndpoints` body becomes:

```csharp
public void MapEndpoints(IEndpointRouteBuilder app)
{
    var tenant = app.MapTenantScopedModule(Slug);

    tenant.MapPost("/applications", CatalogEndpointDelegates.RegisterApplicationAsync)
          .RequireAuthorization(KartovaPermissions.CatalogApplicationsRegister)
          .WithName("RegisterApplication")
          .Produces<ApplicationResponse>(StatusCodes.Status201Created)
          .ProducesProblem(StatusCodes.Status400BadRequest);

    tenant.MapGet("/applications/{id:guid}", CatalogEndpointDelegates.GetApplicationByIdAsync)
          .RequireAuthorization(KartovaPermissions.CatalogRead)
          .WithName("GetApplicationById")
          .Produces<ApplicationResponse>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status404NotFound);

    tenant.MapGet("/applications", CatalogEndpointDelegates.ListApplicationsAsync)
          .RequireAuthorization(KartovaPermissions.CatalogRead)
          .WithName("ListApplications")
          .Produces<CursorPage<ApplicationResponse>>(StatusCodes.Status200OK);

    tenant.MapPut("/applications/{id:guid}", CatalogEndpointDelegates.EditApplicationAsync)
          .RequireAuthorization(KartovaPermissions.CatalogApplicationsEditMetadata)
          .AddEndpointFilter<IfMatchEndpointFilter>()
          .WithName("EditApplication")
          .Produces<ApplicationResponse>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status409Conflict)
          .ProducesProblem(StatusCodes.Status412PreconditionFailed)
          .ProducesProblem(StatusCodes.Status428PreconditionRequired);

    tenant.MapPost("/applications/{id:guid}/deprecate", CatalogEndpointDelegates.DeprecateApplicationAsync)
          .RequireAuthorization(KartovaPermissions.CatalogApplicationsLifecycleForward)
          .WithName("DeprecateApplication")
          .Produces<ApplicationResponse>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status400BadRequest)
          .ProducesProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status409Conflict);

    tenant.MapPost("/applications/{id:guid}/decommission", CatalogEndpointDelegates.DecommissionApplicationAsync)
          .RequireAuthorization(KartovaPermissions.CatalogApplicationsLifecycleForward)
          .WithName("DecommissionApplication")
          .Produces<ApplicationResponse>(StatusCodes.Status200OK)
          .ProducesProblem(StatusCodes.Status404NotFound)
          .ProducesProblem(StatusCodes.Status409Conflict);
}
```

Add `using Kartova.SharedKernel.Multitenancy;` if not already present.

- [ ] **Step 4: Re-run the matrix test, verify PASS**

```bash
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter "FullyQualifiedName~CatalogPermissionMatrixTests"
```

Expected: PASS.

- [ ] **Step 5: Run the full Catalog integration suite to confirm no regression**

```bash
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj
```

Expected: all tests pass (existing slice-3/4/5/6 + new matrix). Existing tests use `admin@orga` = OrgAdmin which has every catalog permission, so they remain green.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs
git commit -m "feat(catalog): gate all Application endpoints by KartovaPermissions"
```

---

### Task 9: Domain — `Application.Reactivate()`

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationReactivateTests.cs` (new file)

- [ ] **Step 1: Write the failing unit tests**

Create `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationReactivateTests.cs`:

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

[TestClass]
public sealed class ApplicationReactivateTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.NewRandom();

    private static Application NewDeprecated(DateTimeOffset sunsetDate)
    {
        var clock = new FakeTimeProvider(Now);
        var app = Application.Create("my-app", "My App", "Desc.", Guid.NewGuid(), Tenant, clock);
        app.Deprecate(sunsetDate, clock);
        return app;
    }

    private static Application NewDecommissioned()
    {
        var clock = new FakeTimeProvider(Now);
        var app = Application.Create("my-app", "My App", "Desc.", Guid.NewGuid(), Tenant, clock);
        app.Deprecate(Now.AddDays(7), clock);
        clock.SetUtcNow(Now.AddDays(8));
        app.Decommission(clock);
        return app;
    }

    [TestMethod]
    public void Reactivate_from_Deprecated_returns_to_Active_and_clears_sunset_date()
    {
        var app = NewDeprecated(sunsetDate: Now.AddDays(30));

        app.Reactivate();

        Assert.AreEqual(Lifecycle.Active, app.Lifecycle);
        Assert.IsNull(app.SunsetDate);
    }

    [TestMethod]
    public void Reactivate_from_Decommissioned_returns_to_Active_and_clears_sunset_date()
    {
        var app = NewDecommissioned();

        app.Reactivate();

        Assert.AreEqual(Lifecycle.Active, app.Lifecycle);
        Assert.IsNull(app.SunsetDate);
    }

    [TestMethod]
    public void Reactivate_from_Active_throws_InvalidLifecycleTransitionException()
    {
        var clock = new FakeTimeProvider(Now);
        var app = Application.Create("my-app", "My App", "Desc.", Guid.NewGuid(), Tenant, clock);

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(() => app.Reactivate());
        Assert.AreEqual(Lifecycle.Active, ex.CurrentLifecycle);
        Assert.AreEqual(nameof(Application.Reactivate), ex.AttemptedTransition);
    }
}
```

If `TenantId.NewRandom()` does not exist, use the project's existing test-helper pattern (e.g., `TenantId.From(Guid.NewGuid())` — check `RegisterApplicationTests` for the precedent).

- [ ] **Step 2: Run, verify FAIL**

```bash
dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter "FullyQualifiedName~ApplicationReactivateTests"
```

Expected: BUILD FAIL — `Application.Reactivate` doesn't exist.

- [ ] **Step 3: Implement `Reactivate()`**

In `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`, after `Decommission(...)` (currently at line ~116), add:

```csharp
public void Reactivate()
{
    if (Lifecycle != Lifecycle.Deprecated && Lifecycle != Lifecycle.Decommissioned)
    {
        throw new InvalidLifecycleTransitionException(Lifecycle, nameof(Reactivate));
    }

    Lifecycle = Lifecycle.Active;
    SunsetDate = null;
}
```

- [ ] **Step 4: Re-run, verify PASS**

Expected: all 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationReactivateTests.cs
git commit -m "feat(domain): Application.Reactivate() returns Deprecated/Decommissioned to Active"
```

---

### Task 10: Domain — `Application.UnDecommission(...)`

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationUnDecommissionTests.cs` (new file)

- [ ] **Step 1: Write the failing unit tests**

Create `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationUnDecommissionTests.cs`:

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Tests;

[TestClass]
public sealed class ApplicationUnDecommissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.NewRandom();

    private static (Application app, FakeTimeProvider clock) NewDecommissioned()
    {
        var clock = new FakeTimeProvider(Now);
        var app = Application.Create("my-app", "My App", "Desc.", Guid.NewGuid(), Tenant, clock);
        app.Deprecate(Now.AddDays(7), clock);
        clock.SetUtcNow(Now.AddDays(8));
        app.Decommission(clock);
        return (app, clock);
    }

    [TestMethod]
    public void UnDecommission_from_Decommissioned_returns_to_Deprecated_with_new_sunset_date()
    {
        var (app, clock) = NewDecommissioned();
        var newSunset = clock.GetUtcNow().AddDays(30);

        app.UnDecommission(newSunset, clock);

        Assert.AreEqual(Lifecycle.Deprecated, app.Lifecycle);
        Assert.AreEqual(newSunset, app.SunsetDate);
    }

    [TestMethod]
    public void UnDecommission_from_Active_throws_InvalidLifecycleTransitionException()
    {
        var clock = new FakeTimeProvider(Now);
        var app = Application.Create("my-app", "My App", "Desc.", Guid.NewGuid(), Tenant, clock);

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(
            () => app.UnDecommission(Now.AddDays(30), clock));
        Assert.AreEqual(Lifecycle.Active, ex.CurrentLifecycle);
        Assert.AreEqual(nameof(Application.UnDecommission), ex.AttemptedTransition);
    }

    [TestMethod]
    public void UnDecommission_from_Deprecated_throws_InvalidLifecycleTransitionException()
    {
        var clock = new FakeTimeProvider(Now);
        var app = Application.Create("my-app", "My App", "Desc.", Guid.NewGuid(), Tenant, clock);
        app.Deprecate(Now.AddDays(7), clock);

        var ex = Assert.ThrowsExactly<InvalidLifecycleTransitionException>(
            () => app.UnDecommission(Now.AddDays(30), clock));
        Assert.AreEqual(Lifecycle.Deprecated, ex.CurrentLifecycle);
        Assert.AreEqual(nameof(Application.UnDecommission), ex.AttemptedTransition);
    }

    [TestMethod]
    public void UnDecommission_with_past_sunset_date_throws_ArgumentException()
    {
        var (app, clock) = NewDecommissioned();
        var pastSunset = clock.GetUtcNow().AddDays(-1);

        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => app.UnDecommission(pastSunset, clock));
        Assert.AreEqual("newSunsetDate", ex.ParamName);
    }

    [TestMethod]
    public void UnDecommission_with_sunset_date_equal_to_now_throws_ArgumentException()
    {
        var (app, clock) = NewDecommissioned();
        var nowSunset = clock.GetUtcNow();   // strict: > now, so == now must reject

        var ex = Assert.ThrowsExactly<ArgumentException>(
            () => app.UnDecommission(nowSunset, clock));
        Assert.AreEqual("newSunsetDate", ex.ParamName);
    }
}
```

- [ ] **Step 2: Run, verify FAIL**

```bash
dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter "FullyQualifiedName~ApplicationUnDecommissionTests"
```

Expected: BUILD FAIL — `UnDecommission` doesn't exist.

- [ ] **Step 3: Implement `UnDecommission(...)`**

In `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`, after `Reactivate()`, add:

```csharp
public void UnDecommission(DateTimeOffset newSunsetDate, TimeProvider clock)
{
    if (Lifecycle != Lifecycle.Decommissioned)
    {
        throw new InvalidLifecycleTransitionException(Lifecycle, nameof(UnDecommission), SunsetDate);
    }

    if (newSunsetDate <= clock.GetUtcNow())
    {
        throw new ArgumentException("sunsetDate must be in the future.", nameof(newSunsetDate));
    }

    Lifecycle = Lifecycle.Deprecated;
    SunsetDate = newSunsetDate;
}
```

- [ ] **Step 4: Re-run, verify all 5 tests PASS**

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationUnDecommissionTests.cs
git commit -m "feat(domain): Application.UnDecommission(sunset, clock) returns to Deprecated"
```

---

### Task 11: Reactivate command, handler, and endpoint delegate

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/ReactivateApplicationCommand.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/ReactivateApplicationHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (DI + endpoint map)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ReactivateApplicationTests.cs` (new file)

- [ ] **Step 1: Write the failing integration tests**

Create `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ReactivateApplicationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public sealed class ReactivateApplicationTests : CatalogIntegrationTestBase
{
    private const string OrgAdmin  = "admin@orga.kartova.local";
    private const string OrgMember = "member@orga.kartova.local";

    [TestMethod]
    public async Task POST_reactivate_from_Deprecated_returns_200_with_Active_state_and_no_sunsetDate()
    {
        var orgAdminClient = await Fx.CreateAuthenticatedClientAsync(OrgAdmin);
        var registered = await Fx.RegisterApplicationAsync(orgAdminClient,
            name: "react-app-1", displayName: "Reactivate 1", description: "Desc.");

        // Deprecate first.
        var dep = await orgAdminClient.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });
        Assert.IsTrue(dep.IsSuccessStatusCode);

        var resp = await orgAdminClient.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/reactivate", content: null);

        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual("active", body!.Lifecycle);
        Assert.IsNull(body.SunsetDate);
    }

    [TestMethod]
    public async Task POST_reactivate_from_Active_returns_409_lifecycle_conflict()
    {
        var orgAdminClient = await Fx.CreateAuthenticatedClientAsync(OrgAdmin);
        var registered = await Fx.RegisterApplicationAsync(orgAdminClient,
            name: "react-app-2", displayName: "Reactivate 2", description: "Desc.");

        var resp = await orgAdminClient.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/reactivate", content: null);

        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);
        var problem = await resp.Content.ReadFromJsonAsync<ProblemPayload>();
        Assert.AreEqual(ProblemTypes.LifecycleConflict, problem!.Type);
    }

    [TestMethod]
    public async Task POST_reactivate_as_Member_returns_403()
    {
        var orgAdminClient = await Fx.CreateAuthenticatedClientAsync(OrgAdmin);
        var registered = await Fx.RegisterApplicationAsync(orgAdminClient,
            name: "react-app-3", displayName: "Reactivate 3", description: "Desc.");

        // Deprecate as OrgAdmin first.
        await orgAdminClient.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{registered.Id}/deprecate",
            new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });

        var memberClient = await Fx.CreateAuthenticatedClientAsync(OrgMember);
        var resp = await memberClient.PostAsync(
            $"/api/v1/catalog/applications/{registered.Id}/reactivate", content: null);

        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_reactivate_unauthenticated_returns_401()
    {
        var client = Fx.CreateUnauthenticatedClient();
        var resp = await client.PostAsync(
            $"/api/v1/catalog/applications/{Guid.NewGuid()}/reactivate", content: null);

        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
```

- [ ] **Step 2: Run, verify BUILD FAIL**

```bash
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter "FullyQualifiedName~ReactivateApplicationTests"
```

Expected: BUILD FAIL — endpoint, command, handler don't exist.

- [ ] **Step 3: Create the command**

Create `src/Modules/Catalog/Kartova.Catalog.Application/ReactivateApplicationCommand.cs`:

```csharp
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public sealed record ReactivateApplicationCommand(ApplicationId Id);
```

- [ ] **Step 4: Create the handler**

Mirror `DeprecateApplicationHandler` exactly except no `TimeProvider` injection (the domain method takes none).

Create `src/Modules/Catalog/Kartova.Catalog.Application/ReactivateApplicationHandler.cs`. Read `DeprecateApplicationHandler.cs` for the exact pattern (load aggregate, call domain method, save, throw `NotFoundException` if missing). Adapt to call `app.Reactivate()` with no parameters and return the updated aggregate.

- [ ] **Step 5: Add the endpoint delegate**

In `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`, after `DeprecateApplicationAsync` and `DecommissionApplicationAsync`, add:

```csharp
internal static async Task<IResult> ReactivateApplicationAsync(
    Guid id,
    ReactivateApplicationHandler handler,
    CancellationToken ct)
{
    var result = await handler.HandleAsync(new ReactivateApplicationCommand(new ApplicationId(id)), ct);
    return Results.Ok(result);
}
```

- [ ] **Step 6: Map the endpoint and register DI**

In `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs`:

In `MapEndpoints`, after the `decommission` mapping, add:

```csharp
tenant.MapPost("/applications/{id:guid}/reactivate", CatalogEndpointDelegates.ReactivateApplicationAsync)
      .RequireAuthorization(KartovaPermissions.CatalogApplicationsLifecycleReverse)
      .WithName("ReactivateApplication")
      .Produces<ApplicationResponse>(StatusCodes.Status200OK)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .ProducesProblem(StatusCodes.Status409Conflict);
```

In `RegisterServices`, alongside the other `AddScoped<XxxHandler>()` calls (lines 88–93), add:

```csharp
services.AddScoped<ReactivateApplicationHandler>();
```

- [ ] **Step 7: Re-run, verify 4 tests PASS**

```bash
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter "FullyQualifiedName~ReactivateApplicationTests"
```

Expected: 4 passed.

- [ ] **Step 8: Extend the permission matrix to cover the new endpoint**

In `CatalogPermissionMatrixTests.cs`, add to the `Endpoints` array:

```csharp
(HttpMethod.Post, "/api/v1/catalog/applications/{id}/reactivate",      KartovaPermissions.CatalogApplicationsLifecycleReverse),
```

The matrix now covers 4 × 7 = 28 cells. Re-run the matrix test to confirm it still passes (Viewer/Member/TeamAdmin should get 403; OrgAdmin should get either 200 or 409 depending on the seeded application's state — both are non-403 so the assertion holds).

```bash
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter "FullyQualifiedName~CatalogPermissionMatrixTests"
```

Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/ReactivateApplicationCommand.cs src/Modules/Catalog/Kartova.Catalog.Application/ReactivateApplicationHandler.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ReactivateApplicationTests.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs
git commit -m "feat(catalog): POST /applications/{id}/reactivate (OrgAdmin only)"
```

---

### Task 12: UnDecommission command, handler, DTO, and endpoint delegate

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/UnDecommissionApplicationCommand.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/UnDecommissionApplicationHandler.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/UnDecommissionApplicationRequest.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (DI + endpoint map)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/UnDecommissionApplicationTests.cs` (new file)

- [ ] **Step 1: Write the failing integration tests**

Create `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/UnDecommissionApplicationTests.cs` with five cases:

1. Happy: Decommissioned → 200 + Deprecated + new sunsetDate echoed.
2. From Deprecated → 409 lifecycle conflict.
3. From Active → 409 lifecycle conflict.
4. Past sunsetDate → 400 `ProblemTypes.ValidationFailed` (or whichever problem-type the existing slice-5 deprecate test uses for the "future-only sunset" rule — check `DeprecateApplicationTests.cs` for the exact constant).
5. As Member → 403.
6. Unauthenticated → 401.

Follow the structure of `ReactivateApplicationTests` exactly. The decommission setup helper looks like:

```csharp
private static async Task DecommissionAsync(HttpClient orgAdminClient, ApplicationResponse registered)
{
    // sunset 1 day in the past relative to "now"; integration tests use the real clock, so wind sunsetDate back.
    await orgAdminClient.PostAsJsonAsync(
        $"/api/v1/catalog/applications/{registered.Id}/deprecate",
        new { sunsetDate = DateTimeOffset.UtcNow.AddSeconds(2) });
    await Task.Delay(TimeSpan.FromSeconds(3));   // wait until sunsetDate passes
    var resp = await orgAdminClient.PostAsync(
        $"/api/v1/catalog/applications/{registered.Id}/decommission", content: null);
    if (!resp.IsSuccessStatusCode)
        throw new InvalidOperationException($"Failed to set up decommissioned state: {resp.StatusCode}");
}
```

If the existing `DecommissionApplicationTests.cs` has a cleaner setup helper, prefer that one — read it first.

- [ ] **Step 2: Run, verify BUILD FAIL**

```bash
dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter "FullyQualifiedName~UnDecommissionApplicationTests"
```

- [ ] **Step 3: Create the command, DTO, handler**

Command (`UnDecommissionApplicationCommand.cs`):

```csharp
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public sealed record UnDecommissionApplicationCommand(ApplicationId Id, DateTimeOffset SunsetDate);
```

DTO (`UnDecommissionApplicationRequest.cs`):

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record UnDecommissionApplicationRequest(DateTimeOffset SunsetDate);
```

Handler (`UnDecommissionApplicationHandler.cs`): mirror `DeprecateApplicationHandler` exactly. Inject `TimeProvider` (passed to `Application.UnDecommission`), load aggregate (throwing if missing), call `app.UnDecommission(cmd.SunsetDate, clock)`, save.

- [ ] **Step 4: Add the endpoint delegate**

In `CatalogEndpointDelegates.cs`, after `ReactivateApplicationAsync`, add:

```csharp
internal static async Task<IResult> UnDecommissionApplicationAsync(
    Guid id,
    UnDecommissionApplicationRequest request,
    UnDecommissionApplicationHandler handler,
    CancellationToken ct)
{
    var result = await handler.HandleAsync(
        new UnDecommissionApplicationCommand(new ApplicationId(id), request.SunsetDate), ct);
    return Results.Ok(result);
}
```

- [ ] **Step 5: Map the endpoint and register DI**

In `CatalogModule.cs.MapEndpoints`, after the `reactivate` mapping, add:

```csharp
tenant.MapPost("/applications/{id:guid}/un-decommission", CatalogEndpointDelegates.UnDecommissionApplicationAsync)
      .RequireAuthorization(KartovaPermissions.CatalogApplicationsLifecycleReverse)
      .WithName("UnDecommissionApplication")
      .Produces<ApplicationResponse>(StatusCodes.Status200OK)
      .ProducesProblem(StatusCodes.Status400BadRequest)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .ProducesProblem(StatusCodes.Status409Conflict);
```

In `RegisterServices`, add:

```csharp
services.AddScoped<UnDecommissionApplicationHandler>();
```

- [ ] **Step 6: Re-run, verify all UnDecommission tests PASS**

Expected: 6 passed (or 5 if unauthenticated case is dropped per Task 7).

- [ ] **Step 7: Extend the permission matrix**

In `CatalogPermissionMatrixTests.cs.Endpoints`, add:

```csharp
(HttpMethod.Post, "/api/v1/catalog/applications/{id}/un-decommission", KartovaPermissions.CatalogApplicationsLifecycleReverse),
```

Extend `AttachShapeValidBody` to attach a JSON body for `/un-decommission`:

```csharp
else if (path.EndsWith("/un-decommission"))
{
    req.Content = JsonContent.Create(new { sunsetDate = DateTimeOffset.UtcNow.AddDays(30) });
}
```

Matrix now covers 4 × 8 = **32 cells**. Re-run.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/UnDecommissionApplicationCommand.cs src/Modules/Catalog/Kartova.Catalog.Application/UnDecommissionApplicationHandler.cs src/Modules/Catalog/Kartova.Catalog.Contracts/UnDecommissionApplicationRequest.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/UnDecommissionApplicationTests.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs
git commit -m "feat(catalog): POST /applications/{id}/un-decommission (OrgAdmin only)"
```

---

### Task 13: SPA permission constants + drift snapshot + arch test

**Files:**
- Create: `web/src/shared/auth/permissions.snapshot.json`
- Create: `web/src/shared/auth/permissions.ts`
- Modify: `tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs`

- [ ] **Step 1: Write the failing arch test for SPA-snapshot parity**

Append to `tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs`:

```csharp
[TestMethod]
public void Ts_snapshot_equals_csharp_KartovaPermissions_All()
{
    var snapshotPath = FindRepoFile("web/src/shared/auth/permissions.snapshot.json");
    Assert.IsTrue(File.Exists(snapshotPath),
        $"Drift sentinel missing: {snapshotPath}. The TS side must commit a JSON list of permission names.");

    using var doc = JsonDocument.Parse(File.ReadAllText(snapshotPath));
    var snapshot = doc.RootElement.EnumerateArray()
                                  .Select(e => e.GetString()!)
                                  .ToHashSet(StringComparer.Ordinal);

    CollectionAssert.AreEquivalent(
        KartovaPermissions.All.ToList(),
        snapshot.ToList(),
        "TS permissions.snapshot.json must match C# KartovaPermissions.All exactly.");
}

private static string FindRepoFile(string relativePath)
{
    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Kartova.slnx")))
    {
        dir = dir.Parent;
    }
    return Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
```

If `FindRepoFile` already exists in the project (from `KeycloakRealmSeedRules.LoadRealmJson` or similar), reuse it.

- [ ] **Step 2: Run, verify FAIL**

```bash
dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter "FullyQualifiedName~Ts_snapshot_equals"
```

Expected: FAIL — snapshot file does not exist.

- [ ] **Step 3: Create the snapshot**

Create `web/src/shared/auth/permissions.snapshot.json`:

```json
[
  "catalog.read",
  "catalog.applications.register",
  "catalog.applications.edit-metadata",
  "catalog.applications.lifecycle.forward",
  "catalog.applications.lifecycle.reverse"
]
```

- [ ] **Step 4: Create `permissions.ts` typed from the snapshot**

Create `web/src/shared/auth/permissions.ts`:

```ts
import snapshot from "./permissions.snapshot.json";

export const KartovaPermissions = {
  CatalogRead: "catalog.read",
  CatalogApplicationsRegister: "catalog.applications.register",
  CatalogApplicationsEditMetadata: "catalog.applications.edit-metadata",
  CatalogApplicationsLifecycleForward: "catalog.applications.lifecycle.forward",
  CatalogApplicationsLifecycleReverse: "catalog.applications.lifecycle.reverse",
} as const;

export type KartovaPermission = (typeof KartovaPermissions)[keyof typeof KartovaPermissions];

/** Identical-set check against the committed snapshot — guards against TS-side drift if a contributor edits only the constants object. */
const declared = new Set(Object.values(KartovaPermissions));
const fromSnapshot = new Set(snapshot as readonly string[]);
if (declared.size !== fromSnapshot.size || [...declared].some((p) => !fromSnapshot.has(p))) {
  throw new Error(
    "KartovaPermissions constants drifted from permissions.snapshot.json. " +
      "If you added a permission in C#, regenerate the snapshot and update both.",
  );
}
```

- [ ] **Step 5: Re-run the arch test, verify PASS**

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add web/src/shared/auth/permissions.snapshot.json web/src/shared/auth/permissions.ts tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs
git commit -m "feat(web): KartovaPermissions constants + drift-snapshot arch test"
```

---

### Task 14: SPA `usePermissions` React Query hook + Vitest

**Files:**
- Create: `web/src/shared/auth/usePermissions.ts`
- Create: `web/src/shared/auth/__tests__/usePermissions.test.tsx`

- [ ] **Step 1: Write the failing Vitest**

Create `web/src/shared/auth/__tests__/usePermissions.test.tsx`:

```tsx
import { describe, it, expect, vi } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import { usePermissions } from "../usePermissions";
import { KartovaPermissions } from "../permissions";

const server = setupServer();

beforeAll(() => server.listen());
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function wrap() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

describe("usePermissions", () => {
  it("returns Viewer set when API returns Viewer role", async () => {
    server.use(
      http.get("*/api/v1/organizations/me/permissions", () =>
        HttpResponse.json({ role: "Viewer", permissions: ["catalog.read"] })),
    );

    const { result } = renderHook(() => usePermissions(), { wrapper: wrap() });
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.role).toBe("Viewer");
    expect(result.current.hasPermission(KartovaPermissions.CatalogRead)).toBe(true);
    expect(result.current.hasPermission(KartovaPermissions.CatalogApplicationsRegister)).toBe(false);
  });

  it("returns OrgAdmin set with all five permissions", async () => {
    server.use(
      http.get("*/api/v1/organizations/me/permissions", () =>
        HttpResponse.json({
          role: "OrgAdmin",
          permissions: [
            "catalog.read",
            "catalog.applications.register",
            "catalog.applications.edit-metadata",
            "catalog.applications.lifecycle.forward",
            "catalog.applications.lifecycle.reverse",
          ],
        })),
    );

    const { result } = renderHook(() => usePermissions(), { wrapper: wrap() });
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.role).toBe("OrgAdmin");
    for (const p of Object.values(KartovaPermissions)) {
      expect(result.current.hasPermission(p)).toBe(true);
    }
  });

  it("isLoading is true initially", () => {
    server.use(
      http.get("*/api/v1/organizations/me/permissions", () =>
        HttpResponse.json({ role: "Member", permissions: ["catalog.read"] }),
      { once: true }),
    );

    const { result } = renderHook(() => usePermissions(), { wrapper: wrap() });
    expect(result.current.isLoading).toBe(true);
    expect(result.current.hasPermission(KartovaPermissions.CatalogRead)).toBe(false);
  });

  it("returns false for all permissions on 401", async () => {
    server.use(
      http.get("*/api/v1/organizations/me/permissions", () => new HttpResponse(null, { status: 401 })),
    );

    const { result } = renderHook(() => usePermissions(), { wrapper: wrap() });
    await waitFor(() => expect(result.current.isLoading).toBe(false));

    expect(result.current.role).toBeNull();
    for (const p of Object.values(KartovaPermissions)) {
      expect(result.current.hasPermission(p)).toBe(false);
    }
  });
});
```

- [ ] **Step 2: Run, verify FAIL**

```bash
cd web && pnpm vitest run src/shared/auth/__tests__/usePermissions.test.tsx
```

Expected: FAIL — `usePermissions` does not exist. (If `msw` is not installed, install it as a dev dep: `pnpm add -D msw`. Check existing test files first — there may already be a project-wide MSW setup.)

- [ ] **Step 3: Create `usePermissions.ts`**

```ts
import { useQuery } from "@tanstack/react-query";
import { useAuth } from "react-oidc-context";

import type { KartovaPermission } from "./permissions";

interface MePermissionsResponse {
  role: string;
  permissions: readonly string[];
}

const QUERY_KEY = ["me", "permissions"] as const;

export interface UsePermissionsResult {
  role: string | null;
  hasPermission: (perm: KartovaPermission) => boolean;
  isLoading: boolean;
}

export function usePermissions(): UsePermissionsResult {
  const auth = useAuth();
  const enabled = auth.isAuthenticated;

  const query = useQuery<MePermissionsResponse>({
    queryKey: QUERY_KEY,
    queryFn: async () => {
      const res = await fetch("/api/v1/organizations/me/permissions", {
        headers: { Authorization: `Bearer ${auth.user?.access_token}` },
      });
      if (!res.ok) throw new Error(`me/permissions returned ${res.status}`);
      return (await res.json()) as MePermissionsResponse;
    },
    enabled,
    staleTime: 5 * 60 * 1000,
    retry: false,
  });

  const set = new Set(query.data?.permissions ?? []);

  return {
    role: query.data?.role ?? null,
    hasPermission: (perm) => set.has(perm),
    isLoading: enabled && query.isLoading,
  };
}
```

If the project uses a different fetch helper (e.g., a generated openapi client at `web/src/generated/openapi.ts`), prefer that helper — read `web/src/features/catalog/api/client.ts` for the established pattern and adapt the `queryFn` to match.

- [ ] **Step 4: Re-run Vitest, verify all 4 cases PASS**

Expected: 4 passed.

- [ ] **Step 5: Commit**

```bash
git add web/src/shared/auth/usePermissions.ts web/src/shared/auth/__tests__/usePermissions.test.tsx
git commit -m "feat(web): usePermissions hook reads role/permissions from /me/permissions"
```

---

### Task 15: SPA — gate Register / Edit / Lifecycle buttons + AppLayout shell gate

**Files:**
- Modify: `web/src/features/catalog/pages/CatalogListPage.tsx`
- Modify: `web/src/features/catalog/pages/ApplicationDetailPage.tsx`
- Modify: `web/src/features/catalog/components/LifecycleMenu.tsx`
- Modify: `web/src/components/layout/AppLayout.tsx`
- Modify: `web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx`
- Modify: `web/src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx`
- Modify: `web/src/features/catalog/components/__tests__/LifecycleMenu.test.tsx` (or create if absent)
- Create: `web/src/components/layout/__tests__/AppLayout.test.tsx` (if absent)

- [ ] **Step 1: Open each component file and find the action elements**

Read each of `CatalogListPage.tsx`, `ApplicationDetailPage.tsx`, `LifecycleMenu.tsx`, `AppLayout.tsx` to identify:

- The Register button on `CatalogListPage`
- The Edit button on `ApplicationDetailPage`
- The lifecycle action items on `LifecycleMenu`
- The protected shell rendering point on `AppLayout`

- [ ] **Step 2: Write failing component tests for permission gating**

For `CatalogListPage.test.tsx`, add:

```tsx
it("hides Register button for Viewer", async () => {
  mockPermissions(["catalog.read"]);
  render(<CatalogListPage />, { wrapper: appWrapper });
  expect(await screen.queryByRole("button", { name: /register application/i })).toBeNull();
});

it("shows Register button for Member", async () => {
  mockPermissions(["catalog.read", "catalog.applications.register"]);
  render(<CatalogListPage />, { wrapper: appWrapper });
  expect(await screen.findByRole("button", { name: /register application/i })).toBeInTheDocument();
});
```

Where `mockPermissions(...)` is a helper that calls `setupServer.use(...)` to stub `/me/permissions` for the test. If similar helpers exist for `useCurrentUser`, mirror the structure.

For `ApplicationDetailPage.test.tsx`, add analogous tests for the Edit button (`catalog.applications.edit-metadata`) and the LifecycleMenu mount (`catalog.applications.lifecycle.forward` or `.reverse`).

For `LifecycleMenu.test.tsx`, add tests for the existing "Deprecate…" / "Decommission" items being hidden for Viewer (they require `catalog.applications.lifecycle.forward`). Reverse items are tested in Task 16.

For `AppLayout.test.tsx` (new):

```tsx
it("renders no-access placeholder for user with empty permission set", async () => {
  mockPermissions([]);   // zero permissions
  render(<AppLayout />, { wrapper: appWrapperWithRouter });
  expect(await screen.findByText(/no access/i)).toBeInTheDocument();
  expect(screen.queryByRole("link", { name: /catalog/i })).toBeNull();
});

it("renders the protected shell for user with CatalogRead", async () => {
  mockPermissions(["catalog.read"]);
  render(<AppLayout />, { wrapper: appWrapperWithRouter });
  expect(await screen.findByTestId("app-shell")).toBeInTheDocument();
});
```

- [ ] **Step 3: Run, verify all new tests FAIL**

```bash
cd web && pnpm vitest run src/features/catalog/pages/__tests__/CatalogListPage.test.tsx src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx src/features/catalog/components/__tests__/LifecycleMenu.test.tsx src/components/layout/__tests__/AppLayout.test.tsx
```

Expected: new tests FAIL; existing tests still PASS.

- [ ] **Step 4: Add gating to each component**

In `CatalogListPage.tsx`, wrap the Register button in a `usePermissions` check:

```tsx
const { hasPermission, isLoading } = usePermissions();
// ...
{!isLoading && hasPermission(KartovaPermissions.CatalogApplicationsRegister) && (
  <RegisterApplicationButton />
)}
```

In `ApplicationDetailPage.tsx`, similarly wrap the Edit button (`CatalogApplicationsEditMetadata`) and the `<LifecycleMenu>` mount point. Mount `<LifecycleMenu>` only if either of the two lifecycle permissions is present:

```tsx
const canForward = hasPermission(KartovaPermissions.CatalogApplicationsLifecycleForward);
const canReverse = hasPermission(KartovaPermissions.CatalogApplicationsLifecycleReverse);
// ...
{(canForward || canReverse) && <LifecycleMenu canForward={canForward} canReverse={canReverse} ... />}
```

In `LifecycleMenu.tsx`, accept the new `canForward`/`canReverse` props and gate each item:

- "Deprecate…" / "Decommission" items render only if `canForward` AND the lifecycle state allows them.
- Reverse items (added in Task 16) render only if `canReverse`.

In `AppLayout.tsx`, wrap the protected shell:

```tsx
const { hasPermission, isLoading } = usePermissions();
if (isLoading) return <SkeletonShell />;
if (!hasPermission(KartovaPermissions.CatalogRead)) return <NoAccessPage />;
return <ProtectedShell />;
```

`NoAccessPage` is a simple component with the message "You don't have access to this organization. Contact your organization admin." Add it to `web/src/components/layout/NoAccessPage.tsx` (1 file, ~20 lines).

- [ ] **Step 5: Re-run, verify all tests PASS**

Expected: all Vitest tests pass.

- [ ] **Step 6: Cold-start the dev server and verify manually with Playwright per CLAUDE.md DoD #5**

Defer the manual smoke to the DoD pipeline. Skip for now.

- [ ] **Step 7: Commit**

```bash
git add web/src/components/layout/AppLayout.tsx web/src/components/layout/NoAccessPage.tsx web/src/components/layout/__tests__/AppLayout.test.tsx web/src/features/catalog/components/LifecycleMenu.tsx web/src/features/catalog/components/__tests__/LifecycleMenu.test.tsx web/src/features/catalog/pages/CatalogListPage.tsx web/src/features/catalog/pages/__tests__/CatalogListPage.test.tsx web/src/features/catalog/pages/ApplicationDetailPage.tsx web/src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx
git commit -m "feat(web): gate Register/Edit/LifecycleMenu/AppLayout by KartovaPermissions"
```

---

### Task 16: SPA — Reactivate + UnDecommission dialogs + LifecycleMenu reverse items + mutation hooks

**Files:**
- Create: `web/src/features/catalog/components/ReactivateConfirmDialog.tsx`
- Create: `web/src/features/catalog/components/UnDecommissionConfirmDialog.tsx`
- Create: `web/src/features/catalog/components/__tests__/ReactivateConfirmDialog.test.tsx`
- Create: `web/src/features/catalog/components/__tests__/UnDecommissionConfirmDialog.test.tsx`
- Create: `web/src/features/catalog/schemas/unDecommissionApplication.ts`
- Modify: `web/src/features/catalog/api/applications.ts`
- Modify: `web/src/features/catalog/components/LifecycleMenu.tsx`
- Modify: `web/src/features/catalog/components/__tests__/LifecycleMenu.test.tsx`

- [ ] **Step 1: Add mutation hooks**

In `web/src/features/catalog/api/applications.ts`, after the existing `useDeprecateApplication` and `useDecommissionApplication` hooks, add:

```ts
export function useReactivateApplication() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiPost(`/api/v1/catalog/applications/${id}/reactivate`),
    onSuccess: (_, id) => {
      qc.invalidateQueries({ queryKey: applicationsKeys.detail(id) });
      qc.invalidateQueries({ queryKey: applicationsKeys.list() });
    },
  });
}

export function useUnDecommissionApplication() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, sunsetDate }: { id: string; sunsetDate: string }) =>
      apiPost(`/api/v1/catalog/applications/${id}/un-decommission`, { sunsetDate }),
    onSuccess: (_, { id }) => {
      qc.invalidateQueries({ queryKey: applicationsKeys.detail(id) });
      qc.invalidateQueries({ queryKey: applicationsKeys.list() });
    },
  });
}
```

Adapt `apiPost`, `applicationsKeys.*` to the actual names used in the file — check the existing forward-lifecycle mutations.

- [ ] **Step 2: Create the zod schema**

`web/src/features/catalog/schemas/unDecommissionApplication.ts`:

```ts
import { z } from "zod";

export const unDecommissionApplicationSchema = z.object({
  sunsetDate: z.string().refine(
    (v) => new Date(v).getTime() > Date.now(),
    { message: "Sunset date must be in the future." },
  ),
});

export type UnDecommissionApplicationInput = z.infer<typeof unDecommissionApplicationSchema>;
```

- [ ] **Step 3: Create `ReactivateConfirmDialog.tsx`**

Mirror the existing `DeprecateConfirmDialog.tsx` exactly — same Dialog wrapper, same Cancel/Confirm buttons — but without a date picker. The body text is:

> Reactivate **{appName}**? The application returns to **Active** and its sunset date is cleared.

The Confirm button calls `useReactivateApplication().mutate(appId)`.

- [ ] **Step 4: Create `UnDecommissionConfirmDialog.tsx`**

Mirror `DeprecateConfirmDialog.tsx` exactly. Body text:

> Restore **{appName}** to **Deprecated**? Provide a new future sunset date.

Include the `DatePicker` for sunsetDate (same component the deprecate dialog uses). Submit calls `useUnDecommissionApplication().mutate({ id, sunsetDate })`.

- [ ] **Step 5: Add the reverse items to `LifecycleMenu.tsx`**

Inside the existing `LifecycleMenu`, after the "Deprecate…" / "Decommission" items, add (conditional on `canReverse` AND state):

```tsx
{canReverse && (lifecycle === "deprecated" || lifecycle === "decommissioned") && (
  <DropdownMenuItem onAction={() => setOpenDialog("reactivate")}>
    Reactivate…
  </DropdownMenuItem>
)}
{canReverse && lifecycle === "decommissioned" && (
  <DropdownMenuItem onAction={() => setOpenDialog("un-decommission")}>
    Restore to Deprecated…
  </DropdownMenuItem>
)}
```

And render the two dialogs based on `openDialog`.

- [ ] **Step 6: Write Vitest cases**

`ReactivateConfirmDialog.test.tsx`:

- Renders the dialog; clicking Confirm calls the mutate function with the app id.
- Cancel closes without mutating.
- On 4xx error, the dialog stays open and renders an inline alert.

`UnDecommissionConfirmDialog.test.tsx`:

- Form fails zod validation with a past sunsetDate.
- Submitting a future sunsetDate calls mutate with the right payload.
- On 4xx error, dialog stays open, inline alert renders.

`LifecycleMenu.test.tsx`: extend with cases asserting reverse items appear only when `canReverse` AND state is Decommissioned (for both) or Deprecated (for Reactivate only).

- [ ] **Step 7: Run, verify all PASS**

```bash
cd web && pnpm vitest run src/features/catalog/components/__tests__/ReactivateConfirmDialog.test.tsx src/features/catalog/components/__tests__/UnDecommissionConfirmDialog.test.tsx src/features/catalog/components/__tests__/LifecycleMenu.test.tsx
```

- [ ] **Step 8: Commit**

```bash
git add web/src/features/catalog/api/applications.ts web/src/features/catalog/components/ReactivateConfirmDialog.tsx web/src/features/catalog/components/UnDecommissionConfirmDialog.tsx web/src/features/catalog/components/__tests__/ReactivateConfirmDialog.test.tsx web/src/features/catalog/components/__tests__/UnDecommissionConfirmDialog.test.tsx web/src/features/catalog/components/LifecycleMenu.tsx web/src/features/catalog/components/__tests__/LifecycleMenu.test.tsx web/src/features/catalog/schemas/unDecommissionApplication.ts
git commit -m "feat(web): Reactivate/UnDecommission dialogs + LifecycleMenu reverse items"
```

---

### Task 17: ADR-0073 addendum + CHECKLIST.md updates

**Files:**
- Modify: `docs/architecture/decisions/ADR-0073-enforced-entity-lifecycle-states.md`
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Append the §5.6 addendum text from the spec**

In `docs/architecture/decisions/ADR-0073-enforced-entity-lifecycle-states.md`, after the existing "Implementation note (slice 6, 2026-05-07)" subsection, add:

```markdown
### Implementation note (slice 7, 2026-05-22)

Backward transitions land as two OrgAdmin-only endpoints: `POST /api/v1/catalog/applications/{id}/reactivate` (empty body; Deprecated→Active OR Decommissioned→Active; clears `sunsetDate`) and `POST /api/v1/catalog/applications/{id}/un-decommission` (`{sunsetDate}` body; Decommissioned→Deprecated; requires strictly-future sunsetDate). Authorization is enforced by the named permission `catalog.applications.lifecycle.reverse` (granted to OrgAdmin only via `KartovaRolePermissions.Map`). Forward transitions remain Member-or-higher. The "may not occur before sunset_date unless an admin overrides" exception on forward `Decommission` is **not** implemented in this slice — sunset-date admin override stays as a registered follow-up (slice 7 §15.1).
```

- [ ] **Step 2: Update `CHECKLIST.md`**

In `docs/product/CHECKLIST.md`, change the lines for E-01.F-04.S-03 and E-01.F-04.S-04 from unchecked to checked with the standard PR-reference annotation:

```markdown
- [x] E-01.F-04.S-03 — RBAC with five roles (slice 7 — PR #XX, 2026-05-22; granular permission model with role→permission map; TeamAdmin forward-compat, ServiceAccount deferred to Phase 5)
- [x] E-01.F-04.S-04 — SSO login via web UI (slice 7 — PR #XX, 2026-05-22; existing OIDC redirect flow satisfies the story; signed-out landing page deferred to §15.9)
```

And update the slice-5 entry for E-02.F-01.S-04 to note that backward transitions landed:

> "backward transitions landed in slice 7 — PR #XX; sunset-date admin override remains follow-up §15.1"

(Replace `#XX` with the actual PR number once the PR is opened.)

Update the "Last updated" date and the Phase 0 + Phase 1 progress counts.

- [ ] **Step 3: Commit**

```bash
git add docs/architecture/decisions/ADR-0073-enforced-entity-lifecycle-states.md docs/product/CHECKLIST.md
git commit -m "docs: ADR-0073 addendum for reverse lifecycle + checklist updates (slice 7)"
```

---

## Definition of Done (mirrors CLAUDE.md + spec §11)

Until **all nine** are green, the honest status is "implementation staged, verification pending" — not "slice 7 complete":

1. **`dotnet build Kartova.slnx`** with `TreatWarningsAsErrors=true` — 0 warnings, 0 errors.
2. **Per-task subagent reviews** (spec-compliance + code-quality) — invoked on each task during implementation, never skipped.
3. **`/superpowers:requesting-code-review`** at slice boundary against full branch diff with this plan + spec as context.
4. **Full test suite green:** `dotnet test Kartova.slnx` (unit + architecture + integration with Testcontainers + KeyCloak) and `cd web && pnpm vitest run`.
5. **`docker compose up` + real HTTP per-role smoke:** log in to the SPA as each of `admin@orga`, `member@orga`, `team-admin@orga`, `viewer@orga`. Confirm `/me/permissions` returns the expected set per role. Confirm that POST `/applications`, PUT `/applications/{id}`, POST `/.../deprecate`, POST `/.../decommission`, POST `/.../reactivate`, POST `/.../un-decommission` succeed or 403 per role. Capture HTTP traces.
6. **`/simplify`** against branch diff — reuse / quality / efficiency lenses.
7. **Mutation feedback loop:** `/misc:mutation-sentinel` → `/misc:test-generator` on changed files until score ≥80% per `stryker-config.json`.
8. **`/pr-review-toolkit:review-pr`** skill.
9. **`/superpowers:deep-review`** against branch diff with spec / plan / ADRs / tests. Blocking + Should-fix addressed; nits triaged.

---

## Self-review

**1. Spec coverage**

| Spec section | Task |
|---|---|
| §4.1 KartovaPermissions constants | 3 |
| §4.2 KartovaRolePermissions.Map | 4 |
| §4.3 KartovaRoles constants (TeamAdmin, Viewer, ServiceAccount) | 2 |
| §4.4 KartovaClaims.Permission | 2 |
| §5.1 TenantClaimsTransformation expansion | 5 |
| §5.2 AddKartovaPermissionPolicies | 6 |
| §5.3 Catalog endpoint topology (existing 6) | 8 |
| §5.3 Catalog endpoint topology (reverse 2) | 11, 12 |
| §5.4 GET /me/permissions | 7 |
| §5.5 PlatformAdmin endpoints unchanged | (no-op — verified by absence of changes) |
| §5.6 ADR-0073 addendum text | 17 |
| §6.1 Reactivate/UnDecommission domain methods | 9, 10 |
| §6.4 Commands / handlers / DTOs / delegates | 11, 12 |
| §7 KeyCloak realm seed | 1 |
| §7.4 Realm-seed arch rules | 1, 2 |
| §8.1 SPA new files (permissions.ts, snapshot, usePermissions, dialogs) | 13, 14, 16 |
| §8.3 SPA gated UI sites | 15 |
| §8.4 Hide-by-default semantics | 15 (AppLayout shell gate) |
| §10 Tests inventory — all rows | distributed across 4, 5, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 |
| §10.1 Token-issuing helpers (viewer + team-admin) | implicit in 1 (realm seed) — `Fx.CreateAuthenticatedClientAsync(username)` already takes any username |
| §10.2 Drift sentinel | 13 |
| §11 DoD | DoD section above |
| §15 Follow-ups | not in scope (documented in spec) |

No spec gaps.

**2. Placeholder scan**

- Task 4 has an "iff present, else colocate" decision on test-project location. The plan tells the engineer to `ls tests/` and pick — that is a concrete decision rule, not a placeholder.
- Task 7 has a fallback for `CreateUnauthenticatedClient` if absent — concrete decision rule, not a placeholder.
- Task 15 references `mockPermissions(...)` and `appWrapper`/`appWrapperWithRouter` test helpers without showing their implementation. These mirror the established Vitest test-utility patterns already in the repo — the engineer should reuse whatever pattern `CatalogListPage.test.tsx` currently uses for mocking. If no pattern exists, the engineer creates one inline (a `setupServer.use(http.get(...))` call wrapped in a function).
- No "TBD", "TODO", "implement later", "fill in details" tokens.
- All steps with code changes include the actual code.

**3. Type consistency**

- `KartovaRolePermissions.ForRole(role)` signature consistent across Tasks 4 (defined), 5 (consumed in transformation), 7 (consumed in integration tests).
- `MePermissionsResponse(string Role, IReadOnlyCollection<string> Permissions)` consistent across Task 7 (DTO + endpoint + tests).
- `usePermissions()` return shape `{ role, hasPermission, isLoading }` consistent across Task 14 (defined) and 15 (consumed).
- Permission name strings (`catalog.read`, `catalog.applications.register`, ...) consistent across Tasks 3, 4, 8, 13.
- `Reactivate()` no-arg signature consistent across Tasks 9 (defined), 11 (handler), 16 (mutation hook).
- `UnDecommission(DateTimeOffset, TimeProvider)` consistent across Tasks 10 (defined), 12 (handler payload `{ Id, SunsetDate }`), 16 (mutation hook).
- ASP.NET policy registration `RequireAuthorization(KartovaPermissions.X)` consistent across Tasks 8, 11, 12.

No type inconsistencies.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-22-slice-7-rbac-roles-and-reverse-lifecycle-plan.md`. Two execution options:

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.

Which approach?
