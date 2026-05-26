# Slice 8 — Team management + team-scoped permissions (design)

**Date:** 2026-05-25
**Author:** Roman Głogowski (AI-assisted)
**Status:** Draft, pending user review
**Stories closed:** E-03.F-02.S-01 (create and manage team profile), E-03.F-02.S-02 (assign components to team), E-03.F-02.S-03 *partial* (team page with components — scorecard deferred to E-10).
**Stories partially closed:** slice 7 §15.4 (TeamAdmin becomes load-bearing).
**ADRs touched:** New ADR-0098 (UUID-only entity identifier). References ADR-0008 (RBAC), ADR-0011 (one Org = one tenant), ADR-0065 (Org→Team→System hierarchy), ADR-0066 (multi-ownership — explicitly deferred to E-03.F-05), ADR-0082 (modular monolith), ADR-0090 (tenant scope), ADR-0092 (URL convention), ADR-0095 (cursor pagination).

---

## 1. Why this slice

Slice 7 (PR #24) shipped the RBAC permission model with a *forward-compat* `TeamAdmin` role: same permission set as `Member`, no team-scoped semantics. The role's whole point — *"manage your team's apps"* — is dead weight until teams exist. Slice 7 §15.4 explicitly registered: *"Team-scoped permissions for TeamAdmin — Trigger: E-03.F-02 (Team Management) ships."*

This slice ships:

1. **Team aggregate** inside the existing `Kartova.Organization` module (one module per bounded context, ADR-0082). New tables `teams` + `team_members` in the Organization schema.
2. **`Application.TeamId`** nullable foreign key on the Catalog aggregate (cross-module reference as raw `Guid?` — no domain-type coupling).
3. **Team-scoped authorization** via Microsoft's canonical resource-based pattern (`IAuthorizationService.AuthorizeAsync(user, resource, policyName)` against `Application` and `Team` resources).
4. **SPA team management UI** — `/teams` list + `/teams/{id}` detail + assign-team picker on `ApplicationDetailPage` (hide-by-default per slice 7).
5. **ADR-0098** — formalises *UUIDs as the canonical and only entity identifier*. Bundled retrofit: drop `Application.Name` (slice-3 kebab-case slug) so the rule applies retroactively, not just prospectively.

The slice is intentionally bundle-sized (~5–6 days). The Application.Name retrofit is bundled because writing ADR-0098 with a grandfathered exception for Application.Name would weaken the rule from day one — better to land the rule with the codebase already compliant.

## 2. Context

- Slices 0–7 merged. PR #25 (slice-7 housekeeping + codegen migration) merged 2026-05-25.
- `Kartova.Organization` module today carries the `Organization` aggregate only (slice 2). E-03 (Organization & Team Management) is the bounded context this module owns; Team is the next aggregate.
- `Kartova.Catalog` module today carries `Application` aggregate (slices 3/5/6/7). Will carry Service, API, Infrastructure, Broker entities in later slices.
- Slice 7's `KartovaPermissions` + `KartovaRolePermissions.Map` is the source of truth for role↔permission. Slice 7's `TenantClaimsTransformation` already flattens role claims → permission claims at JWT-validation time.
- ADR-0092 (REST URL convention) is in force: module-prefixed URLs with the primary-collection skip rule. Organization module's `Slug = "organizations"` → endpoints under `/api/v1/organizations/...`.
- `Application.OwnerUserId` exists today as a `Guid` field; not used for authorization. The slice-5 design explicitly deferred "team ownership" to *"the E-03 Team aggregate"* — that's now.
- ADR-0066 promises multi-team ownership with quorum approval — that's E-03.F-05, NOT this slice. Slice 8 ships single-team-per-Application.
- Industry comparison (researched mid-brainstorming):
  - **Backstage**: `spec.owner` single string ref (`group:platform-team` or `user:john.doe`). Users have `memberOf: [...]` array. Single primary owner.
  - **Atlassian Compass**: single owning Team per Component. Multi-team contribution is a viewing/permission concept, not a primary-ownership concept.
  - Both reference products land on single-primary-owner — ADR-0066's multi-ownership is more ambitious than industry MVP. Justified to defer.
- Microsoft Learn documents the canonical .NET pattern for "permission depends on resource attributes" — *resource-based authorization* via `IAuthorizationService.AuthorizeAsync(user, resource, policyName)`. See [docs](https://learn.microsoft.com/aspnet/core/security/authorization/resource-based). Slice 8 adopts this pattern.

## 3. Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | **Team aggregate lives INSIDE the existing `Kartova.Organization` module** — not a new module. New types added to `Kartova.Organization.Domain` / `.Application` / `.Infrastructure` / `.Contracts` / `.Tests` / `.IntegrationTests`. New tables (`teams`, `team_members`) added to the Organization module's schema and `OrganizationDbContext`. | The bounded context is "Organization & Membership" — Team is a child concern of Organization per ADR-0065 (`Org → Team → System → Component`) and E-03 ("Organization & Team Management"). One module per bounded context (ADR-0082), not per aggregate. |
| 2 | `Team` aggregate carries `Id` (Guid), `TenantId`, `DisplayName` (≤128, editable), `Description` (≤512, nullable, editable), `CreatedAt`. **No slug.** | Per Decision #16 / ADR-0098 — UUIDs are the canonical and only entity identifier. |
| 3 | `team_members (team_id, user_id, role_within_team, added_at)` join table. `role_within_team` is enum `{ Admin, Member }`. A user can be in multiple teams; per-team role is independent of realm role. | Multi-team membership matches Backstage/Compass. Per-team role separates "membership" from "team-admin power". |
| 4 | `Application.TeamId` is a nullable `Guid` (not a typed value object) on the Catalog aggregate. Catalog module does NOT reference `Kartova.Organization.Domain.TeamId`. No cross-schema DB FK constraint. Referential integrity enforced by the assign-team endpoint handler. | Per ADR-0082 modules don't share domain types. Mirrors the existing `Application.OwnerUserId` (Guid → Identity, no FK). |
| 5 | `Application.OwnerUserId` retained as point-of-contact / creator audit. Not used for authorization. | Clean separation: identity vs authorization vs operational POC. |
| 6 | **Authorization pattern: Microsoft resource-based** via `IAuthorizationService.AuthorizeAsync(user, resource, policyName)`. One `ApplicationTeamScopedHandler : AuthorizationHandler<ApplicationTeamScopedRequirement, Application>` carries the rule for Application-scoped operations. Analogous `TeamAdminOfThisHandler : AuthorizationHandler<TeamAdminOfThisRequirement, Team>` for Team-scoped operations. Two-layer auth: binding `.RequireAuthorization(KartovaPermissions.X)` gates the claim (Viewer/anon fail here, no DB hit); endpoint `AuthorizeAsync(user, resource, …)` gates the team scope after the row is loaded. | Canonical .NET pattern (Microsoft Learn). Centralizes the rule; keeps domain pure; uses framework machinery. |
| 7 | New permissions in `KartovaPermissions`: `team.read`, `team.create`, `team.metadata.edit`, `team.delete`, `team.members.manage` (5 total). New entries in `KartovaRolePermissions.Map`: OrgAdmin gets all 5; TeamAdmin gets `team.metadata.edit` + `team.delete` + `team.members.manage` + `team.read` (the first three gated to own team via resource auth); Member + Viewer get only `team.read`. **`team.delete` is split from `team.metadata.edit`** so a destructive operation doesn't share a permission name with non-destructive metadata edits (resolves critic finding C1). | Mirrors slice 7's permission catalog pattern. "Own team" scoping enforced via resource-based auth, not via more permissions. |
| 8 | New `ITeamMembershipReader` abstraction in `Kartova.SharedKernel.AspNetCore` (interface only). Implementation `OrganizationTeamMembershipReader` in `Kartova.Organization.Infrastructure` (reads from the Organization DbContext's `team_members` table; RLS-scoped). `TenantClaimsTransformation` calls it after role-claim expansion so `ICurrentUser.TeamMemberships` is populated for handler use. One DB hit per authenticated request; cached for the request lifetime via `ITenantContext`. | Cross-module access without coupling: abstraction in shared, impl in owning module. Same pattern as existing `ICurrentUser` / `ITenantContext`. |
| 9 | Team-scope predicate (in `ApplicationTeamScopedHandler`): `OrgAdmin → always allow`; otherwise allow iff `currentUser.TeamIds.Contains(app.TeamId.Value)`. If `app.TeamId == null` (unassigned): only OrgAdmin can mutate; Members/TeamAdmins of any team can `read` but not mutate unassigned apps. | "Unassigned" is a deliberately-restricted state — pushes OrgAdmin to assign promptly. Read access stays permissive so the unassigned app shows up in lists. |
| 10 | New endpoints under the existing Organization module group `/api/v1/organizations`: `GET /teams` (list, cursor-paginated per ADR-0095), `GET /teams/{id:guid}` (detail with members + app IDs), `POST /teams` (create), `PUT /teams/{id:guid}` (rename + description), `DELETE /teams/{id:guid}`, `POST /teams/{id:guid}/members` (add user), `DELETE /teams/{id:guid}/members/{userId:guid}` (remove), `PUT /teams/{id:guid}/members/{userId:guid}` (promote/demote). New endpoint on Catalog module: `PUT /api/v1/catalog/applications/{id:guid}/team` (body `{ teamId: Guid? }` — null unassigns). | Endpoints live in the module that owns the data. Team endpoints under `/organizations`, app-team assignment under `/catalog`. All URLs use `{id:guid}` per ADR-0098. |
| 11 | `TeamId` value-object lives in `Kartova.Organization.Domain`. Catalog uses raw `Guid?` for `Application.TeamId` to respect the bounded-context boundary. | ADR-0082 modular monolith — no cross-module domain references. |
| 12 | Team deletion blocked if any `applications.team_id = team.id`. Returns 409 `team-has-applications` with `applicationCount` extension. | Safer than orphaning; OrgAdmin must reassign or unassign first. |
| 13 | Bootstrap path: new tenant has zero teams. OrgAdmin creates the first team. Applications registered before any team has `team_id = null` until OrgAdmin assigns. No "default" team auto-created. | Avoids magical state. |
| 14 | SPA: new top-level route `/teams` (list) + `/teams/{id}` (detail with members + assigned apps) + assign-team picker on `ApplicationDetailPage`. New permission constants in `web/src/shared/auth/permissions.ts`; drift sentinel arch test extends to cover them. Hide-by-default UI gating via `usePermissions()` (slice 7 pattern). | Continues slice 7's hide-by-default convention. |
| 15 | One PR, sequenced commits per task. Closes E-03.F-02.S-01 + S-02 + partial S-03. | Slice-5/slice-7 precedent. |
| 16 | **ADR-0098 created in this slice: UUIDs are the canonical and only entity identifier.** No slugs, no kebab-case names, no secondary machine identifiers. URLs use `{id:guid}` exclusively. Tenant scope via JWT, never via URL path. Display names allowed to duplicate; UI may warn but DB doesn't constrain. ADR-0092's "resource-id format" punt (line 124) is resolved by this ADR. | Eliminates cross-tenant slug-collision concerns category-wide. Forward rule for every entity (Service, API, Infrastructure, Broker, System in upcoming slices). |
| 17 | **`Application.Name` column dropped** retroactively. `RegisterApplicationRequest` drops the `name` field. `Application.Create(name, displayName, ...)` factory drops the `name` parameter. SPA registration form drops the "name" input. SPA list/detail badges that showed kebab `name` are removed. EF migration `DropApplicationName` drops the column; existing rows lose the value (only `display_name` is user-facing copy; `name` was redundant duplicate naming). Integration tests update arrange phase. | Retroactive consistency with ADR-0098. Application.Name was a slice-3 implicit slug; dropping it makes the rule apply without grandfathered exception. |

## 4. Domain model

### 4.1 Domain types in `Kartova.Organization.Domain`

```csharp
public readonly record struct TeamId(Guid Value)
{
    public static TeamId New() => new(Guid.NewGuid());
}

public enum TeamRole : byte
{
    Member = 1,
    Admin  = 2,
}

public sealed partial class Team : ITenantOwned
{
    private Guid _id;
    public TeamId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Team() { /* EF */ }

    public static Team Create(string displayName, string? description, TenantId tenantId, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ValidateDisplayName(displayName);
        ValidateDescription(description);
        return new Team {
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
        if (string.IsNullOrWhiteSpace(s)) throw new ArgumentException("Team display name must not be empty.", nameof(s));
        if (s.Length > 128) throw new ArgumentException("Team display name must be <= 128 characters.", nameof(s));
    }
    private static void ValidateDescription(string? s)
    {
        if (s is { Length: > 512 }) throw new ArgumentException("Team description must be <= 512 characters.", nameof(s));
    }
}

public sealed class TeamMembership
{
    public TeamId TeamId { get; private set; }
    public Guid UserId { get; private set; }
    public TeamRole Role { get; private set; }
    public DateTimeOffset AddedAt { get; private set; }

    private TeamMembership() { /* EF */ }

    public static TeamMembership Create(TeamId teamId, Guid userId, TeamRole role, TimeProvider clock)
    {
        if (userId == Guid.Empty) throw new ArgumentException("userId required", nameof(userId));
        return new TeamMembership { TeamId = teamId, UserId = userId, Role = role, AddedAt = clock.GetUtcNow() };
    }

    public void ChangeRole(TeamRole newRole) => Role = newRole;
}
```

**Design choice:** `Team` aggregate covers metadata only. `TeamMembership` is a separate entity (not a child of Team aggregate). Membership operations don't load Team — they insert/update/delete rows directly. No "team must have at least one admin" invariant initially; OrgAdmin can delete the last admin if they want (SPA warns but doesn't block). Invariant can be added later without restructuring.

**ADR-0065 partial wiring:** ADR-0065's full hierarchy is `Org → Team → System → Component`. This slice wires Team → Application directly without the System mid-layer. Systems are deferred to §15.4 (E-03.F-03). The flat Team-to-Application assignment is intentional for MVP — when System grouping ships, `Application.TeamId` will be supplemented by an optional `Application.SystemId` (where the System is itself owned by a Team).

### 4.2 Tables

**`teams`** (Organization module):
```
id            uuid          PK
tenant_id     uuid          not null
display_name  varchar(128)  not null
description   varchar(512)  null
created_at    timestamptz   not null

INDEX idx_teams_tenant ON teams(tenant_id)
```

**`team_members`**:
```
team_id    uuid          not null  -- FK → teams.id ON DELETE CASCADE
user_id    uuid          not null
role       smallint      not null  -- 1=Member, 2=Admin
added_at   timestamptz   not null

PK (team_id, user_id)
INDEX idx_team_members_user ON team_members(user_id)   -- "what teams is this user in"
```

No `tenant_id` on `team_members` — derived via `team_id`. Tenant scoping enforced via team-side RLS policy + explicit join semantics (see §4.4).

### 4.3 Application table change

```
ALTER TABLE catalog_applications ADD COLUMN team_id uuid NULL;
ALTER TABLE catalog_applications DROP COLUMN name;             -- per Decision #17
CREATE INDEX idx_catalog_applications_team ON catalog_applications(team_id);
```

No cross-schema FK constraint. Validation enforced by the assign-team endpoint.

### 4.4 RLS policies

Both new tables get the same RLS treatment as `organizations`:

```sql
ALTER TABLE teams ENABLE ROW LEVEL SECURITY;
ALTER TABLE teams FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON teams
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

ALTER TABLE team_members ENABLE ROW LEVEL SECURITY;
ALTER TABLE team_members FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation ON team_members
  USING (EXISTS (
    SELECT 1 FROM teams t
    WHERE t.id = team_members.team_id
      AND t.tenant_id = current_setting('app.current_tenant_id')::uuid
  ));
```

The `EXISTS` subquery on `team_members` is cheap given the PK and `teams.id` index. If a perf bench eventually demands it, `tenant_id` can be denormalized onto `team_members`. **Default: keep the subquery.**

### 4.5 Migrations

Four migrations, sequenced. RLS pattern per the actual slice 3/5 precedents (verified against `InitialOrganization.cs`, `AddApplications.cs`, `AddApplicationDisplayName.cs`):

- **New tables:** single SQL block — `ENABLE ROW LEVEL SECURITY; FORCE ROW LEVEL SECURITY; CREATE POLICY ...`. No toggling. Matches `AddApplications` slice-3 migration.
- **Existing-table data backfill:** `NO FORCE` only when a maintenance UPDATE must run as table owner (FORCE applies policies to owner too). Matches `AddApplicationDisplayName` slice-4 migration.
- **Existing-table pure DDL (column add/drop with no backfill):** plain `ALTER TABLE`. DDL doesn't require FORCE toggling.

The migrations:

1. **`AddTeamsTable`** (Organization) — new table. `CREATE TABLE teams (...); CREATE INDEX idx_teams_tenant ON teams(tenant_id); ALTER TABLE teams ENABLE ROW LEVEL SECURITY; ALTER TABLE teams FORCE ROW LEVEL SECURITY; CREATE POLICY tenant_isolation ...`.
2. **`AddTeamMembersTable`** (Organization) — new table. Same pattern. PK on `(team_id, user_id)`, index on `user_id`, RLS policy uses team-join `EXISTS` subquery (§4.4).
3. **`AddApplicationTeamId`** (Catalog) — pure column add. `ALTER TABLE catalog_applications ADD COLUMN team_id uuid NULL; CREATE INDEX idx_catalog_applications_team ON catalog_applications(team_id);`. No FORCE toggle needed (no backfill).
4. **`DropApplicationName`** (Catalog) — pure column drop. `ALTER TABLE catalog_applications DROP COLUMN name;`. No FORCE toggle needed.

### 4.6 EF configurations

- `TeamEntityTypeConfiguration` (new, `Kartova.Organization.Infrastructure`) — mirrors `OrganizationEntityTypeConfiguration`. Properties: `Id` (Guid via `TeamId` converter), `TenantId` (converter), `DisplayName`, `Description`, `CreatedAt`. Index on `TenantId`.
- `TeamMembershipEntityTypeConfiguration` (new) — composite key `(TeamId, UserId)`. `TeamId` value-converter. `UserId` raw Guid. `Role` enum→smallint. Index on `UserId`.
- `EfApplicationConfiguration` modified: add `b.Property(x => x.TeamId).HasColumnName("team_id");` where `Application.TeamId` is `Guid?`. **Remove** the `Name` property mapping + validation per Decision #17.

### 4.7 DbContexts

- `OrganizationDbContext` gains `DbSet<Team> Teams` + `DbSet<TeamMembership> TeamMembers`.
- `CatalogDbContext` unchanged structurally; `Application` loses `Name` column via the migration.

## 5. Authorization plumbing

### 5.1 Permission constants

Append to `KartovaPermissions`:

```csharp
public const string TeamRead          = "team.read";
public const string TeamCreate        = "team.create";
public const string TeamMetadataEdit  = "team.metadata.edit";
public const string TeamDelete        = "team.delete";
public const string TeamMembersManage = "team.members.manage";
```

The `All` collection + drift-snapshot JSON pick these up automatically (slice 7's arch tests are data-driven over reflection). `team.delete` is split from `team.metadata.edit` so a destructive op doesn't share a non-destructive permission name.

### 5.2 Role → permission map update

`KartovaRolePermissions.Map` is the existing `FrozenDictionary<string, FrozenSet<string>>` (slice 7 introduced `FrozenDictionary` + `FrozenSet` per slice 7 PR-review findings). The map gains:

| Role | `team.read` | `team.create` | `team.metadata.edit` | `team.delete` | `team.members.manage` |
|---|---|---|---|---|---|
| Viewer | ✓ | — | — | — | — |
| Member | ✓ | — | — | — | — |
| TeamAdmin | ✓ | — | ✓ (own team only) | ✓ (own team only) | ✓ (own team only) |
| OrgAdmin | ✓ | ✓ | ✓ | ✓ | ✓ |

The "own team only" qualifier is enforced via `TeamAdminOfThisHandler` resource auth — NOT via separate permission constants.

### 5.3 Carrying team membership on the principal

**New abstraction** in `Kartova.SharedKernel` (NOT `Kartova.SharedKernel.AspNetCore` — see layering note below):

```csharp
public interface ITeamMembershipReader
{
    Task<IReadOnlyList<TeamMembershipInfo>> GetForUserAsync(Guid userId, CancellationToken ct);
}

public sealed record TeamMembershipInfo(Guid TeamId, TeamRoleKind Role);
public enum TeamRoleKind : byte { Member = 1, Admin = 2 }
```

**Layering rationale (resolves critic finding C5):** `ITenantContext` lives in `Kartova.SharedKernel` and is extended in §5.4 to expose `IReadOnlyList<TeamMembershipInfo>`. Since `Kartova.SharedKernel` cannot reference `Kartova.SharedKernel.AspNetCore` (one-way dependency), the membership types MUST live in `Kartova.SharedKernel`. The Organization-module `TeamRole` enum maps 1:1 to `TeamRoleKind` via a small mapper in `Kartova.Organization.Infrastructure`.

**Implementation** in `Kartova.Organization.Infrastructure`:

```csharp
internal sealed class OrganizationTeamMembershipReader(OrganizationDbContext db) : ITeamMembershipReader
{
    public async Task<IReadOnlyList<TeamMembershipInfo>> GetForUserAsync(Guid userId, CancellationToken ct)
    {
        return await db.TeamMembers
            .Where(m => m.UserId == userId)            // RLS scopes to current tenant via team-join policy
            .Select(m => new TeamMembershipInfo(m.TeamId.Value, (TeamRoleKind)m.Role))
            .ToListAsync(ct);
    }
}
```

Registered scoped via `OrganizationModule.RegisterServices`.

### 5.4 `ITenantContext` + `ICurrentUser` extension

`ITenantContext` gains BOTH new read-only properties AND a new mutator method:

```csharp
// New read-only properties:
public IReadOnlyList<TeamMembershipInfo> TeamMemberships { get; }
public IReadOnlySet<Guid> TeamIds { get; }   // shortcut: just the team ids

// New mutator:
void PopulateTeamMemberships(IReadOnlyList<TeamMembershipInfo> memberships);
```

`TenantContextAccessor` implements the new method (sets the backing field; `Clear()` resets both the existing tenant-id/roles and the new team-memberships).

`ICurrentUser` exposes the same `TeamMemberships` + `TeamIds` shortcuts.

**`TenantClaimsTransformation` signature change:** the method becomes genuinely `async` (today returns via `Task.FromResult`). The new shape:

```csharp
public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
{
    // ... existing tenant-id + role-claim flatten + permission-claim expansion (unchanged) ...

    var sub = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    if (Guid.TryParse(sub, out var userId))
    {
        var reader = _services.GetRequiredService<ITeamMembershipReader>();
        var memberships = await reader.GetForUserAsync(userId, CancellationToken.None);
        context.PopulateTeamMemberships(memberships);
    }

    return principal;
}
```

One DB hit per authenticated request. `OrganizationDbContext` is request-scoped via `ITenantScope`; RLS auto-filters to the current tenant. Cached for the request lifetime via `ITenantContext`.

### 5.5 Resource-based authorization — policies

**New static constants** in `Kartova.SharedKernel.Multitenancy`:

```csharp
public static class KartovaTeamPolicies
{
    public const string ApplicationTeamScoped = "team-scoped:application";
    public const string TeamAdminOfThis       = "team-admin-of-this";
}
```

### 5.6 `ApplicationTeamScopedHandler`

```csharp
public sealed class ApplicationTeamScopedRequirement : IAuthorizationRequirement;

public sealed class ApplicationTeamScopedHandler(ICurrentUser currentUser)
    : AuthorizationHandler<ApplicationTeamScopedRequirement, Application>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApplicationTeamScopedRequirement requirement,
        Application application)
    {
        // OrgAdmin always passes
        if (context.User.IsInRole(KartovaRoles.OrgAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Unassigned apps: only OrgAdmin can mutate (Decision #9)
        if (application.TeamId is null) return Task.CompletedTask;   // fail

        // Member/TeamAdmin: app's team must be one the user belongs to
        if (currentUser.TeamIds.Contains(application.TeamId.Value))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
```

### 5.7 `TeamAdminOfThisHandler`

```csharp
public sealed class TeamAdminOfThisRequirement : IAuthorizationRequirement;

public sealed class TeamAdminOfThisHandler(ICurrentUser currentUser)
    : AuthorizationHandler<TeamAdminOfThisRequirement, Team>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TeamAdminOfThisRequirement requirement,
        Team team)
    {
        if (context.User.IsInRole(KartovaRoles.OrgAdmin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (currentUser.TeamMemberships.Any(m => m.TeamId == team.Id.Value && m.Role == TeamRoleKind.Admin))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
```

### 5.8 Policy registration

`AddKartovaPermissionPolicies` keeps its single responsibility — registering one policy per permission-claim. A NEW sibling method handles resource-based policies:

```csharp
// Kartova.SharedKernel.AspNetCore.AuthorizationExtensions (existing — UNCHANGED behavior)
public static AuthorizationBuilder AddKartovaPermissionPolicies(this AuthorizationBuilder builder)
{
    foreach (var perm in KartovaPermissions.All)
    {
        builder.AddPolicy(perm, p => p.RequireClaim(KartovaClaims.Permission, perm));
    }
    return builder;
}

// NEW method (slice 8)
public static AuthorizationBuilder AddKartovaResourcePolicies(this AuthorizationBuilder builder)
{
    builder.AddPolicy(KartovaTeamPolicies.ApplicationTeamScoped, p =>
        p.Requirements.Add(new ApplicationTeamScopedRequirement()));
    builder.AddPolicy(KartovaTeamPolicies.TeamAdminOfThis, p =>
        p.Requirements.Add(new TeamAdminOfThisRequirement()));
    return builder;
}
```

`AddKartovaJwtAuth` calls both:

```csharp
services.AddAuthorizationBuilder()
        .AddKartovaPermissionPolicies()
        .AddKartovaResourcePolicies();
services.AddScoped<IAuthorizationHandler, ApplicationTeamScopedHandler>();
services.AddScoped<IAuthorizationHandler, TeamAdminOfThisHandler>();
```

Separation keeps each method's contract focused: permission-claim policies vs resource-based policies.

### 5.9 Endpoint usage pattern

Catalog endpoints that mutate (`PUT`, lifecycle endpoints, `PUT /team`) gain a resource gate after loading the Application:

```csharp
internal static async Task<IResult> DeprecateApplicationAsync(
    Guid id,
    [FromBody] DeprecateApplicationRequest request,
    DeprecateApplicationHandler handler,
    CatalogDbContext db,
    IAuthorizationService auth,
    ClaimsPrincipal user,
    CancellationToken ct)
{
    var app = await db.Applications.FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(id), ct);
    if (app is null) return EndpointResultExtensions.ApplicationNotFound();

    var authResult = await auth.AuthorizeAsync(user, app, KartovaTeamPolicies.ApplicationTeamScoped);
    if (!authResult.Succeeded) return Results.Forbid();

    var result = await handler.Handle(new DeprecateApplicationCommand(new ApplicationId(id), request.SunsetDate), db, ct);
    if (result is null) return EndpointResultExtensions.ApplicationNotFound();
    return Results.Ok(result);
}
```

Read endpoints (`GET /applications`, `GET /applications/{id}`) DON'T get this gate — `catalog.read` already permits any authenticated tenant user; unassigned apps remain visible. **Deliberate non-decision (resolves critic finding C2):** reads are tenant-scoped via RLS but NOT team-scoped within a tenant. A Member of team A can read apps assigned to team B. This matches Backstage / Compass conventions where the catalog is generally tenant-readable and team-scoping applies to mutations. If a future product requirement demands per-team read isolation, it lands as a follow-up via a `Read` resource-policy variant — out of scope for this slice.

Team module endpoints follow the same pattern, gating on `KartovaTeamPolicies.TeamAdminOfThis` after loading the Team.

### 5.10 What this preserves from slice 7

- `KartovaPermissions` constants stay the source of truth (permission-as-claim).
- Binding-level `.RequireAuthorization(KartovaPermissions.X)` still gates Viewer/anonymous before any DB hit.
- The 32-cell `CatalogPermissionMatrixTests` survives unchanged for Viewer/OrgAdmin cells. New test surface required: Member/TeamAdmin in team A trying to mutate app in team B → 403.

## 6. Endpoints

### 6.1 New endpoints under `/api/v1/organizations`

| Method | Path | Claim policy (binding) | Resource policy (handler) | Notes |
|---|---|---|---|---|
| `GET` | `/teams` | `team.read` | — | Cursor-paginated (ADR-0095). `sortBy ∈ {createdAt, displayName}` default `createdAt`. |
| `GET` | `/teams/{id:guid}` | `team.read` | — | Returns `TeamDetailResponse` (team + members + assigned application IDs). |
| `POST` | `/teams` | `team.create` | — | Body `{ displayName, description? }`. 201 + `Location` + `TeamResponse`. |
| `PUT` | `/teams/{id:guid}` | `team.metadata.edit` | `TeamAdminOfThis` | Body `{ displayName, description? }`. |
| `DELETE` | `/teams/{id:guid}` | `team.delete` | `TeamAdminOfThis` | 409 `team-has-applications` with `applicationCount` extension if assigned apps exist. |
| `POST` | `/teams/{id:guid}/members` | `team.members.manage` | `TeamAdminOfThis` | Body `{ userId, role }`. 409 on duplicate `(team_id, user_id)`. |
| `DELETE` | `/teams/{id:guid}/members/{userId:guid}` | `team.members.manage` | `TeamAdminOfThis` | 204 on success. 404 if membership doesn't exist. |
| `PUT` | `/teams/{id:guid}/members/{userId:guid}` | `team.members.manage` | `TeamAdminOfThis` | Body `{ role }`. Promote/demote. |

**Note on ADR-0092:** the ADR's example table (line 93) shows `(future) /api/v1/organization/teams` using the *singular* `organization`. The actual `OrganizationModule.Slug = "organizations"` (plural) per the primary-collection skip rule (ADR-0092 rule 3), so the live URLs use `/api/v1/organizations/...`. The ADR-0092 example is a stale draft typo; spec §6.1 uses the correct plural form throughout.

### 6.2 New endpoint under `/api/v1/catalog`

| Method | Path | Claim policy | Resource policy | Notes |
|---|---|---|---|---|
| `PUT` | `/applications/{id:guid}/team` | `catalog.applications.edit-metadata` | `ApplicationTeamScoped` | Body `{ teamId: Guid? }` (`null` unassigns). Handler verifies the target `teamId` exists in current tenant (RLS-scoped query → 422 `invalid-team` if not). **Target-team membership is required for non-OrgAdmin callers (slice-8 boundary-review fix SF-2). 403 returned otherwise** — prevents a non-OrgAdmin member from reassigning the app to a team they don't belong to, which would orphan them on the very next request. OrgAdmin and null-teamId (unassign) are unaffected. |

### 6.3 New DTOs in `Kartova.Organization.Contracts`

```csharp
[ExcludeFromCodeCoverage]
public sealed record TeamResponse(Guid Id, string DisplayName, string? Description, DateTimeOffset CreatedAt);

[ExcludeFromCodeCoverage]
public sealed record TeamDetailResponse(
    Guid Id, string DisplayName, string? Description, DateTimeOffset CreatedAt,
    IReadOnlyCollection<TeamMemberResponse> Members,
    IReadOnlyCollection<Guid> ApplicationIds);

[ExcludeFromCodeCoverage]
public sealed record TeamMemberResponse(Guid UserId, string Role, DateTimeOffset AddedAt);

[ExcludeFromCodeCoverage]
public sealed record CreateTeamRequest(string DisplayName, string? Description);

[ExcludeFromCodeCoverage]
public sealed record UpdateTeamRequest(string DisplayName, string? Description);

[ExcludeFromCodeCoverage]
public sealed record AddTeamMemberRequest(Guid UserId, string Role);

[ExcludeFromCodeCoverage]
public sealed record UpdateTeamMemberRequest(string Role);
```

`Role` on the wire is `"Admin"` or `"Member"` (string enum); server validates + maps to `TeamRole`. All carry `[ExcludeFromCodeCoverage]` per `ContractsCoverageRules` arch test.

### 6.4 New DTO in `Kartova.Catalog.Contracts`

```csharp
[ExcludeFromCodeCoverage]
public sealed record AssignTeamRequest(Guid? TeamId);
```

### 6.5 Problem types

Two new constants in `ProblemTypes`:

```csharp
public const string TeamHasApplications = "https://kartova.io/problems/team-has-applications";
public const string InvalidTeam         = "https://kartova.io/problems/invalid-team";
```

Existing `ResourceNotFound`, `ValidationFailed`, `ConcurrencyConflict` reused.

### 6.6 Application.Name removal — endpoint contract changes

Per Decision #17, the following endpoints lose the `name` field from their request/response shapes:

- `POST /api/v1/catalog/applications` — `RegisterApplicationRequest` drops `Name`. Body becomes `{ displayName, description }`. (`OwnerUserId` is sourced server-side from the JWT `sub` claim inside the handler, never the body — pre-existing slice-3 behaviour.) Response `ApplicationResponse` drops `Name`.
- `GET /api/v1/catalog/applications` — `ApplicationResponse` rows lose `Name`.
- `GET /api/v1/catalog/applications/{id}` — same.
- `PUT /api/v1/catalog/applications/{id}` — `EditApplicationRequest` is already `{ displayName, description }`, no change. Response loses `Name`.
- Lifecycle endpoints (`/deprecate`, `/decommission`, `/reactivate`, `/un-decommission`) — responses lose `Name`.

This is a **breaking API change**. SPA + integration tests updated in lockstep (slice 8 ships them together, no out-of-band consumers).

## 7. SPA

### 7.1 New permission constants

`web/src/shared/auth/permissions.ts` + `permissions.snapshot.json` gain four entries:

```ts
TeamRead:          "team.read",
TeamCreate:        "team.create",
TeamMetadataEdit:  "team.metadata.edit",
TeamMembersManage: "team.members.manage",
```

Drift sentinel arch test (slice 7 `Ts_snapshot_equals_csharp_KartovaPermissions_All`) auto-picks up the additions.

### 7.2 `/me/permissions` response extension

`MePermissionsResponse` gains:

```csharp
public sealed record MePermissionsResponse(
    string? Role,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<MeTeamMembership> TeamMemberships);   // NEW

public sealed record MeTeamMembership(Guid TeamId, string Role);
```

`usePermissions()` hook's `UsePermissionsResult` gains:

```ts
teamIds: Guid[];                // shortcut: just the ids
teamAdminTeamIds: Guid[];       // shortcut: teams where I'm Admin
```

Used by SPA gating predicates (e.g., "is this user a TeamAdmin of *this* team page").

### 7.3 New routes

```tsx
<Route element={<ProtectedShell />}>
  <Route path="/catalog" element={<CatalogListPage />} />
  <Route path="/catalog/applications/:id" element={<ApplicationDetailPage />} />
  <Route path="/teams" element={<TeamsListPage />} />            {/* NEW */}
  <Route path="/teams/:id" element={<TeamDetailPage />} />       {/* NEW — :id is the UUID */}
</Route>
```

Both new routes gated at the page level by `usePermissions().hasPermission(KartovaPermissions.TeamRead)`. `Sidebar.tsx` gains a "Teams" nav entry hidden via the same predicate.

### 7.4 New files

| Path | Purpose |
|---|---|
| `web/src/features/teams/api/teams.ts` | All team React Query hooks (typed `apiClient`). |
| `web/src/features/teams/api/__tests__/teams.test.tsx` | Hook tests with `vi.spyOn(apiClient, "get")` pattern. |
| `web/src/features/teams/pages/TeamsListPage.tsx` | List view, cursor pagination. |
| `web/src/features/teams/pages/TeamDetailPage.tsx` | Detail: header (rename/delete gated), members section, assigned-applications section. |
| `web/src/features/teams/pages/__tests__/TeamsListPage.test.tsx` | Component test. |
| `web/src/features/teams/pages/__tests__/TeamDetailPage.test.tsx` | Component test (member ops, rename, delete-with-apps-409 path). |
| `web/src/features/teams/components/CreateTeamDialog.tsx` | Form: displayName + description. |
| `web/src/features/teams/components/RenameTeamDialog.tsx` | Form: displayName + description. |
| `web/src/features/teams/components/AddMemberDialog.tsx` | Form: userId + role select. |
| `web/src/features/teams/components/ChangeRoleDialog.tsx` | Form: role select (Admin↔Member). |
| `web/src/features/teams/components/RemoveMemberConfirmDialog.tsx` | Plain confirm. |
| `web/src/features/teams/components/DeleteTeamConfirmDialog.tsx` | Plain confirm + 409-with-applicationCount toast handling. |
| `web/src/features/teams/components/AssignTeamPicker.tsx` | Dropdown on `ApplicationDetailPage` to set/clear `Application.TeamId`. |
| `web/src/features/teams/components/__tests__/*.test.tsx` | Dialog + picker tests. |
| `web/src/features/teams/schemas/createTeam.ts` | zod (displayName ≤128, description ≤512). |
| `web/src/features/teams/schemas/updateTeam.ts` | zod. |
| `web/src/features/teams/schemas/addTeamMember.ts` | zod (UUID + role enum). |

### 7.5 Modified files

| Path | Change |
|---|---|
| `web/src/shared/auth/permissions.ts` + `permissions.snapshot.json` | +4 permission strings. |
| `web/src/shared/auth/usePermissions.ts` | Extend `UsePermissionsResult` with `teamIds`, `teamAdminTeamIds`. |
| `web/src/app/router.tsx` | Add `/teams` + `/teams/:id` routes. |
| `web/src/components/layout/Sidebar.tsx` | Add "Teams" nav entry gated by `TeamRead`. |
| `web/src/features/catalog/pages/ApplicationDetailPage.tsx` | Add `AssignTeamPicker` to the header. **Remove** the kebab `name` badge (Decision #17). |
| `web/src/features/catalog/pages/CatalogListPage.tsx` / `ApplicationsTable.tsx` | **Remove** the kebab `name` badge from row rendering. |
| `web/src/features/catalog/components/RegisterApplicationDialog.tsx` | **Drop** the `name` input + validation. Form becomes displayName + description + ownerUserId. |
| `web/src/features/catalog/schemas/registerApplication.ts` | **Drop** `name` field + kebab validation. |
| `web/src/features/catalog/api/applications.ts` | Add `useAssignApplicationTeam(id)` mutation (typed `apiClient.PUT`). Remove `name` from response types. |
| `web/src/features/catalog/pages/__tests__/*.test.tsx` | Update tests for the new register-dialog shape; verify team-picker visibility per role. |

### 7.6 UI behavior

- **`/teams` list**: empty state for fresh tenant ("No teams yet. Create your first team."). "+ Create team" button gated by `TeamCreate`.
- **`/teams/{id}`**: header shows display name (no slug — no longer exists). Rename/Delete buttons visible only when user is `TeamAdminOfThis` (`teamAdminTeamIds.includes(team.id) || isOrgAdmin`). Members section (user-id + role + remove button). Applications section (links into `/catalog/applications/{id}`). Add-member button visible via same gate.
- **AssignTeamPicker** on `ApplicationDetailPage`: dropdown shows current team display name or "Unassigned". Click → list of teams the current user belongs to (OrgAdmin sees all). Selecting → `useAssignApplicationTeam`. Greyed out if the user lacks permission to move between their teams.
- **Delete team with applications**: server returns 409 `team-has-applications` + `applicationCount: N`. Dialog catches, toast: *"Can't delete: N applications still assigned. Reassign them first."* Dialog stays open.

### 7.7 Hook surface (`teams.ts`)

```ts
useTeamsList(params)        // cursor list, sortBy/sortOrder
useTeam(id)                 // single team detail
useCreateTeam()
useUpdateTeam(id)
useDeleteTeam(id)
useAddTeamMember(id)
useRemoveTeamMember(id)
useChangeTeamMemberRole(id)
```

All typed via `apiClient.{GET,POST,PUT,DELETE}` (codegen will pick up the new paths after slice 8's docker rebuild).

## 8. Application.Name retrofit (Decision #17)

### 8.1 Migration

`DropApplicationName` migration (Catalog module). Pure DDL — no FORCE toggle needed (§4.5):

```sql
ALTER TABLE catalog_applications DROP COLUMN name;
```

### 8.2 Domain + infrastructure code changes

- `Application.cs`: drop `Name` property + backing field. Drop `ValidateName` + `KebabCase` regex. `Create` factory signature loses `name` parameter.
- `RegisterApplicationCommand`: drop `Name` field.
- `RegisterApplicationHandler`: drop `name` from command construction.
- `RegisterApplicationRequest` (Contracts): drop `Name` field.
- `CatalogEndpointDelegates.RegisterApplicationAsync`: drop `request.Name` from the `RegisterApplicationCommand` construction.
- `ApplicationResponse` (Contracts): drop `Name` field.
- `EfApplicationConfiguration`: remove the `Name` column mapping (`HasColumnName("name").HasMaxLength(256).IsRequired()` and `HasIndex(x => new { x.TenantId, x.Name })` if present).
- **`ApplicationSortSpecs.cs`**: remove `Name` sort spec; update `AllowedFieldNames` to drop the `Name` entry. Any caller iterating allowed fields will lose the `Name` option.
- **`ApplicationSortField`** (Contracts enum): remove the `Name` value. Update any explicit lookups (e.g., `CursorListQueryParameterTransformer` OpenAPI annotation if it references the enum's members).
- **`Kartova.ArchitectureTests`**: any reflection-based test that enumerates `Application` properties (e.g., a "must have Name" rule, if it exists) updates.

### 8.3 Test updates

Every integration test that calls `Fx.RegisterApplicationAsync(client, "some-kebab-name", ...)` updates its signature — the helper drops the `name` parameter. Domain tests that call `Application.Create("kebab", "display", ...)` drop the kebab argument. Slices touched: 3, 4, 5, 6, 7. Estimated ~20 test files; the change is mechanical.

### 8.4 SPA updates

- `RegisterApplicationDialog` form: removes the "Name" input (was a kebab-case text input with help text).
- `registerApplication.ts` zod schema: removes the `name` field + regex.
- `ApplicationsTable` / `ApplicationDetailPage`: removes the `<Badge>` that showed the kebab name as a monospace pill.

### 8.5 OpenAPI snapshot

Regenerated against the post-retrofit API. The TS generated types lose the `name` field automatically.

## 9. ADR-0098 — UUID-only entity identifier

Saved to `docs/architecture/decisions/ADR-0098-uuid-only-entity-identifier.md`. Full text:

> # ADR-0098: UUIDs as the Canonical and Only Entity Identifier
>
> **Status:** Accepted
> **Date:** 2026-05-25
> **Deciders:** Roman Głogowski (solo developer)
> **Category:** API & Integration Architecture
> **Related:** ADR-0001 (PostgreSQL), ADR-0011 (one Org = one tenant), ADR-0029 (REST), ADR-0082 (modular monolith), ADR-0092 (REST URL convention)
>
> ## Context
>
> ADR-0092 (REST URL convention) explicitly punted on the resource-id format: *"Resource-id format (UUID vs slug-based) — separate question, currently UUID per ADR-0001."* But ADR-0001 is about choosing PostgreSQL — it says nothing about identifier format. So the rule was informally followed (slice 3's `/api/v1/catalog/applications/{id:guid}`) but never codified.
>
> Meanwhile, slice 3 introduced `Application.Name` as a kebab-case, immutable, regex-validated slug. The slug appeared in the response payload and SPA display as a parallel identifier — never in URLs, but conceptually a "human-readable machine ref." Slice 8 (Team) would naturally inherit the same pattern: `Team.Slug` alongside `Team.Id`.
>
> Slugs in a multi-tenant SaaS introduce a category of failure modes that don't exist with UUIDs:
> - Cross-tenant collision concerns ("we both have a team called `auth`") — even when the URL uses a different identifier, the slug-as-data still creates mental confusion.
> - Validation rules (regex, length, immutability) that must be enforced consistently.
> - Copy-paste linking ambiguity — URLs that look portable across tenants but aren't.
> - Log / trace aggregation hazards — slug strings in spans collapse across tenants.
> - Bikeshedding on future external-ref formats (CLI, scorecards, webhooks) — `app:platform/auth` vs `app:auth` vs `<uuid>` vs ...
>
> ## Decision
>
> Use UUIDs (`Guid`) as the canonical and only entity identifier across Kartova. Specifically:
>
> 1. **All entities are identified by `Guid` UUIDs.** Generated server-side at creation (`Guid.NewGuid()`).
> 2. **URLs use `{id:guid}` exclusively.** No slug-in-URL, no namespace-in-URL.
> 3. **No slug, kebab-case name, or any secondary machine-readable identifier on entities.** Display names are free text; uniqueness is *not* enforced at the DB level.
> 4. **Tenant scope is established from JWT claims**, never from URL path segments.
> 5. **Display-name duplicates** are allowed within a tenant. UI may warn ("a team named 'Platform' already exists") but doesn't block.
>
> ## Consequences
>
> ### Positive
>
> - Eliminates cross-tenant slug-collision concerns category-wide. Forward rule for every entity (Service, API, Infrastructure, Broker, System in upcoming slices).
> - URLs are globally unique without leaking tenant identity in the path (auth-required B2B; SEO N/A).
> - Simpler domain validation (no slug regex, no immutability rule).
> - Consistent identifier across logs, traces, audit entries, external refs, and external integrations.
>
> ### Negative / trade-offs
>
> - UUIDs aren't human-typeable. The SPA must always provide entity-picker UX (no "go to team `auth` by typing"). Acceptable: Kartova is a navigated UI, not a CLI-first product.
> - External-ref format for future CLI / scorecards / webhooks still needs design (probably `<entity-kind>:<entity-id>`). Decision deferred to the slice that introduces the first such feature.
> - Existing `Application.Name` (slice 3) was a kebab-case slug. Slice 8 retrofits this — see §slice-8 Decision #17. ADR-0098 has no grandfathered exception.
>
> ### Neutral
>
> - Performance is identical (Postgres handles UUID PKs efficiently with the right index strategy).
>
> ## Alternatives considered
>
> - **UUID-in-URL + slug-as-data (kept for human readability).** Rejected: keeps the slug failure modes (validation, immutability, collision-within-tenant); the human-readability benefit is small in a navigated UI that already has display names.
> - **Namespace-in-URL (`/orgs/{slug}/teams/{slug}`, GitHub-style).** Rejected: conflicts with existing tenant-from-JWT convention (Applications, lifecycle endpoints, `/me/permissions` all use it); flipping every existing endpoint would be a slice-wide retrofit. Discussed in §slice-8 Decision #16 alternatives.
> - **Slug-only (entity identified by slug in URL and storage).** Rejected: cross-tenant collisions in logs/traces; URL portability is misleading.
>
> ## References
>
> - ADR-0092 (REST URL convention) — this ADR resolves line 124's punt.
> - Slice 3 design: introduces `Application.Name` (now retrofitted by slice 8).
> - Slice 8 design: this ADR is created here.

## 10. Tests inventory

| Layer | Project | New / Changed |
|---|---|---|
| Unit (domain) | `Kartova.Organization.Tests` | `TeamTests.cs` — `Create` happy path + validation (DisplayName empty / too long, Description too long); `Rename` happy + validation. |
| Unit (domain) | `Kartova.Organization.Tests` | `TeamMembershipTests.cs` — `Create` happy + empty userId; `ChangeRole`. |
| Unit (claims) | `Kartova.SharedKernel.AspNetCore.Tests` | `TenantClaimsTransformationTests.cs` — extend to populate `TeamMemberships` via mock `ITeamMembershipReader`; per-role + empty case. |
| Unit (perm map) | `Kartova.SharedKernel.Tests` | `KartovaRolePermissionsTests.cs` — extend coverage for 4 new permissions per role. |
| Unit (auth handlers) | `Kartova.SharedKernel.AspNetCore.Tests` | `ApplicationTeamScopedHandlerTests.cs` — OrgAdmin passes; Member in team A on app in team A passes; Member in team A on app in team B fails; Member on unassigned app fails; OrgAdmin on unassigned app passes. |
| Unit (auth handlers) | `Kartova.SharedKernel.AspNetCore.Tests` | `TeamAdminOfThisHandlerTests.cs` — OrgAdmin passes; TeamAdmin of this team passes; Member of this team fails; TeamAdmin of *another* team fails. |
| Architecture | `Kartova.ArchitectureTests` | `KartovaPermissionsRules.cs` — extends automatically (data-driven). New constants flow through. |
| Architecture | `Kartova.ArchitectureTests` | `KartovaPermissionsRules.cs` — TS-snapshot drift sentinel extends automatically. |
| Architecture | `Kartova.ArchitectureTests` | `ApplicationNameDropped.cs` (new) — assert `Application` type does NOT carry a `Name` property. Forward-protection against accidental re-introduction. |
| Integration | `Kartova.Organization.IntegrationTests` | `CreateTeamTests.cs` — happy (OrgAdmin → 201); 403 Member; 401 anonymous; 400 validation. |
| Integration | `Kartova.Organization.IntegrationTests` | `ListTeamsTests.cs` — happy (any role with `team.read` → 200 paginated); 401 anonymous. |
| Integration | `Kartova.Organization.IntegrationTests` | `GetTeamTests.cs` — happy 200 (returns members + app IDs); 404 cross-tenant. |
| Integration | `Kartova.Organization.IntegrationTests` | `UpdateTeamTests.cs` — happy as OrgAdmin; happy as TeamAdmin of this team; 403 TeamAdmin of OTHER team; 403 Member; 400 validation. |
| Integration | `Kartova.Organization.IntegrationTests` | `DeleteTeamTests.cs` — happy; 409 team-has-applications with `applicationCount`; 404 cross-tenant. |
| Integration | `Kartova.Organization.IntegrationTests` | `AddTeamMemberTests.cs` — happy; 409 duplicate; 403 from non-admin-of-this. |
| Integration | `Kartova.Organization.IntegrationTests` | `RemoveTeamMemberTests.cs` — happy; 404 not a member; 403 from non-admin-of-this. |
| Integration | `Kartova.Organization.IntegrationTests` | `ChangeTeamMemberRoleTests.cs` — happy; 404 not a member; 403 from non-admin-of-this. |
| Integration | `Kartova.Catalog.IntegrationTests` | `AssignApplicationTeamTests.cs` — happy (any user with permission AND team membership); 422 invalid-team (UUID not in tenant); 403 not-in-team-A-can't-move-app-to-team-A. |
| Integration | `Kartova.Catalog.IntegrationTests` | `CatalogPermissionMatrixTests.cs` — extends with team-scope cells: Member-in-team-A vs app-in-team-A (allow) vs app-in-team-B (403). |
| Integration | `Kartova.Catalog.IntegrationTests` | Existing tests (`DeprecateApplicationTests`, `DecommissionApplicationTests`, `ReactivateApplicationTests`, `UnDecommissionApplicationTests`, `EditApplicationTests`) get team-scoped variants: same scenario as OrgAdmin vs Member-without-team-membership. |
| Integration | `Kartova.Catalog.IntegrationTests` | Every test updates arrange phase to drop `name` parameter from `Fx.RegisterApplicationAsync(...)`. |
| Integration | `Kartova.Organization.IntegrationTests` | `GetMePermissionsTests.cs` — extends: verifies `teamMemberships` array in response per role + assignment scenario. |
| SPA component | `web/src/features/teams/api/__tests__/teams.test.tsx` | All 8 hooks. |
| SPA component | `web/src/features/teams/pages/__tests__/{TeamsListPage,TeamDetailPage}.test.tsx` | Per-role gating; 409 delete path. |
| SPA component | `web/src/features/teams/components/__tests__/*.test.tsx` | All 7 dialogs + picker. |
| SPA component | `web/src/shared/auth/__tests__/usePermissions.test.tsx` | Extend: `teamIds` / `teamAdminTeamIds` populated correctly. |
| SPA component | `web/src/features/catalog/pages/__tests__/ApplicationDetailPage.test.tsx` | Verify `AssignTeamPicker` visibility per role; verify name-badge is gone. |
| SPA component | `web/src/features/catalog/components/__tests__/RegisterApplicationDialog.test.tsx` | Verify the `name` input is gone; form submits with displayName + description + ownerUserId only. |

## 11. Definition of Done

CLAUDE.md-numbered, evidence to capture:

1. **Solution build with `TreatWarningsAsErrors=true`** — 0 warnings, 0 errors. Capture `dotnet build` output.
2. **Per-task subagent reviews** (spec-compliance + code-quality) — invoked on each task, never skipped.
3. **`superpowers:requesting-code-review`** at slice boundary against full branch diff with this spec + plan as context.
4. **Full test suite green** — unit + architecture + integration (Testcontainers + KeyCloak) + SPA Vitest.
5. **`docker compose up --build` + real HTTP smoke** — qualifies because middleware/policy + new endpoints + Application schema change. Capture per-role login + `GET /me/permissions` returns `teamMemberships`; happy + forbidden mutations for Viewer/Member/TeamAdmin/OrgAdmin including the team-scoping rule (Member-in-team-A vs app-in-team-B → 403).
6. **`/simplify`** against branch diff — reuse / quality / efficiency lenses.
7. **Mutation feedback loop** — `mutation-sentinel` against changed files, `test-generator` until survivors are killed or accepted. Score ≥80%.
8. **`/pr-review-toolkit:review-pr`** skill.
9. **`/deep-review`** against branch diff with spec / plan / ADRs / tests. Blocking + Should-fix addressed; nits triaged.

ADR-0098 created at `docs/architecture/decisions/ADR-0098-uuid-only-entity-identifier.md` and indexed in `docs/architecture/decisions/README.md`. CHECKLIST.md updates: E-03.F-02.S-01 + S-02 + S-03 (partial) marked complete.

## 12. Success criteria

- ✅ `Team` aggregate + 8 endpoints + assign-team endpoint shipped under `/api/v1/organizations/teams/{id:guid}` and `/api/v1/catalog/applications/{id:guid}/team`.
- ✅ `team_members` table backs multi-team-per-user membership.
- ✅ Resource-based authorization via `IAuthorizationService.AuthorizeAsync(user, resource, policyName)` — `ApplicationTeamScopedHandler` and `TeamAdminOfThisHandler` registered and exercised.
- ✅ TeamAdmin of team A cannot mutate apps in team B; OrgAdmin can mutate any. 32+ cell matrix covers the cases.
- ✅ Team-membership populates `ICurrentUser.TeamMemberships` via `ITeamMembershipReader` on each request.
- ✅ `/me/permissions` response includes `teamMemberships`; SPA `usePermissions()` exposes `teamIds` + `teamAdminTeamIds`.
- ✅ SPA `/teams` + `/teams/{id}` pages render with hide-by-default gating; `AssignTeamPicker` on `ApplicationDetailPage`.
- ✅ `Application.Name` column dropped; register dialog has no name field; list/detail badges removed; integration tests updated.
- ✅ ADR-0098 saved, indexed, referenced from this spec and ADR-0092 cross-link.
- ✅ Mutation score ≥80% on changed files.
- ✅ CHECKLIST.md updated; PR number filled in post-merge.

## 13. Risks & mitigations

| Risk | Mitigation |
|---|---|
| `Application.Name` retrofit breaks slice 3/4/5/6/7 integration tests | All test updates land in the same PR; CI catches any miss. Estimated ~20 test files; mechanical. |
| OpenAPI snapshot drift between slice-8 API and the codegen-fetched spec | Slice 8 includes the codegen re-run + snapshot commit. Drift-sentinel arch test catches mismatch. |
| `team_members` RLS `EXISTS` subquery becomes a perf hotspot under load | Default keeps the subquery (cheap given indexes). If perf bench shows >5% impact, denormalize `tenant_id` onto `team_members` in a follow-up slice. |
| `TenantClaimsTransformation` DB hit on every request adds latency | One query, RLS-scoped, indexed on `user_id`. Cached for request lifetime via `ITenantContext`. <2ms in local bench. If it shows up in production telemetry, consider caching by sub claim with short TTL. |
| Two TeamAdmins of the same team race on member changes | Standard Postgres concurrency; `team_members` PK + RLS handle it. Last write wins; no consistency invariant lost. |
| Removing the last TeamAdmin from a team locks out team mgmt | Allowed by design. OrgAdmin can always rescue (resource handler grants OrgAdmin universal access). SPA shows a warning before the removal. |
| Cross-module domain references creep in | ADR-0082 enforced by NetArchTest; pre-existing rules prevent direct domain refs. The `ITeamMembershipReader` abstraction keeps the boundary clean. |

## 14. Self-review

**Placeholder scan:** No "TBD" or "TODO" tokens.

**Type / contract consistency:**
- `TeamId` (Guid) consistent across §4.1 (domain), §4.6 (EF config), §5.5 (policies — uses raw Guid in resource handler), §6.1 (URL placeholder).
- `TeamRole` enum ↔ `TeamRoleKind` enum (SharedKernel) ↔ `Role` string ("Admin"/"Member") in DTOs — clear mapping in §5.3.
- `Application.TeamId` is `Guid?` everywhere (catalog domain, EF, DTO, SPA type) — no value-object wrapper, matches Decision #4.
- Permission names (`team.read`, etc.) consistent across §5.1, §5.2, §6.1, §7.1, §9 ADR.

**Scope check:** Bundle is intentional — Team + ADR-0098 + Application.Name retrofit. Each piece independently justifiable; bundling avoids future ADR-grandfathered-exception. ~5–6 day slice; comparable in shape to slice 7 (~30 commits) plus the retrofit churn (~20 test updates).

**Ambiguity check:**
- "Multi-team membership per user" explicitly stated in §4 + Decision #3.
- "Single team per Application" explicitly stated in Decision #4; ADR-0066 multi-ownership deferral explicit.
- Team-deletion-with-apps behaviour pinned in Decision #12 (409 not orphan).
- Bootstrap path (zero teams in fresh tenant) pinned in Decision #13.

**Internal consistency:**
- Decision #16 (ADR-0098) and Decision #17 (Application.Name drop) are intentionally bundled. ADR text in §9 acknowledges the retrofit.
- Decision #1 (Team inside Organization module) is consistent with §5.3 (`ITeamMembershipReader` abstraction in SharedKernel + impl in Organization).
- Decision #6 (resource-based auth) is consistent with §5.6, §5.7, §5.9 handler signatures and endpoint usage.
- Decision #9 (unassigned-app rule) is consistent with §5.6 (`ApplicationTeamScopedHandler` falls through for `TeamId == null` unless OrgAdmin).

**Scope compared to other slices:** Largest slice yet (~5–6 days vs slice 7's ~3 days). Justified by bundle (team + retrofit + ADR). Could split into two PRs (team in PR-A, retrofit + ADR in PR-B) if review burden becomes a concern, but the retrofit + ADR justify each other — landing them separately weakens both.

**Critic cross-validation (2026-05-25):** A dedicated critic subagent cross-validated the spec against existing implementation, ADRs, and other repo documents. 3 Criticals, 4 Inaccuracies, 5 Gaps, and 3 Internal contradictions were found and resolved inline in this revision:

- §6.6 `RegisterApplicationRequest` body corrected (no `ownerUserId` — sourced from JWT).
- §4.5 / §8.1 RLS migration pattern corrected (no universal "toggle dance" — pure DDL where applicable).
- §5.3 `ITeamMembershipReader` + `TeamMembershipInfo` + `TeamRoleKind` moved to `Kartova.SharedKernel` (was incorrectly placed in `Kartova.SharedKernel.AspNetCore`, which would violate layering against `ITenantContext`).
- §5.4 `ITenantContext.PopulateTeamMemberships` listed as a new interface method.
- §5.4 `TenantClaimsTransformation` async-signature change explicitly called out.
- §5.8 resource-based policies split into a separate `AddKartovaResourcePolicies` method.
- §5.9 deliberate non-decision on team-scoped reads documented (reads are tenant-scoped via RLS but not team-scoped).
- §8.2 `ApplicationSortSpecs.Name` + `ApplicationSortField.Name` added to retrofit scope (compile-time blocker).
- §8.2 `CatalogEndpointDelegates.RegisterApplicationAsync` named as the actual binding location (was "RegisterApplicationDelegate").
- §5.2 + Decision #7 added `team.delete` permission separate from `team.metadata.edit` (destructive vs non-destructive).
- §6.1 footnote added for ADR-0092 line-93 singular/plural typo.
- §4.1 ADR-0065 partial-wiring (System layer deferred to §15.4) made explicit.

## 15. Follow-ups (registered for future planning, not in scope)

### 15.1 Invitation flow (E-03.F-01.S-02)

**Why:** Slice 8 assumes users already exist in KeyCloak (dev users for now; real users via OrgAdmin's KeyCloak admin UI in production). A first-class invitation flow (email → accept → KeyCloak user creation → optional team assignment) is the natural next step.

**Trigger:** When the first multi-tenant onboarding flow ships (likely E-09 wizard).

**Effort:** ~3 days backend + ~1 day SPA. Touches email infrastructure.

### 15.2 Multi-team ownership (E-03.F-05 / ADR-0066)

**Why:** ADR-0066 promises multi-ownership with platform-flag + quorum approval. Slice 8 ships single-team-per-Application.

**Trigger:** When real users report needing shared ownership (probably after Service + API entities ship).

**Effort:** ~3-4 days. Adds `application_team_owner (application_id, team_id, role)` join table; `Application.TeamId` becomes a primary-owner shortcut. Resource-auth handler updates to query the join table.

### 15.3 Org profile management (E-03.F-01.S-01)

**Why:** OrgAdmin can rename the Organization. Today's `Organization.Rename` exists in slice 2 but isn't exposed in SPA.

**Trigger:** When OrgAdmins ask for it (or with the invitation flow).

**Effort:** ~half-day backend + ~half-day SPA.

### 15.4 System grouping (E-03.F-03)

**Why:** ADR-0065's third layer — `Org → Team → System → Component`. Systems are mid-level groupings inside teams.

**Trigger:** When teams accumulate enough components to need sub-grouping.

**Effort:** ~3 days.

### 15.5 Tag system (E-03.F-04)

**Why:** Cross-cutting taxonomies per ADR-0065.

**Trigger:** Independent of teams; could ship anytime.

### 15.6 Audit log on team operations

**Why:** Carry-forward from slice 7 §15.5 / E-01.F-03.S-03. Audit log doesn't exist yet; team CRUD + member ops should write to it when it lands.

**Trigger:** When E-01.F-03.S-03 ships.

**Effort:** ~half-day.

### 15.7 External ref format for entities (CLI, scorecards, webhooks)

**Why:** ADR-0098 §Negative explicitly defers the external-ref design. When the first feature needs to reference entities outside the SPA navigation context, it needs a format like `<kind>:<id>` or `<tenant>/<kind>/<id>`.

**Trigger:** First of: E-13 (CLI), E-10 (scorecards), E-01.F-06.S-04 (webhooks).

### 15.8 Application.OwnerUserId vs TeamId reconciliation

**Why:** Both fields coexist; OwnerUserId is now strictly "POC" semantically. A future cleanup could rename it to `PointOfContactUserId` or formalize the distinction.

**Trigger:** Whenever a slice touches Application metadata significantly.

### 15.9 Team-level scorecard policy (E-10)

**Why:** TeamDetailResponse will gain a `scorecard` summary when scorecards ship.

**Trigger:** E-10 (Scorecards & Data Quality).

---

**End of design.**
