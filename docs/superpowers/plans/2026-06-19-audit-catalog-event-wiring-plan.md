# Audit Catalog Event-Wiring Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the 7 Catalog application mutations (register / edit / 4 lifecycle transitions / team-assign) to the append-only audit log, closing the last deferred chunk of E-01.F-03.S-03.

**Architecture:** Inline `IAuditWriter.AppendAsync` on each handler's success path, after the business `SaveChangesAsync`, riding the same per-request `ITenantScope` transaction (fail-closed). `IAuditWriter` is injected as a Wolverine **method parameter** (matching Catalog's existing `db`/`tenant`/`user` injection style). A new Catalog-local action taxonomy supplies the stable `audit_log.action`/`target_type` strings. No audit mechanism changes — `ICurrentUser.DisplayName` + `actor_display` already shipped 2026-06-17.

**Tech Stack:** .NET 10 / C#, Wolverine (direct-dispatch handlers), EF Core + Npgsql, PostgreSQL 18 (RLS), MSTest v4 + NSubstitute, Testcontainers (real-seam integration tests).

## Global Constraints

- `TreatWarningsAsErrors=true` — 0 warnings, 0 errors (gate 1).
- `AuditEntry.Data` values are **strings only** (jsonb-hash-stability rule); `null` for absent. `targetId` is a string.
- No cross-module reference from `Kartova.Catalog` to `Kartova.Organization.*` or `Kartova.Audit.Infrastructure` (ADR-0082). Handlers see only `Kartova.SharedKernel.Audit.IAuditWriter`. NetArchTest must stay green.
- Audit append is on the **success path only** — rejected/not-found/conflict early-returns and thrown lifecycle/concurrency exceptions emit no row.
- Solution file: `Kartova.slnx`. Windows shell: PowerShell or `cmd //c` wrappers for `dotnet`.
- All `data` values via `.ToString()`; `DateTimeOffset` via `.ToString("O")` (round-trip).

---

## File Structure

**Production (Kartova.Catalog):**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs` — action + target-type taxonomy constants.
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogAuditEntries.cs` — shared `AuditEntry` factory for the 4 lifecycle handlers (DRY).
- Modify: `RegisterApplicationHandler.cs`, `EditApplicationHandler.cs`, `DeprecateApplicationHandler.cs`, `DecommissionApplicationHandler.cs`, `ReactivateApplicationHandler.cs`, `UnDecommissionApplicationHandler.cs`, `AssignApplicationTeamHandler.cs` (all in `Kartova.Catalog.Infrastructure`).

**Test (Kartova.Catalog.IntegrationTests + Kartova.Catalog.Infrastructure.Tests):**
- Modify: `Kartova.Catalog.IntegrationTests.csproj` — add Audit project reference.
- Modify: `Kartova.Catalog.IntegrationTests/KartovaApiFixture.cs` — migrate `AuditDbContext`; add `ReadAuditLogAsync` + `AuditRowRecord`.
- Create: `Kartova.Catalog.IntegrationTests/AuditWiringTests.cs` — real-seam happy + negative cases.
- Create: `Kartova.Catalog.Infrastructure.Tests/CatalogAuditEntriesTests.cs` — unit test for lifecycle payload shaping.

---

## Task 1: Test scaffolding — audit schema migration + read helper in the Catalog fixture

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj`
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/KartovaApiFixture.cs:29-37` (RunModuleMigrationsAsync) and end-of-class (new helper)

**Interfaces:**
- Consumes: `PostgresTestBootstrap.RunMigrationsAsync<TContext>`, `Kartova.Audit.Infrastructure.AuditDbContext`, `BypassConnectionString` (all existing).
- Produces: `Fx.ReadAuditLogAsync(Guid tenantId) → Task<IReadOnlyList<KartovaApiFixture.AuditRowRecord>>`; record `AuditRowRecord(long Seq, string Action, Guid? ActorId, string? ActorDisplay, string TargetType, string TargetId, string? DataJson, byte[] PrevHash, byte[] RowHash, string ActorType)`.

- [ ] **Step 1: Add the Audit project reference to the test csproj**

In `Kartova.Catalog.IntegrationTests.csproj`, add this `ProjectReference` inside the existing `<ItemGroup>` that holds the other references:

