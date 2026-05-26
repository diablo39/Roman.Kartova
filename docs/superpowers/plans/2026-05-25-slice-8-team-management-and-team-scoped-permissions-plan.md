# Slice 8 — Team management + team-scoped permissions (implementation plan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the `Team` aggregate inside the existing `Kartova.Organization` module with multi-team membership, gate all Catalog mutation endpoints by team-scope using Microsoft's resource-based authorization pattern, expose the team management surface in the SPA, formalize UUIDs as the only entity identifier (ADR-0098), and retroactively drop `Application.Name` to align with that ADR.

**Architecture:** Team metadata in a new `teams` table within the Organization schema; multi-team-per-user membership in `team_members` join table; `Application.TeamId` as a nullable `Guid?` (no cross-module domain coupling per ADR-0082). Team-scoped authorization via `IAuthorizationService.AuthorizeAsync(user, resource, policyName)` with two resource handlers using marker interfaces (`ITeamScopedResource`, `ITeamOwnedResource`) so SharedKernel.AspNetCore avoids referencing concrete domain types. **Team memberships are populated by `TenantScopeBeginMiddleware` immediately after `SET LOCAL app.current_tenant_id`** (not by `TenantClaimsTransformation` — claims transformation runs before middleware and the RLS-scoped query would fail with no tenant setting). SPA mirrors slice 7's hide-by-default UI gating.

**Tech Stack:** .NET 10 · ASP.NET Core minimal APIs · EF Core 10 + Npgsql · Wolverine direct dispatch (ADR-0093) · PostgreSQL 18 with RLS (ADR-0090) · MSTest v4 + NSubstitute (ADR-0097) · Testcontainers + KeyCloak realm seed · React 19 + TanStack Query + Untitled UI · TypeScript with openapi-typescript codegen.

**Spec:** `docs/superpowers/specs/2026-05-25-slice-8-team-management-and-team-scoped-permissions-design.md`

## Revision notes (post-critic)

This plan was revised after a critic cross-validation pass on 2026-05-25. Material changes from v1:

