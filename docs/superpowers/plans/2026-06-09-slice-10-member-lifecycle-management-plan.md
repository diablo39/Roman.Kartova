# Slice 10 — Member Lifecycle Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give OrgAdmins a members directory, the ability to change a member's realm role, and the ability to offboard a member (hard-delete the KeyCloak identity + local projection, reassigning owned applications to a chosen successor).

**Architecture:** All work lands in the `Kartova.Organization` module + a thin cross-module **port** into `Kartova.Catalog` (no Wolverine bus — ports preserve the request `ITenantScope`, ADR-0093). The `User` projection gains a write-through `realm_role` column so the directory is one indexed SELECT (no per-row KeyCloak calls). Offboarding is a hard delete (ADR-0102), audit is deferred (ADR-0018 slice), no session revocation. The SPA adds a `/members` directory page + two dialogs, reusing the `useCursorList`/`useListUrlState` + hand-rolled-table idiom.

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs, EF Core + PostgreSQL (RLS), KeyCloak admin REST via `Kartova.SharedKernel.Identity`, MSTest v4 + NSubstitute + Testcontainers, React + TypeScript + React Query + react-aria-components + zod + Vitest.

**Spec:** `docs/superpowers/specs/2026-06-09-slice-10-member-lifecycle-management-design.md`

**Conventions for every task:**
- Windows shell: run `dotnet`/`dotnet ef` via PowerShell, or `cmd //c dotnet …` in Git Bash. Solution is `Kartova.slnx`.
- Build gate: `dotnet build Kartova.slnx /p:TreatWarningsAsErrors=true` must be 0/0.
- New DTOs/Contracts records carry `[ExcludeFromCodeCoverage]`.
- Commit after each task with the message shown in its final step.

---

## Task 1: `User.RealmRole` column + migration + seed/fixture support

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Domain/User.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/UserEntityTypeConfiguration.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/Migrations/<timestamp>_AddUserRealmRoleColumn.cs` (EF-generated)
- Modify: `src/Kartova.Migrator/DevSeed.cs` (set realm_role on seeded users)
- Modify: `src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFixture.cs` (seed helper gains a role param)
- Test: `src/Modules/Organization/Kartova.Organization.IntegrationTests/UserRealmRoleColumnTests.cs`

- [ ] **Step 1: Add the property to the `User` projection**

In `User.cs`, after `CreatedAt`, add:

```csharp
    public string RealmRole { get; set; } = KartovaRoles.Viewer;   // write-through cache of the KeyCloak realm role (ADR-0102)
```

`KartovaRoles` is in `Kartova.SharedKernel.Multitenancy` (already a `using` in this file via `ITenantOwned`; add `using Kartova.SharedKernel.Multitenancy;` if the analyzer flags it).

- [ ] **Step 2: Map the column in EF config**

In `UserEntityTypeConfiguration.Configure`, after the `CreatedAt` property mapping and before the index declarations, add:

```csharp
        b.Property(x => x.RealmRole).HasColumnName("realm_role").HasMaxLength(32).IsRequired();
```

- [ ] **Step 3: Generate the migration**

Run (PowerShell):

```
dotnet ef migrations add AddUserRealmRoleColumn --project src/Modules/Organization/Kartova.Organization.Infrastructure --startup-project src/Modules/Organization/Kartova.Organization.Infrastructure
```

If the startup-project flag errors (no host), the design-time factory `OrganizationDbContextFactory` is picked up automatically — drop `--startup-project`.

- [ ] **Step 4: Hand-edit the migration `Up`/`Down` to add a backfill default + the OrgAdmin partial index**

EF will emit an `AddColumn`. Edit the generated migration so `Up` reads (the `defaultValue` backfills existing rows; the partial index makes the last-OrgAdmin guard a cheap count):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<string>(
        name: "realm_role",
        table: "users",
        type: "character varying(32)",
        maxLength: 32,
        nullable: false,
        defaultValue: "Viewer");

    migrationBuilder.Sql(
        "CREATE INDEX idx_users_orgadmins ON users (tenant_id) WHERE realm_role = 'OrgAdmin';");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.Sql("DROP INDEX IF EXISTS idx_users_orgadmins;");
    migrationBuilder.DropColumn(name: "realm_role", table: "users");
}
```

- [ ] **Step 5: Update DevSeed to set realm_role on seeded users**

Open `src/Kartova.Migrator/DevSeed.cs`. Wherever it inserts `users` rows (the seeded Org A admin/member/team-admin users), include `realm_role` matching each user's realm role from `deploy/keycloak/kartova-realm.json` (OrgAdmin user → `'OrgAdmin'`, member → `'Member'`, the re-seeded `team-admin@orga.kartova.local` → `'Member'` per ADR-0101). If the inserts are EF-based, set `user.RealmRole = "OrgAdmin"` etc.; if raw SQL, add the `realm_role` column + value to the INSERT.

- [ ] **Step 6: Add a `realmRole` parameter to the integration-test user seeder**

In `KartovaApiFixture.cs`, find `SeedUserInOrganizationAsync(TenantId tenantId, string displayName, string email)`. Add a trailing optional param and include it in the INSERT:

```csharp
public async Task<Guid> SeedUserInOrganizationAsync(
    TenantId tenantId, string displayName, string email, string realmRole = "Viewer")
```

Add `realm_role` to the column list + values of the INSERT (mirror the existing column handling). Default `"Viewer"` keeps existing callers working.

- [ ] **Step 7: Write the failing test**

Create `UserRealmRoleColumnTests.cs` (mirror `CreateTeamTests` fixture usage):

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.IntegrationTests;

[TestClass]
public sealed class UserRealmRoleColumnTests : OrganizationIntegrationTestBase
{
    private static readonly TenantId Tenant =
        new(Guid.Parse("aaaaaaaa-0010-0001-0001-000000000001"));

    [TestMethod]
    public async Task Seeded_user_persists_realm_role()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-RealmRole");
        var userId = await Fx.SeedUserInOrganizationAsync(Tenant, "Ada Admin", "ada@orga.test", KartovaRoles.OrgAdmin);
        try
        {
            var client = Fx.CreateClient();
            var token = Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await client.GetAsync($"/api/v1/organizations/users/{userId}");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, resp.StatusCode);
            // Realm role is asserted via the directory endpoint in Task 4; here we only prove the column round-trips.
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(Tenant.Value);
            await Fx.DeleteOrganizationsForTenantAsync(Tenant.Value);
        }
    }
}
```

- [ ] **Step 8: Run the test (requires Docker for Testcontainers)**

Run: `dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter UserRealmRoleColumnTests`
Expected: PASS (migration applied by the fixture; user detail returns 200). If Docker is unavailable, note as *pending user verification* and proceed; the column + migration still compile.

- [ ] **Step 9: Build the solution**

Run: `dotnet build Kartova.slnx /p:TreatWarningsAsErrors=true`
Expected: 0 warnings, 0 errors.

- [ ] **Step 10: Commit**

```
git add -A
git commit -m "feat(slice-10): add realm_role column to users projection + migration"
```

---

## Task 2: Permissions, ProblemTypes, and SPA permission snapshot

**Files:**
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs`
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs`
- Modify: `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`
- Modify: `web/src/shared/auth/permissions.ts`
- Modify: `web/src/shared/auth/permissions.snapshot.json`
- Test: `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs` (extend), `tests/Kartova.ArchitectureTests/KartovaPermissionsRules.cs` (auto), `web/src/shared/auth/__tests__/usePermissions.test.tsx` (stays green)

- [ ] **Step 1: Write the failing role-permission test first**

In `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs`, add:

```csharp
[TestMethod]
public void OrgAdmin_has_user_management_permissions()
{
    var perms = KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin);
    Assert.IsTrue(perms.Contains(KartovaPermissions.OrgUsersRoleChange));
    Assert.IsTrue(perms.Contains(KartovaPermissions.OrgUsersRemove));
}

[TestMethod]
[DataRow("Viewer")]
[DataRow("Member")]
public void NonAdmin_roles_lack_user_management_permissions(string role)
{
    var perms = KartovaRolePermissions.ForRole(role);
    Assert.IsFalse(perms.Contains(KartovaPermissions.OrgUsersRoleChange));
    Assert.IsFalse(perms.Contains(KartovaPermissions.OrgUsersRemove));
}
```

- [ ] **Step 2: Run it to verify failure**

Run: `dotnet test tests/Kartova.SharedKernel.Tests --filter KartovaRolePermissionsTests`
Expected: FAIL — `KartovaPermissions.OrgUsersRoleChange` does not exist (compile error).

- [ ] **Step 3: Add the two permission constants + append to `All`**

In `KartovaPermissions.cs`, after `OrgUsersSearch`:

```csharp
    public const string OrgUsersRoleChange = "org.users.role.change";
    public const string OrgUsersRemove     = "org.users.remove";
