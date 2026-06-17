# Audit Event Wiring (Phase 2 — Organization mutations) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the 10 user-initiated Organization HTTP mutations to the Phase-1 audit log so every identity/access change records a tamper-evident `audit_log` row inside its own transaction.

**Architecture:** Inline `IAuditWriter.AppendAsync(...)` on each handler's success path (Approach A from the spec). The writer's `AuditDbContext` shares the per-request `ITenantScope` transaction (ADR-0090), so the audit row commits atomically with the business change and is fail-closed. `actor_display` is sourced from the JWT via a new `ICurrentUser.DisplayName`. Action strings + target-type literals live in a new `OrganizationAuditActions` constants class.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core (Npgsql), Wolverine (direct-dispatch handlers), PostgreSQL 18 + RLS, MSTest v4 + NSubstitute, Testcontainers (real Postgres + KeyCloak).

**Spec:** [docs/superpowers/specs/2026-06-17-audit-event-wiring-design.md](../specs/2026-06-17-audit-event-wiring-design.md)

## Global Constraints

- **Data values are strings only** — `AuditEntry.Data` is `IReadOnlyDictionary<string, string?>?`; never put raw numbers/floats (jsonb hash-stability rule). `targetId` is a `string`.
- **Audit call sits below all early-return guards** — only successful (state-changing) outcomes append a row; NotFound / conflict / last-OrgAdmin / validation returns emit nothing.
- **No cross-module Infrastructure reference** (ADR-0082) — handlers depend only on the SharedKernel `IAuditWriter`; NetArchTest must stay green.
- **No `System` actor, no Catalog, no `invitation.expired`** in this slice (deferred per spec §2).
- **Build:** `TreatWarningsAsErrors=true` — 0 warnings, 0 errors.
- **Windows shell:** use `cmd //c "dotnet ..."` or PowerShell wrappers for `dotnet`.
- **Solution:** `Kartova.slnx`.

---

## File Structure

**Modify:**
- `src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs` — add `string DisplayName { get; }`.
- `src/Kartova.SharedKernel.AspNetCore/HttpContextCurrentUser.cs` — implement `DisplayName` (claim fallback).
- `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditWriter.cs` — `actorDisplay: currentUser.DisplayName`.
- `tests/Kartova.Testing.Auth/TestJwtSigner.cs` + `KartovaApiFixtureBase.cs` — optional `name` claim.
- The 10 Organization handlers (see Tasks 3–5).
- `docs/product/CHECKLIST.md` — mark `E-01.F-03.S-03` done.

**Create:**
- `src/Modules/Organization/Kartova.Organization.Application/OrganizationAuditActions.cs` — taxonomy constants.
- `tests/Kartova.SharedKernel.AspNetCore.Tests/HttpContextCurrentUserDisplayNameTests.cs` — unit (claim fallback).
- `src/Modules/Organization/Kartova.Organization.IntegrationTests/AuditWiringTests.cs` — gate-5 real-seam tests.

---

## Task 1: `actor_display` snapshot from the JWT

**Files:**
- Modify: `src/Kartova.SharedKernel.AspNetCore/ICurrentUser.cs`
- Modify: `src/Kartova.SharedKernel.AspNetCore/HttpContextCurrentUser.cs`
- Modify: `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditWriter.cs:62-63`
- Modify: `tests/Kartova.Testing.Auth/TestJwtSigner.cs`, `tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs`
- Test: `tests/Kartova.SharedKernel.AspNetCore.Tests/HttpContextCurrentUserDisplayNameTests.cs`

**Interfaces:**
- Produces: `ICurrentUser.DisplayName` (`string`) — claim fallback `name` → `preferred_username` → `email` → `sub`. Consumed by `AuditWriter` (Task 1) and asserted by Task 3.
- Produces: `TestJwtSigner.IssueForTenant(..., string? name = null)` and `KartovaApiFixtureBase.CreateAuthenticatedClientAsync(..., string? nameClaim = null)` — used by Task 3.