1. **Membership population moved from `TenantClaimsTransformation` to `TenantScopeBeginMiddleware`** — the original design would have run an RLS-scoped DB query before `SET LOCAL app.current_tenant_id` was executed, causing the team_members RLS policy to fail (Critical #7/#8).
2. **Mutation-endpoint refactor (Task 22)** — existing Catalog mutation delegates do NOT pre-load the application; they delegate to handlers that load. Adding the resource-auth gate requires an explicit pre-load in each delegate. The handler still runs its own load (acceptable trade-off — the second hit is change-tracker-cached). Critical #4.
3. **`HttpContextCurrentUser` ctor change made explicit (Task 5)** — the original incorrectly assumed `ITenantContext` was already injected. Critical #1.
4. **`ProblemTypes` path corrected** to `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`. Critical #2.
5. **`KartovaTeamPolicies` path corrected** to `src/Kartova.SharedKernel/Multitenancy/KartovaTeamPolicies.cs`. Critical #3.
6. **`Application.Name` retrofit (Task 25)** — phantom `Fx.RegisterApplicationAsync` helper removed; actual surface is `KartovaApiFixture.SeedApplicationsAsync` / `SeedApplicationsWithLifecycleAsync` plus direct `new RegisterApplicationRequest(...)` construction in test bodies. Critical #6.
7. **New Task 23** — team-membership seeding helper on `KartovaApiFixture` + migration of existing Catalog integration test arrange phases (auth gate breakage from Task 22). Gap #1/#2.
8. **SPA reordered** — codegen (Task 28) now runs before usePermissions extension (Task 30) so the TS type for `teamMemberships` exists when the hook is extended. Gap #3.
9. **`ApplicationNameDropped` architecture test added** to Task 25. Spec §10 / Gap #6.
10. **`OrganizationTeamMembershipReader` invocation strategy** — middleware reads via a fresh request-scoped `OrganizationDbContext` resolved from `HttpContext.RequestServices`. The reader runs after `ITenantScope.BeginAsync`, so RLS is honored. Critical #8.
11. **Role-map test added** to Task 2 — original plan only verified `KartovaPermissions.All` contents, not that each role got the right additional permissions.
12. **Misc:** AddTeamMember endpoint now returns 201 with body (was 204 — inconsistent with CreateTeam shape); `FrozenSet<Guid>.Empty` used for empty TeamIds; explicit DI registration line for `OrganizationTeamExistenceChecker`; verification spike step added to Task 13 confirming `AuthorizationHandler<TRequirement, TInterface>` dispatch.

---

## File Structure

### New backend files

| Path | Responsibility |
|---|---|
| `docs/architecture/decisions/ADR-0098-uuid-only-entity-identifier.md` | ADR-0098 full text per spec §9. |
| `src/Kartova.SharedKernel/Multitenancy/ITeamMembershipReader.cs` | Abstraction for "what teams is this user in?". |
| `src/Kartova.SharedKernel/Multitenancy/TeamMembershipInfo.cs` | `record TeamMembershipInfo(Guid TeamId, TeamRoleKind Role)`. |
| `src/Kartova.SharedKernel/Multitenancy/TeamRoleKind.cs` | `enum TeamRoleKind : byte { Member = 1, Admin = 2 }`. |
| `src/Kartova.SharedKernel/Multitenancy/KartovaTeamPolicies.cs` | Resource-based policy name constants. |
| `src/Kartova.SharedKernel/Multitenancy/ITeamScopedResource.cs` | `interface ITeamScopedResource { Guid? TeamId { get; } }`. |
| `src/Kartova.SharedKernel/Multitenancy/ITeamOwnedResource.cs` | `interface ITeamOwnedResource { Guid TeamId { get; } }`. |
| `src/Kartova.SharedKernel/Multitenancy/IApplicationCountByTeamReader.cs` | Cross-module read abstraction. Impl in Catalog.Infrastructure. |
| `src/Kartova.SharedKernel/Multitenancy/IApplicationIdsByTeamReader.cs` | Cross-module read abstraction. Impl in Catalog.Infrastructure. |
| `src/Kartova.SharedKernel/Multitenancy/IOrganizationTeamExistenceChecker.cs` | Cross-module check abstraction. Impl in Organization.Infrastructure. |
| `src/Kartova.SharedKernel.AspNetCore/AuthorizationHandlers/ApplicationTeamScopedRequirement.cs` | Marker requirement. |
| `src/Kartova.SharedKernel.AspNetCore/AuthorizationHandlers/ApplicationTeamScopedHandler.cs` | `AuthorizationHandler<…, ITeamScopedResource>`. |
| `src/Kartova.SharedKernel.AspNetCore/AuthorizationHandlers/TeamAdminOfThisRequirement.cs` | Marker requirement. |
| `src/Kartova.SharedKernel.AspNetCore/AuthorizationHandlers/TeamAdminOfThisHandler.cs` | `AuthorizationHandler<…, ITeamOwnedResource>`. |
| `src/Modules/Organization/Kartova.Organization.Domain/TeamId.cs` | `readonly record struct TeamId(Guid Value)`. |
| `src/Modules/Organization/Kartova.Organization.Domain/TeamRole.cs` | `enum TeamRole : byte { Member = 1, Admin = 2 }`. |
| `src/Modules/Organization/Kartova.Organization.Domain/Team.cs` | Aggregate root with `ITenantOwned` + `ITeamOwnedResource`. |
| `src/Modules/Organization/Kartova.Organization.Domain/TeamMembership.cs` | Entity (separate aggregate per spec §4). |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/TeamEntityTypeConfiguration.cs` | EF mapping. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/TeamMembershipEntityTypeConfiguration.cs` | EF mapping (composite key). |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationTeamMembershipReader.cs` | `ITeamMembershipReader` impl. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationTeamExistenceChecker.cs` | `IOrganizationTeamExistenceChecker` impl. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/<ts>_AddTeamsTable.cs` | EF migration (new table + RLS). |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/<ts>_AddTeamMembersTable.cs` | EF migration (new table + RLS with EXISTS subquery). |
| `src/Modules/Organization/Kartova.Organization.Application/*.cs` | 9 command/query records + handlers (Create/Update/Delete Team, Add/Remove/Update TeamMember, Get/List Teams). |
| `src/Modules/Organization/Kartova.Organization.Contracts/*.cs` | 7 new DTOs (TeamResponse, TeamDetailResponse, TeamMemberResponse, CreateTeamRequest, UpdateTeamRequest, AddTeamMemberRequest, UpdateTeamMemberRequest, MeTeamMembership). |
| `src/Modules/Catalog/Kartova.Catalog.Application/AssignApplicationTeamCommand.cs` + handler | `(Id, TeamId?)` → result. |
| `src/Modules/Catalog/Kartova.Catalog.Contracts/AssignTeamRequest.cs` | DTO `(Guid? TeamId)`. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApplicationCountByTeamReader.cs` | Reader impl. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApplicationIdsByTeamReader.cs` | Reader impl. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<ts>_AddApplicationTeamId.cs` | EF migration. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<ts>_DropApplicationName.cs` | EF migration. |
| `tests/Kartova.ArchitectureTests/ApplicationNameDroppedRules.cs` | Arch test — `Application` no longer carries `Name`. |

### Modified backend files

| Path | Change |
|---|---|
| `docs/architecture/decisions/README.md` | Add ADR-0098 row. |
| `docs/product/CHECKLIST.md` | Tick E-03.F-02.S-01/S-02; partial S-03. |
| `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` | Add 5 new constants + `All` set entries. |
| `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` | Extend each role set per spec §5.2. |
| `src/Kartova.SharedKernel/Multitenancy/ITenantContext.cs` | Add `TeamMemberships`, `TeamIds`, `PopulateTeamMemberships(...)`. |
| `src/Kartova.SharedKernel/Multitenancy/TenantContextAccessor.cs` | Implement new members + reset in `Clear()`. |
| `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs` | Add `TeamHasApplications` + `InvalidTeam` constants. **(File lives in AspNetCore, not SharedKernel.)** |
| `src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs` | Add `TeamMemberships`, `TeamIds`. |
| `src/Kartova.SharedKernel.AspNetCore/HttpContextCurrentUser.cs` | **Add `ITenantContext` ctor parameter** (currently only `IHttpContextAccessor`). Pass-through new properties. |
| `src/Kartova.SharedKernel.AspNetCore/TenantScopeBeginMiddleware.cs` | After `ITenantScope.BeginAsync`, call `ITeamMembershipReader.GetForUserAsync(userId, ct)` and `ITenantContext.PopulateTeamMemberships(...)`. |
| `src/Kartova.SharedKernel.AspNetCore/AuthorizationExtensions.cs` | Add `AddKartovaResourcePolicies` method. |
| `src/Kartova.SharedKernel.AspNetCore/JwtAuthenticationExtensions.cs` | Wire `AddKartovaResourcePolicies` + handler registrations. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationDbContext.cs` | Add `Teams` + `TeamMembers` DbSets. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs` | Wire new endpoints + register reader/checker + team handlers. |
| `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationEndpointDelegates.cs` | Add team-CRUD + member endpoint delegates. Extend `GetMePermissions` to include `teamMemberships`. |
| `src/Modules/Organization/Kartova.Organization.Contracts/MePermissionsResponse.cs` | Add `TeamMemberships` collection. |
| `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs` | Add `TeamId` + `AssignTeam(...)`. Implement `ITeamScopedResource`. Drop `Name` + `ValidateName` + `KebabCase` regex. |
| `src/Modules/Catalog/Kartova.Catalog.Application/RegisterApplicationCommand.cs` | Drop `Name`. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterApplicationHandler.cs` | Drop `name` from `Application.Create` call. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs` | Map `TeamId`. Drop `Name`. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` | Map `PUT /applications/{id}/team`. Register handler + readers. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | Add `AssignApplicationTeamAsync`. **Refactor each mutation delegate to pre-load the application, then call resource-auth gate, then existing handler dispatch.** Drop `request.Name` from `RegisterApplicationAsync`. |
| `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApplicationSortSpecs.cs` | Drop `Name` sort spec; update `AllowedFieldNames`. |
| `src/Modules/Catalog/Kartova.Catalog.Contracts/RegisterApplicationRequest.cs` | Drop `Name`. |
| `src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationResponse.cs` | Drop `Name`. Add `Guid? TeamId`. |
| `src/Modules/Catalog/Kartova.Catalog.Contracts/ApplicationSortField.cs` | Drop `Name` enum value. |
| `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/KartovaApiFixture.cs` | Add `SeedTeamAsync` + `SeedTeamMembershipAsync` (bypass-RLS direct insert). Update `SeedApplicationsAsync` / `SeedApplicationsWithLifecycleAsync` to drop `name` arg. Update `DeleteApplicationsByPrefixAsync` to filter by `DisplayName` (was `name`). |

### New SPA files

| Path | Responsibility |
|---|---|
| `web/src/features/teams/api/teams.ts` | Team React Query hooks. |
| `web/src/features/teams/api/__tests__/teams.test.tsx` | Hook tests. |
| `web/src/features/teams/pages/TeamsListPage.tsx` | `/teams` list. |
| `web/src/features/teams/pages/TeamDetailPage.tsx` | `/teams/:id` detail. |
| `web/src/features/teams/pages/__tests__/{TeamsListPage,TeamDetailPage}.test.tsx` | Component tests. |
| `web/src/features/teams/components/{CreateTeamDialog,RenameTeamDialog,DeleteTeamConfirmDialog,AddMemberDialog,RemoveMemberConfirmDialog,ChangeRoleDialog,AssignTeamPicker}.tsx` | Dialogs + picker. |
| `web/src/features/teams/components/__tests__/*.test.tsx` | Per-component tests. |
| `web/src/features/teams/schemas/{createTeam,updateTeam,addTeamMember}.ts` | zod schemas. |

### Modified SPA files

| Path | Change |
|---|---|
| `web/src/shared/auth/permissions.ts` + `permissions.snapshot.json` | +5 permission constants. |
| `web/src/shared/auth/usePermissions.ts` | Extend with `teamIds`, `teamAdminTeamIds`. |
| `web/src/shared/auth/__tests__/usePermissions.test.tsx` | Audit existing mocks for `MePermissionsResponse` shape drift. |
| `web/src/app/router.tsx` | Add `/teams` + `/teams/:id` routes. |
| `web/src/components/layout/Sidebar.tsx` | Add "Teams" entry gated by `team.read`. |
| `web/src/features/catalog/pages/{ApplicationDetailPage,CatalogListPage}.tsx` | Drop kebab-name badge. Add `AssignTeamPicker` on detail. |
| `web/src/features/catalog/components/{RegisterApplicationDialog,ApplicationsTable}.tsx` | Drop kebab-name UI. |
| `web/src/features/catalog/schemas/registerApplication.ts` | Drop `name` + kebab regex. |
| `web/src/features/catalog/api/applications.ts` | Add `useAssignApplicationTeam`. |
| `web/openapi-snapshot.json` + `web/src/generated/openapi.ts` | Regenerated against slice-8 API. |

---

## Tasks

### Task 1: ADR-0098 + README index

**Files:**
- Create: `docs/architecture/decisions/ADR-0098-uuid-only-entity-identifier.md`
- Modify: `docs/architecture/decisions/README.md`

- [ ] **Step 1: Write ADR-0098 content per spec §9**

Create the ADR file with the exact text from spec §9 (full body, starting "# ADR-0098: UUIDs as the Canonical and Only Entity Identifier" through the References section).

- [ ] **Step 2: Index ADR-0098 in README**

Read `docs/architecture/decisions/README.md`. Insert a row for ADR-0098 in the sorted table between ADR-0097 and the keyword index:

```markdown
| [0098](ADR-0098-uuid-only-entity-identifier.md) | UUIDs as the Canonical and Only Entity Identifier | API & Integration Architecture | Accepted | 0001, 0011, 0029, 0082, 0092 | UUIDs are the canonical and only entity identifier across Kartova. URLs use `{id:guid}` exclusively; no slugs anywhere. |
```

Also add ADR-0098 to the **Resource identifier** keyword group.

- [ ] **Step 3: Commit**

```bash
git add docs/architecture/decisions/ADR-0098-uuid-only-entity-identifier.md docs/architecture/decisions/README.md
git commit -m "docs(adr): ADR-0098 — UUIDs as the canonical and only entity identifier"
```

---

### Task 2: Permission constants + role map

**Files:**
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs`
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs`
- Modify: `tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs`

- [ ] **Step 1: Write failing tests — both `All` membership AND role map shape**

Append to `KartovaPermissionsRules.cs`:

```csharp
[TestMethod]
public void Team_permissions_are_present_in_KartovaPermissions_All()
{
    string[] expected = ["team.read", "team.create", "team.metadata.edit", "team.delete", "team.members.manage"];
    foreach (var perm in expected)
        Assert.IsTrue(KartovaPermissions.All.Contains(perm), $"missing: {perm}");
}

[DataTestMethod]
[DataRow(KartovaRoles.Viewer,   new[] { "team.read" })]
[DataRow(KartovaRoles.Member,   new[] { "team.read" })]
[DataRow(KartovaRoles.TeamAdmin, new[] { "team.read", "team.metadata.edit", "team.delete", "team.members.manage" })]
[DataRow(KartovaRoles.OrgAdmin, new[] { "team.read", "team.create", "team.metadata.edit", "team.delete", "team.members.manage" })]
public void Role_permissions_include_team_perms(string role, string[] requiredPerms)
{
    Assert.IsTrue(KartovaRolePermissions.Map.TryGetValue(role, out var perms), $"role missing: {role}");
    foreach (var p in requiredPerms)
        Assert.IsTrue(perms.Contains(p), $"role {role} missing perm {p}");
}
```

- [ ] **Step 2: Run, expect FAIL**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter \"FullyQualifiedName~Team_permissions|FullyQualifiedName~Role_permissions_include_team_perms\" --nologo"
```

Expected: FAIL — constants and map entries don't exist.

- [ ] **Step 3: Add the 5 new constants to `KartovaPermissions.cs`**

Append after existing Catalog permissions:

```csharp
public const string TeamRead          = "team.read";
public const string TeamCreate        = "team.create";
public const string TeamMetadataEdit  = "team.metadata.edit";
public const string TeamDelete        = "team.delete";
public const string TeamMembersManage = "team.members.manage";
```

Add to the `All` `FrozenSet<string>` initializer.

- [ ] **Step 4: Extend `KartovaRolePermissions.Map`**

Per spec §5.2:

```csharp
// Viewer:    + team.read
// Member:    + team.read
// TeamAdmin: + team.read, team.metadata.edit, team.delete, team.members.manage
// OrgAdmin:  + team.read, team.create, team.metadata.edit, team.delete, team.members.manage
```

Preserve the `FrozenDictionary<string, FrozenSet<string>>` shape.

- [ ] **Step 5: Run tests, all pass**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --nologo"
cmd //c "dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --nologo"
```

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs
git commit -m "feat(perm): add 5 team permissions and role map entries (slice 8)"
```

---

### Task 3: SharedKernel membership abstractions

**Files:**
- Create: `src/Kartova.SharedKernel/Multitenancy/TeamRoleKind.cs`
- Create: `src/Kartova.SharedKernel/Multitenancy/TeamMembershipInfo.cs`
- Create: `src/Kartova.SharedKernel/Multitenancy/ITeamMembershipReader.cs`
- Create: `src/Kartova.SharedKernel/Multitenancy/ITeamScopedResource.cs`
- Create: `src/Kartova.SharedKernel/Multitenancy/ITeamOwnedResource.cs`

- [ ] **Step 1: Create the 5 files**

```csharp
// TeamRoleKind.cs
namespace Kartova.SharedKernel.Multitenancy;
public enum TeamRoleKind : byte { Member = 1, Admin = 2 }

// TeamMembershipInfo.cs
namespace Kartova.SharedKernel.Multitenancy;
public sealed record TeamMembershipInfo(Guid TeamId, TeamRoleKind Role);

// ITeamMembershipReader.cs
namespace Kartova.SharedKernel.Multitenancy;
public interface ITeamMembershipReader
{
    Task<IReadOnlyList<TeamMembershipInfo>> GetForUserAsync(Guid userId, CancellationToken ct);
}

// ITeamScopedResource.cs
namespace Kartova.SharedKernel.Multitenancy;
public interface ITeamScopedResource { Guid? TeamId { get; } }

// ITeamOwnedResource.cs
namespace Kartova.SharedKernel.Multitenancy;
public interface ITeamOwnedResource { Guid TeamId { get; } }
```

- [ ] **Step 2: Build**

```bash
cmd //c "dotnet build src/Kartova.SharedKernel/Kartova.SharedKernel.csproj --nologo"
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/{TeamRoleKind,TeamMembershipInfo,ITeamMembershipReader,ITeamScopedResource,ITeamOwnedResource}.cs
git commit -m "feat(shared): team-membership abstractions and resource markers (slice 8)"
```

---

### Task 4: Extend `ITenantContext` + `TenantContextAccessor`

**Files:**
- Modify: `src/Kartova.SharedKernel/Multitenancy/ITenantContext.cs`
- Modify: `src/Kartova.SharedKernel/Multitenancy/TenantContextAccessor.cs`
- Modify: `tests/Kartova.SharedKernel.Tests/TenantContextAccessorTests.cs`

- [ ] **Step 1: Failing tests**

Append to `TenantContextAccessorTests.cs`:

```csharp
[TestMethod]
public void PopulateTeamMemberships_sets_collections_and_TeamIds_shortcut()
{
    var ctx = new TenantContextAccessor();
    var memberships = new List<TeamMembershipInfo>
    {
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"), TeamRoleKind.Member),
        new(Guid.Parse("22222222-2222-2222-2222-222222222222"), TeamRoleKind.Admin),
    };

    ctx.PopulateTeamMemberships(memberships);

    CollectionAssert.AreEquivalent(memberships, ctx.TeamMemberships.ToList());
    CollectionAssert.AreEquivalent(memberships.Select(m => m.TeamId).ToList(), ctx.TeamIds.ToList());
}

[TestMethod]
public void Clear_resets_team_memberships()
{
    var ctx = new TenantContextAccessor();
    ctx.PopulateTeamMemberships(new[] { new TeamMembershipInfo(Guid.NewGuid(), TeamRoleKind.Admin) });
    ctx.Clear();
    Assert.AreEqual(0, ctx.TeamMemberships.Count);
    Assert.AreEqual(0, ctx.TeamIds.Count);
}

[TestMethod]
public void TeamMemberships_default_is_empty()
{
    var ctx = new TenantContextAccessor();
    Assert.AreEqual(0, ctx.TeamMemberships.Count);
    Assert.AreEqual(0, ctx.TeamIds.Count);
}
```

- [ ] **Step 2: Run, expect FAIL**

```bash
cmd //c "dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj --filter \"FullyQualifiedName~TenantContextAccessor\" --nologo"
```

- [ ] **Step 3: Extend `ITenantContext`**

Add to the interface:

```csharp
IReadOnlyList<TeamMembershipInfo> TeamMemberships { get; }
IReadOnlySet<Guid> TeamIds { get; }
void PopulateTeamMemberships(IReadOnlyList<TeamMembershipInfo> memberships);
```

- [ ] **Step 4: Implement in `TenantContextAccessor`**

```csharp
private IReadOnlyList<TeamMembershipInfo> _teamMemberships = Array.Empty<TeamMembershipInfo>();
private IReadOnlySet<Guid> _teamIds = FrozenSet<Guid>.Empty;

public IReadOnlyList<TeamMembershipInfo> TeamMemberships => _teamMemberships;
public IReadOnlySet<Guid> TeamIds => _teamIds;

public void PopulateTeamMemberships(IReadOnlyList<TeamMembershipInfo> memberships)
{
    _teamMemberships = memberships;
    _teamIds = memberships.Select(m => m.TeamId).ToFrozenSet();
}
```

Update `Clear()` to reset both fields to their empty defaults.

- [ ] **Step 5: Run, expect PASS**

Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/ITenantContext.cs src/Kartova.SharedKernel/Multitenancy/TenantContextAccessor.cs tests/Kartova.SharedKernel.Tests/TenantContextAccessorTests.cs
git commit -m "feat(shared): ITenantContext exposes team memberships (slice 8)"
```

---

### Task 5: Extend `ICurrentUser` + inject `ITenantContext` into `HttpContextCurrentUser`

**Files:**
- Modify: `src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs`
- Modify: `src/Kartova.SharedKernel.AspNetCore/HttpContextCurrentUser.cs`

> **Important:** `HttpContextCurrentUser` currently injects only `IHttpContextAccessor`. This task adds `ITenantContext` to the constructor — verify no existing tests new-up `HttpContextCurrentUser` directly with the old signature; if they do, update them.

- [ ] **Step 1: Add `ITenantContext` to ctor and properties**

Read `HttpContextCurrentUser.cs` for current state. Then modify:

```csharp
internal sealed class HttpContextCurrentUser(
    IHttpContextAccessor httpContextAccessor,
    ITenantContext tenantContext) : ICurrentUser
{
    // existing properties — keep
    // …

    public IReadOnlyList<TeamMembershipInfo> TeamMemberships => tenantContext.TeamMemberships;
    public IReadOnlySet<Guid> TeamIds => tenantContext.TeamIds;
}
```

Use primary-constructor syntax if existing class already does; otherwise add as field + ctor param consistent with current style.

- [ ] **Step 2: Extend `ICurrentUser`**

```csharp
IReadOnlyList<TeamMembershipInfo> TeamMemberships { get; }
IReadOnlySet<Guid> TeamIds { get; }
```

Add `using Kartova.SharedKernel.Multitenancy;` if needed.

- [ ] **Step 3: Audit direct constructions**

```bash
cmd //c "findstr /s /n /r \"new HttpContextCurrentUser\" src tests"
```

Update any direct constructions to pass an `ITenantContext` substitute (e.g., `new TenantContextAccessor()` or a NSubstitute mock).

- [ ] **Step 4: Build**

```bash
cmd //c "dotnet build src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj --nologo"
cmd //c "dotnet build tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --nologo"
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs src/Kartova.SharedKernel.AspNetCore/HttpContextCurrentUser.cs <any test updates>
git commit -m "feat(shared): ICurrentUser exposes team memberships via ITenantContext (slice 8)"
```

---

### Task 6: Add ProblemTypes constants

**Files:**
- Modify: `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`

> **Note:** `ProblemTypes` lives in `Kartova.SharedKernel.AspNetCore`, NOT `Kartova.SharedKernel.Multitenancy`.

- [ ] **Step 1: Append constants**

```csharp
public const string TeamHasApplications = "https://kartova.io/problems/team-has-applications";
public const string InvalidTeam         = "https://kartova.io/problems/invalid-team";
```

- [ ] **Step 2: Build full solution**

```bash
cmd //c "dotnet build Kartova.slnx --nologo"
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs
git commit -m "feat(shared): add team-related problem types (slice 8)"
```

---

### Task 7: Team domain — `TeamId` + `TeamRole` + `Team` aggregate

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Domain/{TeamId,TeamRole,Team}.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Tests/TeamTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Organization.Tests;

[TestClass]
public sealed class TeamTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());

    [TestMethod]
    public void Create_with_valid_inputs_sets_properties()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var team = Team.Create("Platform", "Owns infra and tooling", Tenant, clock);

        Assert.AreEqual("Platform", team.DisplayName);
        Assert.AreEqual("Owns infra and tooling", team.Description);
        Assert.AreEqual(Tenant, team.TenantId);
        Assert.AreEqual(clock.GetUtcNow(), team.CreatedAt);
        Assert.AreNotEqual(Guid.Empty, team.Id.Value);
    }

    [TestMethod]
    public void Create_with_null_description_is_allowed()
    {
        var team = Team.Create("Platform", null, Tenant, new FakeTimeProvider(DateTimeOffset.UtcNow));
        Assert.IsNull(team.Description);
    }

    [TestMethod]
    public void Create_with_empty_display_name_throws()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Assert.ThrowsExactly<ArgumentException>(() => Team.Create("", null, Tenant, clock));
        Assert.ThrowsExactly<ArgumentException>(() => Team.Create("   ", null, Tenant, clock));
    }

    [TestMethod]
    public void Create_with_too_long_display_name_throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            Team.Create(new string('a', 129), null, Tenant, new FakeTimeProvider(DateTimeOffset.UtcNow)));
    }

    [TestMethod]
    public void Create_with_too_long_description_throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            Team.Create("Platform", new string('a', 513), Tenant, new FakeTimeProvider(DateTimeOffset.UtcNow)));
    }

    [TestMethod]
    public void Rename_updates_display_name_and_description()
    {
        var team = Team.Create("Platform", "Initial", Tenant, new FakeTimeProvider(DateTimeOffset.UtcNow));
        team.Rename("Platform v2", "Updated");
        Assert.AreEqual("Platform v2", team.DisplayName);
        Assert.AreEqual("Updated", team.Description);
    }

    [TestMethod]
    public void Team_implements_ITenantOwned_and_ITeamOwnedResource()
    {
        var team = Team.Create("Platform", null, Tenant, new FakeTimeProvider(DateTimeOffset.UtcNow));
        Assert.IsInstanceOfType<ITenantOwned>(team);
        Assert.IsInstanceOfType<ITeamOwnedResource>(team);
        Assert.AreEqual(team.Id.Value, ((ITeamOwnedResource)team).TeamId);
    }
}
```

- [ ] **Step 2: Run, expect BUILD FAILURE**

```bash
cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj --filter \"FullyQualifiedName~TeamTests\" --nologo"
```

- [ ] **Step 3: Create `TeamId.cs` + `TeamRole.cs`**

```csharp
// TeamId.cs
namespace Kartova.Organization.Domain;
public readonly record struct TeamId(Guid Value)
{
    public static TeamId New() => new(Guid.NewGuid());
}

// TeamRole.cs
namespace Kartova.Organization.Domain;
public enum TeamRole : byte { Member = 1, Admin = 2 }
```

- [ ] **Step 4: Create `Team.cs`**

```csharp
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.Domain;

public sealed partial class Team : ITenantOwned, ITeamOwnedResource
{
    private Guid _id;

    public TeamId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    Guid ITeamOwnedResource.TeamId => _id;

    private Team() { /* EF */ }

    public static Team Create(string displayName, string? description, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        return new Team
        {
            _id = Guid.NewGuid(),
            TenantId = tenantId,
            DisplayName = displayName,
            Description = description,
            CreatedAt = clock.GetUtcNow(),
        };
    }

    public void Rename(string newDisplayName, string? newDescription)
    {
        ValidateDisplayName(newDisplayName);
        ValidateDescription(newDescription);
        DisplayName = newDisplayName;
        Description = newDescription;
    }

    private static void ValidateDisplayName(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("Team display name must not be empty.", nameof(s));
        if (s.Length > 128)
            throw new ArgumentException("Team display name must be <= 128 characters.", nameof(s));
    }

    private static void ValidateDescription(string? s)
    {
        if (s is { Length: > 512 })
            throw new ArgumentException("Team description must be <= 512 characters.", nameof(s));
    }
}
```

- [ ] **Step 5: Run tests, expect PASS**

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Domain/{TeamId,TeamRole,Team}.cs src/Modules/Organization/Kartova.Organization.Tests/TeamTests.cs
git commit -m "feat(domain): Team aggregate (slice 8)"
```