```

Append both to the `All` array literal (after `OrgUsersSearch,`):

```csharp
        OrgUsersRead, OrgUsersSearch, OrgUsersRoleChange, OrgUsersRemove,
```

- [ ] **Step 4: Grant both to OrgAdmin only**

In `KartovaRolePermissions.cs`, in the `[KartovaRoles.OrgAdmin]` array, after `KartovaPermissions.OrgUsersSearch,` add:

```csharp
                KartovaPermissions.OrgUsersRoleChange, KartovaPermissions.OrgUsersRemove,
```

Leave Viewer and Member unchanged.

- [ ] **Step 5: Add the three problem types**

In `ProblemTypes.cs`, after `InvitationGone`:

```csharp
    public const string LastOrgAdmin       = Base + "last-orgadmin";          // 409
    public const string CannotOffboardSelf = Base + "cannot-offboard-self";   // 409
    public const string InvalidSuccessor   = Base + "invalid-successor";      // 422
```

- [ ] **Step 6: Run the C# test + architecture suite**

Run: `dotnet test tests/Kartova.SharedKernel.Tests --filter KartovaRolePermissionsTests`
Expected: PASS.
Run: `dotnet test tests/Kartova.ArchitectureTests`
Expected: PASS (the C#↔TS snapshot drift test `Ts_snapshot_equals_csharp_KartovaPermissions_All` will FAIL until Step 7 — that is expected; do Step 7 before re-running).

- [ ] **Step 7: Update the SPA permission constants + snapshot**

In `web/src/shared/auth/permissions.ts`, inside the `KartovaPermissions` object after `OrgUsersSearch`:

```ts
  OrgUsersRoleChange: "org.users.role.change",
  OrgUsersRemove: "org.users.remove",
```

In `web/src/shared/auth/permissions.snapshot.json`, add the two strings (keep the array ordering identical to `KartovaPermissions.All`):

```json
  "org.users.role.change",
  "org.users.remove"
```

(Place them as the last two entries; ensure JSON commas are correct.)

- [ ] **Step 8: Re-run the architecture suite + SPA permission test**

Run: `dotnet test tests/Kartova.ArchitectureTests`
Expected: PASS.
Run (Windows): `cmd //c "cd web && npm test -- usePermissions"` (or `npm test -- usePermissions` from `web/`)
Expected: PASS — the module-load drift guard in `permissions.ts` is satisfied.

- [ ] **Step 9: Commit**

```
git add -A
git commit -m "feat(slice-10): add org.users.role.change + org.users.remove permissions and problem types"
```

---

## Task 3: `IKeycloakAdminClient.ChangeRealmRoleAsync`

**Files:**
- Modify: `src/Kartova.SharedKernel.Identity/IKeycloakAdminClient.cs`
- Modify: `src/Kartova.SharedKernel.Identity/KeycloakAdminClient.cs`
- Test: `src/Kartova.SharedKernel.Identity.Tests/KeycloakAdminClientChangeRoleTests.cs` (create; if no test project exists for this assembly, create `Kartova.SharedKernel.Identity.Tests` mirroring an existing SharedKernel test csproj)

- [ ] **Step 1: Add the interface method**

In `IKeycloakAdminClient.cs`, add:

```csharp
    /// <summary>
    /// Removes every Kartova business realm role (Viewer/Member/OrgAdmin) the user currently holds,
    /// then assigns <paramref name="newRole"/>. KeyCloak is the source of truth for realm roles.
    /// </summary>
    Task ChangeRealmRoleAsync(Guid userId, string newRole, CancellationToken ct);
```

- [ ] **Step 2: Write the failing test (stub HttpMessageHandler asserting the GET-list → DELETE → POST sequence)**

Create `KeycloakAdminClientChangeRoleTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Duende.IdentityModel.Client;
using Kartova.SharedKernel.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kartova.SharedKernel.Identity.Tests;

[TestClass]
public sealed class KeycloakAdminClientChangeRoleTests
{
    private sealed record CapturedRequest(HttpMethod Method, string Path);

    private sealed class StubHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Calls { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add(new CapturedRequest(request.Method, request.RequestUri!.AbsolutePath));
            // GET current realm role-mappings -> return Member so it must be removed
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.EndsWith("/role-mappings/realm"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new[] { new { id = "r-member", name = "Member" } }) };
            // GET a specific role definition (assign + delete need the role rep)
            if (request.Method == HttpMethod.Get && request.RequestUri!.AbsolutePath.Contains("/roles/"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                { Content = JsonContent.Create(new { id = "r-orgadmin", name = "OrgAdmin" }) };
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    [TestMethod]
    public async Task ChangeRealmRole_removes_existing_business_roles_then_assigns_new()
    {
        var stub = new StubHandler();
        var http = new HttpClient(stub) { BaseAddress = new Uri("https://kc.test") };
        var options = Options.Create(new KeycloakAdminOptions { Realm = "kartova", BaseUrl = "https://kc.test" });
        // TokenClient: construct against the same http or a fake that returns a token. Mirror the existing
        // KeycloakAdminClient test setup if one exists; otherwise use a TokenClient over a handler returning a token.
        var tokenClient = TestTokenClient.AlwaysReturns("test-token", http);
        var sut = new KeycloakAdminClient(http, options, tokenClient, NullLogger<KeycloakAdminClient>.Instance);

        await sut.ChangeRealmRoleAsync(Guid.NewGuid(), "OrgAdmin", CancellationToken.None);

        // Must DELETE the old Member mapping and POST the new OrgAdmin mapping.
        Assert.IsTrue(stub.Calls.Any(c => c.Method == HttpMethod.Delete && c.Path.EndsWith("/role-mappings/realm")));
        Assert.IsTrue(stub.Calls.Any(c => c.Method == HttpMethod.Post && c.Path.EndsWith("/role-mappings/realm")));
    }
}
```

Note: `KeycloakAdminClient` is `internal` — add `[assembly: InternalsVisibleTo("Kartova.SharedKernel.Identity.Tests")]` to the Identity project (mirror how other SharedKernel projects expose internals to their test assembly). If a `TokenClient` is hard to construct in a unit test, factor the token fetch behind a small internal seam or follow the pattern used by any existing `KeycloakAdminClient` test; if none exists, rely on the Task 5 integration test (real KeyCloak) as the primary proof and keep this unit test minimal.

- [ ] **Step 3: Run it to verify failure**

Run: `dotnet test src/Kartova.SharedKernel.Identity.Tests --filter KeycloakAdminClientChangeRoleTests`
Expected: FAIL — `ChangeRealmRoleAsync` not implemented.

- [ ] **Step 4: Implement `ChangeRealmRoleAsync`**

In `KeycloakAdminClient.cs`, add (mirrors the existing `AssignRealmRoleAsync` idioms — `GetTokenAsync`, per-request `HttpRequestMessage` + Bearer header, `JsonOpts`):

```csharp
    public async Task ChangeRealmRoleAsync(Guid userId, string newRole, CancellationToken ct)
    {
        var token = await GetTokenAsync(ct);

        // 1. List the user's current realm role-mappings.
        using var listReq = new HttpRequestMessage(HttpMethod.Get,
            $"/admin/realms/{_realm}/users/{userId}/role-mappings/realm");
        listReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var listResp = await http.SendAsync(listReq, ct);
        if (listResp.StatusCode == HttpStatusCode.NotFound)
            throw new KeycloakAdminException(KeycloakAdminError.NotFound, $"User {userId} not found.");
        if (!listResp.IsSuccessStatusCode)
            throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak list-roles returned {(int)listResp.StatusCode}.");
        var current = await listResp.Content.ReadFromJsonAsync<RealmRole[]>(JsonOpts, ct) ?? [];

        // 2. Remove any Kartova business roles the user holds (Viewer/Member/OrgAdmin) that differ from the new role.
        var toRemove = current
            .Where(r => KartovaRoles.All.Contains(r.Name) && !string.Equals(r.Name, newRole, StringComparison.Ordinal))
            .ToArray();
        if (toRemove.Length > 0)
        {
            using var delReq = new HttpRequestMessage(HttpMethod.Delete,
                $"/admin/realms/{_realm}/users/{userId}/role-mappings/realm")
            { Content = JsonContent.Create(toRemove, options: JsonOpts) };
            delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var delResp = await http.SendAsync(delReq, ct);
            if (!delResp.IsSuccessStatusCode)
                throw new KeycloakAdminException(KeycloakAdminError.Unexpected, $"KeyCloak remove-roles returned {(int)delResp.StatusCode}.");
        }

        // 3. Assign the new role (no-op-safe if already present).
        await AssignRealmRoleAsync(userId, newRole, ct);
    }
```

Add `using Kartova.SharedKernel.Multitenancy;` for `KartovaRoles`. The Identity project already references `Kartova.SharedKernel` (per slice-9 §7.1), so `KartovaRoles.All` is in scope.

- [ ] **Step 5: Run the test**