- [ ] **Step 1: Write the failing unit test**

Create `tests/Kartova.SharedKernel.AspNetCore.Tests/HttpContextCurrentUserDisplayNameTests.cs`:

```csharp
using System.Security.Claims;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Kartova.SharedKernel.AspNetCore.Tests;

[TestClass]
public sealed class HttpContextCurrentUserDisplayNameTests
{
    private static HttpContextCurrentUser Build(params Claim[] claims)
    {
        var http = Substitute.For<IHttpContextAccessor>();
        http.HttpContext.Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
        });
        return new HttpContextCurrentUser(http, Substitute.For<ITenantContext>());
    }

    [TestMethod]
    public void DisplayName_PrefersNameClaim()
        => Assert.AreEqual("Ada Lovelace", Build(
            new Claim("sub", "s"), new Claim("name", "Ada Lovelace"),
            new Claim("preferred_username", "ada"), new Claim("email", "ada@x.io")).DisplayName);

    [TestMethod]
    public void DisplayName_FallsBackToPreferredUsername()
        => Assert.AreEqual("ada", Build(
            new Claim("sub", "s"), new Claim("preferred_username", "ada"),
            new Claim("email", "ada@x.io")).DisplayName);

    [TestMethod]
    public void DisplayName_FallsBackToEmail()
        => Assert.AreEqual("ada@x.io", Build(
            new Claim("sub", "s"), new Claim("email", "ada@x.io")).DisplayName);

    [TestMethod]
    public void DisplayName_FallsBackToSub()
        => Assert.AreEqual("s", Build(new Claim("sub", "s")).DisplayName);
}
```

- [ ] **Step 2: Run it — verify it fails to compile (no `DisplayName` member)**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests --filter FullyQualifiedName~HttpContextCurrentUserDisplayNameTests"`
Expected: build error — `'ICurrentUser' does not contain a definition for 'DisplayName'`.

- [ ] **Step 3: Add `DisplayName` to the interface**

In `ICurrentUser.cs`, add inside the interface (after `UserId`):

```csharp
    /// <summary>
    /// Human-readable snapshot of the current principal for audit <c>actor_display</c>.
    /// Resolved from JWT claims: <c>name</c> → <c>preferred_username</c> → <c>email</c> → <c>sub</c>.
    /// Captured at write time so an audit row still names who acted even after that
    /// actor is later offboarded (audit foundation decision 4).
    /// </summary>
    string DisplayName { get; }
```

- [ ] **Step 4: Implement `DisplayName` in `HttpContextCurrentUser`**

Add to `HttpContextCurrentUser` (after the `UserId` property):

```csharp
    public string DisplayName
    {
        get
        {
            var user = _http.HttpContext?.User
                       ?? throw new InvalidOperationException("No HttpContext on current request.");
            return user.FindFirstValue("name")
                   ?? user.FindFirstValue("preferred_username")
                   ?? user.FindFirstValue("email")
                   ?? user.FindFirstValue("sub")
                   ?? throw new InvalidOperationException("No identifying claim on current user.");
        }
    }
```

Add `using System.Security.Claims;` if not already present (it is — `FindFirstValue` is used by `UserId`).

- [ ] **Step 5: Run the unit test — verify pass**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.AspNetCore.Tests --filter FullyQualifiedName~HttpContextCurrentUserDisplayNameTests"`
Expected: PASS (4 tests).

- [ ] **Step 6: Use the snapshot in `AuditWriter`**

In `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditWriter.cs`, change the `Create(...)` call:

```csharp
            actorType: AuditActorType.User,
            actorId: currentUser.UserId,
            actorDisplay: currentUser.DisplayName,
```

(was `actorDisplay: null`). Update the class XML comment line that says `actor_display is left null here` to: `actor_display is the JWT display snapshot (name → preferred_username → email → sub).`

- [ ] **Step 7: Add an optional `name` claim to the test signer + fixture**