```xml
    <!-- Audit event-wiring: AuditWiringTests assert rows in audit_log, which requires the
         audit schema to be migrated in the shared Postgres container. Direct reference so
         RunModuleMigrationsAsync can call PostgresTestBootstrap.RunMigrationsAsync<AuditDbContext>. -->
    <ProjectReference Include="..\..\Audit\Kartova.Audit.Infrastructure\Kartova.Audit.Infrastructure.csproj" />
```

- [ ] **Step 2: Migrate the Audit schema in the fixture**

In `KartovaApiFixture.cs`, add the using at the top (after `using Kartova.Catalog.Infrastructure;`):

```csharp
using Kartova.Audit.Infrastructure;
```

Then extend `RunModuleMigrationsAsync` (currently migrates Catalog + Organization) to also migrate the audit schema:

```csharp
    protected override async Task RunModuleMigrationsAsync(string migratorConnectionString)
    {
        await PostgresTestBootstrap.RunMigrationsAsync<CatalogDbContext>(
            migratorConnectionString,
            opts => new CatalogDbContext(opts));
        await PostgresTestBootstrap.RunMigrationsAsync<OrganizationDbContext>(
            migratorConnectionString,
            opts => new OrganizationDbContext(opts));
        // Audit event-wiring: AuditWiringTests assert rows in audit_log; the table must exist
        // before the test host starts. Mirrors the Organization fixture.
        await PostgresTestBootstrap.RunMigrationsAsync<AuditDbContext>(
            migratorConnectionString,
            opts => new AuditDbContext(opts));
    }
```

- [ ] **Step 3: Add the `ReadAuditLogAsync` helper + `AuditRowRecord`**

Add just before the closing brace of `KartovaApiFixture` (mirrors the Organization fixture; uses the fully-qualified `Npgsql.NpgsqlConnection` to avoid a new using):

```csharp
    /// <summary>Reads audit_log rows for a tenant via the BYPASSRLS pool, ordered by seq.</summary>
    public async Task<IReadOnlyList<AuditRowRecord>> ReadAuditLogAsync(Guid tenantId)
    {
        await using var conn = new Npgsql.NpgsqlConnection(BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT seq, action, actor_id, actor_display, target_type, target_id,
                   data::text, prev_hash, row_hash, actor_type
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
                (byte[])r[7], (byte[])r[8],
                r.GetString(9)));
        }
        return rows;
    }

    public sealed record AuditRowRecord(
        long Seq, string Action, Guid? ActorId, string? ActorDisplay,
        string TargetType, string TargetId, string? DataJson, byte[] PrevHash, byte[] RowHash,
        string ActorType);
```

- [ ] **Step 4: Build the test project to verify it compiles**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj"`
Expected: Build succeeded, 0 warnings, 0 errors. (No behavior change yet — this is scaffolding consumed by Tasks 3–6.)

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/
git commit -m "test(audit): migrate audit schema + add ReadAuditLogAsync to Catalog fixture"
```

---

## Task 2: Catalog audit taxonomy

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs`

**Interfaces:**
- Produces: `CatalogAuditActions` (consts `ApplicationRegistered`, `ApplicationEdited`, `ApplicationLifecycleChanged`, `ApplicationTeamAssigned`); `CatalogAuditTargetTypes` (const `Application`). Consumed by every handler in Tasks 3–6 and by `CatalogAuditEntries` (Task 5).

- [ ] **Step 1: Create the taxonomy file**

```csharp
namespace Kartova.Catalog.Application;

/// <summary>
/// Audit action taxonomy for Catalog-module mutations (design §4). Action strings
/// are the stable contract written to <c>audit_log.action</c>; do not rename without
/// a migration of historical rows. The four lifecycle transitions share a single
/// <c>application.lifecycle_changed</c> action, distinguished by <c>from</c>/<c>to</c>
/// in the row's <c>data</c>.
/// </summary>
public static class CatalogAuditActions
{
    public const string ApplicationRegistered = "application.registered";
    public const string ApplicationEdited = "application.edited";
    public const string ApplicationLifecycleChanged = "application.lifecycle_changed";
    public const string ApplicationTeamAssigned = "application.team_assigned";
}

/// <summary>
/// Audit <c>target_type</c> literals for Catalog (design §4). Catalog-local because
/// it cannot reference Organization's <c>AuditTargetTypes</c> (ADR-0082).
/// </summary>
public static class CatalogAuditTargetTypes
{
    public const string Application = "Application";
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Application/Kartova.Catalog.Application.csproj"`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs
git commit -m "feat(audit): add Catalog audit action taxonomy"
```

---

## Task 3: Wire `application.registered`

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterApplicationHandler.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/AuditWiringTests.cs` (create)