---

### Task 8: TeamMembership entity

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Domain/TeamMembership.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Tests/TeamMembershipTests.cs`

- [ ] **Step 1: Failing tests**

```csharp
[TestClass]
public sealed class TeamMembershipTests
{
    [TestMethod]
    public void Create_with_valid_inputs_sets_properties()
    {
        var teamId = TeamId.New();
        var userId = Guid.NewGuid();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);

        var m = TeamMembership.Create(teamId, userId, TeamRole.Admin, clock);

        Assert.AreEqual(teamId, m.TeamId);
        Assert.AreEqual(userId, m.UserId);
        Assert.AreEqual(TeamRole.Admin, m.Role);
        Assert.AreEqual(clock.GetUtcNow(), m.AddedAt);
    }

    [TestMethod]
    public void Create_with_empty_user_id_throws()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            TeamMembership.Create(TeamId.New(), Guid.Empty, TeamRole.Member, new FakeTimeProvider(DateTimeOffset.UtcNow)));
    }

    [TestMethod]
    public void ChangeRole_updates_role()
    {
        var m = TeamMembership.Create(TeamId.New(), Guid.NewGuid(), TeamRole.Member,
            new FakeTimeProvider(DateTimeOffset.UtcNow));
        m.ChangeRole(TeamRole.Admin);
        Assert.AreEqual(TeamRole.Admin, m.Role);
    }
}
```

- [ ] **Step 2: Run, expect BUILD FAILURE**

- [ ] **Step 3: Create `TeamMembership.cs`**

```csharp
namespace Kartova.Organization.Domain;

public sealed class TeamMembership
{
    public TeamId TeamId { get; private set; }
    public Guid UserId { get; private set; }
    public TeamRole Role { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    private TeamMembership() { /* EF */ }

    public static TeamMembership Create(TeamId teamId, Guid userId, TeamRole role, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (userId == Guid.Empty)
            throw new ArgumentException("userId required", nameof(userId));
        return new TeamMembership
        {
            TeamId = teamId,
            UserId = userId,
            Role = role,
            AddedAt = clock.GetUtcNow(),
        };
    }

    public void ChangeRole(TeamRole newRole) => Role = newRole;
}
```

- [ ] **Step 4: Run tests, expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Domain/TeamMembership.cs src/Modules/Organization/Kartova.Organization.Tests/TeamMembershipTests.cs
git commit -m "feat(domain): TeamMembership entity (slice 8)"
```

---

### Task 9: EF configurations + DbContext

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/{TeamEntityTypeConfiguration,TeamMembershipEntityTypeConfiguration}.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationDbContext.cs`

- [ ] **Step 1: Create `TeamEntityTypeConfiguration.cs`**

```csharp
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Organization.Infrastructure;

internal sealed class TeamEntityTypeConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams");
        builder.HasKey("_id");
        builder.Property<Guid>("_id").HasColumnName("id");
        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(t => t.Value, g => new TenantId(g));
        builder.Property(x => x.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(128).IsRequired();
        builder.Property(x => x.Description)
            .HasColumnName("description")
            .HasMaxLength(512);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.HasIndex(x => x.TenantId).HasDatabaseName("idx_teams_tenant");
        builder.Ignore(x => x.Id);
    }
}
```

- [ ] **Step 2: Create `TeamMembershipEntityTypeConfiguration.cs`**

```csharp
internal sealed class TeamMembershipEntityTypeConfiguration : IEntityTypeConfiguration<TeamMembership>
{
    public void Configure(EntityTypeBuilder<TeamMembership> builder)
    {
        builder.ToTable("team_members");
        builder.HasKey(x => new { x.TeamId, x.UserId });
        builder.Property(x => x.TeamId)
            .HasColumnName("team_id")
            .HasConversion(t => t.Value, g => new TeamId(g));
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Role).HasColumnName("role").HasConversion<byte>();
        builder.Property(x => x.AddedAt).HasColumnName("added_at");
        builder.HasIndex(x => x.UserId).HasDatabaseName("idx_team_members_user");
    }
}
```

- [ ] **Step 3: Extend `OrganizationDbContext`**

Add:

```csharp
public DbSet<Team> Teams => Set<Team>();
public DbSet<TeamMembership> TeamMembers => Set<TeamMembership>();
```

- [ ] **Step 4: Build**

```bash
cmd //c "dotnet build src/Modules/Organization/Kartova.Organization.Infrastructure/Kartova.Organization.Infrastructure.csproj --nologo"
```

Expected: 0 errors, 0 warnings. Note: the EF model snapshot file (`OrganizationDbContextModelSnapshot.cs`) regenerates automatically in Task 10 during `dotnet ef migrations add`.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/{TeamEntityTypeConfiguration,TeamMembershipEntityTypeConfiguration,OrganizationDbContext}.cs
git commit -m "feat(infra): Team + TeamMembership EF mappings (slice 8)"
```

---