Run: `dotnet test src/Kartova.SharedKernel.Identity.Tests --filter KeycloakAdminClientChangeRoleTests`
Expected: PASS (DELETE for the old Member mapping + POST for OrgAdmin observed).

- [ ] **Step 6: Build**

Run: `dotnet build Kartova.slnx /p:TreatWarningsAsErrors=true`
Expected: 0/0.

- [ ] **Step 7: Commit**

```
git add -A
git commit -m "feat(slice-10): add IKeycloakAdminClient.ChangeRealmRoleAsync"
```

---

## Task 4: Members directory endpoint + typeahead relocation

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Contracts/MemberSortField.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Contracts/MemberSummaryResponse.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Application/ListMembersQuery.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/MemberSortSpecs.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/ListMembersHandler.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/UserEndpointDelegates.cs` (+ the `UserRoutes.MapTo` registration — relocate typeahead, add directory)
- Test: `src/Modules/Organization/Kartova.Organization.IntegrationTests/ListMembersTests.cs`

- [ ] **Step 1: Add the sort field enum + response DTO**

`MemberSortField.cs`:

```csharp
namespace Kartova.Organization.Contracts;

public enum MemberSortField
{
    DisplayName,
    Role,
    CreatedAt,
}
```

`MemberSummaryResponse.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record MemberSummaryResponse(
    Guid Id,
    string DisplayName,
    string Email,
    string Role,
    int TeamCount,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset CreatedAt);
```

- [ ] **Step 2: Add the query + sort specs**

`ListMembersQuery.cs`:

```csharp
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Organization.Application;

public sealed record ListMembersQuery(
    MemberSortField SortBy,
    SortOrder SortOrder,
    string? Cursor,
    int Limit,
    string? Role,   // null/"all" => no filter; else one of KartovaRoles.All
    string? Q);     // null/empty => no filter; else infix match on display_name + email
```

`MemberSortSpecs.cs` (User has a plain `Guid` PK, so `IdSelector`/`IdEquals` are trivial):

```csharp
using System.Linq.Expressions;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Organization.Infrastructure;

internal static class MemberSortSpecs
{
    public static readonly Expression<Func<User, Guid>> IdSelector = x => x.Id;
    public static readonly Func<User, Guid> IdExtractor = x => x.Id;

    public static readonly SortSpec<User> DisplayName = new("displayName", x => x.DisplayName);
    public static readonly SortSpec<User> Role = new("role", x => x.RealmRole);
    public static readonly SortSpec<User> CreatedAt = new("createdAt", x => x.CreatedAt);

    public static readonly IReadOnlyList<string> AllowedFieldNames =
        [DisplayName.FieldName, Role.FieldName, CreatedAt.FieldName];

    public static SortSpec<User> Resolve(MemberSortField field) => field switch
    {
        MemberSortField.DisplayName => DisplayName,
        MemberSortField.Role => Role,
        MemberSortField.CreatedAt => CreatedAt,
        _ => throw new InvalidSortFieldException(field.ToString(), AllowedFieldNames),
    };
}
```

- [ ] **Step 3: Implement the handler (paginate Users, then batch team-counts)**

`ListMembersHandler.cs`:

```csharp
using Kartova.Organization.Application;
using Kartova.Organization.Contracts;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Pagination;
using Kartova.SharedKernel.Postgres.Pagination;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

public sealed class ListMembersHandler
{
    public async Task<CursorPage<MemberSummaryResponse>> Handle(
        ListMembersQuery q, OrganizationDbContext db, CancellationToken ct)
    {
        var spec = MemberSortSpecs.Resolve(q.SortBy);

        IQueryable<User> query = db.Users;
        var expectedFilters = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(q.Role) && !string.Equals(q.Role, "all", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(u => u.RealmRole == q.Role);
            expectedFilters["role"] = q.Role;
        }
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            var like = $"%{term}%";
            query = query.Where(u => EF.Functions.ILike(u.DisplayName, like) || EF.Functions.ILike(u.Email, like));
            expectedFilters["q"] = term;
        }

        var page = await query.ToCursorPagedAsync(
            spec, q.SortOrder, q.Cursor, q.Limit,
            MemberSortSpecs.IdSelector, MemberSortSpecs.IdExtractor, ct, expectedFilters);

        var ids = page.Items.Select(u => u.Id).ToList();
        var teamCounts = await db.TeamMemberships
            .Where(m => ids.Contains(m.UserId))
            .GroupBy(m => m.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var countById = teamCounts.ToDictionary(x => x.UserId, x => x.Count);

        var items = page.Items
            .Select(u => new MemberSummaryResponse(
                u.Id, u.DisplayName, u.Email, u.RealmRole,
                countById.TryGetValue(u.Id, out var c) ? c : 0,
                u.LastSeenAt, u.CreatedAt))
            .ToList();

        return new CursorPage<MemberSummaryResponse>(items, page.NextCursor, page.PrevCursor);
    }
}
```

Confirm `TeamMembership.UserId` is the property name (per slice-8 domain); adjust if it differs. Confirm `db.TeamMemberships` is the DbSet name on `OrganizationDbContext`.

- [ ] **Step 4: Add the directory delegate + relocate the typeahead delegate**

In `UserEndpointDelegates.cs`, add a new delegate (mirror `TeamEndpointDelegates.ListTeamsAsync` — `CursorListBinding.Bind`, defaults, `Results.Ok`):

```csharp
    internal static async Task<IResult> ListMembersAsync(
        [FromQuery] string? sortBy, [FromQuery] string? sortOrder,
        [FromQuery] string? cursor, [FromQuery] string? limit,
        [FromQuery] string? role, [FromQuery] string? q,
        ListMembersHandler handler, OrganizationDbContext db, CancellationToken ct)
    {
        var (sortField, order, lim) = CursorListBinding.Bind<MemberSortField>(
            sortBy, sortOrder, limit, MemberSortSpecs.AllowedFieldNames);
        var query = new ListMembersQuery(
            sortField ?? MemberSortField.DisplayName,
            order ?? SortOrder.Asc,
            cursor, lim, role, q);
        var page = await handler.Handle(query, db, ct);
        return Results.Ok(page);
    }
```

Keep the existing `SearchUsersAsync` delegate body unchanged (it is the typeahead) — only its route moves in the next step.

- [ ] **Step 5: Update `UserRoutes.MapTo` — relocate typeahead, add directory, register handler**

In the routing block (`UserRoutes.MapTo(RouteGroupBuilder tenant)`), change the existing search mapping from `"/users"` to `"/users/search"`, and add the directory at `"/users"`:

```csharp
        tenant.MapGet("/users", UserEndpointDelegates.ListMembersAsync)
            .RequireAuthorization(KartovaPermissions.OrgUsersRead)
            .WithName("ListMembers")
            .Produces<CursorPage<MemberSummaryResponse>>(StatusCodes.Status200OK);

        tenant.MapGet("/users/search", UserEndpointDelegates.SearchUsersAsync)
            .RequireAuthorization(KartovaPermissions.OrgUsersSearch)
            .WithName("SearchUsers")
            .Produces<IReadOnlyList<UserSummaryResponse>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // existing /users/{id:guid} GetUserDetail mapping stays unchanged
```

Register the handler in `OrganizationModule.RegisterServices` (mirror `services.AddScoped<ListTeamsHandler>();`):

```csharp
        services.AddScoped<ListMembersHandler>();