**Interfaces:**
- Consumes: `CatalogAuditActions`, `CatalogAuditTargetTypes` (Task 2); `Fx.ReadAuditLogAsync`, `Fx.AuditRowRecord` (Task 1); `IAuditWriter` (SharedKernel); `AuditEntry` (SharedKernel).

- [ ] **Step 1: Write the failing test**

Create `AuditWiringTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class AuditWiringTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";

    // --- Happy: register writes a correct, chained audit row ---
    [TestMethod]
    public async Task Register_WritesApplicationRegisteredAuditRow()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Audit Reg Team");
        var client = await Fx.CreateAuthenticatedClientAsync(
            OrgAUser, new[] { KartovaRoles.OrgAdmin }, nameClaim: "Ada Catalog");

        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest("Audit Reg App", "Desc.", teamId));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode,
            $"Expected 201. Body: {await resp.Content.ReadAsStringAsync()}");
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.ApplicationRegistered &&
            r.TargetId == body!.Id.ToString());
        Assert.AreEqual(await Fx.GetSubClaimAsync(OrgAUser), row.ActorId);
        Assert.AreEqual("Ada Catalog", row.ActorDisplay);
        Assert.AreEqual(CatalogAuditTargetTypes.Application, row.TargetType);
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Audit Reg App", data.RootElement.GetProperty("displayName").GetString());
        Assert.AreEqual(teamId.ToString(), data.RootElement.GetProperty("teamId").GetString());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~AuditWiringTests.Register_WritesApplicationRegisteredAuditRow"`
Expected: FAIL — `rows.Single(...)` throws because no `application.registered` row was written.

- [ ] **Step 3: Wire the handler**

Replace the body of `RegisterApplicationHandler.cs`. Add `using Kartova.SharedKernel.Audit;`, add the `IAuditWriter audit` method parameter, and append after `SaveChangesAsync`:

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Infrastructure;

// (XML doc comment unchanged)
public sealed class RegisterApplicationHandler
{
    private readonly TimeProvider _clock;

    public RegisterApplicationHandler(TimeProvider clock) => _clock = clock;

    public async Task<ApplicationResponse> Handle(
        RegisterApplicationCommand cmd,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var app = Kartova.Catalog.Domain.Application.Create(
            cmd.DisplayName, cmd.Description, user.UserId, cmd.TeamId, tenant.Id, _clock);
        db.Applications.Add(app);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.ApplicationRegistered,
            CatalogAuditTargetTypes.Application,
            app.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = app.DisplayName,
                ["teamId"] = app.TeamId.ToString(),
            }), ct);
        return app.ToResponse();
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~AuditWiringTests.Register_WritesApplicationRegisteredAuditRow"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/RegisterApplicationHandler.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/AuditWiringTests.cs
git commit -m "feat(audit): wire application.registered event"
```

---

## Task 4: Wire `application.edited`

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EditApplicationHandler.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/AuditWiringTests.cs` (add method)

**Interfaces:**
- Consumes: same as Task 3. Edit requires an `If-Match` ETag header carrying the current `Version`; the register response's `Version` supplies it.

- [ ] **Step 1: Add the failing test**

Add to `AuditWiringTests.cs` (helper + test):