### Task 10: Migrations — `AddTeamsTable` + `AddTeamMembersTable`

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/<ts>_AddTeamsTable.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/<ts>_AddTeamMembersTable.cs`

- [ ] **Step 1: Generate `AddTeamsTable`**

```bash
cmd //c "dotnet ef migrations add AddTeamsTable --project src/Modules/Organization/Kartova.Organization.Infrastructure --startup-project src/Kartova.Migrator --context OrganizationDbContext"
```

- [ ] **Step 2: Add RLS to the generated `Up`**

After the `CreateTable` block:

```csharp
migrationBuilder.Sql("ALTER TABLE teams ENABLE ROW LEVEL SECURITY;");
migrationBuilder.Sql("ALTER TABLE teams FORCE ROW LEVEL SECURITY;");
migrationBuilder.Sql(@"
    CREATE POLICY tenant_isolation ON teams
      USING (tenant_id = current_setting('app.current_tenant_id')::uuid);");
```

In `Down`, drop the policy.

Print the generated `Up` to confirm pure DDL (no backfill toggle dance needed — new table):

```bash
cmd //c "type src\\Modules\\Organization\\Kartova.Organization.Infrastructure\\Migrations\\*AddTeamsTable.cs"
```

- [ ] **Step 3: Generate `AddTeamMembersTable`**

```bash
cmd //c "dotnet ef migrations add AddTeamMembersTable --project src/Modules/Organization/Kartova.Organization.Infrastructure --startup-project src/Kartova.Migrator --context OrganizationDbContext"
```

- [ ] **Step 4: Add RLS with EXISTS-subquery policy**

```csharp
migrationBuilder.Sql("ALTER TABLE team_members ENABLE ROW LEVEL SECURITY;");
migrationBuilder.Sql("ALTER TABLE team_members FORCE ROW LEVEL SECURITY;");
migrationBuilder.Sql(@"
    CREATE POLICY tenant_isolation ON team_members
      USING (EXISTS (
        SELECT 1 FROM teams t
        WHERE t.id = team_members.team_id
          AND t.tenant_id = current_setting('app.current_tenant_id')::uuid
      ));");
```

- [ ] **Step 5: Build**

```bash
cmd //c "dotnet build src/Modules/Organization/Kartova.Organization.Infrastructure/Kartova.Organization.Infrastructure.csproj --nologo"
```

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/
git commit -m "feat(infra): migrations for teams + team_members tables (slice 8)"
```

---

### Task 11: `OrganizationTeamMembershipReader` + DI registration

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationTeamMembershipReader.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs`

- [ ] **Step 1: Create the reader**

```csharp
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

internal sealed class OrganizationTeamMembershipReader(OrganizationDbContext db) : ITeamMembershipReader
{
    public async Task<IReadOnlyList<TeamMembershipInfo>> GetForUserAsync(Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return Array.Empty<TeamMembershipInfo>();

        var rows = await db.TeamMembers
            .Where(m => m.UserId == userId)
            .Select(m => new TeamMembershipInfo(m.TeamId.Value, (TeamRoleKind)(byte)m.Role))
            .ToListAsync(ct);

        return rows;
    }
}
```

- [ ] **Step 2: Register in `OrganizationModule.RegisterServices`**

```csharp
services.AddScoped<ITeamMembershipReader, OrganizationTeamMembershipReader>();
```

- [ ] **Step 3: Build**

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationTeamMembershipReader.cs src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs
git commit -m "feat(infra): OrganizationTeamMembershipReader + DI registration (slice 8)"
```

---

### Task 12: Populate team memberships from `TenantScopeBeginMiddleware`

**Files:**
- Modify: `src/Kartova.SharedKernel.AspNetCore/TenantScopeBeginMiddleware.cs`
- Modify: `tests/Kartova.SharedKernel.AspNetCore.Tests/TenantScopeBeginMiddlewareTests.cs` (if exists; otherwise create)

> **Why here and not in `TenantClaimsTransformation`:** claims transformation runs during the authentication phase, *before* `TenantScopeBeginMiddleware` executes `SET LOCAL app.current_tenant_id` on the request-scoped connection. A DB query at that point either fails the RLS cast (`current_setting(...)::uuid` on empty string) or reads from the wrong tenant scope. Populating right after `BeginAsync` guarantees the connection has the tenant_id set and the request DbContext is the one all subsequent code sees.

- [ ] **Step 1: Failing integration-style test**

```csharp
[TestMethod]
public async Task Middleware_populates_team_memberships_after_tenant_scope_begins()
{
    var userId = Guid.NewGuid();
    var memberships = new List<TeamMembershipInfo>
    {
        new(Guid.NewGuid(), TeamRoleKind.Admin),
        new(Guid.NewGuid(), TeamRoleKind.Member),
    };

    var reader = Substitute.For<ITeamMembershipReader>();
    reader.GetForUserAsync(userId, Arg.Any<CancellationToken>()).Returns(memberships);

    var scope = Substitute.For<ITenantScope>();
    var tenantContext = new TenantContextAccessor();
    tenantContext.SetTenant(new TenantId(Guid.NewGuid()));

    var sut = new TenantScopeBeginMiddleware(/*next*/ ctx => Task.CompletedTask);

    var httpContext = BuildHttpContextWithUser(userId, tenantContext, scope, reader);
    await sut.InvokeAsync(httpContext);

    Assert.AreEqual(2, tenantContext.TeamMemberships.Count);
    CollectionAssert.AreEquivalent(
        memberships.Select(m => m.TeamId).ToList(),
        tenantContext.TeamIds.ToList());
}
```

(Adapt `BuildHttpContextWithUser` to existing fixture helpers in the test project.)

- [ ] **Step 2: Run, expect FAIL**

```bash
cmd //c "dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --filter \"FullyQualifiedName~Middleware_populates_team_memberships\" --nologo"
```

- [ ] **Step 3: Extend `TenantScopeBeginMiddleware.InvokeAsync`**

Read the existing implementation. After the `await scope.BeginAsync(...)` call and before `await _next(context)`, insert:

```csharp
var sub = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
if (Guid.TryParse(sub, out var userId))
{
    var reader = context.RequestServices.GetService<ITeamMembershipReader>();
    if (reader is not null)
    {
        var memberships = await reader.GetForUserAsync(userId, context.RequestAborted);
        tenantContext.PopulateTeamMemberships(memberships);
    }
}
```

(Resolve `reader` from `RequestServices`, not the captured root provider — guarantees same scope as the DbContext.)

- [ ] **Step 4: Re-run, expect PASS**

Plus all existing TenantScopeBeginMiddleware tests still pass.

- [ ] **Step 5: Commit**

```bash
git add src/Kartova.SharedKernel.AspNetCore/TenantScopeBeginMiddleware.cs tests/Kartova.SharedKernel.AspNetCore.Tests/TenantScopeBeginMiddlewareTests.cs
git commit -m "feat(middleware): populate team memberships after tenant scope begins (slice 8)"
```

---

### Task 13: Authorization requirements + handlers + policies

**Files:**
- Create: `src/Kartova.SharedKernel/Multitenancy/KartovaTeamPolicies.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/AuthorizationHandlers/{ApplicationTeamScopedRequirement,ApplicationTeamScopedHandler,TeamAdminOfThisRequirement,TeamAdminOfThisHandler}.cs`
- Modify: `src/Kartova.SharedKernel.AspNetCore/{AuthorizationExtensions,JwtAuthenticationExtensions}.cs`
- Create: `tests/Kartova.SharedKernel.AspNetCore.Tests/{ApplicationTeamScopedHandlerTests,TeamAdminOfThisHandlerTests,ResourcePolicyIntegrationTests}.cs`

- [ ] **Step 1: Create `KartovaTeamPolicies.cs`**

```csharp
namespace Kartova.SharedKernel.Multitenancy;

public static class KartovaTeamPolicies
{
    public const string ApplicationTeamScoped = "team-scoped:application";
    public const string TeamAdminOfThis       = "team-admin-of-this";
}
```

- [ ] **Step 2: Create requirements**

```csharp
// ApplicationTeamScopedRequirement.cs
using Microsoft.AspNetCore.Authorization;
namespace Kartova.SharedKernel.AspNetCore.AuthorizationHandlers;
public sealed class ApplicationTeamScopedRequirement : IAuthorizationRequirement;

// TeamAdminOfThisRequirement.cs
public sealed class TeamAdminOfThisRequirement : IAuthorizationRequirement;
```

- [ ] **Step 3: Failing handler unit tests** (per spec §10 — Member/TeamAdmin/OrgAdmin matrix on FakeApp + FakeTeam)

Write `ApplicationTeamScopedHandlerTests.cs` covering 4 cases: OrgAdmin always succeeds; non-OrgAdmin on unassigned app fails; Member in team succeeds; Member not in team fails. (Full code in v1 plan — keep that exact body.)

Write `TeamAdminOfThisHandlerTests.cs` covering 4 cases: OrgAdmin succeeds; TeamAdmin-of-this-team succeeds; Member-of-team but not Admin fails; TeamAdmin-of-other-team fails. (Full code in v1 plan — keep.)

For test fakes:

```csharp
private sealed class FakeApp : ITeamScopedResource { public Guid? TeamId { get; init; } }
private sealed class FakeTeam : ITeamOwnedResource { public Guid TeamId { get; init; } }
```

Use `AuthorizationHandlerContext` ctor `(IEnumerable<IAuthorizationRequirement>, ClaimsPrincipal, object?)`. Call `await ((IAuthorizationHandler)sut).HandleAsync(ctx)` (the base `AuthorizationHandler<>` implements `IAuthorizationHandler.HandleAsync` publicly and dispatches to the protected `HandleRequirementAsync`).

- [ ] **Step 4: Create handlers**

```csharp
// ApplicationTeamScopedHandler.cs
public sealed class ApplicationTeamScopedHandler(ICurrentUser currentUser)
    : AuthorizationHandler<ApplicationTeamScopedRequirement, ITeamScopedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApplicationTeamScopedRequirement requirement,
        ITeamScopedResource resource)
    {
        if (context.User.IsInRole(KartovaRoles.OrgAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (resource.TeamId is null) return Task.CompletedTask;

        if (currentUser.TeamIds.Contains(resource.TeamId.Value))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}

// TeamAdminOfThisHandler.cs
public sealed class TeamAdminOfThisHandler(ICurrentUser currentUser)
    : AuthorizationHandler<TeamAdminOfThisRequirement, ITeamOwnedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TeamAdminOfThisRequirement requirement,
        ITeamOwnedResource resource)
    {
        if (context.User.IsInRole(KartovaRoles.OrgAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (currentUser.TeamMemberships.Any(m =>
                m.TeamId == resource.TeamId && m.Role == TeamRoleKind.Admin))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: Add `AddKartovaResourcePolicies` extension**

```csharp
public static AuthorizationBuilder AddKartovaResourcePolicies(this AuthorizationBuilder builder)
{
    builder.AddPolicy(KartovaTeamPolicies.ApplicationTeamScoped, p =>
        p.Requirements.Add(new ApplicationTeamScopedRequirement()));
    builder.AddPolicy(KartovaTeamPolicies.TeamAdminOfThis, p =>
        p.Requirements.Add(new TeamAdminOfThisRequirement()));
    return builder;
}
```

- [ ] **Step 6: Wire from `JwtAuthenticationExtensions.AddKartovaJwtAuth`**

After the existing `.AddKartovaPermissionPolicies()` call:

```csharp
.AddKartovaResourcePolicies();
```

Plus:

```csharp
services.AddScoped<IAuthorizationHandler, ApplicationTeamScopedHandler>();
services.AddScoped<IAuthorizationHandler, TeamAdminOfThisHandler>();
```

- [ ] **Step 7: Integration spike — verify `AuthorizationHandler<TRequirement, TInterface>` dispatches by interface**

Write `ResourcePolicyIntegrationTests.cs`:

```csharp
[TestMethod]
public async Task AuthorizeAsync_resolves_handler_by_interface_match()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddAuthorizationBuilder().AddKartovaResourcePolicies();
    services.AddScoped<IAuthorizationHandler, ApplicationTeamScopedHandler>();

    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.TeamIds.Returns(new HashSet<Guid>());
    services.AddSingleton(currentUser);

    var sp = services.BuildServiceProvider();
    var auth = sp.GetRequiredService<IAuthorizationService>();

    var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.Role, KartovaRoles.OrgAdmin),
    }, "test"));

    // Concrete type that implements ITeamScopedResource — handler must match by interface
    var fakeApp = new FakeAppResource { TeamId = null };
    var result = await auth.AuthorizeAsync(principal, fakeApp, KartovaTeamPolicies.ApplicationTeamScoped);

    Assert.IsTrue(result.Succeeded, "OrgAdmin should succeed; this also proves handler dispatch via interface.");
}

private sealed class FakeAppResource : ITeamScopedResource
{
    public Guid? TeamId { get; init; }
}
```

Run:

```bash
cmd //c "dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj --filter \"FullyQualifiedName~ResourcePolicyIntegration|FullyQualifiedName~TeamScopedHandler|FullyQualifiedName~TeamAdminOfThisHandler\" --nologo"
```

Expected: all PASS. If the integration spike fails, the `AuthorizationHandler<,>` framework rejects interface-typed `TResource` — fallback is to type handlers on `object` and do an `is ITeamScopedResource` check inside, but this is documented to work in modern ASP.NET Core and the spike validates it cheaply.

- [ ] **Step 8: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/KartovaTeamPolicies.cs src/Kartova.SharedKernel.AspNetCore/AuthorizationHandlers/ src/Kartova.SharedKernel.AspNetCore/{AuthorizationExtensions,JwtAuthenticationExtensions}.cs tests/Kartova.SharedKernel.AspNetCore.Tests/{ApplicationTeamScopedHandlerTests,TeamAdminOfThisHandlerTests,ResourcePolicyIntegrationTests}.cs
git commit -m "feat(auth): resource-based handlers for ApplicationTeamScoped + TeamAdminOfThis (slice 8)"
```

---

### Task 14: Team commands + handlers (Create, Update, Delete)

**Files:**
- Create: 6 files in `src/Modules/Organization/Kartova.Organization.Application/`
- Create: 3 DTOs in `src/Modules/Organization/Kartova.Organization.Contracts/`
- Create: `src/Kartova.SharedKernel/Multitenancy/IApplicationCountByTeamReader.cs`

- [ ] **Step 1: Create DTOs (with `[ExcludeFromCodeCoverage]`)**

```csharp
[ExcludeFromCodeCoverage] public sealed record TeamResponse(Guid Id, string DisplayName, string? Description, DateTimeOffset CreatedAt);
[ExcludeFromCodeCoverage] public sealed record CreateTeamRequest(string DisplayName, string? Description);
[ExcludeFromCodeCoverage] public sealed record UpdateTeamRequest(string DisplayName, string? Description);
```

- [ ] **Step 2: Create command records**

```csharp
public sealed record CreateTeamCommand(string DisplayName, string? Description);
public sealed record UpdateTeamCommand(Guid Id, string DisplayName, string? Description);
public sealed record DeleteTeamCommand(Guid Id);
public sealed record DeleteTeamResult(bool Deleted, bool NotFound, int? ApplicationsAssigned);
```

- [ ] **Step 3: Create cross-module reader abstraction**

```csharp
// src/Kartova.SharedKernel/Multitenancy/IApplicationCountByTeamReader.cs
namespace Kartova.SharedKernel.Multitenancy;
public interface IApplicationCountByTeamReader
{
    Task<int> CountForTeamAsync(Guid teamId, CancellationToken ct);
}
```

- [ ] **Step 4: Create handlers (Wolverine direct-dispatch shape per ADR-0093)**

