# Team-Admin Authority via Per-Team Membership — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the `TeamAdmin` realm role and its three `team.*` mutation permission claims; make team-admin authority conferred solely by an `Admin`-level per-team membership via the existing `TeamAdminOfThis` resource gate.

**Architecture:** The 5 team-mutation routes drop their permission-claim gate (falling back to authenticated + tenant scope per ADR-0090); the inline `TeamAdminOfThis` resource gate (unchanged, keyed on `TeamRoleKind.Admin` membership) becomes the sole authorization. Realm roles collapse to `Viewer`/`Member`/`OrgAdmin`. Team creation stays OrgAdmin-only.

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs, MSTest v4 + NSubstitute, Testcontainers (PostgreSQL + KeyCloak), React + TypeScript + Vitest, KeyCloak realm JSON.

**Spec:** `docs/superpowers/specs/2026-06-09-team-admin-membership-authority-design.md`
**ADR:** ADR-0101 (already committed).

## Prerequisites

- **Docker must be running** for Tasks 1, 2, and 3 (Organization + Catalog integration suites use Testcontainers; Task 5 uses `docker compose`). Confirm with `docker ps` before starting those tasks.
- Branch `feat/team-admin-membership-authority` is already checked out (ADR-0101 + spec committed at `bb61e8e`).
- Windows shell: use `cmd /c "dotnet ..."` or PowerShell wrappers for `dotnet` (per CLAUDE.md).

## File Structure