In `tests/Kartova.Testing.Auth/TestJwtSigner.cs`:
- Add `string? name = null` as the last parameter of `IssueForTenant` and pass it through to `Build`.
- Add `string? name` as the last parameter of `Build`; after the `email` block add:

```csharp
        if (!string.IsNullOrEmpty(name))
        {
            claims.Add(new Claim("name", name));
        }
```

In `tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs`, `CreateAuthenticatedClientAsync`:
- Add `string? nameClaim = null` as the last parameter.
- Pass `name: nameClaim` into the `Signer.IssueForTenant(...)` call.

- [ ] **Step 8: Build the whole solution**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: 0 warnings, 0 errors. (Any other `ICurrentUser` implementer/fake now needs `DisplayName` — if the build flags one, add the property/stub. NSubstitute fakes auto-implement; only hand-rolled fakes need a line.)

- [ ] **Step 9: Commit**

```bash
git add src/Kartova.SharedKernel.AspNetCore tests/Kartova.SharedKernel.AspNetCore.Tests tests/Kartova.Testing.Auth src/Modules/Audit/Kartova.Audit.Infrastructure/AuditWriter.cs
git commit -m "feat(audit): actor_display snapshot from JWT via ICurrentUser.DisplayName"
```

---

## Task 2: Action taxonomy constants

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.Application/OrganizationAuditActions.cs`

**Interfaces:**
- Produces: `OrganizationAuditActions` (action strings) and `AuditTargetTypes` (target-type literals) — consumed by Tasks 3–5.

- [ ] **Step 1: Create the constants file**

```csharp
namespace Kartova.Organization.Application;

/// <summary>
/// Audit action taxonomy for Organization-module mutations (spec §4). Action
/// strings are the stable contract written to <c>audit_log.action</c>; do not
/// rename without a migration of historical rows.
/// </summary>
public static class OrganizationAuditActions
{
    public const string MemberRoleChanged = "member.role_changed";
    public const string MemberOffboarded = "member.offboarded";
    public const string TeamCreated = "team.created";
    public const string TeamUpdated = "team.updated";
    public const string TeamDeleted = "team.deleted";
    public const string TeamMemberAdded = "team.member_added";
    public const string TeamMemberRemoved = "team.member_removed";
    public const string TeamMemberRoleChanged = "team.member_role_changed";
    public const string InvitationCreated = "invitation.created";
    public const string OrgProfileUpdated = "org.profile_updated";
}

/// <summary>Audit <c>target_type</c> literals (spec §4).</summary>
public static class AuditTargetTypes
{
    public const string User = "User";
    public const string Team = "Team";
    public const string Invitation = "Invitation";
    public const string Organization = "Organization";
}
```

- [ ] **Step 2: Build**

Run: `cmd //c "dotnet build src/Modules/Organization/Kartova.Organization.Application"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Application/OrganizationAuditActions.cs
git commit -m "feat(audit): Organization audit action taxonomy constants"
```

---

## Task 3: Wire member events (TDD via real-seam integration tests)

**Files:**
- Test: `src/Modules/Organization/Kartova.Organization.IntegrationTests/AuditWiringTests.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFixture.cs` (add `ReadAuditLogAsync` helper)
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/ChangeMemberRoleHandler.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure/OffboardMemberHandler.cs`

**Interfaces:**
- Consumes: `OrganizationAuditActions`, `AuditTargetTypes` (Task 2); `IAuditWriter`/`AuditEntry` (SharedKernel); `ICurrentUser.DisplayName` + signer `nameClaim` (Task 1).
- Produces: the `member.role_changed` and `member.offboarded` audit rows asserted by the tests.

- [ ] **Step 1: Add the audit-read helper to the fixture**

In `KartovaApiFixture.cs`, add (uses the BYPASSRLS connection so the test process — which is not in a tenant scope — can read rows):