```csharp
public sealed class CreateTeamHandler(TimeProvider clock, ITenantContext tenantCtx)
{
    public async Task<TeamResponse> Handle(CreateTeamCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        var team = Team.Create(cmd.DisplayName, cmd.Description, tenantCtx.Id, clock);
        db.Teams.Add(team);
        await db.SaveChangesAsync(ct);
        return new TeamResponse(team.Id.Value, team.DisplayName, team.Description, team.CreatedAt);
    }
}

public sealed class UpdateTeamHandler
{
    public async Task<TeamResponse?> Handle(UpdateTeamCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => EF.Property<Guid>(t, "_id") == cmd.Id, ct);
        if (team is null) return null;
        team.Rename(cmd.DisplayName, cmd.Description);
        await db.SaveChangesAsync(ct);
        return new TeamResponse(team.Id.Value, team.DisplayName, team.Description, team.CreatedAt);
    }
}

public sealed class DeleteTeamHandler(IApplicationCountByTeamReader appCountReader)
{
    public async Task<DeleteTeamResult> Handle(DeleteTeamCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => EF.Property<Guid>(t, "_id") == cmd.Id, ct);
        if (team is null) return new DeleteTeamResult(false, true, null);

        var appCount = await appCountReader.CountForTeamAsync(team.Id.Value, ct);
        if (appCount > 0) return new DeleteTeamResult(false, false, appCount);

        db.Teams.Remove(team);
        await db.SaveChangesAsync(ct);
        return new DeleteTeamResult(true, false, null);
    }
}
```

- [ ] **Step 5: Build**

```bash
cmd //c "dotnet build src/Modules/Organization/Kartova.Organization.Application/Kartova.Organization.Application.csproj --nologo"
```

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Application/{Create,Update,Delete}Team* src/Modules/Organization/Kartova.Organization.Contracts/{Team,CreateTeam,UpdateTeam}* src/Kartova.SharedKernel/Multitenancy/IApplicationCountByTeamReader.cs
git commit -m "feat(team): commands and handlers for Team CRUD (slice 8)"
```

---

### Task 15: Team query handlers (GetTeam, ListTeams) + ids reader

**Files:**
- Create: query handlers + DTOs (`TeamDetailResponse`, `TeamMemberResponse`)
- Create: `src/Kartova.SharedKernel/Multitenancy/IApplicationIdsByTeamReader.cs`

- [ ] **Step 1: DTOs**

```csharp
[ExcludeFromCodeCoverage]
public sealed record TeamDetailResponse(
    Guid Id, string DisplayName, string? Description, DateTimeOffset CreatedAt,
    IReadOnlyCollection<TeamMemberResponse> Members,
    IReadOnlyCollection<Guid> ApplicationIds);

[ExcludeFromCodeCoverage]
public sealed record TeamMemberResponse(Guid UserId, string Role, DateTimeOffset AddedAt);
```

- [ ] **Step 2: Create `IApplicationIdsByTeamReader`**

```csharp
namespace Kartova.SharedKernel.Multitenancy;
public interface IApplicationIdsByTeamReader
{
    Task<IReadOnlyList<Guid>> GetIdsByTeamAsync(Guid teamId, CancellationToken ct);
}
```

- [ ] **Step 3: Queries + handlers**

```csharp
public sealed record GetTeamQuery(Guid Id);
public sealed record ListTeamsQuery(string? Cursor, int Limit, string SortBy, string SortOrder);

public sealed class GetTeamHandler(IApplicationIdsByTeamReader appIdsReader)
{
    public async Task<TeamDetailResponse?> Handle(GetTeamQuery q, OrganizationDbContext db, CancellationToken ct)
    {
        var team = await db.Teams.FirstOrDefaultAsync(t => EF.Property<Guid>(t, "_id") == q.Id, ct);
        if (team is null) return null;

        var members = await db.TeamMembers
            .Where(m => m.TeamId == new TeamId(q.Id))
            .OrderBy(m => m.AddedAt)
            .Select(m => new TeamMemberResponse(m.UserId, m.Role.ToString(), m.AddedAt))
            .ToListAsync(ct);

        var appIds = await appIdsReader.GetIdsByTeamAsync(q.Id, ct);

        return new TeamDetailResponse(team.Id.Value, team.DisplayName, team.Description, team.CreatedAt, members, appIds);
    }
}
```

`ListTeamsHandler` — cursor-paginated per ADR-0095. Sort by `createdAt | displayName`, default `createdAt desc`. Mirror the existing `ListApplicationsHandler` shape from slice 4 (don't reinvent — copy the pagination boilerplate).

- [ ] **Step 4: Build**

```bash
cmd //c "dotnet build src/Modules/Organization/Kartova.Organization.Application/Kartova.Organization.Application.csproj --nologo"
```

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Application/{GetTeam,ListTeams}* src/Modules/Organization/Kartova.Organization.Contracts/{TeamDetail,TeamMember}* src/Kartova.SharedKernel/Multitenancy/IApplicationIdsByTeamReader.cs
git commit -m "feat(team): query handlers for Get and List (slice 8)"
```

---

### Task 16: Team-member commands + handlers

**Files:**
- Create: 3 commands + 3 handlers + 2 DTOs (`AddTeamMemberRequest`, `UpdateTeamMemberRequest`)

- [ ] **Step 1: DTOs**

```csharp
[ExcludeFromCodeCoverage] public sealed record AddTeamMemberRequest(Guid UserId, string Role);
[ExcludeFromCodeCoverage] public sealed record UpdateTeamMemberRequest(string Role);
```

- [ ] **Step 2: Commands**

```csharp
public sealed record AddTeamMemberCommand(Guid TeamId, Guid UserId, TeamRole Role);
public sealed record RemoveTeamMemberCommand(Guid TeamId, Guid UserId);
public sealed record UpdateTeamMemberCommand(Guid TeamId, Guid UserId, TeamRole NewRole);

public sealed record AddTeamMemberResult(bool Added, bool TeamNotFound, bool AlreadyMember);
public sealed record RemoveTeamMemberResult(bool Removed, bool TeamNotFound, bool MemberNotFound);
public sealed record UpdateTeamMemberResult(bool Updated, bool TeamNotFound, bool MemberNotFound);
```

- [ ] **Step 3: Handlers**

Each handler verifies team exists (404 path if not) then performs the membership operation. Role string-to-enum conversion happens in the endpoint delegate via `Enum.TryParse<TeamRole>` (throw `ArgumentException` on bad input → 400 via existing problem-details handler).

- [ ] **Step 4: Build**

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Application/{AddTeamMember,RemoveTeamMember,UpdateTeamMember}* src/Modules/Organization/Kartova.Organization.Contracts/{AddTeamMember,UpdateTeamMember}*
git commit -m "feat(team): member commands and handlers (slice 8)"
```

---

### Task 17: Team endpoints + delegates

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/{OrganizationModule,OrganizationEndpointDelegates}.cs`

- [ ] **Step 1: Add delegate methods**

`CreateTeamAsync` → returns `201 Created` with `Location: /api/v1/organizations/teams/{id}` + body.

`UpdateTeamAsync` / `DeleteTeamAsync` / member endpoints — each loads the team first (404 if missing), runs `IAuthorizationService.AuthorizeAsync(user, team, KartovaTeamPolicies.TeamAdminOfThis)` for team-admin-gated operations (Update / Delete / member endpoints), and returns 403 on auth failure.

`DeleteTeamAsync` translates `DeleteTeamResult.ApplicationsAssigned > 0` to a 409 `ProblemTypes.TeamHasApplications` with `applicationCount` extension.

`AddTeamMemberAsync` returns `201 Created` with body (consistent with CreateTeam; revised from v1 plan which had 204).

- [ ] **Step 2: Wire endpoints in `OrganizationModule.MapEndpoints`**

```csharp
tenant.MapGet("/teams", OrganizationEndpointDelegates.ListTeamsAsync)
    .RequireAuthorization(KartovaPermissions.TeamRead)
    .WithName("ListTeams")
    .Produces<CursorPage<TeamResponse>>(StatusCodes.Status200OK);

tenant.MapGet("/teams/{id:guid}", OrganizationEndpointDelegates.GetTeamAsync)
    .RequireAuthorization(KartovaPermissions.TeamRead)
    .WithName("GetTeam")
    .Produces<TeamDetailResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status404NotFound);

tenant.MapPost("/teams", OrganizationEndpointDelegates.CreateTeamAsync)
    .RequireAuthorization(KartovaPermissions.TeamCreate)
    .WithName("CreateTeam")
    .Produces<TeamResponse>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status400BadRequest);

tenant.MapPut("/teams/{id:guid}", OrganizationEndpointDelegates.UpdateTeamAsync)
    .RequireAuthorization(KartovaPermissions.TeamMetadataEdit)
    .WithName("UpdateTeam")
    .Produces<TeamResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound);

tenant.MapDelete("/teams/{id:guid}", OrganizationEndpointDelegates.DeleteTeamAsync)
    .RequireAuthorization(KartovaPermissions.TeamDelete)
    .WithName("DeleteTeam")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);

tenant.MapPost("/teams/{id:guid}/members", OrganizationEndpointDelegates.AddTeamMemberAsync)
    .RequireAuthorization(KartovaPermissions.TeamMembersManage)
    .WithName("AddTeamMember")
    .Produces<TeamMemberResponse>(StatusCodes.Status201Created)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status409Conflict);

tenant.MapDelete("/teams/{id:guid}/members/{userId:guid}", OrganizationEndpointDelegates.RemoveTeamMemberAsync)
    .RequireAuthorization(KartovaPermissions.TeamMembersManage)
    .WithName("RemoveTeamMember")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound);

tenant.MapPut("/teams/{id:guid}/members/{userId:guid}", OrganizationEndpointDelegates.UpdateTeamMemberAsync)
    .RequireAuthorization(KartovaPermissions.TeamMembersManage)
    .WithName("UpdateTeamMember")
    .Produces(StatusCodes.Status204NoContent)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound);
```

- [ ] **Step 3: Register handlers in `OrganizationModule.RegisterServices`**

```csharp
services.AddScoped<CreateTeamHandler>();
services.AddScoped<UpdateTeamHandler>();
services.AddScoped<DeleteTeamHandler>();
services.AddScoped<AddTeamMemberHandler>();
services.AddScoped<RemoveTeamMemberHandler>();
services.AddScoped<UpdateTeamMemberHandler>();
services.AddScoped<GetTeamHandler>();
services.AddScoped<ListTeamsHandler>();
```

- [ ] **Step 4: Build full solution**

```bash
cmd //c "dotnet build Kartova.slnx --nologo"
```

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/{OrganizationModule,OrganizationEndpointDelegates}.cs
git commit -m "feat(team): map team CRUD + member endpoints (slice 8)"
```

---

### Task 18: Application.TeamId — migration + EF + domain interface

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs`
- Create: migration `<ts>_AddApplicationTeamId.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/{ApplicationCountByTeamReader,ApplicationIdsByTeamReader}.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs`

- [ ] **Step 1: Add `TeamId` + `AssignTeam` + `ITeamScopedResource` to `Application`**

```csharp
public sealed partial class Application : ITenantOwned, ITeamScopedResource
{
    // existing properties …
    public Guid? TeamId { get; private set; }

    Guid? ITeamScopedResource.TeamId => TeamId;

    public void AssignTeam(Guid? teamId)
    {
        if (Lifecycle == Lifecycle.Decommissioned)
            throw new InvalidLifecycleTransitionException(Lifecycle, nameof(AssignTeam));
        TeamId = teamId;
    }
}
```

(Decommissioned-blocks-assign invariant: add a domain test + integration test for 409 on this case. Document as a deliberate invariant addition not in the original spec — flag for spec amendment post-merge.)

- [ ] **Step 2: Map in `EfApplicationConfiguration`**

```csharp
b.Property(x => x.TeamId).HasColumnName("team_id");
b.HasIndex(x => x.TeamId).HasDatabaseName("idx_catalog_applications_team");
```

- [ ] **Step 3: Generate migration**

```bash
cmd //c "dotnet ef migrations add AddApplicationTeamId --project src/Modules/Catalog/Kartova.Catalog.Infrastructure --startup-project src/Kartova.Migrator --context CatalogDbContext"
```

Verify pure DDL (no toggle dance needed — no backfill):

```bash
cmd //c "type src\\Modules\\Catalog\\Kartova.Catalog.Infrastructure\\Migrations\\*AddApplicationTeamId.cs"
```

- [ ] **Step 4: Implement readers**

```csharp
internal sealed class ApplicationCountByTeamReader(CatalogDbContext db) : IApplicationCountByTeamReader
{
    public Task<int> CountForTeamAsync(Guid teamId, CancellationToken ct)
        => db.Applications.CountAsync(a => a.TeamId == teamId, ct);
}

internal sealed class ApplicationIdsByTeamReader(CatalogDbContext db) : IApplicationIdsByTeamReader
{
    public async Task<IReadOnlyList<Guid>> GetIdsByTeamAsync(Guid teamId, CancellationToken ct)
        => await db.Applications
            .Where(a => a.TeamId == teamId)
            .Select(a => EF.Property<Guid>(a, "_id"))
            .ToListAsync(ct);
}
```

- [ ] **Step 5: Register readers in `CatalogModule.RegisterServices`**