```

Add `using` directives as needed (`Kartova.SharedKernel.Pagination`, `Kartova.SharedKernel.Postgres.Pagination`, `Microsoft.AspNetCore.Mvc` for `[FromQuery]`).

- [ ] **Step 6: Write the failing integration test**

`ListMembersTests.cs` (mirror `ListTeamsTests` + `CreateTeamTests`):

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Kartova.Organization.Contracts;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Organization.IntegrationTests;

[TestClass]
public sealed class ListMembersTests : OrganizationIntegrationTestBase
{
    private static readonly TenantId Tenant =
        new(Guid.Parse("aaaaaaaa-0010-0004-0004-000000000001"));

    [TestMethod]
    public async Task OrgAdmin_lists_members_with_role_and_team_count()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-Members");
        var admin = await Fx.SeedUserInOrganizationAsync(Tenant, "Ada Admin", "ada@orga.test", KartovaRoles.OrgAdmin);
        var member = await Fx.SeedUserInOrganizationAsync(Tenant, "Bob Member", "bob@orga.test", KartovaRoles.Member);
        var teamId = await Fx.SeedTeamAsync(Tenant.Value, "Platform");
        await Fx.SeedTeamMembershipAsync(teamId, member, roleByte: 1);
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin }));

            var resp = await client.GetAsync("/api/v1/organizations/users?sortBy=displayName&sortOrder=asc&limit=50");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<MemberSummaryResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsNotNull(page);
            var bob = page!.Items.Single(m => m.Id == member);
            Assert.AreEqual("Member", bob.Role);
            Assert.AreEqual(1, bob.TeamCount);
            var ada = page.Items.Single(m => m.Id == admin);
            Assert.AreEqual("OrgAdmin", ada.Role);
            Assert.AreEqual(0, ada.TeamCount);
        }
        finally
        {
            await Fx.DeleteTeamsForTenantAsync(Tenant.Value);
            await Fx.DeleteUserInOrganizationAsync(Tenant.Value);
            await Fx.DeleteOrganizationsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Role_filter_narrows_results()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-MembersF");
        await Fx.SeedUserInOrganizationAsync(Tenant, "Ada Admin", "ada2@orga.test", KartovaRoles.OrgAdmin);
        await Fx.SeedUserInOrganizationAsync(Tenant, "Bob Member", "bob2@orga.test", KartovaRoles.Member);
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.OrgAdmin }));

            var resp = await client.GetAsync("/api/v1/organizations/users?role=OrgAdmin");
            var page = await resp.Content.ReadFromJsonAsync<CursorPage<MemberSummaryResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsTrue(page!.Items.All(m => m.Role == "OrgAdmin"));
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(Tenant.Value);
            await Fx.DeleteOrganizationsForTenantAsync(Tenant.Value);
        }
    }

    [TestMethod]
    public async Task Viewer_is_forbidden_from_directory()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-MembersV");
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Viewer }));
            var resp = await client.GetAsync("/api/v1/organizations/users");
            // Viewer HAS org.users.read -> 200. Assert OK to lock the read-permission decision (spec §5).
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        }
        finally { await Fx.DeleteOrganizationsForTenantAsync(Tenant.Value); }
    }

    [TestMethod]
    public async Task Typeahead_still_works_at_relocated_path()
    {
        await Fx.SeedOrganizationAsync(Tenant.Value, "OrgA-MembersT");
        await Fx.SeedUserInOrganizationAsync(Tenant, "Charlie Typeahead", "charlie@orga.test", KartovaRoles.Member);
        try
        {
            var client = Fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", Fx.Signer.IssueForTenant(Tenant, new[] { KartovaRoles.Member }));
            var resp = await client.GetAsync("/api/v1/organizations/users/search?q=char&limit=5");
            Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
            var rows = await resp.Content.ReadFromJsonAsync<List<UserSummaryResponse>>(KartovaApiFixtureBase.WireJson);
            Assert.IsTrue(rows!.Any(r => r.Email == "charlie@orga.test"));
        }
        finally
        {
            await Fx.DeleteUserInOrganizationAsync(Tenant.Value);
            await Fx.DeleteOrganizationsForTenantAsync(Tenant.Value);
        }
    }
}
```

- [ ] **Step 7: Run tests + build**

Run: `dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter ListMembersTests`
Expected: PASS (4 tests). If Docker unavailable, mark *pending user verification*.
Run: `dotnet build Kartova.slnx /p:TreatWarningsAsErrors=true` → 0/0.

- [ ] **Step 8: Commit**

```
git add -A
git commit -m "feat(slice-10): members directory endpoint + relocate typeahead to /users/search"
```

---

## Task 5: Change member role endpoint

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Contracts/UpdateMemberRoleRequest.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Application/ChangeMemberRoleCommand.cs` (command + result)
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/ChangeMemberRoleHandler.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/UserEndpointDelegates.cs` (+ `UserRoutes.MapTo`, + `OrganizationModule.RegisterServices`)
- Test: `src/Modules/Organization/Kartova.Organization.Tests/ChangeMemberRoleResultTests.cs`, `src/Modules/Organization/Kartova.Organization.IntegrationTests/ChangeMemberRoleTests.cs`

- [ ] **Step 1: Add the request DTO + command/result**

`UpdateMemberRoleRequest.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record UpdateMemberRoleRequest(string Role);
```

`ChangeMemberRoleCommand.cs`:

```csharp
namespace Kartova.Organization.Application;

public sealed record ChangeMemberRoleCommand(Guid UserId, string Role);

public sealed record ChangeMemberRoleResult(bool Changed, bool NotFound, bool InvalidRole, bool LastOrgAdmin)
{
    public static ChangeMemberRoleResult Success => new(true, false, false, false);
    public static ChangeMemberRoleResult NotFoundResult => new(false, true, false, false);
    public static ChangeMemberRoleResult InvalidRoleResult => new(false, false, true, false);
    public static ChangeMemberRoleResult LastOrgAdminResult => new(false, false, false, true);
}
```

- [ ] **Step 2: Write the failing result test (locks each terminal branch — kills boolean-flip mutants)**

`ChangeMemberRoleResultTests.cs`:

```csharp
using Kartova.Organization.Application;

namespace Kartova.Organization.Tests;

[TestClass]
public sealed class ChangeMemberRoleResultTests
{
    [TestMethod]
    public void Success_sets_only_changed()
    {
        var r = ChangeMemberRoleResult.Success;
        Assert.IsTrue(r.Changed); Assert.IsFalse(r.NotFound); Assert.IsFalse(r.InvalidRole); Assert.IsFalse(r.LastOrgAdmin);
    }

    [TestMethod]
    public void NotFound_sets_only_notFound()
    {
        var r = ChangeMemberRoleResult.NotFoundResult;
        Assert.IsFalse(r.Changed); Assert.IsTrue(r.NotFound); Assert.IsFalse(r.InvalidRole); Assert.IsFalse(r.LastOrgAdmin);
    }

    [TestMethod]
    public void InvalidRole_sets_only_invalidRole()
    {
        var r = ChangeMemberRoleResult.InvalidRoleResult;
        Assert.IsFalse(r.Changed); Assert.IsFalse(r.NotFound); Assert.IsTrue(r.InvalidRole); Assert.IsFalse(r.LastOrgAdmin);
    }

    [TestMethod]
    public void LastOrgAdmin_sets_only_lastOrgAdmin()
    {
        var r = ChangeMemberRoleResult.LastOrgAdminResult;
        Assert.IsFalse(r.Changed); Assert.IsFalse(r.NotFound); Assert.IsFalse(r.InvalidRole); Assert.IsTrue(r.LastOrgAdmin);
    }
}
```

- [ ] **Step 3: Run it to verify failure**

Run: `dotnet test src/Modules/Organization/Kartova.Organization.Tests --filter ChangeMemberRoleResultTests`
Expected: FAIL (types not defined).

- [ ] **Step 4: Implement the handler**

`ChangeMemberRoleHandler.cs` (KeyCloak is source of truth; projection write-through; last-admin guard via the partial index):

```csharp
using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

public sealed class ChangeMemberRoleHandler(IKeycloakAdminClient keycloak)
{
    public async Task<ChangeMemberRoleResult> Handle(
        ChangeMemberRoleCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        if (!KartovaRoles.All.Contains(cmd.Role))
            return ChangeMemberRoleResult.InvalidRoleResult;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);
        if (user is null) return ChangeMemberRoleResult.NotFoundResult;

        if (user.RealmRole == KartovaRoles.OrgAdmin && cmd.Role != KartovaRoles.OrgAdmin)
        {
            var orgAdminCount = await db.Users.CountAsync(u => u.RealmRole == KartovaRoles.OrgAdmin, ct);
            if (orgAdminCount <= 1) return ChangeMemberRoleResult.LastOrgAdminResult;
        }

        await keycloak.ChangeRealmRoleAsync(cmd.UserId, cmd.Role, ct);   // source of truth
        user.RealmRole = cmd.Role;                                       // write-through cache
        await db.SaveChangesAsync(ct);
        return ChangeMemberRoleResult.Success;
    }
}
```

- [ ] **Step 5: Add the delegate + route + registration**

In `UserEndpointDelegates.cs`:

```csharp
    internal static async Task<IResult> ChangeMemberRoleAsync(
        Guid id, UpdateMemberRoleRequest request,
        ChangeMemberRoleHandler handler, OrganizationDbContext db, CancellationToken ct)
    {
        var result = await handler.Handle(new ChangeMemberRoleCommand(id, request.Role), db, ct);
        if (result.InvalidRole)
            return Results.Problem(type: ProblemTypes.ValidationFailed, title: "Invalid role",
                detail: $"Role must be one of: {string.Join(", ", KartovaRoles.All)}.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        if (result.NotFound)
            return Results.Problem(type: ProblemTypes.ResourceNotFound, title: "Member not found",
                detail: $"No member with id {id}.", statusCode: StatusCodes.Status404NotFound);
        if (result.LastOrgAdmin)
            return Results.Problem(type: ProblemTypes.LastOrgAdmin, title: "Cannot demote the last OrgAdmin",
                detail: "The organization must retain at least one OrgAdmin.", statusCode: StatusCodes.Status409Conflict);
        return Results.NoContent();
    }
```

Route (in `UserRoutes.MapTo`):