```csharp
    /// <summary>Reads audit_log rows for a tenant via the BYPASSRLS pool, ordered by seq.</summary>
    public async Task<IReadOnlyList<AuditRowRecord>> ReadAuditLogAsync(Guid tenantId)
    {
        await using var conn = new NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT seq, action, actor_id, actor_display, target_type, target_id,
                   data::text, prev_hash, row_hash
            FROM audit_log WHERE tenant_id = $1 ORDER BY seq
            """;
        cmd.Parameters.AddWithValue(tenantId);
        var rows = new List<AuditRowRecord>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            rows.Add(new AuditRowRecord(
                r.GetInt64(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetGuid(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                (byte[])r[7], (byte[])r[8]));
        }
        return rows;
    }

    public sealed record AuditRowRecord(
        long Seq, string Action, Guid? ActorId, string? ActorDisplay,
        string TargetType, string TargetId, string? DataJson, byte[] PrevHash, byte[] RowHash);
```

- [ ] **Step 2: Write the failing integration tests**

Create `AuditWiringTests.cs`. Model auth/seeding on `ChangeMemberRoleTests` and `OffboardMemberTests` (real KeyCloak user provisioned via the invitation flow). Use a distinct tenant per test via a unique email domain.

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Organization.Application;
using Kartova.Testing.Auth;

namespace Kartova.Organization.IntegrationTests;

[TestClass]
public sealed class AuditWiringTests : OrganizationIntegrationTestBase
{
    // --- Happy: role change writes a correct, chained audit row ---
    [TestMethod]
    public async Task ChangeRole_WritesMemberRoleChangedAuditRow()
    {
        // Arrange: an OrgAdmin acting principal (with a name claim) + a target Member
        // provisioned via the invitation flow so KeyCloak ChangeRealmRoleAsync succeeds.
        var (admin, adminEmail, target, tenantId) = await SeedAdminAndMemberAsync(nameClaim: "Ada Lovelace");

        // Act
        var resp = await admin.PutAsJsonAsync(
            $"/api/v1/organizations/users/{target}/role", new { role = "OrgAdmin" });
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);

        // Assert
        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r => r.Action == OrganizationAuditActions.MemberRoleChanged);
        Assert.AreEqual(await Fx.GetSubClaimAsync(adminEmail), row.ActorId);
        Assert.AreEqual("Ada Lovelace", row.ActorDisplay);
        Assert.AreEqual(AuditTargetTypes.User, row.TargetType);
        Assert.AreEqual(target.ToString(), row.TargetId);
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Member", data.RootElement.GetProperty("oldRole").GetString());
        Assert.AreEqual("OrgAdmin", data.RootElement.GetProperty("newRole").GetString());
        AssertChainLinked(rows);
    }

    // --- Happy: offboard snapshot survives the target's hard-delete ---
    [TestMethod]
    public async Task Offboard_WritesSnapshotThatSurvivesTargetDeletion()
    {
        var (admin, _, target, tenantId) = await SeedAdminAndMemberAsync(nameClaim: "Grace H");

        var resp = await admin.DeleteAsync($"/api/v1/organizations/users/{target}");
        Assert.AreEqual(HttpStatusCode.NoContent, resp.StatusCode);

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r => r.Action == OrganizationAuditActions.MemberOffboarded);
        Assert.AreEqual(target.ToString(), row.TargetId);
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.IsFalse(string.IsNullOrEmpty(data.RootElement.GetProperty("email").GetString()));
        // The users row was hard-deleted in the same txn, yet the snapshot persisted.
    }

    // --- Negative: a rejected mutation writes no audit row ---
    [TestMethod]
    public async Task ChangeRole_LastOrgAdminGuard_WritesNoAuditRow()
    {
        // A tenant with exactly one OrgAdmin; demoting them is rejected (409) before SaveChanges.
        var (admin, _, soleAdminId, tenantId) = await SeedSoleOrgAdminAsync();

        var resp = await admin.PutAsJsonAsync(
            $"/api/v1/organizations/users/{soleAdminId}/role", new { role = "Member" });
        Assert.AreEqual(HttpStatusCode.Conflict, resp.StatusCode);

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        Assert.AreEqual(0, rows.Count(r => r.Action == OrganizationAuditActions.MemberRoleChanged));
    }

    private static void AssertChainLinked(IReadOnlyList<KartovaApiFixture.AuditRowRecord> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            Assert.AreEqual(i + 1, rows[i].Seq, "seq must be contiguous from 1");
            var expectedPrev = i == 0 ? new byte[32] : rows[i - 1].RowHash;
            CollectionAssert.AreEqual(expectedPrev, rows[i].PrevHash, "prev_hash must link to predecessor row_hash");
        }
    }
}
```

> **Helper note:** `SeedAdminAndMemberAsync(nameClaim)`, `SeedSoleOrgAdminAsync()`, and KC cleanup follow the exact provisioning + teardown idioms already in `ChangeMemberRoleTests.cs` / `OffboardMemberTests.cs` (invitation-flow user creation, `CreateAuthenticatedClientAsync(email, roles, nameClaim:)`, `Fx.DeleteUserInOrganizationAsync`). Reuse those — do not invent a new seeding path. Place the helpers as private methods in this test class (or lift shared ones into `OrganizationIntegrationTestBase` if they already exist there).

- [ ] **Step 3: Run the tests — verify they fail (no audit rows written yet)**

Run: `cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter FullyQualifiedName~AuditWiringTests"`
Expected: the two happy tests FAIL on `rows.Single(...)` (sequence contains no matching element) because the handlers don't append yet; the negative test already passes.