```csharp
services.AddScoped<IApplicationCountByTeamReader, ApplicationCountByTeamReader>();
services.AddScoped<IApplicationIdsByTeamReader, ApplicationIdsByTeamReader>();
```

- [ ] **Step 6: Build**

```bash
cmd //c "dotnet build Kartova.slnx --nologo"
```

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/{EfApplicationConfiguration,ApplicationCountByTeamReader,ApplicationIdsByTeamReader,CatalogModule}.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/*AddApplicationTeamId.cs
git commit -m "feat(catalog): Application.TeamId + readers + migration (slice 8)"
```

---

### Task 19: AssignApplicationTeam endpoint + team-existence checker

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/{AssignApplicationTeamCommand,AssignApplicationTeamHandler}.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/AssignTeamRequest.cs`
- Create: `src/Kartova.SharedKernel/Multitenancy/IOrganizationTeamExistenceChecker.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationTeamExistenceChecker.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/{CatalogModule,CatalogEndpointDelegates}.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationModule.cs`

- [ ] **Step 1: DTO + command**

```csharp
[ExcludeFromCodeCoverage] public sealed record AssignTeamRequest(Guid? TeamId);

public sealed record AssignApplicationTeamCommand(Guid Id, Guid? TeamId);

public sealed record AssignApplicationTeamResult(bool IsSuccess, bool IsNotFound, bool IsInvalidTeam, Application? App)
{
    public static AssignApplicationTeamResult NotFound => new(false, true, false, null);
    public static AssignApplicationTeamResult InvalidTeam => new(false, false, true, null);
    public static AssignApplicationTeamResult Success(Application app) => new(true, false, false, app);
}
```

- [ ] **Step 2: Cross-module checker abstraction + impl**

```csharp
// SharedKernel/Multitenancy/IOrganizationTeamExistenceChecker.cs
namespace Kartova.SharedKernel.Multitenancy;
public interface IOrganizationTeamExistenceChecker
{
    Task<bool> ExistsAsync(Guid teamId, CancellationToken ct);
}

// Organization.Infrastructure/OrganizationTeamExistenceChecker.cs
internal sealed class OrganizationTeamExistenceChecker(OrganizationDbContext db) : IOrganizationTeamExistenceChecker
{
    public Task<bool> ExistsAsync(Guid teamId, CancellationToken ct)
        => db.Teams.AnyAsync(t => EF.Property<Guid>(t, "_id") == teamId, ct);
}
```

Register in `OrganizationModule.RegisterServices`:

```csharp
services.AddScoped<IOrganizationTeamExistenceChecker, OrganizationTeamExistenceChecker>();
```

- [ ] **Step 3: Handler**

```csharp
public sealed class AssignApplicationTeamHandler(IOrganizationTeamExistenceChecker teamChecker)
{
    public async Task<AssignApplicationTeamResult> Handle(
        AssignApplicationTeamCommand cmd, CatalogDbContext db, CancellationToken ct)
    {
        var app = await db.Applications.FirstOrDefaultAsync(a => EF.Property<Guid>(a, "_id") == cmd.Id, ct);
        if (app is null) return AssignApplicationTeamResult.NotFound;

        if (cmd.TeamId.HasValue)
        {
            var exists = await teamChecker.ExistsAsync(cmd.TeamId.Value, ct);
            if (!exists) return AssignApplicationTeamResult.InvalidTeam;
        }

        app.AssignTeam(cmd.TeamId);
        await db.SaveChangesAsync(ct);
        return AssignApplicationTeamResult.Success(app);
    }
}
```

- [ ] **Step 4: Endpoint delegate**

```csharp
internal static async Task<IResult> AssignApplicationTeamAsync(
    Guid id,
    [FromBody] AssignTeamRequest request,
    AssignApplicationTeamHandler handler,
    CatalogDbContext db,
    IAuthorizationService auth,
    ClaimsPrincipal user,
    CancellationToken ct)
{
    var app = await db.Applications.FirstOrDefaultAsync(a => EF.Property<Guid>(a, "_id") == id, ct);
    if (app is null) return EndpointResultExtensions.ApplicationNotFound();

    var authResult = await auth.AuthorizeAsync(user, app, KartovaTeamPolicies.ApplicationTeamScoped);
    if (!authResult.Succeeded) return Results.Forbid();

    var result = await handler.Handle(new AssignApplicationTeamCommand(id, request.TeamId), db, ct);
    if (result.IsInvalidTeam)
        return Results.Problem(
            type: ProblemTypes.InvalidTeam,
            title: "Invalid team",
            detail: "The target team does not exist in the current tenant.",
            statusCode: StatusCodes.Status422UnprocessableEntity);

    return Results.Ok(result.App!.ToResponse());
}
```

- [ ] **Step 5: Map endpoint + register handler**

```csharp
tenant.MapPut("/applications/{id:guid}/team", CatalogEndpointDelegates.AssignApplicationTeamAsync)
    .RequireAuthorization(KartovaPermissions.CatalogApplicationsEditMetadata)
    .WithName("AssignApplicationTeam")
    .Produces<ApplicationResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status403Forbidden)
    .ProducesProblem(StatusCodes.Status404NotFound)
    .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

services.AddScoped<AssignApplicationTeamHandler>();
```

- [ ] **Step 6: Build**

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/AssignApplication* src/Modules/Catalog/Kartova.Catalog.Contracts/AssignTeamRequest.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/{CatalogModule,CatalogEndpointDelegates}.cs src/Kartova.SharedKernel/Multitenancy/IOrganizationTeamExistenceChecker.cs src/Modules/Organization/Kartova.Organization.Infrastructure/{OrganizationTeamExistenceChecker,OrganizationModule}.cs
git commit -m "feat(catalog): PUT /applications/{id}/team + team-existence checker (slice 8)"
```

---

### Task 20: Extend `/me/permissions` with team memberships

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Contracts/MePermissionsResponse.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationEndpointDelegates.cs`

> Doing this BEFORE Task 22 so the API surface is final before SPA codegen.

- [ ] **Step 1: Extend DTO**

```csharp
[ExcludeFromCodeCoverage]
public sealed record MePermissionsResponse(
    string? Role,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<MeTeamMembership> TeamMemberships);

[ExcludeFromCodeCoverage]
public sealed record MeTeamMembership(Guid TeamId, string Role);
```

- [ ] **Step 2: Update endpoint delegate**

Existing `GetMePermissions` likely takes `ClaimsPrincipal user`. Update to take `ICurrentUser currentUser, HttpContext httpContext`:

```csharp
internal static IResult GetMePermissions(ICurrentUser currentUser, HttpContext httpContext)
{
    var role = httpContext.User.FindFirst(ClaimTypes.Role)?.Value;
    var permissions = httpContext.User.Claims
        .Where(c => c.Type == KartovaClaims.Permission)
        .Select(c => c.Value)
        .ToArray();
    var memberships = currentUser.TeamMemberships
        .Select(m => new MeTeamMembership(m.TeamId, m.Role.ToString()))
        .ToArray();
    return Results.Ok(new MePermissionsResponse(role, permissions, memberships));
}
```

- [ ] **Step 3: Build**

```bash
cmd //c "dotnet build Kartova.slnx --nologo"
```

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Contracts/MePermissionsResponse.cs src/Modules/Organization/Kartova.Organization.Infrastructure/OrganizationEndpointDelegates.cs
git commit -m "feat(api): /me/permissions includes team memberships (slice 8)"
```

---

### Task 21: Integration tests — team CRUD + assign-team

**Files:**
- Create: 8 new files under `src/Modules/Organization/Kartova.Organization.IntegrationTests/`
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/AssignApplicationTeamTests.cs`

> Tests are written BEFORE Task 22 (mutation-endpoint auth gate) so they pin behavior before that refactor. Task 23 (existing-test migration) handles the test churn from Task 22.

- [ ] **Step 1: Extend `KartovaApiFixture` with team-seeding helpers**

In `KartovaApiFixture.cs`, add:

```csharp
public async Task<Guid> SeedTeamAsync(Guid tenantId, string displayName, string? description = null)
{
    using var conn = await OpenBypassRlsConnectionAsync();
    var teamId = Guid.NewGuid();
    await conn.ExecuteAsync(
        @"INSERT INTO teams (id, tenant_id, display_name, description, created_at)
          VALUES (@id, @tenantId, @displayName, @description, NOW())",
        new { id = teamId, tenantId, displayName, description });
    return teamId;
}

public async Task SeedTeamMembershipAsync(Guid teamId, Guid userId, TeamRoleKind role)
{
    using var conn = await OpenBypassRlsConnectionAsync();
    await conn.ExecuteAsync(
        @"INSERT INTO team_members (team_id, user_id, role, added_at)
          VALUES (@teamId, @userId, @role, NOW())
          ON CONFLICT (team_id, user_id) DO NOTHING",
        new { teamId, userId, role = (byte)role });
}
```

(`OpenBypassRlsConnectionAsync` is the existing helper that opens a connection without `app.current_tenant_id` set — the role used for tests has BYPASSRLS. If the fixture doesn't expose this exactly, add it.)

- [ ] **Step 2: Write `CreateTeamTests.cs`**

Per spec §10:
- happy (OrgAdmin → 201 + Location + body),
- 403 Member (claim gate),
- 401 anonymous,
- 400 validation (empty displayName, too long).

- [ ] **Step 3: Write remaining 7 team integration test files**

`ListTeamsTests`, `GetTeamTests`, `UpdateTeamTests`, `DeleteTeamTests`, `AddTeamMemberTests`, `RemoveTeamMemberTests`, `UpdateTeamMemberTests`.

Cover:
- `DeleteTeamTests` — happy + 409 team-has-applications (assert `applicationCount`).
- `UpdateTeamTests` — TeamAdmin of THIS team succeeds; TeamAdmin of OTHER team → 403.
- `AddTeamMemberTests` — duplicate `(team_id, user_id)` → 409. Returns 201 with `TeamMemberResponse` body.
- `GetTeamTests` — bootstrap case: fresh tenant lists no teams, OrgAdmin creates one, can GET it.

- [ ] **Step 4: Write `AssignApplicationTeamTests.cs`**

- happy: TeamAdmin in team A reassigns app from null → team A.
- 422 invalid-team: target teamId not in tenant.
- 403: Member not in team A tries to assign app to team A.

- [ ] **Step 5: Run**

```bash
cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --nologo"
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter \"FullyQualifiedName~AssignApplicationTeam\" --nologo"
```

Expected: all PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/KartovaApiFixture.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/ src/Modules/Catalog/Kartova.Catalog.IntegrationTests/AssignApplicationTeamTests.cs
git commit -m "test: integration tests for team CRUD + assign-team (slice 8)"
```

---

### Task 22: Apply team-scope auth to existing Catalog mutation endpoints

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (5 delegate methods)

> **Pattern change:** Existing delegates pass the bare command to the handler, which loads the application itself. To run the resource-auth gate, the delegate must now pre-load. Trade-off: the handler still loads — the second `FirstOrDefaultAsync` hits the change tracker (no extra round-trip) but is wasted CPU. This is acceptable for slice 8; a future refactor can pass the loaded entity to the handler.

- [ ] **Step 1: Refactor each mutation delegate**

For each of: `EditApplicationAsync`, `DeprecateApplicationAsync`, `DecommissionApplicationAsync`, `ReactivateApplicationAsync`, `UnDecommissionApplicationAsync`:

Add `IAuthorizationService auth, ClaimsPrincipal user` to the parameter list. Pre-load + gate before the handler call:

```csharp
var app = await db.Applications.FirstOrDefaultAsync(a => EF.Property<Guid>(a, "_id") == id, ct);
if (app is null) return EndpointResultExtensions.ApplicationNotFound();

var authResult = await auth.AuthorizeAsync(user, app, KartovaTeamPolicies.ApplicationTeamScoped);
if (!authResult.Succeeded) return Results.Forbid();

var result = await handler.Handle(<existing command>, db, ct);
// existing result→IResult translation, unchanged
```

- [ ] **Step 2: Update endpoint mappings**

The `IAuthorizationService` + `ClaimsPrincipal` parameters are bound automatically by ASP.NET Core; no `.MapPut()` changes needed beyond `.ProducesProblem(StatusCodes.Status403Forbidden)`:

```csharp
.ProducesProblem(StatusCodes.Status403Forbidden)
```

Apply to each affected endpoint.

- [ ] **Step 3: Build**

```bash
cmd //c "dotnet build Kartova.slnx --nologo"
```

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/{CatalogEndpointDelegates,CatalogModule}.cs
git commit -m "feat(catalog): team-scope auth gate on mutation endpoints (slice 8)"
```

---

### Task 23: Migrate existing Catalog integration tests for the team-scoped world

**Files:**
- Modify: 6 existing integration test files in `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/`
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs`

> Task 22 introduces a 403 path for non-OrgAdmin actors on unassigned applications. Every existing test that uses a Member or TeamAdmin client to mutate an app — without first joining the actor's team to the app — will now fail.

- [ ] **Step 1: Inventory failing tests**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --nologo --logger \"console;verbosity=normal\""
```

Read failures. Likely culprits per slice-7 baseline: `EditApplicationTests`, `DeprecateApplicationTests`, `DecommissionApplicationTests`, `ReactivateApplicationTests`, `UnDecommissionApplicationTests`, `RegisterApplicationTests` (registration is OK — registers under OrgAdmin or sets `OwnerUserId`), `CatalogPermissionMatrixTests`.

- [ ] **Step 2: Choose migration strategy per failing test**

For each failing test:
- **If the test exercises an OrgAdmin path**: no change needed.
- **If the test exercises a Member/TeamAdmin path**: arrange must create a team, assign the application to it, and add the actor as a team member. Use `Fx.SeedTeamAsync` + `Fx.SeedTeamMembershipAsync` (from Task 21) + direct DB update of `catalog_applications.team_id`.

Example for `DeprecateApplicationTests.Deprecate_succeeds_for_member_in_team`:

```csharp
[TestMethod]
public async Task Deprecate_succeeds_for_team_member()
{
    var teamId = await Fx.SeedTeamAsync(tenantId, "Platform");
    var appId = await Fx.SeedSingleApplicationAsync(tenantId, ownerUserId, teamId);
    await Fx.SeedTeamMembershipAsync(teamId, memberUserId, TeamRoleKind.Member);

    using var client = Fx.CreateClientForOrgA(memberEmail);
    var resp = await client.PostAsync($"/api/v1/catalog/applications/{appId}/deprecate", null);

    Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
}
```

Add `SeedSingleApplicationAsync` to fixture if missing (accept optional `teamId`).

- [ ] **Step 3: Extend `CatalogPermissionMatrixTests`**

Add team-scope cells:
- `Member-in-team-A vs app-in-team-A` → allow.
- `Member-in-team-A vs app-in-team-B` → 403.
- `Member-in-team-A vs unassigned-app` → 403 (Decision #9).
- `OrgAdmin vs unassigned-app` → allow.

- [ ] **Step 4: Run full Catalog integration suite**

```bash
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --nologo"
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/
git commit -m "test: migrate Catalog integration tests for team-scoped world (slice 8)"
```

---

### Task 24: Application.Name retrofit — domain, contracts, EF, sort, arch test

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/RegisterApplicationCommand.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterApplicationHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfApplicationConfiguration.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApplicationSortSpecs.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Contracts/{RegisterApplicationRequest,ApplicationResponse,ApplicationSortField}.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Tests/` (domain unit tests calling `Application.Create(name, ...)`)
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/` (test bodies + `KartovaApiFixture.SeedApplicationsAsync/SeedApplicationsWithLifecycleAsync/DeleteApplicationsByPrefixAsync`)
- Create: migration `<ts>_DropApplicationName.cs`
- Create: `tests/Kartova.ArchitectureTests/ApplicationNameDroppedRules.cs`

- [ ] **Step 1: Domain — drop `Name`**

In `Application.cs`:
- Remove `public string Name { get; private set; }` property.
- Remove `_name` backing field if any.
- Remove `ValidateName` method.
- Remove the `[GeneratedRegex]` `KebabCase` regex source-generated method.
- Update `Application.Create` factory: drop the `name` parameter.

- [ ] **Step 2: Contracts**

```csharp
// RegisterApplicationRequest.cs
[ExcludeFromCodeCoverage]
public sealed record RegisterApplicationRequest(string DisplayName, string Description);

// ApplicationResponse.cs
[ExcludeFromCodeCoverage]
public sealed record ApplicationResponse(
    Guid Id, string DisplayName, string Description,
    Guid OwnerUserId, DateTimeOffset CreatedAt,
    Lifecycle Lifecycle, DateTimeOffset? SunsetDate,
    Guid? TeamId,
    string Version);

// ApplicationSortField.cs — drop Name enum value
public enum ApplicationSortField { CreatedAt = 1, DisplayName = 2, Lifecycle = 3, /* … no Name */ }
```

- [ ] **Step 3: Application + handler**

`RegisterApplicationCommand`: drop `Name` field. `RegisterApplicationHandler.Handle`: drop `cmd.Name` from `Application.Create` call.

`CatalogEndpointDelegates.RegisterApplicationAsync`: drop `request.Name` — pass only `request.DisplayName, request.Description`.

- [ ] **Step 4: EF + sort**

`EfApplicationConfiguration.cs`: remove `b.Property(x => x.Name).HasColumnName("name").HasMaxLength(256).IsRequired();` and the unique index `(tenant_id, name)` if it exists.

`ApplicationSortSpecs.cs`: remove the `Name` `SortSpec<Application>` entry. Update `AllowedFieldNames` to drop `"name"`.

- [ ] **Step 5: Migration**

```bash
cmd //c "dotnet ef migrations add DropApplicationName --project src/Modules/Catalog/Kartova.Catalog.Infrastructure --startup-project src/Kartova.Migrator --context CatalogDbContext"
```

Verify pure DDL (`ALTER TABLE catalog_applications DROP COLUMN name` + index drop if exists). If a unique index `(tenant_id, name)` exists, it's auto-dropped with the column — confirm in generated SQL.

- [ ] **Step 6: Architecture test**

Create `tests/Kartova.ArchitectureTests/ApplicationNameDroppedRules.cs`:

```csharp
using System.Reflection;
using Kartova.Catalog.Domain;