```csharp
    private async Task<ApplicationResponse> RegisterAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync(
            "/api/v1/catalog/applications",
            new RegisterApplicationRequest(name, "Desc.", teamId));
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode,
            $"Expected 201. Body: {await resp.Content.ReadAsStringAsync()}");
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!;
    }

    // --- Happy: edit writes an application.edited row with the new state ---
    [TestMethod]
    public async Task Edit_WritesApplicationEditedAuditRow()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Audit Edit Team");
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser, new[] { KartovaRoles.OrgAdmin });
        var app = await RegisterAsync(client, teamId, "Audit Edit App");

        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/catalog/applications/{app.Id}")
        {
            Content = JsonContent.Create(new EditApplicationRequest("Edited Name", "Edited desc.")),
        };
        req.Headers.TryAddWithoutValidation("If-Match", $"\"{app.Version}\"");
        var resp = await client.SendAsync(req);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
            $"Expected 200. Body: {await resp.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.ApplicationEdited &&
            r.TargetId == app.Id.ToString());
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Edited Name", data.RootElement.GetProperty("displayName").GetString());
        Assert.AreEqual("Edited desc.", data.RootElement.GetProperty("description").GetString());
    }
```

> Note: confirm the `If-Match` ETag format against an existing passing test in `EditApplicationTests.cs` (it already exercises this endpoint). If that test builds the header differently (e.g. via a weak-ETag helper), copy its exact construction here rather than the literal above.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~AuditWiringTests.Edit_WritesApplicationEditedAuditRow"`
Expected: FAIL — no `application.edited` row.

- [ ] **Step 3: Wire the handler**

In `EditApplicationHandler.cs`, add `using Kartova.SharedKernel.Audit;`, add the `IAuditWriter audit` parameter, and append after the `try/catch` (so a concurrency conflict — which `throw`s — never reaches it):

```csharp
    public async Task<ApplicationResponse?> Handle(
        EditApplicationCommand cmd,
        CatalogDbContext db,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        db.Entry(app).Property(a => a.Version).OriginalValue = cmd.ExpectedVersion;

        app.EditMetadata(cmd.DisplayName, cmd.Description);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await TryCaptureCurrentVersionAsync(ex, ct);
            throw;
        }

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.ApplicationEdited,
            CatalogAuditTargetTypes.Application,
            app.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["displayName"] = app.DisplayName,
                ["description"] = app.Description,
            }), ct);

        return app.ToResponse();
    }
```

(`TryCaptureCurrentVersionAsync` is unchanged.)

- [ ] **Step 4: Run the test to verify it passes**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~AuditWiringTests.Edit_WritesApplicationEditedAuditRow"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/EditApplicationHandler.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/AuditWiringTests.cs
git commit -m "feat(audit): wire application.edited event"
```

---

## Task 5: Wire `application.lifecycle_changed` (4 handlers) + unit test

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogAuditEntries.cs`
- Modify: `DeprecateApplicationHandler.cs`, `DecommissionApplicationHandler.cs`, `ReactivateApplicationHandler.cs`, `UnDecommissionApplicationHandler.cs`
- Test: `Kartova.Catalog.Infrastructure.Tests/CatalogAuditEntriesTests.cs` (create); `AuditWiringTests.cs` (add happy + negative)

**Interfaces:**
- Produces: `CatalogAuditEntries.LifecycleChanged(Kartova.Catalog.Domain.Application app, Kartova.Catalog.Domain.Lifecycle from) → AuditEntry`. Used by all 4 lifecycle handlers. `from` is the pre-transition lifecycle; `to`/`sunsetDate` are read from the post-transition `app`.
- Consumes: `CatalogAuditActions`, `CatalogAuditTargetTypes`, `AuditEntry`.

- [ ] **Step 1: Write the failing unit test for the payload factory**

Create `Kartova.Catalog.Infrastructure.Tests/CatalogAuditEntriesTests.cs`:

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.Catalog.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.Extensions.Time.Testing;

namespace Kartova.Catalog.Infrastructure.Tests;

[TestClass]
public class CatalogAuditEntriesTests
{
    private static Application NewDeprecatedApp(out DateTimeOffset sunset)
    {
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(DateTimeOffset.Parse("2026-06-19T10:00:00Z"));
        // Application.Create(displayName, description, createdByUserId, teamId, tenantId, clock)
        var app = Application.Create(
            "App", "Desc", Guid.NewGuid(), Guid.NewGuid(), new TenantId(Guid.NewGuid()), clock);
        sunset = clock.GetUtcNow().AddDays(30);
        app.Deprecate(sunset, clock);
        return app;
    }