- [ ] **Step 4: Wire `ChangeMemberRoleHandler`**

Replace the handler body so it captures the old role and appends after the business save:

```csharp
using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Organization.Infrastructure;

public sealed class ChangeMemberRoleHandler(IKeycloakAdminClient keycloak, IAuditWriter audit)
{
    public async Task<ChangeMemberRoleOutcome> Handle(
        ChangeMemberRoleCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        if (!KartovaRoles.All.Contains(cmd.Role))
            return ChangeMemberRoleOutcome.InvalidRole;

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct);
        if (user is null) return ChangeMemberRoleOutcome.NotFound;

        if (cmd.Role != KartovaRoles.OrgAdmin && await OrgAdminFloor.IsLastOrgAdminAsync(db, user, ct))
            return ChangeMemberRoleOutcome.LastOrgAdmin;

        var oldRole = user.RealmRole;                                     // snapshot before write-through
        await keycloak.ChangeRealmRoleAsync(cmd.UserId, cmd.Role, ct);    // source of truth
        user.RealmRole = cmd.Role;                                        // write-through cache
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.MemberRoleChanged,
            AuditTargetTypes.User,
            cmd.UserId.ToString(),
            new Dictionary<string, string?> { ["oldRole"] = oldRole, ["newRole"] = cmd.Role }), ct);

        return ChangeMemberRoleOutcome.Success;
    }
}
```

- [ ] **Step 5: Wire `OffboardMemberHandler`**

Capture the target snapshot before deletion; append after the save. Add the ctor dep and the snapshot/append:

```csharp
public sealed class OffboardMemberHandler(IKeycloakAdminClient keycloak, IAuditWriter audit)
{
    public async Task<OffboardMemberOutcome> Handle(
        OffboardMemberCommand cmd, OrganizationDbContext db, CancellationToken ct)
    {
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.Target.Value, ct);
        if (target is null) return OffboardMemberOutcome.NotFound;
        if (cmd.Target.Value == cmd.Actor.Value) return OffboardMemberOutcome.CannotOffboardSelf;

        if (await OrgAdminFloor.IsLastOrgAdminAsync(db, target, ct))
            return OffboardMemberOutcome.LastOrgAdmin;

        // Snapshot the target's identifying fields BEFORE the hard-delete (ADR-0102) so the
        // audit row still names who was offboarded.
        var targetDisplay = target.DisplayName;
        var targetEmail = target.Email;

        try
        {
            await keycloak.DeleteUserAsync(cmd.Target.Value, ct);
        }
        catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound)
        {
            // Idempotent: KC identity already gone.
        }

        var memberships = await db.TeamMembers.Where(m => m.UserId == cmd.Target.Value).ToListAsync(ct);
        db.TeamMembers.RemoveRange(memberships);
        db.Users.Remove(target);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.MemberOffboarded,
            AuditTargetTypes.User,
            cmd.Target.Value.ToString(),
            new Dictionary<string, string?> { ["displayName"] = targetDisplay, ["email"] = targetEmail }), ct);

        return OffboardMemberOutcome.Offboarded;
    }
}
```