namespace Kartova.ArchitectureTests;

[TestClass]
public sealed class ApplicationNameDroppedRules
{
    [TestMethod]
    public void Application_does_not_expose_a_Name_property()
    {
        var prop = typeof(Application).GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
        Assert.IsNull(prop, "Application.Name was dropped per ADR-0098; do not reintroduce.");
    }

    [TestMethod]
    public void ApplicationResponse_does_not_expose_a_Name_property()
    {
        var prop = typeof(Kartova.Catalog.Contracts.ApplicationResponse).GetProperty("Name");
        Assert.IsNull(prop);
    }

    [TestMethod]
    public void ApplicationSortField_does_not_define_a_Name_value()
    {
        Assert.IsFalse(
            Enum.GetNames(typeof(Kartova.Catalog.Contracts.ApplicationSortField)).Contains("Name"),
            "ApplicationSortField.Name was dropped per ADR-0098.");
    }
}
```

- [ ] **Step 7: Domain unit tests**

Search `src/Modules/Catalog/Kartova.Catalog.Tests/` for `Application.Create(`. Remove the first (name) argument from each call.

```bash
cmd //c "findstr /s /n /r \"Application.Create(\" src\\Modules\\Catalog\\Kartova.Catalog.Tests"
```

- [ ] **Step 8: Integration test fixture + bodies**

Update `KartovaApiFixture.cs`:
- `SeedApplicationsAsync(...)` (lines ~65-83 in current code) — drop `name` from the `Application.Create` direct call and from the INSERT statement if present.
- `SeedApplicationsWithLifecycleAsync(...)` (lines ~108-153) — same.
- `DeleteApplicationsByPrefixAsync(...)` (lines ~161-171) — currently filters `WHERE name LIKE @prefix`. Rewrite to filter by `display_name` (or by tenant_id + delete-all-since-test-id range).

Update every test file that constructs `new RegisterApplicationRequest(name, displayName, description)`:

```bash
cmd //c "findstr /s /n /r \"new RegisterApplicationRequest(\" src\\Modules\\Catalog\\Kartova.Catalog.IntegrationTests"
```

For each match, drop the first positional argument. Audit each test for assertions on `body.Name` — remove.

- [ ] **Step 9: Build + run all tests**

```bash
cmd //c "dotnet build Kartova.slnx --nologo"
cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --nologo"
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --nologo"
cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --nologo"
```

Expected: all green.

- [ ] **Step 10: Commit**

```bash
git add -A src/Modules/Catalog/ tests/Kartova.ArchitectureTests/ApplicationNameDroppedRules.cs
git commit -m "refactor(catalog): drop Application.Name per ADR-0098 (slice 8)"
```

---

### Task 25: SPA codegen regeneration

**Files:**
- Modify: `web/openapi-snapshot.json` (regenerated)
- Modify: `web/src/generated/openapi.ts` (regenerated)

> Done BEFORE the SPA permissions/hook work so the new `MePermissionsResponse.teamMemberships` shape is in the generated types when downstream code consumes it.

- [ ] **Step 1: Rebuild API + regenerate**

```bash
cmd //c "docker compose down -v"
cmd //c "docker compose up --build -d"
cd web && pnpm codegen
```

- [ ] **Step 2: Smoke-test new endpoints**

After containers are healthy, run a quick HTTP smoke (use `curl` with a test JWT or a small Vitest hitting the live API). Validate at minimum:

- `GET /api/v1/organizations/teams` returns 200 + `CursorPage<TeamResponse>` shape.
- `POST /api/v1/organizations/teams` (OrgAdmin) returns 201 + `Location` header.
- `GET /api/v1/organizations/me/permissions` returns the new `teamMemberships` array.

Capture output for the slice's verification evidence.

- [ ] **Step 3: Commit**

```bash
git add web/openapi-snapshot.json web/src/generated/openapi.ts
git commit -m "chore(web): regenerate openapi types for slice 8"
```

---

### Task 26: SPA permission constants + drift snapshot

**Files:**
- Modify: `web/src/shared/auth/permissions.ts`
- Modify: `web/src/shared/auth/permissions.snapshot.json`

- [ ] **Step 1: Add 5 new TS constants**

```ts
TeamRead:          "team.read",
TeamCreate:        "team.create",
TeamMetadataEdit:  "team.metadata.edit",
TeamDelete:        "team.delete",
TeamMembersManage: "team.members.manage",
```

- [ ] **Step 2: Update `permissions.snapshot.json`**

Check the existing file's shape (likely an array of strings sorted alphabetically). Add the 5 strings; preserve sort.

If the snapshot is *generated* by a script (look for `pnpm run snapshot-perms` or similar), run that instead of editing by hand:

```bash
cd web && pnpm run --silent | grep -i snapshot
```

If no such script exists, hand-edit is correct.

- [ ] **Step 3: Run typecheck + drift arch test**

```bash
cd web && pnpm tsc -b --noEmit
cd ..
cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter \"FullyQualifiedName~Ts_snapshot\" --nologo"
```

Expected: clean.

- [ ] **Step 4: Commit**

```bash
git add web/src/shared/auth/permissions.ts web/src/shared/auth/permissions.snapshot.json
git commit -m "feat(web): add team permission constants + snapshot (slice 8)"
```

---

### Task 27: SPA `usePermissions` hook extension + test audit

**Files:**
- Modify: `web/src/shared/auth/usePermissions.ts`
- Modify: `web/src/shared/auth/__tests__/usePermissions.test.tsx`

- [ ] **Step 1: Extend `UsePermissionsResult` + hook body**

```ts
export interface UsePermissionsResult {
  role: string | null;
  hasPermission: (perm: KartovaPermission) => boolean;
  isLoading: boolean;
  isError: boolean;
  teamIds: string[];
  teamAdminTeamIds: string[];
}

// in hook body:
const teamMemberships = data?.teamMemberships ?? [];
const teamIds = teamMemberships.map(m => m.teamId);
const teamAdminTeamIds = teamMemberships
  .filter(m => m.role === "Admin")
  .map(m => m.teamId);

return { role, hasPermission, isLoading, isError, teamIds, teamAdminTeamIds };
```

- [ ] **Step 2: Audit existing test mocks**

Search:

```bash
cmd //c "findstr /s /n \"MePermissionsResponse\" web\\src\\shared\\auth\\__tests__"
```

Every existing mock of `MePermissionsResponse` shape (`{ role, permissions }`) must add `teamMemberships: []` to remain type-valid (assuming codegen marks the new field as non-optional).

- [ ] **Step 3: Add new tests for team-membership derivation**

```ts
it("returns teamIds and teamAdminTeamIds from /me/permissions", async () => {
  mockMePermissions({
    role: "TeamAdmin",
    permissions: ["catalog.read", "team.read"],
    teamMemberships: [
      { teamId: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", role: "Admin" },
      { teamId: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", role: "Member" },
    ],
  });
  const { result } = renderHook(() => usePermissions(), { wrapper });
  await waitFor(() => expect(result.current.isLoading).toBe(false));
  expect(result.current.teamIds).toEqual([
    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
    "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
  ]);
  expect(result.current.teamAdminTeamIds).toEqual(["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"]);
});
```

- [ ] **Step 4: Run Vitest**

```bash
cd web && pnpm vitest run src/shared/auth/__tests__/usePermissions.test.tsx
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/shared/auth/usePermissions.ts web/src/shared/auth/__tests__/usePermissions.test.tsx
git commit -m "feat(web): usePermissions exposes teamIds + teamAdminTeamIds (slice 8)"
```

---

### Task 28: SPA teams API hooks

**Files:**
- Create: `web/src/features/teams/api/teams.ts`

- [ ] **Step 1: Create `teams.ts`**

```ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/features/catalog/api/client";

export const teamKeys = {
  all: ["teams"] as const,
  list: () => [...teamKeys.all, "list"] as const,
  detail: (id: string) => [...teamKeys.all, "detail", id] as const,
};

export function useTeamsList() { /* GET /api/v1/organizations/teams */ }
export function useTeam(id: string) { /* GET /api/v1/organizations/teams/{id} */ }
export function useCreateTeam() { /* POST */ }
export function useUpdateTeam(id: string) { /* PUT */ }
export function useDeleteTeam(id: string) { /* DELETE */ }
export function useAddTeamMember(teamId: string) { /* POST /members */ }
export function useRemoveTeamMember(teamId: string) { /* DELETE /members/{userId} */ }
export function useChangeTeamMemberRole(teamId: string, userId: string) { /* PUT /members/{userId} */ }
```

(Full body — mirror slice-7's `applications.ts` exactly: `apiClient.GET/POST/PUT/DELETE`, throw on `error`, invalidate `teamKeys.all` on mutation success.)

- [ ] **Step 2: Run typecheck**

```bash
cd web && pnpm tsc -b --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add web/src/features/teams/api/teams.ts
git commit -m "feat(web): teams API hooks (slice 8)"
```

---

### Task 29: SPA teams list + detail pages + routes

**Files:**
- Create: `web/src/features/teams/pages/{TeamsListPage,TeamDetailPage}.tsx`
- Modify: `web/src/app/router.tsx`
- Modify: `web/src/components/layout/Sidebar.tsx`

- [ ] **Step 1: `TeamsListPage`**

Uses `useTeamsList`. Wires `useCursorList` + `useListUrlState` + `<DataTable>` per CLAUDE.md "every new list screen" requirement (ADR-0095). Empty state for fresh tenants. "Create team" button gated by `usePermissions().hasPermission(KartovaPermissions.TeamCreate)`. Row links to `/teams/{id}`.

- [ ] **Step 2: `TeamDetailPage`**

Reads `:id` from URL. Uses `useTeam(id)`. Sections:
- Header — display name, description, Rename/Delete buttons gated by `usePermissions().teamAdminTeamIds.includes(id) || role === "OrgAdmin"`.
- Members table — `userId | role | remove`.
- Applications list — assigned application IDs as links to `/catalog/applications/{id}`.

- [ ] **Step 3: Add routes**

```tsx
<Route path="/teams" element={<TeamsListPage />} />
<Route path="/teams/:id" element={<TeamDetailPage />} />
```

(Place inside whatever shell wrapper slice-7 uses for `/catalog/...` — match the existing pattern.)

- [ ] **Step 4: Sidebar entry**

Add "Teams" nav link gated by `usePermissions().hasPermission(KartovaPermissions.TeamRead)`.

- [ ] **Step 5: Run typecheck**

```bash
cd web && pnpm tsc -b --noEmit
```

- [ ] **Step 6: Commit**

```bash
git add web/src/features/teams/pages/ web/src/app/router.tsx web/src/components/layout/Sidebar.tsx
git commit -m "feat(web): /teams list + detail pages (slice 8)"
```

---

### Task 30: SPA team dialogs

**Files:**
- Create: 6 dialog components + 3 zod schemas

- [ ] **Step 1: zod schemas**

```ts
// createTeam.ts
export const createTeamSchema = z.object({
  displayName: z.string().min(1, "Required").max(128),
  description: z.string().max(512).optional(),
});

// updateTeam.ts — same shape

// addTeamMember.ts
export const addTeamMemberSchema = z.object({
  userId: z.string().uuid("Invalid user ID"),
  role: z.enum(["Admin", "Member"]),
});
```

- [ ] **Step 2: Dialog components**

Use the existing dialog wrapper pattern from slice 7 (`@/components/application/modals/modal` — `ModalOverlay + Modal + Dialog` from react-aria-components). Mirror `EditApplicationDialog.tsx` structure for form + zod resolver.

`DeleteTeamConfirmDialog` handles 409:

```tsx
const handleDelete = async () => {
  try {
    await deleteTeam.mutateAsync();
    onClose();
  } catch (err: any) {
    if (err.status === 409) {
      toast.error(`Can't delete: ${err.applicationCount} applications still assigned. Reassign them first.`);
    }
  }
};
```

- [ ] **Step 3: Wire into pages**

`TeamsListPage`: "Create team" button → `CreateTeamDialog`. `TeamDetailPage`: Rename/Delete buttons + Add-member + per-row Remove/Change-role buttons → respective dialogs.

- [ ] **Step 4: Run typecheck**

```bash
cd web && pnpm tsc -b --noEmit
```

- [ ] **Step 5: Commit**

```bash
git add web/src/features/teams/components/ web/src/features/teams/schemas/
git commit -m "feat(web): team dialogs (slice 8)"
```

---

### Task 31: SPA AssignTeamPicker + ApplicationDetailPage integration

**Files:**
- Create: `web/src/features/teams/components/AssignTeamPicker.tsx`
- Modify: `web/src/features/catalog/api/applications.ts`
- Modify: `web/src/features/catalog/pages/ApplicationDetailPage.tsx`

- [ ] **Step 1: `useAssignApplicationTeam` hook**

```ts
export function useAssignApplicationTeam(id: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (teamId: string | null) => {
      const { data, error } = await apiClient.PUT(
        "/api/v1/catalog/applications/{id}/team",
        { params: { path: { id } }, body: { teamId } });
      if (error) throw error;
      return data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ["applications"] }),
  });
}
```

- [ ] **Step 2: `AssignTeamPicker.tsx`**

Shows current team display name (or "Unassigned") + dropdown of available teams.
- For OrgAdmin: lists all teams in tenant (use `useTeamsList`).
- For Member/TeamAdmin: lists only their teams + "Unassigned" option.

Selecting a team calls `useAssignApplicationTeam`.

- [ ] **Step 3: Wire into `ApplicationDetailPage`**

Add `<AssignTeamPicker applicationId={app.id} currentTeamId={app.teamId} />` to the header next to the lifecycle menu.

- [ ] **Step 4: Run typecheck**

- [ ] **Step 5: Commit**

```bash
git add web/src/features/teams/components/AssignTeamPicker.tsx web/src/features/catalog/api/applications.ts web/src/features/catalog/pages/ApplicationDetailPage.tsx
git commit -m "feat(web): AssignTeamPicker on Application detail (slice 8)"
```

---

### Task 32: SPA Application.Name removal

**Files:**
- Modify: `web/src/features/catalog/{components/RegisterApplicationDialog,components/ApplicationsTable,pages/ApplicationDetailPage,pages/CatalogListPage,schemas/registerApplication,api/applications}.{ts,tsx}`

- [ ] **Step 1: zod schema**

Drop the `name: z.string().regex(/^[a-z0-9-]+$/)` field from `registerApplication.ts`.

- [ ] **Step 2: Register dialog**

Drop the kebab-name input. Submit `{ displayName, description }` only.

- [ ] **Step 3: Drop kebab badges**

`ApplicationsTable.tsx` + `ApplicationDetailPage.tsx`: remove the `<Badge>` rendering `application.name`.

- [ ] **Step 4: Drop sort-by-name**

If `CatalogListPage.tsx` exposes a sort dropdown with "Name", remove that option.

- [ ] **Step 5: Update Vitest fixtures**

```bash
cmd //c "findstr /s /n \"name:\" web\\src\\features\\catalog\\__tests__"
```

Remove `name: "..."` from any mocked `ApplicationResponse` objects.

- [ ] **Step 6: Run typecheck + Vitest**

```bash
cd web && pnpm tsc -b --noEmit
cd web && pnpm vitest run
```

Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add web/src/features/catalog/
git commit -m "refactor(web): drop Application.Name from SPA per ADR-0098 (slice 8)"
```