    [TestMethod]
    public void LifecycleChanged_CapturesFromToAndSunsetDate()
    {
        var app = NewDeprecatedApp(out var sunset);

        var entry = CatalogAuditEntries.LifecycleChanged(app, from: Lifecycle.Active);

        Assert.AreEqual(CatalogAuditActions.ApplicationLifecycleChanged, entry.Action);
        Assert.AreEqual(CatalogAuditTargetTypes.Application, entry.TargetType);
        Assert.AreEqual(app.Id.Value.ToString(), entry.TargetId);
        Assert.AreEqual("Active", entry.Data!["from"]);
        Assert.AreEqual("Deprecated", entry.Data!["to"]);
        Assert.AreEqual(sunset.ToString("O"), entry.Data!["sunsetDate"]);
    }

    [TestMethod]
    public void LifecycleChanged_NullSunsetDate_SerializesAsNull()
    {
        var clock = new FakeTimeProvider();
        clock.SetUtcNow(DateTimeOffset.Parse("2026-06-19T10:00:00Z"));
        var app = Application.Create(
            "App", "Desc", Guid.NewGuid(), Guid.NewGuid(), new TenantId(Guid.NewGuid()), clock);
        app.Deprecate(clock.GetUtcNow().AddDays(30), clock);
        app.Reactivate(); // clears SunsetDate, lifecycle -> Active

        var entry = CatalogAuditEntries.LifecycleChanged(app, from: Lifecycle.Deprecated);

        Assert.AreEqual("Deprecated", entry.Data!["from"]);
        Assert.AreEqual("Active", entry.Data!["to"]);
        Assert.IsNull(entry.Data!["sunsetDate"]);
    }
}
```

> Note: verify the exact `Application.Create` parameter order against `Application.cs` (`displayName, description, createdByUserId, teamId, tenantId, clock`) before finalizing — the test relies on it.

- [ ] **Step 2: Run the unit test to verify it fails**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/Kartova.Catalog.Infrastructure.Tests.csproj --filter FullyQualifiedName~CatalogAuditEntriesTests"`
Expected: FAIL — `CatalogAuditEntries` does not exist (compile error).

- [ ] **Step 3: Create the payload factory**

Create `CatalogAuditEntries.cs`:

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Audit;

namespace Kartova.Catalog.Infrastructure;

/// <summary>
/// Builds the <see cref="AuditEntry"/> for the four lifecycle transitions, which all
/// share the <c>application.lifecycle_changed</c> action (design §4) distinguished by
/// <c>from</c>/<c>to</c>. <paramref name="from"/> is the pre-transition lifecycle —
/// each handler must read it BEFORE invoking the domain transition method.
/// </summary>
public static class CatalogAuditEntries
{
    public static AuditEntry LifecycleChanged(Application app, Lifecycle from) =>
        new(CatalogAuditActions.ApplicationLifecycleChanged,
            CatalogAuditTargetTypes.Application,
            app.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["from"] = from.ToString(),
                ["to"] = app.Lifecycle.ToString(),
                ["sunsetDate"] = app.SunsetDate?.ToString("O"),
            });
}
```

- [ ] **Step 4: Run the unit test to verify it passes**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/Kartova.Catalog.Infrastructure.Tests.csproj --filter FullyQualifiedName~CatalogAuditEntriesTests"`
Expected: PASS (both methods).

- [ ] **Step 5: Wire all four lifecycle handlers**

Each handler: add `using Kartova.SharedKernel.Audit;`, add the `IAuditWriter audit` parameter, capture `from` before the transition, and append after `SaveChangesAsync`.

`DeprecateApplicationHandler.cs`:

```csharp
    public async Task<ApplicationResponse?> Handle(
        DeprecateApplicationCommand cmd,
        CatalogDbContext db,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        var from = app.Lifecycle;
        app.Deprecate(cmd.SunsetDate, _clock);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(CatalogAuditEntries.LifecycleChanged(app, from), ct);
        return app.ToResponse();
    }
```

`DecommissionApplicationHandler.cs`:

```csharp
    public async Task<ApplicationResponse?> Handle(
        DecommissionApplicationCommand cmd,
        CatalogDbContext db,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        var from = app.Lifecycle;
        app.Decommission(_clock);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(CatalogAuditEntries.LifecycleChanged(app, from), ct);
        return app.ToResponse();
    }
```