Add `using Kartova.Organization.Application;` and `using Kartova.SharedKernel.Audit;` to the file's usings.

- [ ] **Step 6: Run the integration tests — verify pass**

Run: `cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter FullyQualifiedName~AuditWiringTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Run the existing member-handler tests to confirm no regression**

Run: `cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter FullyQualifiedName~ChangeMemberRoleTests|FullyQualifiedName~OffboardMemberTests"`
Expected: PASS. Also run the unit `OffboardMemberHandlerTests` — it constructs the handler; add an `IAuditWriter` substitute (`Substitute.For<IAuditWriter>()`) to the ctor call if it news the handler directly.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure/ChangeMemberRoleHandler.cs src/Modules/Organization/Kartova.Organization.Infrastructure/OffboardMemberHandler.cs src/Modules/Organization/Kartova.Organization.IntegrationTests
git commit -m "feat(audit): wire member.role_changed + member.offboarded events"
```

---

## Task 4: Wire team events

**Files (modify):**
- `CreateTeamHandler.cs`, `UpdateTeamHandler.cs`, `DeleteTeamHandler.cs`, `AddTeamMemberHandler.cs`, `RemoveTeamMemberHandler.cs`, `UpdateTeamMemberHandler.cs` (all in `Kartova.Organization.Infrastructure`).

**Interfaces:**
- Consumes: `OrganizationAuditActions`, `AuditTargetTypes`, `IAuditWriter`, `AuditEntry`.

> Each handler gains an `IAuditWriter audit` constructor parameter and an `AppendAsync` on its success path only. Add `using Kartova.SharedKernel.Audit;` (and `using Kartova.Organization.Application;` where missing) to each file. `TeamRole`/role enums are written with `.ToString()`.

- [ ] **Step 1: `CreateTeamHandler` — append `team.created`**

Add `IAuditWriter audit` to the existing constructor (`clock`, `tenant`, `audit`); after `await db.SaveChangesAsync(ct);`:

```csharp
        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.TeamCreated, AuditTargetTypes.Team, team.Id.Value.ToString(),
            new Dictionary<string, string?> { ["displayName"] = team.DisplayName }), ct);
```

- [ ] **Step 2: `UpdateTeamHandler` — append `team.updated`**

Give it a primary ctor `(IAuditWriter audit)`; after `await db.SaveChangesAsync(ct);` (success path, `team` non-null):

```csharp
        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.TeamUpdated, AuditTargetTypes.Team, team.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = team.DisplayName,
                ["description"] = team.Description,
            }), ct);
```

- [ ] **Step 3: `DeleteTeamHandler` — append `team.deleted` (success branch only)**

Add `IAuditWriter audit` to the ctor (alongside `_appCountReader`). Capture `team.DisplayName` before `Remove`; append only on the `Deleted` branch:

```csharp
        var displayName = team.DisplayName;
        db.Teams.Remove(team);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.TeamDeleted, AuditTargetTypes.Team, team.Id.Value.ToString(),
            new Dictionary<string, string?> { ["displayName"] = displayName }), ct);
        return new DeleteTeamResult(true, false, null);