---

### Task 33: SPA Vitest tests for teams

**Files:**
- Create: 6 test files under `web/src/features/teams/`

- [ ] **Step 1: `teams.test.tsx`** — hook tests. Mirror slice-7 `applications.test.tsx` (mock `apiClient`, cover useTeamsList / useTeam / useCreateTeam / useDeleteTeam — especially the 409 path).

- [ ] **Step 2: Page tests** — `TeamsListPage.test.tsx` + `TeamDetailPage.test.tsx`. Cover: renders teams + "Create" button gated; detail shows header/members/apps; Rename/Delete buttons visible when teamAdminTeamIds includes the id.

- [ ] **Step 3: Dialog tests** — `CreateTeamDialog.test.tsx` (zod validation + submit payload); `DeleteTeamConfirmDialog.test.tsx` (409 toast with applicationCount); `AssignTeamPicker.test.tsx` (Member lists only their teams; OrgAdmin lists all).

- [ ] **Step 4: Run Vitest**

```bash
cd web && pnpm vitest run
```

Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/teams/
git commit -m "test(web): Vitest for team feature (slice 8)"
```

---

### Task 34: CHECKLIST.md update

**Files:**
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Read current state**

```bash
cmd //c "type docs\\product\\CHECKLIST.md | findstr /n \"E-03.F-02\""
```

- [ ] **Step 2: Tick stories**

```
- [x] E-03.F-02.S-01 — Create and manage team profile (slice 8 — PR #XX, 2026-05-25; teams table + DisplayName/Description; OrgAdmin creates, TeamAdmin renames own team)
- [x] E-03.F-02.S-02 — Assign components to team (slice 8 — PR #XX, 2026-05-25; PUT /applications/{id}/team; team-scoped Catalog mutations)
- [~] E-03.F-02.S-03 — Team page with components and scorecard (slice 8 — PR #XX, 2026-05-25; team detail page with members + assigned application IDs; scorecard deferred to E-10)
```

Recompute the Phase 1 progress counts from the read-state output.

- [ ] **Step 3: Commit**

```bash
git add docs/product/CHECKLIST.md
git commit -m "docs(checklist): tick E-03.F-02 stories (slice 8)"
```

---

## Self-review

Spec coverage check:

| Spec section | Tasks |
|---|---|
| §3 Decision #1 (Team in Organization module) | T7, T8, T9, T11, T17 |
| §3 Decision #2 (Team aggregate shape) | T7 |
| §3 Decision #3 (TeamMembership join + multi-team) | T8, T9, T10 |
| §3 Decision #4 (Application.TeamId nullable Guid) | T18 |
| §3 Decision #5 (OwnerUserId retained) | T24 (retrofit retains it) |
| §3 Decision #6 (Resource-based auth) | T13, T22 |
| §3 Decision #7 (5 new permissions) | T2 |
| §3 Decision #8 (ITeamMembershipReader + populate) | T3, T11, T12 |
| §3 Decision #9 (Team-scope predicate) | T13 |
| §3 Decision #10 (Endpoints) | T17, T19 |
| §3 Decision #11 (Domain types in Organization.Domain) | T7, T8 |
| §3 Decision #12 (Delete blocked when apps assigned) | T14, T17 |
| §3 Decision #13 (Bootstrap path) | T21 Step 3 (bootstrap case in `GetTeamTests`) |
| §3 Decision #14 (SPA hide-by-default) | T27, T29, T30 |
| §3 Decision #15 (Single PR sequenced commits) | Plan-wide |
| §3 Decision #16 (ADR-0098) | T1 |
| §3 Decision #17 (Drop Application.Name) | T24, T32 |
| §4.1 Domain types | T7, T8 |
| §4.2 Tables | T9, T10 |
| §4.3 Application.TeamId | T18 |
| §4.4 RLS policies | T10 |
| §4.5 Migrations sequence | T10, T18, T24 |
| §4.6 EF configurations | T9, T18 |
| §4.7 DbContexts | T9 |
| §5 Authorization plumbing | T2, T3, T4, T5, T6, T12, T13 |
| §6 Endpoints | T17, T19, T20, T22 |
| §7 SPA | T25, T26, T27, T28, T29, T30, T31 |
| §8 Application.Name retrofit | T24, T32 |
| §9 ADR-0098 text | T1 |
| §10 Tests inventory | T13, T21, T23, T24 (arch test), T33 |
| §11 DoD | Implementation tasks cover steps 1, 4 (full suite green via T21/T23/T24). Steps 2/3/5/6/7/8/9 (per-task + slice-boundary reviews, docker smoke beyond T25, /simplify, mutation loop, /pr-review-toolkit, /deep-review) are post-implementation gates — invoked outside this plan. |

Placeholder scan: no "TBD", "implement later", or "similar to Task N". Task 34's "Recompute progress counts" is a directive to the engineer at execution time, not a placeholder.

Type consistency: `TeamRoleKind` (SharedKernel) vs `TeamRole` (Organization.Domain) — both deliberate; verify usages match in each task. `ApplicationTeamScopedHandler` / `TeamAdminOfThisHandler` / `*Requirement` names consistent across T13 and T22. `KartovaTeamPolicies.ApplicationTeamScoped` / `.TeamAdminOfThis` consistent. `IApplicationCountByTeamReader` / `IApplicationIdsByTeamReader` / `IOrganizationTeamExistenceChecker` / `ITeamMembershipReader` placed consistently in `Kartova.SharedKernel/Multitenancy/`.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-25-slice-8-team-management-and-team-scoped-permissions-plan.md`. 34 tasks total.

Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, two-stage review between tasks (spec compliance → code quality), fast iteration.

**2. Inline Execution** — execute in this session using `superpowers:executing-plans`, batch execution with checkpoints for review.

Which approach?