`ReactivateApplicationHandler.cs` (no `_clock` field — signature has no `TimeProvider`):

```csharp
    public async Task<ApplicationResponse?> Handle(
        ReactivateApplicationCommand cmd,
        CatalogDbContext db,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        var from = app.Lifecycle;
        app.Reactivate();
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(CatalogAuditEntries.LifecycleChanged(app, from), ct);
        return app.ToResponse();
    }
```

`UnDecommissionApplicationHandler.cs`:

```csharp
    public async Task<ApplicationResponse?> Handle(
        UnDecommissionApplicationCommand cmd,
        CatalogDbContext db,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id.Value), ct);
        if (app is null) return null;

        var from = app.Lifecycle;
        app.UnDecommission(cmd.SunsetDate, _clock);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(CatalogAuditEntries.LifecycleChanged(app, from), ct);
        return app.ToResponse();
    }
```

- [ ] **Step 6: Add the integration happy + negative tests**

Add to `AuditWiringTests.cs`:

```csharp
    // --- Happy: deprecate writes a lifecycle_changed row with from/to ---
    [TestMethod]
    public async Task Deprecate_WritesLifecycleChangedAuditRow()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Audit Dep Team");
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser, new[] { KartovaRoles.OrgAdmin });
        var app = await RegisterAsync(client, teamId, "Audit Dep App");

        var sunset = DateTimeOffset.UtcNow.AddDays(30);
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{app.Id}/deprecate",
            new DeprecateApplicationRequest(sunset));
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
            $"Expected 200. Body: {await resp.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.ApplicationLifecycleChanged &&
            r.TargetId == app.Id.ToString());
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual("Active", data.RootElement.GetProperty("from").GetString());
        Assert.AreEqual("Deprecated", data.RootElement.GetProperty("to").GetString());
        Assert.IsFalse(string.IsNullOrEmpty(data.RootElement.GetProperty("sunsetDate").GetString()));
    }

    // --- Negative: a rejected transition writes no row ---
    [TestMethod]
    public async Task Decommission_BeforeSunset_WritesNoAuditRow()
    {
        var tenantId = Fx.TenantIdForEmail(OrgAUser).Value;
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Audit NoRow Team");
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser, new[] { KartovaRoles.OrgAdmin });
        var app = await RegisterAsync(client, teamId, "Audit NoRow App");

        // Deprecate with a far-future sunset, then attempt to decommission before it -> 409.
        var depResp = await client.PostAsJsonAsync(
            $"/api/v1/catalog/applications/{app.Id}/deprecate",
            new DeprecateApplicationRequest(DateTimeOffset.UtcNow.AddDays(30)));
        Assert.AreEqual(HttpStatusCode.OK, depResp.StatusCode);

        var decResp = await client.PostAsync(
            $"/api/v1/catalog/applications/{app.Id}/decommission", content: null);
        Assert.AreEqual(HttpStatusCode.Conflict, decResp.StatusCode,
            $"Expected 409. Body: {await decResp.Content.ReadAsStringAsync()}");

        // Exactly one lifecycle_changed row (the deprecate), none for the rejected decommission.
        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var lifecycleRows = rows.Where(r =>
            r.Action == CatalogAuditActions.ApplicationLifecycleChanged &&
            r.TargetId == app.Id.ToString()).ToList();
        Assert.AreEqual(1, lifecycleRows.Count, "rejected decommission must not write an audit row");
        using var data = JsonDocument.Parse(lifecycleRows[0].DataJson!);
        Assert.AreEqual("Deprecated", data.RootElement.GetProperty("to").GetString());
    }
```

> Note: confirm the decommission endpoint's empty-body call shape against `DecommissionApplicationTests.cs` (it already calls this route) — match its `PostAsync`/`PostAsJsonAsync` usage exactly.

- [ ] **Step 7: Run the lifecycle integration tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~AuditWiringTests"`
Expected: PASS (Register, Edit, Deprecate, Decommission-before-sunset).

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/ src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/CatalogAuditEntriesTests.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/AuditWiringTests.cs
git commit -m "feat(audit): wire application.lifecycle_changed across 4 transitions"
```

---

## Task 6: Wire `application.team_assigned`

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/AssignApplicationTeamHandler.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/AuditWiringTests.cs` (add method)