| File | Change | Task |
|------|--------|------|
| `src/Modules/Organization/Kartova.Organization.Infrastructure/TeamEndpointDelegates.cs` | Drop claim gate on 5 mutation routes | 1 |
| `src/Modules/Organization/Kartova.Organization.IntegrationTests/{UpdateTeamTests,UpdateTeamMemberTests,RemoveTeamMemberTests,AddTeamMemberTests}.cs` | Realm-role `TeamAdmin`→`Member` (red→green proof) | 1 |
| `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` | Remove 3 constants + from `All` | 2 |
| `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` | Remove `[TeamAdmin]` entry; remove 3 from `OrgAdmin` | 2 |
| `src/Kartova.SharedKernel/Multitenancy/KartovaRoles.cs` | Remove `TeamAdmin` const + from `All` | 2 |
| `web/src/shared/auth/permissions.ts` + `permissions.snapshot.json` | Remove 3 keys (matched pair with C# `All`) | 2 |
| `web/src/shared/auth/__tests__/usePermissions.test.tsx` | Drop refs to removed perms if any | 2 |
| `src/Modules/.../CreateInvitationRequest.cs` + `InvitationEndpointDelegates.cs` | XML doc + detail string drop `TeamAdmin` | 2 |
| `tests/.../KartovaRolePermissionsTests.cs`, `OrganizationPermissionMatrixTests.cs`, `KartovaPermissionsRules.cs`, `TeamAdminOfThisHandlerTests.cs`, `TenantClaimsTransformationTests.cs`, `GetMePermissionsTests.cs`, `CatalogPermissionMatrixTests.cs` | Update/remove `TeamAdmin` expectations | 2 |
| `deploy/keycloak/kartova-realm.json` | Remove `TeamAdmin` role; reassign dev user to `Member` | 3 |
| `src/Kartova.Migrator/DevSeed.cs` | Seed demo team + `Admin` membership for the dev user | 3 |
| `tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs` | Update realm-role assertion | 3 |
| `web/src/features/organization/schemas/inviteUser.ts` | Drop `TeamAdmin` from role enum | 4 |

`TeamAdminOfThisHandler.cs` and `ApplicationTeamScopedHandler` are **not** modified — they are already membership-based.

---

## Task 1: Drop the route claim gate + prove the fix (red→green)

**Why first:** This is the only genuine new behavior — a realm-`Member` who is `Admin` of a team can now manage it. We rewrite the existing integration tests to issue `Member` tokens (RED against the current claim gate → 403), then remove the claim gate (GREEN). The `KartovaRoles.TeamAdmin` constant still exists at this point, so everything compiles; the symbol is removed in Task 2.

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/TeamEndpointDelegates.cs` (route registrations in `TeamRoutes.MapTo`)
- Test: `src/Modules/Organization/Kartova.Organization.IntegrationTests/UpdateTeamTests.cs`
- Test: `src/Modules/Organization/Kartova.Organization.IntegrationTests/UpdateTeamMemberTests.cs`
- Test: `src/Modules/Organization/Kartova.Organization.IntegrationTests/RemoveTeamMemberTests.cs`
- Test: `src/Modules/Organization/Kartova.Organization.IntegrationTests/AddTeamMemberTests.cs`

- [ ] **Step 1: Rewrite the UpdateTeamTests success/scope/cross-tenant cases to use the `Member` realm role**

In `UpdateTeamTests.cs`, make these three edits (the `subject:`-bearing token is what populates `TeamMemberships`; the realm role no longer needs to be `TeamAdmin`):

`TeamAdmin_of_this_team_updates_returns_200` → rename to `Member_who_is_team_admin_updates_returns_200`; change the token line:

```csharp
// before
var token = Fx.Signer.IssueForTenant(
    Tenant,
    new[] { KartovaRoles.TeamAdmin },
    subject: userId.ToString());
// after
var token = Fx.Signer.IssueForTenant(
    Tenant,
    new[] { KartovaRoles.Member },         // realm-Member who holds an Admin membership on this team
    subject: userId.ToString());
```

`TeamAdmin_of_other_team_returns_403` → rename to `Member_admin_of_other_team_returns_403`; same `KartovaRoles.TeamAdmin` → `KartovaRoles.Member` swap on its token line.

`Cross_tenant_TeamAdmin_returns_404` → rename to `Cross_tenant_member_returns_404`; swap `new[] { KartovaRoles.TeamAdmin }` → `new[] { KartovaRoles.Member }`. Update the XML comment: the 404 (RLS-hidden team) now fires for any caller because `LoadAndAuthorizeTeamAsync` reads the team first (404 if not visible under the tenant) before the resource gate runs — there is no longer a claim gate in front.

In `Plain_Member_of_team_returns_403`, update the comment block to: `// The resource gate (TeamAdminOfThis) denies: this user's membership role is Member, not Admin. (Previously the claim gate blocked Member before the resource gate; now the resource gate is the sole check — same 403 result.)`

- [ ] **Step 2: Apply the same `TeamAdmin`→`Member` swap to the member-management success tests**

In each of `UpdateTeamMemberTests.cs`, `RemoveTeamMemberTests.cs`, `AddTeamMemberTests.cs`, find the single test that mints a token with `new[] { KartovaRoles.TeamAdmin }` and a `subject:` matching a seeded `Admin` membership (the "team admin of this team succeeds" case). Change `KartovaRoles.TeamAdmin` → `KartovaRoles.Member` and rename the method's `TeamAdmin` prefix to `Member_who_is_team_admin`. Leave all other tests (OrgAdmin, invalid-role-string, not-a-member 403) unchanged.

- [ ] **Step 3: Run the Organization integration suite — expect RED**

Run: `cmd /c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --filter FullyQualifiedName~UpdateTeamTests"`
Expected: FAIL — `Member_who_is_team_admin_updates_returns_200` asserts 200 but gets **403**, because the route still requires the `team.metadata.edit` claim which the `Member` role does not carry.

- [ ] **Step 4: Drop the claim gate on the 5 team-mutation routes**

In `TeamEndpointDelegates.cs`, `TeamRoutes.MapTo`, change `RequireAuthorization(<permission>)` to a bare `RequireAuthorization()` on the five mutation endpoints. Keep the reads (`team.read`) and create (`team.create`) exactly as they are.

```csharp
// PUT /teams/{id}
tenant.MapPut("/teams/{id:guid}", TeamEndpointDelegates.UpdateTeamAsync)
    .RequireAuthorization()                                   // was: .RequireAuthorization(KartovaPermissions.TeamMetadataEdit)
    .WithName("UpdateTeam")
    // ... Produces(...) unchanged

// DELETE /teams/{id}
tenant.MapDelete("/teams/{id:guid}", TeamEndpointDelegates.DeleteTeamAsync)
    .RequireAuthorization()                                   // was: KartovaPermissions.TeamDelete
    .WithName("DeleteTeam")
    // ...

// POST /teams/{id}/members
tenant.MapPost("/teams/{id:guid}/members", TeamEndpointDelegates.AddTeamMemberAsync)
    .RequireAuthorization()                                   // was: KartovaPermissions.TeamMembersManage
    .WithName("AddTeamMember")
    // ...

// DELETE /teams/{id}/members/{userId}
tenant.MapDelete("/teams/{id:guid}/members/{userId:guid}", TeamEndpointDelegates.RemoveTeamMemberAsync)
    .RequireAuthorization()                                   // was: KartovaPermissions.TeamMembersManage
    .WithName("RemoveTeamMember")
    // ...

// PUT /teams/{id}/members/{userId}
tenant.MapPut("/teams/{id:guid}/members/{userId:guid}", TeamEndpointDelegates.UpdateTeamMemberAsync)
    .RequireAuthorization()                                   // was: KartovaPermissions.TeamMembersManage
    .WithName("UpdateTeamMember")
    // ...
```

Add a short comment above the first mutation route: `// Mutation routes authorize via the inline TeamAdminOfThis resource gate (LoadAndAuthorizeTeamAsync), not a claim policy — team-admin authority is per-team Admin membership (ADR-0101). RequireAuthorization() keeps the authenticated+tenant baseline so anonymous callers still get 401.`

- [ ] **Step 5: Run the Organization integration suite — expect GREEN**

Run: `cmd /c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --filter FullyQualifiedName~Team"`
Expected: PASS — the rewritten `Member_who_is_team_admin_*` tests now return 200/201/204; the `403`/`404` negative cases still hold (resource gate denies non-admins; RLS hides cross-tenant teams).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/TeamEndpointDelegates.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/UpdateTeamTests.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/UpdateTeamMemberTests.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/RemoveTeamMemberTests.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/AddTeamMemberTests.cs
git commit -m "feat(team-admin): drop claim gate on team mutations; resource gate is sole authorization"
```

---

## Task 2: Remove the `TeamAdmin` role + three `team.*` permissions (C# + SPA, matched pair)

**Why atomic:** Two drift sentinels force this into one commit — `KartovaPermissionsRules.Ts_snapshot_equals_csharp_KartovaPermissions_All` (C# `All` ↔ `permissions.snapshot.json`) and the runtime guard in `permissions.ts` (TS object ↔ snapshot). Removing the `KartovaRoles.TeamAdmin` symbol also breaks compilation in every consuming test, so all consumers update together.

**Files:**
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs`
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs`
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaRoles.cs`
- Modify: `web/src/shared/auth/permissions.ts`
- Modify: `web/src/shared/auth/permissions.snapshot.json`
- Modify: `web/src/shared/auth/__tests__/usePermissions.test.tsx`
- Modify: `src/Modules/Organization/Kartova.Organization.Contracts/CreateInvitationRequest.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/InvitationEndpointDelegates.cs`
- Test: `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs`
- Test: `tests/Kartova.ArchitectureTests/OrganizationPermissionMatrixTests.cs`
- Test: `tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs`
- Test: `tests/Kartova.SharedKernel.AspNetCore.Tests/TeamAdminOfThisHandlerTests.cs`
- Test: `tests/Kartova.SharedKernel.AspNetCore.Tests/TenantClaimsTransformationTests.cs`
- Test: `src/Modules/Organization/Kartova.Organization.IntegrationTests/GetMePermissionsTests.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs`

- [ ] **Step 1: Remove the three permission constants**

In `KartovaPermissions.cs`, delete these three lines from the constant block and from the `All` initializer:

```csharp
public const string TeamMetadataEdit  = "team.metadata.edit";
public const string TeamDelete        = "team.delete";
public const string TeamMembersManage = "team.members.manage";
```

`TeamRead` and `TeamCreate` stay.

- [ ] **Step 2: Remove the `TeamAdmin` map entry and strip the 3 perms from OrgAdmin**

In `KartovaRolePermissions.cs`: delete the entire `[KartovaRoles.TeamAdmin] = new[] { ... }.ToFrozenSet(...)` entry. In the `[KartovaRoles.OrgAdmin]` entry, delete the three lines `KartovaPermissions.TeamMetadataEdit,`, `KartovaPermissions.TeamDelete,`, `KartovaPermissions.TeamMembersManage,`. OrgAdmin keeps `TeamRead` + `TeamCreate`. Update the inline comment block to: `// OrgAdmin's authority on teams comes from the IsInRole(OrgAdmin) bypass in TeamAdminOfThisHandler, not from team-mutation claims (ADR-0101).`

- [ ] **Step 3: Remove the `TeamAdmin` realm role**

In `KartovaRoles.cs`: delete `public const string TeamAdmin = "TeamAdmin";`. Change `All` to `new[] { Viewer, Member, OrgAdmin }.ToFrozenSet(...)`. Update the `<summary>` on `All` from "four roles" to "three roles" and drop `TeamAdmin` from the prose.

- [ ] **Step 4: Update the SPA permission constants + snapshot (matched pair)**

In `web/src/shared/auth/permissions.ts`, delete the three lines `TeamMetadataEdit: ...`, `TeamDelete: ...`, `TeamMembersManage: ...`. In `web/src/shared/auth/permissions.snapshot.json`, delete the three array entries `"team.metadata.edit"`, `"team.delete"`, `"team.members.manage"`. (Order must stay aligned with `KartovaPermissions.All`; the C# arch test compares as sets so exact order is not required, but keep them adjacent to `team.create` for readability.)

- [ ] **Step 5: Update `usePermissions.test.tsx` if it references the removed perms**

Read `web/src/shared/auth/__tests__/usePermissions.test.tsx`. If any assertion references `TeamMetadataEdit`/`TeamDelete`/`TeamMembersManage` (or their string values), remove those references. If the test only asserts the snapshot count or iterates `KartovaPermissions`, no change is needed beyond the count.

- [ ] **Step 6: Update invitation role docs/detail (drop TeamAdmin)**

In `CreateInvitationRequest.cs` XML doc, change `(Viewer / Member / TeamAdmin / OrgAdmin)` → `(Viewer / Member / OrgAdmin)`. In `InvitationEndpointDelegates.cs`, the `CreateInvitationError.Validation` detail string — change `role must be one of: Viewer, Member, TeamAdmin, OrgAdmin.` → `role must be one of: Viewer, Member, OrgAdmin.` (No code-path change: `KartovaRoles.All` no longer contains `TeamAdmin`, so `Invitation.Create` / `CreateInvitationHandler` already reject it with `Validation` → **422**.)

- [ ] **Step 7: Update `KartovaRolePermissionsTests.cs`**

Replace the `TeamAdmin_is_superset_of_Member_with_team_management_perms` test with this one (constants are gone, so assert on string values):

```csharp
[TestMethod]
public void OrgAdmin_does_not_carry_team_mutation_claims_resource_gate_owns_them()
{
    // ADR-0101: team metadata/delete/members are no longer permission claims —
    // team-admin authority is the per-team Admin membership via TeamAdminOfThis.
    var orgAdmin = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
    Assert.IsTrue(orgAdmin.Contains(KartovaPermissions.TeamRead));
    Assert.IsTrue(orgAdmin.Contains(KartovaPermissions.TeamCreate));
    Assert.IsFalse(orgAdmin.Contains("team.metadata.edit"));
    Assert.IsFalse(orgAdmin.Contains("team.delete"));
    Assert.IsFalse(orgAdmin.Contains("team.members.manage"));
}
```

In `OrgAdmin_uniquely_owns_reverse_lifecycle`, change the role array `new[] { KartovaRoles.Viewer, KartovaRoles.Member, KartovaRoles.TeamAdmin }` → `new[] { KartovaRoles.Viewer, KartovaRoles.Member }`.

- [ ] **Step 8: Update `OrganizationPermissionMatrixTests.cs`**

Delete the `(KartovaRoles.TeamAdmin, [...])` row from the `Matrix` array. The remaining rows (Viewer, Member, OrgAdmin) are unchanged.

- [ ] **Step 9: Update `KartovaPermissionsRules.cs`**

In `Team_permissions_are_present_in_KartovaPermissions_All`, change `expected` to `["team.read", "team.create"]`. In `Role_permissions_include_team_perms`: delete the `[DataRow(KartovaRoles.TeamAdmin, ...)]` line, and change the OrgAdmin DataRow to `[DataRow(KartovaRoles.OrgAdmin, new[] { "team.read", "team.create" })]`. Viewer/Member DataRows (`new[] { "team.read" }`) are unchanged.

- [ ] **Step 10: Update `TeamAdminOfThisHandlerTests.cs`**

Rename `TeamAdmin_of_this_team_succeeds` → `Member_realm_role_with_Admin_membership_succeeds` and change `MakePrincipal(KartovaRoles.TeamAdmin)` → `MakePrincipal(KartovaRoles.Member)`. Rename `TeamAdmin_of_another_team_fails` → `Member_with_Admin_membership_of_another_team_fails` and change `MakePrincipal(KartovaRoles.TeamAdmin)` → `MakePrincipal(KartovaRoles.Member)`. Add a new test that locks the no-membership case:

```csharp
[TestMethod]
public async Task Member_with_no_membership_fails()
{
    var currentUser = Substitute.For<ICurrentUser>();
    currentUser.TeamMemberships.Returns(Array.Empty<TeamMembershipInfo>());

    var sut = new TeamAdminOfThisHandler(currentUser);
    var requirement = new TeamAdminOfThisRequirement();
    var resource = new FakeTeam { TeamId = Guid.NewGuid() };

    var principal = MakePrincipal(KartovaRoles.Member);
    var ctx = new AuthorizationHandlerContext(new[] { requirement }, principal, resource);

    await ((IAuthorizationHandler)sut).HandleAsync(ctx);

    Assert.IsFalse(ctx.HasSucceeded);
}
```

- [ ] **Step 11: Update `TenantClaimsTransformationTests.cs`**

Delete the entire `Expands_role_claims_into_permission_claims_for_TeamAdmin` test method (the role no longer exists). The Viewer/Member/OrgAdmin expansion tests stay.

- [ ] **Step 12: Update `GetMePermissionsTests.cs`**

Delete the entire `GET_me_permissions_returns_TeamAdmin_set` test method. The OrgAdmin/Member/Viewer/empty-role/401 tests stay.

- [ ] **Step 13: Update `CatalogPermissionMatrixTests.cs`**

Delete the `TeamAdminEmail` const, remove `(KartovaRoles.TeamAdmin, TeamAdminEmail)` from the `Roles` array, and delete the two lines `var teamAdminSub = await Fx.GetSubClaimAsync(TeamAdminEmail);` and `await Fx.SeedTeamMembershipAsync(teamId, teamAdminSub, TeamRoleAdmin);`. The `Team_scope_matrix_for_metadata_edit` test uses only the `Member` user (membership-based) — leave it unchanged. If `TeamRoleAdmin` (the `const byte ... = 2`) is now unused anywhere in the file, delete it so `-warnaserror` stays clean (`TeamRoleMember` is still used by `Team_scope_matrix_for_metadata_edit`).

- [ ] **Step 14: Build the full solution (0 warnings)**

Run: `cmd /c "dotnet build Kartova.slnx -warnaserror"`
Expected: Build succeeded, 0 warnings, 0 errors. (If a stray `KartovaRoles.TeamAdmin` / removed-constant reference remains, the compiler names the file — fix it.)

- [ ] **Step 15: Run C# unit + architecture suites — expect GREEN**

Run: `cmd /c "dotnet test tests/Kartova.SharedKernel.Tests/Kartova.SharedKernel.Tests.csproj tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj tests/Kartova.SharedKernel.AspNetCore.Tests/Kartova.SharedKernel.AspNetCore.Tests.csproj"`
Expected: PASS. In particular `Ts_snapshot_equals_csharp_KartovaPermissions_All` passes (snapshot now matches `All`).

- [ ] **Step 16: Run the SPA unit tests — expect GREEN**

Run: `cmd /c "cd web && npm test"`
Expected: PASS — the `permissions.ts` runtime drift guard does not throw (object ↔ snapshot match), and `usePermissions.test.tsx` passes.

- [ ] **Step 17: Run the Org + Catalog integration matrices — expect GREEN** (Docker required)

Run: `cmd /c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --filter FullyQualifiedName~GetMePermissions"`
Run: `cmd /c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~CatalogPermissionMatrix"`
Expected: PASS for both.

- [ ] **Step 18: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs src/Kartova.SharedKernel/Multitenancy/KartovaRoles.cs web/src/shared/auth/permissions.ts web/src/shared/auth/permissions.snapshot.json web/src/shared/auth/__tests__/usePermissions.test.tsx src/Modules/Organization/Kartova.Organization.Contracts/CreateInvitationRequest.cs src/Modules/Organization/Kartova.Organization.Infrastructure/InvitationEndpointDelegates.cs tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs tests/Kartova.ArchitectureTests/OrganizationPermissionMatrixTests.cs tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs tests/Kartova.SharedKernel.AspNetCore.Tests/TeamAdminOfThisHandlerTests.cs tests/Kartova.SharedKernel.AspNetCore.Tests/TenantClaimsTransformationTests.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/GetMePermissionsTests.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs
git commit -m "feat(team-admin): remove TeamAdmin realm role + team.* mutation claims (ADR-0101)"
```

---

## Task 3: KeyCloak realm + dev seed + realm arch test

**Why:** Clean-break cutover (spec §7). Remove the realm role; re-seed the dev user as a realm-`Member` who is an `Admin` of a seeded demo team, so `docker compose up` demonstrates the new model.

**Files:**
- Modify: `deploy/keycloak/kartova-realm.json`
- Modify: `src/Kartova.Migrator/DevSeed.cs`
- Test: `tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs`

- [ ] **Step 1: Remove the `TeamAdmin` realm role and reassign the dev user**

In `deploy/keycloak/kartova-realm.json`: delete the realm-role object `{ "name": "TeamAdmin" }` from `roles.realm`. In the `team-admin@orga.kartova.local` user's `realmRoles` array, replace `"TeamAdmin"` with `"Member"`. Keep the user's `username`, `email`, and `id` unchanged. **Record the user's `id` GUID** (the `"id"` field on that user object) — Step 3 needs it for the membership seed.

- [ ] **Step 2: Confirm the teams / team_members / users column names**

Read `src/Modules/Organization/Kartova.Organization.Infrastructure/` for the EF entity configurations of `Team`, `TeamMembership`, and `User` (file names like `TeamEntityTypeConfiguration.cs`, `TeamMembershipEntityTypeConfiguration.cs`, `UserEntityTypeConfiguration.cs`, or the `OrganizationDbContext` `OnModelCreating`). Note the exact table + column names (expected: `teams(id, tenant_id, display_name, description, created_at)`, `team_members(team_id, user_id, role, added_at)`, `users(id, tenant_id, email, display_name, ...)`, with `role` stored as smallint where `Admin = 2`). Also confirm whether `team_members.user_id` has an FK to `users(id)` (it determines whether Step 3 must seed a `users` row first).

- [ ] **Step 3: Seed a demo team + Admin membership for the dev user in `DevSeed.cs`**

Append a new block to `DevSeed.RunAsync` after the applications seed, mirroring the existing `NO FORCE ROW LEVEL SECURITY` → seed → `FORCE` try/finally pattern. Use the dev user's `id` from Step 1 as a `static readonly Guid TeamAdminUserId`. Insert (idempotently, `ON CONFLICT DO NOTHING`): the `users` row for the dev user (if the FK in Step 2 requires it), one `teams` row (a fixed demo team id), and one `team_members` row with `role = 2` (Admin). Use the exact column names confirmed in Step 2. Template (adjust names to match Step 2):

```csharp
private static readonly Guid DemoTeamId      = Guid.Parse("dddddddd-0001-0001-0001-000000000001");
private static readonly Guid TeamAdminUserId = Guid.Parse("<id from kartova-realm.json team-admin@orga user>");

// ... inside RunAsync, after the applications block:
await ExecAsync(conn, "ALTER TABLE users NO FORCE ROW LEVEL SECURITY;");
await ExecAsync(conn, "ALTER TABLE teams NO FORCE ROW LEVEL SECURITY;");
await ExecAsync(conn, "ALTER TABLE team_members NO FORCE ROW LEVEL SECURITY;");
try
{
    await using var u = conn.CreateCommand();
    u.CommandText = """
        INSERT INTO users (id, tenant_id, email, display_name)
        VALUES ($1, $2, $3, $4) ON CONFLICT (id) DO NOTHING;
        """;
    u.Parameters.AddWithValue(TeamAdminUserId);
    u.Parameters.AddWithValue(OrgATenantId);
    u.Parameters.AddWithValue("team-admin@orga.kartova.local");
    u.Parameters.AddWithValue("Tim TeamAdmin");
    await u.ExecuteNonQueryAsync();

    await using var t = conn.CreateCommand();
    t.CommandText = """
        INSERT INTO teams (id, tenant_id, display_name, description, created_at)
        VALUES ($1, $2, $3, $4, now()) ON CONFLICT (id) DO NOTHING;
        """;
    t.Parameters.AddWithValue(DemoTeamId);
    t.Parameters.AddWithValue(OrgATenantId);
    t.Parameters.AddWithValue("Demo Team");
    t.Parameters.AddWithValue("Seeded so a realm-Member who is team Admin can be exercised in docker compose.");
    await t.ExecuteNonQueryAsync();

    await using var m = conn.CreateCommand();
    m.CommandText = """
        INSERT INTO team_members (team_id, user_id, role, added_at)
        VALUES ($1, $2, 2, now()) ON CONFLICT (team_id, user_id) DO NOTHING;
        """;
    m.Parameters.AddWithValue(DemoTeamId);
    m.Parameters.AddWithValue(TeamAdminUserId);
    await m.ExecuteNonQueryAsync();
    logger.LogInformation("Dev seed: demo team + team-admin Admin membership ensured.");
}
finally
{
    await ExecAsync(conn, "ALTER TABLE team_members FORCE ROW LEVEL SECURITY;");
    await ExecAsync(conn, "ALTER TABLE teams FORCE ROW LEVEL SECURITY;");
    await ExecAsync(conn, "ALTER TABLE users FORCE ROW LEVEL SECURITY;");
}
```

If the migrator role lacks `INSERT` grants on `users`/`teams`/`team_members`, mirror the existing GRANT approach used for `organizations`/`catalog_applications` (see `docker/postgres/init.sql`).

- [ ] **Step 4: Update the realm-seed architecture test**

In `KeycloakRealmSeedRules.cs`, the test `Realm_seed_includes_Viewer_and_TeamAdmin_roles_and_dev_users` → rename to `Realm_seed_includes_Viewer_role_and_dev_users` and:
- delete `Assert.IsTrue(roles.Contains("TeamAdmin"), ...);`
- add `Assert.IsFalse(roles.Contains("TeamAdmin"), "TeamAdmin realm role was removed in ADR-0101.");`
- keep the `viewer@orga` + `team-admin@orga` username assertions (the dev user still exists, now as a Member).

`Every_KartovaRoles_constant_except_ServiceAccount_appears_in_realm_seed` and `Every_role_in_KartovaRolePermissions_Map_has_at_least_one_dev_user` need no edits — they read from the (now smaller) `KartovaRoles` / `Map` reflectively, and the dev user re-seeded as `Member` keeps Member covered.

- [ ] **Step 5: Run the architecture suite — expect GREEN**

Run: `cmd /c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter FullyQualifiedName~KeycloakRealmSeedRules"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add deploy/keycloak/kartova-realm.json src/Kartova.Migrator/DevSeed.cs tests/Kartova.ArchitectureTests/KeycloakRealmSeedRules.cs
git commit -m "chore(team-admin): remove TeamAdmin realm role from seed; seed demo team + Admin membership"
```

---

## Task 4: SPA invite role dropdown

**Files:**
- Modify: `web/src/features/organization/schemas/inviteUser.ts`

- [ ] **Step 1: Drop `TeamAdmin` from the invite role enum**

In `inviteUser.ts`, change `export const KARTOVA_ROLES = ["Viewer", "Member", "TeamAdmin", "OrgAdmin"] as const;` → `export const KARTOVA_ROLES = ["Viewer", "Member", "OrgAdmin"] as const;`. Update the XML/JSDoc comment: "The four Kartova realm roles" → "The three Kartova realm roles" and the wire-enum list `(Viewer | Member | TeamAdmin | OrgAdmin)` → `(Viewer | Member | OrgAdmin)`.

- [ ] **Step 2: Run the SPA tests + typecheck — expect GREEN**

Run: `cmd /c "cd web && npm test && npm run build"`
Expected: PASS. (If a test or component hard-codes `"TeamAdmin"` as a selectable option, update it; grep `web/src` for `TeamAdmin` to confirm none remain.)

- [ ] **Step 3: Commit**

```bash
git add web/src/features/organization/schemas/inviteUser.ts
git commit -m "feat(team-admin): drop TeamAdmin from SPA invite role options"
```

---

## Task 5: Full verification + Definition of Done

**Files:** none (verification only). Docker required.

- [ ] **Step 1: Full solution build, 0 warnings**

Run: `cmd /c "dotnet build Kartova.slnx -warnaserror"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite — unit + architecture + integration** (Docker required)

Run: `cmd /c "dotnet test Kartova.slnx"`
Expected: all green. Confirm no surviving reference to `TeamAdmin` in C#: `git grep -n "TeamAdmin" -- "*.cs"` should return only ADR/spec/plan docs and the `team-admin@orga` username string (not `KartovaRoles.TeamAdmin`).

- [ ] **Step 3: SPA tests + build**

Run: `cmd /c "cd web && npm test && npm run build"`
Expected: green.

- [ ] **Step 4: docker compose HTTP evidence** (DoD #5)

Bring up the stack (`docker compose up -d --build`; if the realm changed, `docker compose down -v` first to force a clean realm import). Obtain a token for `team-admin@orga.kartova.local` (now realm-`Member`, `Admin` of the seeded Demo Team) via the password grant. Capture:
- **Happy:** `PUT /api/v1/organizations/teams/{DemoTeamId}` with a valid body → **200** (a realm-Member managing the team they admin — the exact case that returned 403 before this change).
- **Negative:** same token, `PUT /api/v1/organizations/teams/{some-other-team-id}` → **403** (not an Admin of that team).
- **Invitation:** `POST /api/v1/organizations/invitations` with `{"email":"x@example.com","role":"TeamAdmin"}` as an OrgAdmin → **422** (role no longer valid).
Save the captured curl output under `docs/superpowers/evidence/2026-06-09-team-admin-membership-authority/` or append to a verification doc.

- [ ] **Step 5: Quality gates (per CLAUDE.md DoD)**

Run, in order, against the branch diff: `/simplify` → `/misc:mutation-sentinel` + `/misc:test-generator` (≥80% on changed files) → `/superpowers:requesting-code-review` → `/pr-review-toolkit:review-pr` → `/deep-review`. Address Blocking/Should-fix items.

- [ ] **Step 6: Update CHECKLIST + open PR**

Note ADR-0101 + this change in `docs/product/CHECKLIST.md` (under E-01.F-04), push the branch, open a PR referencing ADR-0101 and the design spec.