```

(The `NotFound` and `ApplicationsAssigned > 0` early returns get no audit row.)

- [ ] **Step 4: `AddTeamMemberHandler` — append `team.member_added` (Added branch only)**

Add `IAuditWriter audit` to the existing `(clock)` ctor. After `await db.SaveChangesAsync(ct);` (success, before building the success result):

```csharp
        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.TeamMemberAdded, AuditTargetTypes.Team, cmd.TeamId.ToString(),
            new Dictionary<string, string?>
            {
                ["userId"] = cmd.UserId.ToString(),
                ["role"] = cmd.Role.ToString(),
            }), ct);
```

- [ ] **Step 5: `RemoveTeamMemberHandler` — append `team.member_removed` (Removed branch only)**

Add primary ctor `(IAuditWriter audit)`. After `await db.SaveChangesAsync(ct);`:

```csharp
        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.TeamMemberRemoved, AuditTargetTypes.Team, cmd.TeamId.ToString(),
            new Dictionary<string, string?> { ["userId"] = cmd.UserId.ToString() }), ct);
```

- [ ] **Step 6: `UpdateTeamMemberHandler` — append `team.member_role_changed` (Updated branch only)**

Add primary ctor `(IAuditWriter audit)`. Capture the old role before `ChangeRole`; append after save:

```csharp
        var oldRole = membership.Role;
        membership.ChangeRole(cmd.NewRole);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.TeamMemberRoleChanged, AuditTargetTypes.Team, cmd.TeamId.ToString(),
            new Dictionary<string, string?>
            {
                ["userId"] = cmd.UserId.ToString(),
                ["oldRole"] = oldRole.ToString(),
                ["newRole"] = cmd.NewRole.ToString(),
            }), ct);
```

- [ ] **Step 7: Build**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: 0 warnings, 0 errors. If any unit test directly news one of these handlers, pass `Substitute.For<IAuditWriter>()`.

- [ ] **Step 8: Run the team-handler test suites (regression)**

Run: `cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter FullyQualifiedName~TeamTests|FullyQualifiedName~TeamMemberTests"`
Expected: PASS (CreateTeam/UpdateTeam/DeleteTeam/AddTeamMember/RemoveTeamMember/UpdateTeamMember suites green).

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure
git commit -m "feat(audit): wire team create/update/delete + membership events"
```

---

## Task 5: Wire invitation + org-profile events

**Files (modify):**
- `CreateInvitationHandler.cs`, `UpdateOrgProfileHandler.cs` (in `Kartova.Organization.Infrastructure`).

**Interfaces:**
- Consumes: `OrganizationAuditActions`, `AuditTargetTypes`, `IAuditWriter`, `AuditEntry`.

- [ ] **Step 1: `CreateInvitationHandler` — append `invitation.created` (Created path only)**

Add `IAuditWriter audit` to the primary constructor parameter list (after `logger`). After the final successful `await db.SaveChangesAsync(ct);` (i.e. after the catch blocks, before building `inviteUrl`):

```csharp
        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.InvitationCreated,
            AuditTargetTypes.Invitation,
            invitation.Id.Value.ToString(),
            new Dictionary<string, string?> { ["email"] = email, ["role"] = request.Role }), ct);
```

Add `using Kartova.SharedKernel.Audit;`. (Every `Failed(...)` return is above this line, so failed conflicts emit no row.)

- [ ] **Step 2: `UpdateOrgProfileHandler` — append `org.profile_updated` (Ok path only)**

Add `IAuditWriter audit` to the constructor (alongside `_db`). After the successful `await _db.SaveChangesAsync(ct);` (before `return UpdateOrgProfileResult.Ok;`):

```csharp
        await audit.AppendAsync(new AuditEntry(
            OrganizationAuditActions.OrgProfileUpdated,
            AuditTargetTypes.Organization,
            org.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = request.DisplayName,
                ["defaultTimeZone"] = request.DefaultTimeZone,
            }), ct);