**Interfaces:**
- Consumes: `CatalogAuditActions`, `CatalogAuditTargetTypes`, `AuditEntry`, `IAuditWriter`. Append only on `AssignApplicationTeamResult.Success`.

- [ ] **Step 1: Add the failing test**

Add to `AuditWiringTests.cs`:

```csharp
    // --- Happy: team assignment writes from/to team ids ---
    [TestMethod]
    public async Task AssignTeam_WritesTeamAssignedAuditRow()
    {
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var tenantId = tenant.Value;
        var fromTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Audit From Team");
        var toTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Audit To Team");
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser, new[] { KartovaRoles.OrgAdmin });
        var app = await RegisterAsync(client, fromTeam, "Audit Assign App");

        var resp = await client.PutAsJsonAsync(
            $"/api/v1/catalog/applications/{app.Id}/team",
            new AssignApplicationTeamRequest(toTeam));
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode,
            $"Expected 200. Body: {await resp.Content.ReadAsStringAsync()}");

        var rows = await Fx.ReadAuditLogAsync(tenantId);
        var row = rows.Single(r =>
            r.Action == CatalogAuditActions.ApplicationTeamAssigned &&
            r.TargetId == app.Id.ToString());
        using var data = JsonDocument.Parse(row.DataJson!);
        Assert.AreEqual(fromTeam.ToString(), data.RootElement.GetProperty("fromTeamId").GetString());
        Assert.AreEqual(toTeam.ToString(), data.RootElement.GetProperty("toTeamId").GetString());
    }
```

> Note: confirm the assign-team request DTO name + route shape against `AssignApplicationTeamTests.cs` (`AssignApplicationTeamRequest` and `PUT .../team`); match its exact request construction.

- [ ] **Step 2: Run the test to verify it fails**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~AuditWiringTests.AssignTeam_WritesTeamAssignedAuditRow"`
Expected: FAIL — no `application.team_assigned` row.

- [ ] **Step 3: Wire the handler**

In `AssignApplicationTeamHandler.cs`, add `using Kartova.SharedKernel.Audit;`, add the `IAuditWriter audit` parameter, capture `fromTeamId` before `AssignTeam`, and append after `SaveChangesAsync` (only the `Success` path reaches it):

```csharp
    public async Task<AssignApplicationTeamResult> Handle(
        AssignApplicationTeamCommand cmd,
        CatalogDbContext db,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var app = await db.Applications
            .FirstOrDefaultAsync(ApplicationSortSpecs.IdEquals(cmd.Id), ct);
        if (app is null) return AssignApplicationTeamResult.NotFound;

        var exists = await teamChecker.ExistsAsync(cmd.TeamId, ct);
        if (!exists) return AssignApplicationTeamResult.InvalidTeam;

        var fromTeamId = app.TeamId;
        app.AssignTeam(cmd.TeamId);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.ApplicationTeamAssigned,
            CatalogAuditTargetTypes.Application,
            app.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["fromTeamId"] = fromTeamId.ToString(),
                ["toTeamId"] = cmd.TeamId.ToString(),
            }), ct);
        return AssignApplicationTeamResult.Success(app);
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~AuditWiringTests.AssignTeam_WritesTeamAssignedAuditRow"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/AssignApplicationTeamHandler.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/AuditWiringTests.cs
git commit -m "feat(audit): wire application.team_assigned event"
```

---

## Task 7: Full verification + checklist update

**Files:**
- Modify: `docs/product/CHECKLIST.md:46` (E-01.F-03.S-03 note)

- [ ] **Step 1: Full solution build (gate 1)**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Architecture tests (NetArchTest) green**

Run: `cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj"`
Expected: PASS — confirms no Catalog → Audit.Infrastructure reference leaked, `IAuditWriter` still SharedKernel-only.

- [ ] **Step 3: Catalog unit + integration suites green (gate 3)**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Infrastructure.Tests/Kartova.Catalog.Infrastructure.Tests.csproj"`
Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj"`
Expected: PASS. (Per `project_full_suite_docker_flake` memory: if an integration assembly fails with a Docker named-pipe TimeoutException under container saturation, re-run that assembly in isolation before treating it as red.)