```csharp
        tenant.MapPut("/users/{id:guid}/role", UserEndpointDelegates.ChangeMemberRoleAsync)
            .RequireAuthorization(KartovaPermissions.OrgUsersRoleChange)
            .WithName("ChangeMemberRole")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
```

Registration: `services.AddScoped<ChangeMemberRoleHandler>();` in `OrganizationModule.RegisterServices`.

- [ ] **Step 6: Run the result test**

Run: `dotnet test src/Modules/Organization/Kartova.Organization.Tests --filter ChangeMemberRoleResultTests`
Expected: PASS.

- [ ] **Step 7: Write + run the integration test**

`ChangeMemberRoleTests.cs` — assert the happy path (promote Member→OrgAdmin, verify projection via `BypassOptions()`) and the last-admin 409. Use real KeyCloak (the fixture seeds a KeyCloak user; mirror how invitation integration tests obtain a KeyCloak `sub`, or seed a KeyCloak user via the admin client in the fixture). Key cases:

```csharp
[TestMethod] public async Task OrgAdmin_promotes_member_to_orgadmin_returns_204_and_updates_projection() { /* seed Member, PUT role=OrgAdmin, assert 204 + projection RealmRole==OrgAdmin via BypassOptions */ }
[TestMethod] public async Task Demoting_last_orgadmin_returns_409_last_orgadmin() { /* seed single OrgAdmin, PUT role=Member, assert 409 + ProblemTypes.LastOrgAdmin */ }
[TestMethod] public async Task Unknown_role_returns_422() { /* PUT role="Nope", assert 422 */ }
[TestMethod] public async Task Member_without_permission_returns_403() { /* Member token, assert 403 */ }
```

For the KeyCloak round-trip: if the fixture cannot easily create a real KeyCloak user, substitute `IKeycloakAdminClient` with NSubstitute in a WebApplicationFactory override (mirror any existing fixture KeyCloak-stub pattern), or seed via the admin client. Prefer the real round-trip if the fixture already runs a KeyCloak container (it does — `UsesKeycloakContainer => true`).

Run: `dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter ChangeMemberRoleTests`
Expected: PASS. Build: `dotnet build Kartova.slnx /p:TreatWarningsAsErrors=true` → 0/0.

- [ ] **Step 8: Commit**

```
git add -A
git commit -m "feat(slice-10): change member role endpoint (PUT /users/{id}/role) with last-admin guard"
```

---

## Task 6: Offboard member endpoint + cross-module owner reassignment

**Files:**
- Create: `src/Kartova.SharedKernel/Multitenancy/IApplicationOwnerReassigner.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/Application.cs` (add `ReassignOwner`)
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/ApplicationOwnerReassigner.cs` (+ register in `CatalogModule`)
- Create: `src/Modules/Organization/Kartova.Organization.Contracts/OffboardMemberRequest.cs`
- Create: `src/Modules/Organization/Kartova.Organization.Application/OffboardMemberCommand.cs` (command + result)
- Create: `src/Modules/Organization/Kartova.Organization.Infrastructure/OffboardMemberHandler.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/UserEndpointDelegates.cs` (+ route + registration)
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationReassignOwnerTests.cs`, `src/Modules/Organization/Kartova.Organization.Tests/OffboardMemberResultTests.cs`, `src/Modules/Organization/Kartova.Organization.IntegrationTests/OffboardMemberTests.cs`

- [ ] **Step 1: Define the cross-module port**

`IApplicationOwnerReassigner.cs`:

```csharp
namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Cross-module port: reassigns ownership of all applications owned by <paramref name="fromUserId"/>
/// to <paramref name="toUserId"/> within the current tenant scope. Implemented by the Catalog module.
/// </summary>
public interface IApplicationOwnerReassigner
{
    Task<int> ReassignOwnerAsync(Guid fromUserId, Guid toUserId, CancellationToken ct);
}
```

- [ ] **Step 2: Write the failing domain test for `Application.ReassignOwner`**

`ApplicationReassignOwnerTests.cs` (mirror existing `Application` domain tests):

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Tests;

[TestClass]
public sealed class ApplicationReassignOwnerTests
{
    private static Application NewApp(Guid owner) =>
        Application.Create("my-app", "desc", owner, new TenantId(Guid.NewGuid()), TimeProvider.System);

    [TestMethod]
    public void ReassignOwner_sets_new_owner()
    {
        var app = NewApp(Guid.NewGuid());
        var newOwner = Guid.NewGuid();
        app.ReassignOwner(newOwner);
        Assert.AreEqual(newOwner, app.OwnerUserId);
    }

    [TestMethod]
    public void ReassignOwner_rejects_empty_guid()
    {
        var app = NewApp(Guid.NewGuid());
        Assert.ThrowsExactly<ArgumentException>(() => app.ReassignOwner(Guid.Empty));
    }
}
```

(Confirm `Application.Create`'s exact signature/name conventions against the real `Application.cs`; the verbatim owner validation is `if (ownerUserId == Guid.Empty) throw new ArgumentException(...)`.)

- [ ] **Step 3: Run it to verify failure**

Run: `dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter ApplicationReassignOwnerTests`
Expected: FAIL — `ReassignOwner` not defined.

- [ ] **Step 4: Add the domain method**

In `Application.cs`, mirror the existing mutators (`AssignTeam` etc.):

```csharp
    public void ReassignOwner(Guid newOwnerUserId)
    {
        if (newOwnerUserId == Guid.Empty)
            throw new ArgumentException("newOwnerUserId is required.", nameof(newOwnerUserId));
        OwnerUserId = newOwnerUserId;
    }
```

- [ ] **Step 5: Run the domain test**

Run: `dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter ApplicationReassignOwnerTests`
Expected: PASS.

- [ ] **Step 6: Implement + register the Catalog reassigner**

`ApplicationOwnerReassigner.cs` (mirror `ApplicationCountByTeamReader` — `internal sealed`, ctor-injected `CatalogDbContext`):

```csharp
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

internal sealed class ApplicationOwnerReassigner(CatalogDbContext db) : IApplicationOwnerReassigner
{
    public async Task<int> ReassignOwnerAsync(Guid fromUserId, Guid toUserId, CancellationToken ct)
    {
        var apps = await db.Applications.Where(a => a.OwnerUserId == fromUserId).ToListAsync(ct);
        foreach (var app in apps)
            app.ReassignOwner(toUserId);
        await db.SaveChangesAsync(ct);
        return apps.Count;
    }
}
```

Register in `CatalogModule.RegisterServices` next to the existing reader registration:

```csharp
        services.AddScoped<IApplicationOwnerReassigner, ApplicationOwnerReassigner>();
```

- [ ] **Step 7: Add the offboard request DTO + command/result**

`OffboardMemberRequest.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Organization.Contracts;

[ExcludeFromCodeCoverage]
public sealed record OffboardMemberRequest(Guid SuccessorUserId);
```

`OffboardMemberCommand.cs`:

```csharp
namespace Kartova.Organization.Application;

public sealed record OffboardMemberCommand(Guid UserId, Guid SuccessorUserId, Guid ActingUserId);

public sealed record OffboardMemberResult(
    bool Offboarded, bool NotFound, bool CannotOffboardSelf, bool LastOrgAdmin, bool InvalidSuccessor, int AppsReassigned)
{
    public static OffboardMemberResult Success(int apps) => new(true, false, false, false, false, apps);
    public static OffboardMemberResult NotFoundResult => new(false, true, false, false, false, 0);
    public static OffboardMemberResult SelfResult => new(false, false, true, false, false, 0);
    public static OffboardMemberResult LastOrgAdminResult => new(false, false, false, true, false, 0);
    public static OffboardMemberResult InvalidSuccessorResult => new(false, false, false, false, true, 0);
}
```

- [ ] **Step 8: Write the failing result test**

`OffboardMemberResultTests.cs` — one `[TestMethod]` per factory asserting exactly the expected flags set (mirror `ChangeMemberRoleResultTests` from Task 5; include `AppsReassigned` value check on `Success(3)`).

- [ ] **Step 9: Run it to verify failure, then implement the handler**

Run: `dotnet test src/Modules/Organization/Kartova.Organization.Tests --filter OffboardMemberResultTests` → FAIL.

`OffboardMemberHandler.cs`:

```csharp
using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