```

Add `using Kartova.Organization.Application;` and `using Kartova.SharedKernel.Audit;`. (`NotFound` and `ConcurrencyConflict` returns are above/separate, so they emit no row.)

- [ ] **Step 3: Build**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: 0 warnings, 0 errors. The `CreateInvitationHandlerTests` unit test news the handler directly — add `Substitute.For<IAuditWriter>()` as the new ctor argument.

- [ ] **Step 4: Run invitation + org-profile suites (regression)**

Run: `cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter FullyQualifiedName~InvitationTests|FullyQualifiedName~OrgProfileAndLogoTests"` and `cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.Infrastructure.Tests --filter FullyQualifiedName~CreateInvitationHandlerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Infrastructure src/Modules/Organization/Kartova.Organization.Infrastructure.Tests
git commit -m "feat(audit): wire invitation.created + org.profile_updated events"
```

---

## Task 6: Checklist update + terminal verification

**Files:**
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Run the architecture suite (no cross-module leak)**

Run: `cmd //c "dotnet test tests/Kartova.ArchitectureTests"`
Expected: PASS — confirms Organization still depends only on the SharedKernel `IAuditWriter`, not `Audit.Infrastructure`.

- [ ] **Step 2: Full solution build**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Full test suite**

Run: `cmd //c "dotnet test Kartova.slnx"`
Expected: all green (unit + architecture + integration). Investigate any failure before proceeding.

- [ ] **Step 4: Mark the story done**

In `docs/product/CHECKLIST.md`, change line 46 (`E-01.F-03.S-03`) marker `[~]` → `[x]` and append:

```
 Phase 2 (audit-event-wiring, 2026-06-17): 10 Organization mutations wired to IAuditWriter (member role-change/offboard, team CRUD + membership, invitation.created, org.profile_updated); actor_display snapshot from JWT. Catalog events + System-actor/expiry-sweep deferred.
```

Update the Phase 0 progress count in the summary table accordingly (Foundation `10/33` → `11/33`).

- [ ] **Step 5: Commit**

```bash
git add docs/product/CHECKLIST.md
git commit -m "docs(checklist): close E-01.F-03.S-03 — audit event wiring (Phase 2)"
```

---

## Definition of Done (per CLAUDE.md — links the nine gates; not restated)

Run the eight always-blocking gates after Task 6, in fail-fast order. **Gate 6 (mutation) is should-do** here — this slice adds wiring + constants + one claim-fallback method, no Domain/Application business logic; run `/misc:mutation-sentinel` on `HttpContextCurrentUser.DisplayName` + the handler payload-shaping if practical, else skip with this note. **Gate 4 (container build):** no Dockerfile/new-project change, so the existing `images` job covers it — flag if a new file lands outside an already-copied path. **Terminal re-verify:** after gates 5–9, re-run `dotnet build Kartova.slnx` + `dotnet test Kartova.slnx` and confirm green before claiming completion.

## Self-Review (completed by author)

- **Spec coverage:** §2 scope (10 events, deferrals) → Tasks 2–5 + DoD note. §3 decisions 1,4 (inline-on-success) → Tasks 3–5 placement. Decision 2 (actor_display/JWT) → Task 1. Decision 3 (taxonomy in Application) → Task 2. Decision 5 (offboard pre-delete snapshot) → Task 3 Step 5. §4 payload table → Tasks 3–5 verbatim. §7 gate-5 artifacts (3 integration tests + unit fallback) → Tasks 1 + 3. §8 (checklist close, gate-6 should-do) → Task 6 + DoD. No gaps.
- **Placeholder scan:** none — every code step shows full code; the only prose deferral (test seeding helpers) points to named existing files/idioms.
- **Type consistency:** `OrganizationAuditActions.*` / `AuditTargetTypes.*` (Task 2) used verbatim in 3–5; `AuditEntry(Action, TargetType, TargetId, Data)` matches the SharedKernel record; `ICurrentUser.DisplayName` (Task 1) consumed by `AuditWriter` + asserted in Task 3; `ReadAuditLogAsync`/`AuditRowRecord` defined in Task 3 Step 1 and used in Step 2.