- [ ] **Step 4: Update the checklist**

In `docs/product/CHECKLIST.md`, append to the E-01.F-03.S-03 line (the long note at line 46), after the existing "Catalog app events remain the last deferred chunk." sentence:

```
 Phase 2 follow-up (audit-catalog-event-wiring, 2026-06-19): 7 Catalog application mutations wired to IAuditWriter — application.registered/edited/team_assigned + a single application.lifecycle_changed (from/to/sunsetDate in data) across deprecate/decommission/reactivate/un-decommission. Audit event-wiring fully closed.
```

- [ ] **Step 5: Commit**

```bash
git add docs/product/CHECKLIST.md
git commit -m "docs(audit): mark Catalog audit event-wiring complete (E-01.F-03.S-03)"
```

---

## Post-plan gates (per CLAUDE.md DoD — run after Task 7)

These are not per-task steps but the slice-boundary gates. Run in order after the implementation tasks are green:

- **Gate 4 — container build:** no Dockerfile/`COPY` change in this slice; the existing `images` CI job covers it. No action unless a new file lands outside an already-copied path.
- **Gate 5 — `/simplify`** against the branch diff.
- **Gate 6 — mutation (conditional, should-do here):** the slice adds wiring + constants + payload shaping, not Domain/Application *business* logic. Run `/misc:mutation-sentinel` on `CatalogAuditEntries.cs` + the handlers if practical; else skip with this note.
- **Gate 7 — `/superpowers:requesting-code-review`** against the full branch diff (spec + this plan as context).
- **Gate 8 — `/pr-review-toolkit:review-pr`.**
- **Gate 9 — `/deep-review`** against the branch diff.
- **Terminal re-verify:** re-run gate 1 (build) + gate 3 (full Catalog suites) after any fixes from gates 5–9.
- **Pre-push:** run `scripts/ci-local.sh backend` (Release build+test) before opening the PR.

---

## Self-Review

**Spec coverage:**
- Design §2 (7 handlers, 4 actions) → Tasks 3 (register), 4 (edit), 5 (4 lifecycle), 6 (team-assign). ✓
- Design §3 decision 1 (method-param injection) → every handler task adds `IAuditWriter audit` as a method parameter. ✓
- Design §3 decision 2 (success-path only) → Task 4 appends after the try/catch (concurrency throws skip it); Task 5 negative test asserts a rejected decommission writes no row; Task 6 appends only on the `Success` return. ✓
- Design §3 decision 3 (old-value before mutation) → Tasks 5 & 6 capture `from`/`fromTeamId` before the domain call; unit test (Task 5) + integration `from=Active` assertion prove it. ✓
- Design §3 decision 4 (Catalog-local taxonomy) → Task 2 creates `CatalogAuditActions` + `CatalogAuditTargetTypes` in `Kartova.Catalog.Application`. ✓
- Design §4 payloads (displayName/teamId; displayName/description; from/to/sunsetDate; fromTeamId/toTeamId) → asserted in each task's integration test + the factory unit test. ✓
- Design §7 gate-5 artifacts (happy register, happy lifecycle, negative rejected) → Tasks 3, 5 (happy + negative). ✓ The named "Unit: payload shaping" → Task 5 `CatalogAuditEntriesTests`. ✓
- Design §7 chain-intact assertion: the foundation already proves chain linkage in the Organization suite; this slice's rows ride the same writer. The Catalog tests assert action/actor/data correctness; chain verification is not re-duplicated here (the writer is unchanged). Honest deviation — noted.
- Design §8 (checklist update) → Task 7 Step 4. ✓

**Placeholder scan:** No TBD/TODO. The two unit-test helper expressions flagged with `> Note:` (TenantId construction, `Application.Create` arg order) are explicit "verify against the real signature" guards, not placeholders — the canonical form (`new TenantId(Guid.NewGuid())`, the documented Create arg order) is given.

**Type consistency:** `IAuditWriter audit` parameter name, `CatalogAuditActions.*`/`CatalogAuditTargetTypes.Application` constant names, `CatalogAuditEntries.LifecycleChanged(app, from)` signature, and `AuditRowRecord` field names are identical across all tasks. Lifecycle handler signatures match their real source (Reactivate has no `TimeProvider`; the others do).