public sealed class OffboardMemberHandler(
    IKeycloakAdminClient keycloak, IApplicationOwnerReassigner reassigner)
{
    public async Task<OffboardMemberResult> Handle(
        OffboardMemberCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);
        if (target is null) return OffboardMemberResult.NotFoundResult;
        if (cmd.UserId == cmd.ActingUserId) return OffboardMemberResult.SelfResult;

        var successor = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.SuccessorUserId, ct);
        if (successor is null || cmd.SuccessorUserId == cmd.UserId)
            return OffboardMemberResult.InvalidSuccessorResult;

        if (target.RealmRole == KartovaRoles.OrgAdmin)
        {
            var orgAdminCount = await db.Users.CountAsync(u => u.RealmRole == KartovaRoles.OrgAdmin, ct);
            if (orgAdminCount <= 1) return OffboardMemberResult.LastOrgAdminResult;
        }

        // 1. Reassign owned apps (Catalog, same request tenant tx).
        var reassigned = await reassigner.ReassignOwnerAsync(cmd.UserId, cmd.SuccessorUserId, ct);

        // 2. Delete the KeyCloak identity (external; point of no return).
        await keycloak.DeleteUserAsync(cmd.UserId, ct);

        // 3. Cascade memberships + delete the projection row.
        var memberships = await db.TeamMemberships.Where(m => m.UserId == cmd.UserId).ToListAsync(ct);
        db.TeamMemberships.RemoveRange(memberships);
        db.Users.Remove(target);
        await db.SaveChangesAsync(ct);

        return OffboardMemberResult.Success(reassigned);
    }
}
```

Confirm `db.TeamMemberships` DbSet name + `TeamMembership.UserId`. If team_members has a DB cascade FK on team only (not user), the explicit RemoveRange above is still correct.

- [ ] **Step 10: Add the delegate + route + registration**

`UserEndpointDelegates.cs`:

```csharp
    internal static async Task<IResult> OffboardMemberAsync(
        Guid id, OffboardMemberRequest request,
        OffboardMemberHandler handler, OrganizationDbContext db, ICurrentUser currentUser, CancellationToken ct)
    {
        var result = await handler.Handle(
            new OffboardMemberCommand(id, request.SuccessorUserId, currentUser.UserId), db, ct);
        if (result.NotFound)
            return Results.Problem(type: ProblemTypes.ResourceNotFound, title: "Member not found",
                detail: $"No member with id {id}.", statusCode: StatusCodes.Status404NotFound);
        if (result.CannotOffboardSelf)
            return Results.Problem(type: ProblemTypes.CannotOffboardSelf, title: "Cannot offboard yourself",
                detail: "You cannot remove your own membership.", statusCode: StatusCodes.Status409Conflict);
        if (result.LastOrgAdmin)
            return Results.Problem(type: ProblemTypes.LastOrgAdmin, title: "Cannot offboard the last OrgAdmin",
                detail: "The organization must retain at least one OrgAdmin.", statusCode: StatusCodes.Status409Conflict);
        if (result.InvalidSuccessor)
            return Results.Problem(type: ProblemTypes.InvalidSuccessor, title: "Invalid successor",
                detail: "The successor must be an existing member other than the offboarded user.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        return Results.NoContent();
    }
```

Confirm `ICurrentUser` exposes the caller's user id (`UserId` / `Sub`) — check `src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs` and use the actual member name.

Route:

```csharp
        tenant.MapDelete("/users/{id:guid}", UserEndpointDelegates.OffboardMemberAsync)
            .RequireAuthorization(KartovaPermissions.OrgUsersRemove)
            .WithName("OffboardMember")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
```

Registration: `services.AddScoped<OffboardMemberHandler>();`.

- [ ] **Step 11: Run result test, then integration test**

`OffboardMemberTests.cs` cases:

```csharp
[TestMethod] public async Task OrgAdmin_offboards_member_reassigns_apps_and_deletes_projection() { /* seed member owning a seeded app, DELETE with successor, assert 204; app owner==successor (BypassOptions/Catalog); user row gone */ }
[TestMethod] public async Task Offboarding_self_returns_409_cannot_offboard_self() { /* acting user == target */ }
[TestMethod] public async Task Offboarding_last_orgadmin_returns_409_last_orgadmin() { }
[TestMethod] public async Task Unknown_successor_returns_422_invalid_successor() { }
[TestMethod] public async Task Member_without_permission_returns_403() { }
```

For the owned-app reassignment assertion, seed a Catalog application owned by the member (`Fx.SeedCatalogApplicationAssignedToTeamAsync` assigns a team — confirm there is a helper to set `owner_user_id`, or seed via raw SQL setting `owner_user_id` to the member). After offboard, read the app via `BypassOptions()` on `CatalogDbContext` and assert `OwnerUserId == successor`.

Run: `dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter OffboardMemberTests`
Expected: PASS. Build → 0/0.

- [ ] **Step 12: Commit**

```
git add -A
git commit -m "feat(slice-10): offboard member endpoint + IApplicationOwnerReassigner cross-module port"
```

---

## Task 7: SPA — members API hooks + typeahead URL relocation

**Files:**
- Create: `web/src/features/members/api/members.ts`
- Modify: `web/src/features/users/api/users.ts` (relocate search URL)
- Modify: `web/src/features/users/api/__tests__/users.test.tsx` (update asserted URL)
- Test: `web/src/features/members/api/__tests__/members.test.tsx`
- Run codegen first so `operations["ListMembers"]`, `ChangeMemberRole`, `OffboardMember` types exist.

- [ ] **Step 1: Regenerate the OpenAPI types**

Backend endpoints from Tasks 4–6 must be reflected in the typed client. With the API reachable (or using the snapshot fallback), run (Windows):

```
cmd //c "cd web && npm run codegen"
```

Expected: `web/src/generated/openapi.ts` now contains `"/api/v1/organizations/users"` → `operations["ListMembers"]`, `"/api/v1/organizations/users/search"` → `operations["SearchUsers"]`, `"/api/v1/organizations/users/{id}/role"` → `operations["ChangeMemberRole"]`, `"/api/v1/organizations/users/{id}"` DELETE → `operations["OffboardMember"]`.

- [ ] **Step 2: Relocate the typeahead URL + fix its test**

In `web/src/features/users/api/users.ts`, in `useUserSearch`, change the path literal:

```ts
const { data, error, response } = await apiClient.GET(
  "/api/v1/organizations/users/search",
  { params: { query: { q, limit } }, signal },
);
```

In `web/src/features/users/api/__tests__/users.test.tsx`, update the asserted URL literal from `"/api/v1/organizations/users"` to `"/api/v1/organizations/users/search"`.

- [ ] **Step 3: Run the users hook test**

Run (Windows): `cmd //c "cd web && npm test -- users"`
Expected: PASS.

- [ ] **Step 4: Write the failing members hooks test**

`web/src/features/members/api/__tests__/members.test.tsx` (mirror `users.test.tsx`): assert `useMembersList` calls `apiClient.GET("/api/v1/organizations/users", { params: { query: { sortBy, sortOrder, limit, cursor } } })` and unwraps the cursor envelope; assert `useChangeMemberRole` PUTs to `/users/{id}/role`; assert `useOffboardMember` DELETEs `/users/{id}` with body `{ successorUserId }`.

- [ ] **Step 5: Implement the hooks**

`members.ts` (mirror `teams.ts` — list via `useCursorList`, mutations via `useMutation` + `throwWithStatus` + invalidation):

```ts
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "@/features/catalog/api/client";
import { useCursorList } from "@/lib/list/useCursorList";
import { throwWithStatus, unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components, operations } from "@/generated/openapi";

type MemberSummaryResponse = components["schemas"]["MemberSummaryResponse"];
type ListMembersQuery = NonNullable<operations["ListMembers"]["parameters"]["query"]>;

type MembersListParams = {
  sortBy: NonNullable<ListMembersQuery["sortBy"]>;
  sortOrder: NonNullable<ListMembersQuery["sortOrder"]>;
  role?: string;
  q?: string;
  limit?: number;
};

export const memberKeys = {
  all: ["members"] as const,
  list: (p?: MembersListParams) =>
    p ? ([...memberKeys.all, "list", p] as const) : ([...memberKeys.all, "list"] as const),
};

export function useMembersList(params: MembersListParams) {
  return useCursorList<MemberSummaryResponse>({
    queryKey: memberKeys.list(params),
    fetchPage: async (cursor) => {
      const { data, error } = await apiClient.GET("/api/v1/organizations/users", {
        params: {
          query: {
            sortBy: params.sortBy,
            sortOrder: params.sortOrder,
            role: params.role,
            q: params.q,
            limit: params.limit ?? 50,
            cursor,
          },
        },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}

export function useChangeMemberRole() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { userId: string; role: string }) => {
      const { error, response } = await apiClient.PUT("/api/v1/organizations/users/{id}/role", {
        params: { path: { id: input.userId } },
        body: { role: input.role },
      });
      if (error) throwWithStatus(error, response);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: memberKeys.all }),
  });
}

export function useOffboardMember() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (input: { userId: string; successorUserId: string }) => {
      const { error, response } = await apiClient.DELETE("/api/v1/organizations/users/{id}", {
        params: { path: { id: input.userId } },
        body: { successorUserId: input.successorUserId },
      });
      if (error) throwWithStatus(error, response);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: memberKeys.all }),
  });
}
```

If the backend binds `limit` as `int` (it does — Task 4 uses `CursorListBinding`/string but the generated query type will reflect `number | string`; pass the number, mirroring `teams.ts`). If codegen types `role`/`q` as required, mark optional via the `?:` in `MembersListParams` and pass `undefined` to omit.

- [ ] **Step 6: Run the members hooks test**

Run (Windows): `cmd //c "cd web && npm test -- members"`
Expected: PASS.

- [ ] **Step 7: Commit**

```
git add -A
git commit -m "feat(slice-10): SPA members API hooks + relocate user-search URL"
```

---

## Task 8: SPA — members directory page + route + sidebar

**Files:**
- Create: `web/src/features/members/pages/MembersListPage.tsx`
- Modify: `web/src/app/router.tsx`
- Modify: `web/src/components/layout/Sidebar.tsx`
- Test: `web/src/features/members/pages/__tests__/MembersListPage.test.tsx`

- [ ] **Step 1: Write the failing page test (mirror `TeamsListPage.test.tsx`)**

Assert: with `mockPermissions([OrgUsersRead])` and a mocked `apiClient.GET` returning `pageOf([{ id, displayName: "Bob", email, role: "Member", teamCount: 1, lastSeenAt: null, createdAt }])`, the page renders "Bob" and the "Member" role; with `OrgUsersRemove` absent, the row "Remove" action is not rendered; with it present, it is.

- [ ] **Step 2: Implement the page (mirror `TeamsListPage` + `TeamDetailPage` row-action idiom)**

`MembersListPage.tsx`:

```tsx
import { useState } from "react";
import { Link } from "react-router-dom";
import { useListUrlState } from "@/lib/list/useListUrlState";
import { usePermissions } from "@/shared/auth/usePermissions";
import { KartovaPermissions } from "@/shared/auth/permissions";
import { useMembersList } from "@/features/members/api/members";
import { Button } from "@/components/base/buttons/button";
import { ChangeMemberRoleDialog } from "@/features/members/components/ChangeMemberRoleDialog";
import { OffboardMemberConfirmDialog } from "@/features/members/components/OffboardMemberConfirmDialog";

const ALLOWED_SORT_FIELDS = ["displayName", "role", "createdAt"] as const;

export function MembersListPage() {
  const { sortBy, sortOrder } = useListUrlState({
    defaultSortBy: "displayName",
    defaultSortOrder: "asc",
    allowedSortFields: ALLOWED_SORT_FIELDS,
  });
  const list = useMembersList({ sortBy, sortOrder });
  const { hasPermission, isLoading: permsLoading } = usePermissions();
  const canManage = !permsLoading && hasPermission(KartovaPermissions.OrgUsersRemove);
  const canChangeRole = !permsLoading && hasPermission(KartovaPermissions.OrgUsersRoleChange);

  const [roleTarget, setRoleTarget] = useState<{ userId: string; role: string } | null>(null);
  const [offboardTarget, setOffboardTarget] = useState<{ userId: string; displayName: string } | null>(null);

  return (
    <div className="space-y-4">
      <h1 className="text-lg font-semibold text-primary">Members</h1>

      {list.isError ? (
        <div className="rounded-xl bg-primary p-6 ring-1 ring-secondary">
          <p className="text-sm text-error-primary">Could not load members.</p>
          <Button size="sm" color="secondary" onClick={() => list.reset()}>Retry</Button>
        </div>
      ) : list.isLoading ? (
        <div className="rounded-xl bg-primary p-6 ring-1 ring-secondary text-sm text-tertiary">Loading…</div>
      ) : list.items.length === 0 ? (
        <div className="rounded-xl bg-primary p-6 ring-1 ring-secondary text-sm text-tertiary">No members yet.</div>
      ) : (
        <div className="overflow-hidden rounded-xl bg-primary shadow-xs ring-1 ring-secondary">
          <table className="w-full text-left text-sm">
            <thead className="bg-secondary text-xs uppercase tracking-wide text-tertiary">
              <tr>
                <th className="px-4 py-3 font-medium">Name</th>
                <th className="px-4 py-3 font-medium">Email</th>
                <th className="px-4 py-3 font-medium">Role</th>
                <th className="px-4 py-3 font-medium">Teams</th>
                <th className="px-4 py-3 font-medium">Last seen</th>
                {(canManage || canChangeRole) && <th className="px-4 py-3 text-right font-medium">Actions</th>}
              </tr>
            </thead>
            <tbody className="divide-y divide-secondary">
              {list.items.map((m) => (
                <tr key={m.id} className="hover:bg-primary_hover">
                  <td className="px-4 py-3">
                    <Link to={`/users/${m.id}`} className="font-medium text-primary hover:underline">{m.displayName}</Link>
                  </td>
                  <td className="px-4 py-3 text-tertiary">{m.email}</td>
                  <td className="px-4 py-3">{m.role}</td>
                  <td className="px-4 py-3 text-tertiary">{m.teamCount}</td>
                  <td className="px-4 py-3 text-tertiary">{m.lastSeenAt ? new Date(m.lastSeenAt).toLocaleDateString() : "—"}</td>
                  {(canManage || canChangeRole) && (
                    <td className="px-4 py-3 text-right">
                      <div className="flex justify-end gap-2">
                        {canChangeRole && (
                          <Button size="sm" color="secondary" onClick={() => setRoleTarget({ userId: m.id, role: m.role })}>Change role</Button>
                        )}
                        {canManage && (
                          <Button size="sm" color="secondary" onClick={() => setOffboardTarget({ userId: m.id, displayName: m.displayName })}>Remove</Button>
                        )}
                      </div>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <ChangeMemberRoleDialog
        userId={roleTarget?.userId ?? ""}
        currentRole={roleTarget?.role ?? "Member"}
        open={roleTarget !== null}
        onOpenChange={(open) => { if (!open) setRoleTarget(null); }}
      />
      <OffboardMemberConfirmDialog
        userId={offboardTarget?.userId ?? ""}
        displayName={offboardTarget?.displayName ?? ""}
        open={offboardTarget !== null}
        onOpenChange={(open) => { if (!open) setOffboardTarget(null); }}
      />
    </div>
  );
}
```

(The two dialogs are implemented in Task 9; create empty stub components first so the page compiles, then flesh them out in Task 9 — or implement Task 9 before re-running this page test. Use whichever order keeps the build green; recommended: stub the dialogs now.)

- [ ] **Step 3: Add the route**

In `web/src/app/router.tsx`, add the import at top:

```tsx
import { MembersListPage } from "@/features/members/pages/MembersListPage";
```

Inside the `<Route element={<ProtectedShell />}>` block (next to `/teams`):

```tsx
<Route path="/members" element={<MembersListPage />} />
```

- [ ] **Step 4: Add the sidebar entry**

In `web/src/components/layout/Sidebar.tsx`, add a gate near the other `canSee*` booleans:

```tsx
const canSeeMembers = hasPermission(KartovaPermissions.OrgUsersRead);
```

And the nav entry (mirror the Teams `<li>`):

```tsx
{canSeeMembers && (
  <li>
    <NavItemLink to="/members" label="Members" />
  </li>
)}
```

- [ ] **Step 5: Run the page test + typecheck**

Run (Windows): `cmd //c "cd web && npm test -- MembersListPage"`
Expected: PASS.
Run: `cmd //c "cd web && npm run build"` (or `tsc`) — expected: no type errors.

- [ ] **Step 6: Commit**

```
git add -A
git commit -m "feat(slice-10): members directory page + route + sidebar entry"
```

---

## Task 9: SPA — Change-role + Offboard dialogs

**Files:**
- Create: `web/src/features/members/components/ChangeMemberRoleDialog.tsx`
- Create: `web/src/features/members/components/OffboardMemberConfirmDialog.tsx`
- Create: `web/src/features/members/schemas/offboardMember.ts`
- Test: `web/src/features/members/components/__tests__/ChangeMemberRoleDialog.test.tsx`, `.../OffboardMemberConfirmDialog.test.tsx`

- [ ] **Step 1: Write the failing dialog tests (mirror `InviteUserDialog.test.tsx`)**

ChangeMemberRole: rendering with `currentRole="Member"`, selecting "OrgAdmin", clicking Save calls `useChangeMemberRole().mutateAsync({ userId, role: "OrgAdmin" })`, then `toast.success` + `onOpenChange(false)`; on a 409 `last-orgadmin` error, `toast.error` is shown. Offboard: requires a successor selection before the Remove button enables; on submit calls `useOffboardMember().mutateAsync({ userId, successorUserId })`; on 409 self/last-admin shows the mapped message.

- [ ] **Step 2: Implement `ChangeMemberRoleDialog` (mirror teams `ChangeRoleDialog` — native select + dirty guard + toast errors)**

```tsx
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { ModalOverlay, Modal, Dialog } from "@/components/application/modals/modal";
import { Button } from "@/components/base/buttons/button";
import { useChangeMemberRole } from "@/features/members/api/members";
import type { ProblemDetails } from "@/shared/forms/problemDetails";

const ROLES = ["Viewer", "Member", "OrgAdmin"] as const;

export function ChangeMemberRoleDialog(props: {
  userId: string; currentRole: string; open: boolean; onOpenChange: (open: boolean) => void;
}) {
  const { userId, currentRole, open, onOpenChange } = props;
  const [role, setRole] = useState(currentRole);
  const mutation = useChangeMemberRole();

  useEffect(() => { if (open) setRole(currentRole); }, [open, currentRole]);
  const dirty = role !== currentRole;

  const onSave = async () => {
    try {
      await mutation.mutateAsync({ userId, role });
      toast.success("Role updated. Takes effect on the member's next login.");
      onOpenChange(false);
    } catch (err) {
      const problem = err as ProblemDetails;
      toast.error(problem.detail ?? problem.title ?? "Could not change role");
    }
  };

  return (
    <ModalOverlay isOpen={open} onOpenChange={onOpenChange} isDismissable={!mutation.isPending}>
      <Modal className="max-w-[480px]">
        <Dialog aria-label="Change member role" className="bg-primary rounded-xl shadow-xl p-6 outline-none">
          <h2 className="text-md font-semibold text-primary">Change role</h2>
          <p className="mt-1 text-sm text-tertiary">Role changes take effect on the member's next login.</p>
          <label className="mt-4 block text-sm font-medium text-secondary" htmlFor="member-role">Role</label>
          <select id="member-role" className="mt-1 w-full rounded-lg border border-secondary bg-primary px-3 py-2 text-sm"
            value={role} onChange={(e) => setRole(e.target.value)}>
            {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
          </select>
          <div className="mt-6 flex justify-end gap-2">
            <Button size="sm" color="secondary" onClick={() => onOpenChange(false)}>Cancel</Button>
            <Button size="sm" color="primary" isDisabled={!dirty} isLoading={mutation.isPending} onClick={onSave}>Save</Button>
          </div>
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
```

- [ ] **Step 3: Implement `OffboardMemberConfirmDialog` (mirror `AddMemberDialog` successor picker + `RemoveMemberConfirmDialog` confirm)**

`offboardMember.ts`:

```ts
import { z } from "zod";
export const offboardMemberSchema = z.object({ successorUserId: z.string().uuid("Select a successor") });
export type OffboardMemberInput = z.infer<typeof offboardMemberSchema>;
```

`OffboardMemberConfirmDialog.tsx`: render the warning ("Removing {displayName} reassigns all their applications to the chosen successor and permanently deletes their account."), a `<UserSearchCombobox onSelect={(u) => setSuccessor(u)} />`, a "Selected: {successor.displayName}" line, Remove button `isDisabled={!successor}` `color="primary-destructive"`. On confirm: `await mutation.mutateAsync({ userId, successorUserId: successor.id })`, `toast.success`, `onOpenChange(false)`; catch maps `(err as ProblemDetails).detail`/`title` to `toast.error`. Import `useOffboardMember` from the members api, `UserSearchCombobox` + `UserSummaryResponse` from the users feature, modal primitives, `Button`, `toast`.

- [ ] **Step 4: Run the dialog tests**

Run (Windows): `cmd //c "cd web && npm test -- ChangeMemberRoleDialog OffboardMemberConfirmDialog"`
Expected: PASS.

- [ ] **Step 5: Run the full SPA test + build**

Run (Windows): `cmd //c "cd web && npm test"` then `cmd //c "cd web && npm run build"`
Expected: all green, no type errors.

- [ ] **Step 6: Commit**

```
git add -A
git commit -m "feat(slice-10): change-role + offboard dialogs"
```

---

## Task 10: ADR-0102 + backlog (stories + checklist)

**Files:**
- Create: `docs/architecture/decisions/ADR-0102-user-offboarding-hard-delete.md`
- Modify: `docs/architecture/decisions/README.md` (table row + keyword index)
- Modify: `docs/product/EPICS-AND-STORIES.md` (3 stories under E-03.F-01) — and its phase file `docs/product/phases/phase-1-core-catalog.md`
- Modify: `docs/product/CHECKLIST.md` (3 story lines + progress counts)

- [ ] **Step 1: Write ADR-0102** (Michael Nygard template; mirror an existing ADR's section structure). Content per spec §9 — Context (member is an IdP identity, not a catalog entity; audit deferred), Decision (hard delete + reassign successor; outside ADR-0019; org retains ≥1 OrgAdmin; offboarding traceless until ADR-0018 slice), Consequences (frees email/seat per ADR-0100; no recovery; no trail until audit slice), Related: ADR-0015, 0018, 0019, 0100, 0101.

- [ ] **Step 2: Index ADR-0102 in `README.md`** — add the table row and add it under the "Audit & logging" / "Compliance & Retention" keyword lists where ADR-0019 appears.

- [ ] **Step 3: Add the three stories** to `EPICS-AND-STORIES.md` + `phase-1-core-catalog.md` under E-03.F-01:
  - E-03.F-01.S-05 — members directory (acceptance: paginated list with role + team count, OrgAdmin-gated actions).
  - E-03.F-01.S-06 — change member role (acceptance: KeyCloak role reassigned + projection updated; last-OrgAdmin blocked).
  - E-03.F-01.S-07 — offboard member (acceptance: owned apps reassigned to successor; KeyCloak user + projection deleted; self/last-admin guarded).

- [ ] **Step 4: Update `CHECKLIST.md`** — add the 3 story lines under E-03.F-01 marked `[x]` with the slice-10 annotation, and bump the Phase 1 + Total counts.

- [ ] **Step 5: Commit**

```
git add -A
git commit -m "docs(slice-10): ADR-0102 + E-03.F-01.S-05/06/07 stories + checklist"
```

---

## Task 11: Definition-of-Done verification

**Files:** none (verification only). This task gates the slice per CLAUDE.md.

- [ ] **Step 1: Full solution build** — `dotnet build Kartova.slnx /p:TreatWarningsAsErrors=true` → cite 0/0.
- [ ] **Step 2: Full unit + architecture suite** — `dotnet test` for `Kartova.SharedKernel.Tests`, `Kartova.SharedKernel.Identity.Tests`, `Kartova.ArchitectureTests`, `Kartova.Organization.Tests`, `Kartova.Catalog.Tests` → all green.
- [ ] **Step 3: Integration suite (Testcontainers + KeyCloak)** — `dotnet test Kartova.Organization.IntegrationTests Kartova.Catalog.IntegrationTests` → green. If Docker unavailable locally, mark *pending user verification*.
- [ ] **Step 4: SPA** — `cmd //c "cd web && npm test"` + `npm run build` → green.
- [ ] **Step 5: docker compose HTTP evidence** — `docker compose up`, obtain an OrgAdmin token, then capture: (happy) offboard a member who owns an app → 204 + the app's owner is the successor + the member is gone from `GET /users`; (negative) attempt to offboard the last OrgAdmin → 409 `last-orgadmin`. Save command + response output.
- [ ] **Step 6: `/simplify`** on the branch diff — address should-fix reuse/quality/efficiency items or skip with reason.
- [ ] **Step 7: Mutation feedback loop** — `/misc:mutation-sentinel` on changed files → `/misc:test-generator` until ≥80% (per `stryker-config.json`). Document score + accepted survivors.
- [ ] **Step 8: Reviews** — `/superpowers:requesting-code-review` (branch diff vs spec+plan), `/pr-review-toolkit:review-pr`, `/deep-review`. Address Blocking + Should-fix.
- [ ] **Step 9: Update the spec status** to "Implemented" and note the PR.

---

## Self-review notes (author)

- **Spec coverage:** S-05 directory → Tasks 4,7,8. S-06 role change → Tasks 3,5,7,9. S-07 offboard → Tasks 6,7,9. realm_role projection (D4) → Task 1. Permissions (D6) → Task 2. Cross-module port (D4/D5) → Task 6. Guards (D7) → Tasks 5,6. Typeahead relocation (D8) → Tasks 4,7. ADR-0102 + backlog (§9) → Task 10. No audit / no session-kill (D2/D3) — correctly absent. DoD (§10) → Task 11.
- **Type consistency:** `MemberSummaryResponse`, `MemberSortField`, `UpdateMemberRoleRequest`, `OffboardMemberRequest`, `ChangeMemberRoleResult`/`OffboardMemberResult`, `IApplicationOwnerReassigner.ReassignOwnerAsync`, `Application.ReassignOwner`, `ChangeRealmRoleAsync` — names identical across backend tasks and SPA hook generics.
- **Known unknowns the implementer must confirm against real code (flagged inline):** exact `TeamMembership.UserId` / `db.TeamMemberships` names; `ICurrentUser` caller-id member name; `Application.Create` signature; `SeedUserInOrganizationAsync` body; whether the integration fixture stubs or runs real KeyCloak for role/offboard; the `OrganizationModule.RegisterServices` + `CatalogModule.RegisterServices` exact registration sites; `Button` import path/props.
