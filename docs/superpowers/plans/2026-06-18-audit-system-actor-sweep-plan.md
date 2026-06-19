# Audit System-actor + invitation-expiry sweep — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the audit log a `System`-actor write path and wire the hourly invitation-expiry sweep to record one tamper-evident `invitation.expired` row per expired invitation, inside each tenant's RLS-scoped chain.

**Architecture:** Extend `IAuditWriter` with `AppendSystemAsync(TenantId, AuditEntry, ct)` (a second public method over a shared private core; the existing `User` path is byte-for-byte unchanged). Refactor `ExpireInvitationsHostedService` to mirror `AuditCheckpointHostedService`: enumerate due invitations cross-tenant via the BYPASSRLS `AdminOrganizationDbContext`, then process each tenant inside its own `ITenantScope` transaction (the periodic job is the ADR-0090 transport adapter) writing through the RLS `OrganizationDbContext` + `IAuditWriter`.

**Tech Stack:** .NET 10 / EF Core · Npgsql · Postgres 18 RLS · MSTest v4 + NSubstitute · Wolverine (unaffected) · per-tenant SHA-256 hash chain (ADR-0018).

## Global Constraints

- `TreatWarningsAsErrors=true` — 0 warnings, 0 errors across the full solution build.
- Solution file: `Kartova.slnx`. Windows shell: use `cmd //c` or PowerShell wrappers for `dotnet`.
- `AuditEntry.Data` values are **strings only** (jsonb-hash-stability rule); `TargetId` is a string.
- No cross-module Infrastructure references (ADR-0082) — the sweep depends only on the SharedKernel `IAuditWriter`; DI binds the `Kartova.Audit.Infrastructure` impl. NetArchTest must stay green.
- Audit action strings are a stable contract written to `audit_log.action` — do not rename without migrating historical rows.
- All tenant-scoped DB work runs inside `ITenantScope`; the periodic job owns `BeginAsync`/`CommitAsync`, handlers/writer never touch the scope (ADR-0090).
- Source of truth: `docs/superpowers/specs/2026-06-18-audit-system-actor-sweep-design.md`.

---

### Task 1: `AppendSystemAsync` — System-actor write path on the audit writer

**Files:**
- Modify: `src/Kartova.SharedKernel/Audit/IAuditWriter.cs`
- Modify: `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditWriter.cs`
- Test: `src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/AuditWriterTests.cs`

**Interfaces:**
- Consumes: existing `AuditEntry` (`Kartova.SharedKernel.Audit`), `TenantId` (`Kartova.SharedKernel.Multitenancy`), `AuditActorType.System` (already defined), `AuditLogEntry.Create` (already permits `actorId: null` for non-User actors).
- Produces: `Task IAuditWriter.AppendSystemAsync(TenantId tenant, AuditEntry entry, CancellationToken ct)` — consumed by Task 3's sweep. Records `actor_type=System`, `actor_id=NULL`, `actor_display="System"`.

- [ ] **Step 1: Write the failing integration test**

Add to `AuditWriterTests.cs` (after the existing `SampleEntry` helper, reuse the existing `BuildProvider` / `BeginScopeAsync` / `Fx`):

```csharp
    private static async Task<(string ActorType, Guid? ActorId, string? ActorDisplay)> ReadLatestActorAsync(Guid tenantId)
    {
        await using var conn = new NpgsqlConnection(Fx.BypassConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT actor_type, actor_id, actor_display FROM audit_log " +
                          "WHERE tenant_id = $1 ORDER BY seq DESC LIMIT 1";
        cmd.Parameters.AddWithValue(tenantId);
        await using var r = await cmd.ExecuteReaderAsync();
        Assert.IsTrue(await r.ReadAsync(), "expected at least one audit row");
        return (r.GetString(0), r.IsDBNull(1) ? null : r.GetGuid(1), r.IsDBNull(2) ? null : r.GetString(2));
    }

    [TestMethod]
    public async Task AppendSystem_writes_System_actor_row_with_null_actor_and_chains()
    {
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider(Guid.NewGuid());

        // Two System appends in one transaction: the second must chain onto the first.
        using (var scope = sp.CreateScope())
        {
            await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
            var writer = scope.ServiceProvider.GetRequiredService<AuditWriter>();
            await writer.AppendSystemAsync(tenant,
                new AuditEntry("invitation.expired", "Invitation", Guid.NewGuid().ToString(),
                    new Dictionary<string, string?> { ["email"] = "a@x.io", ["role"] = "Member" }),
                CancellationToken.None);
            await writer.AppendSystemAsync(tenant,
                new AuditEntry("invitation.expired", "Invitation", Guid.NewGuid().ToString(),
                    new Dictionary<string, string?> { ["email"] = "b@x.io", ["role"] = "Viewer" }),
                CancellationToken.None);
            await handle.CommitAsync(CancellationToken.None);
        }

        var (actorType, actorId, actorDisplay) = await ReadLatestActorAsync(tenant.Value);
        Assert.AreEqual("System", actorType);
        Assert.IsNull(actorId, "a System actor row must have a NULL actor_id");
        Assert.AreEqual("System", actorDisplay);
        Assert.AreEqual(2L, await CountRowsAsync(Fx.BypassConnectionString, tenant.Value));

        using (var scope = sp.CreateScope())
        {
            await using var handle = await BeginScopeAsync(scope.ServiceProvider, tenant);
            var verifier = scope.ServiceProvider.GetRequiredService<AuditChainVerifier>();
            var result = await verifier.VerifyAsync(tenant, CancellationToken.None);
            Assert.IsTrue(result.Intact, $"System chain broken: seq={result.FirstBrokenSeq} reason={result.Reason}");
        }
    }
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `cmd //c "dotnet build src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests"`
Expected: FAIL — `'IAuditWriter' does not contain a definition for 'AppendSystemAsync'` / `'AuditWriter' does not contain a definition for 'AppendSystemAsync'`.

- [ ] **Step 3: Add the interface method**

In `src/Kartova.SharedKernel/Audit/IAuditWriter.cs`, add `using Kartova.SharedKernel.Multitenancy;` at the top and a second method to the interface:

```csharp
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.SharedKernel.Audit;

/// <summary>
/// Appends one tamper-evident audit row inside the caller's current tenant transaction
/// (ADR-0018 + ADR-0090). Synchronous and fail-closed: if the append throws, the caller's
/// transaction rolls back, so a business mutation can never commit without its audit row.
/// Implemented by the Audit module.
/// </summary>
public interface IAuditWriter
{
    /// <summary>Appends a row attributed to the current authenticated <c>User</c> (from <c>ICurrentUser</c>).</summary>
    Task AppendAsync(AuditEntry entry, CancellationToken ct);

    /// <summary>
    /// Appends a row attributed to the <c>System</c> actor (background jobs with no HTTP principal):
    /// <c>actor_type=System</c>, <c>actor_id=NULL</c>, <c>actor_display="System"</c>. The tenant is
    /// passed explicitly because background callers run outside the request <c>ITenantContext</c>;
    /// the caller must already hold an open <c>ITenantScope</c> for <paramref name="tenant"/>
    /// (the writer's row still rides that transaction and the RLS <c>WITH CHECK</c>).
    /// </summary>
    Task AppendSystemAsync(TenantId tenant, AuditEntry entry, CancellationToken ct);
}
```

- [ ] **Step 4: Refactor the writer to share a core and add the System path**

Replace the body of `AppendAsync` in `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditWriter.cs` with a shared `AppendCoreAsync` plus the two public methods. The full class body of the two methods + core:

```csharp
    /// <summary>Display snapshot for background (no-principal) System appends.</summary>
    private const string SystemActorDisplay = "System";

    public Task AppendAsync(AuditEntry entry, CancellationToken ct)
        => AppendCoreAsync(
            tenant.Id.Value, AuditActorType.User, currentUser.UserId, currentUser.DisplayName, entry, ct);

    public Task AppendSystemAsync(TenantId t, AuditEntry entry, CancellationToken ct)
        => AppendCoreAsync(
            t.Value, AuditActorType.System, actorId: null, actorDisplay: SystemActorDisplay, entry, ct);

    private async Task AppendCoreAsync(
        Guid tenantId,
        AuditActorType actorType,
        Guid? actorId,
        string? actorDisplay,
        AuditEntry entry,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Serialize appends for this tenant within the current transaction.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('kartova.audit_chain'), hashtext({tenantId.ToString()}))",
            ct);

        var head = await db.AuditEntries
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.Seq)
            .Select(e => new { e.Seq, e.RowHash })
            .FirstOrDefaultAsync(ct);

        var seq = (head?.Seq ?? 0) + 1;
        var prevHash = head?.RowHash ?? AuditRowHasher.GenesisHash;

        // Truncate to microseconds so the hashed timestamp matches what Postgres timestamptz
        // stores and returns (PG resolution is 1µs; .NET ticks are 100ns) — otherwise the
        // verifier's recomputed hash would diverge from the stored one on read-back.
        var raw = clock.GetUtcNow().ToUniversalTime();
        var occurredAt = new DateTimeOffset(raw.Ticks - (raw.Ticks % 10), TimeSpan.Zero);

        var row = AuditLogEntry.Create(
            id: Guid.CreateVersion7(occurredAt),
            tenantId: tenantId,
            seq: seq,
            occurredAt: occurredAt,
            actorType: actorType,
            actorId: actorId,
            actorDisplay: actorDisplay,
            action: entry.Action,
            targetType: entry.TargetType,
            targetId: entry.TargetId,
            data: entry.Data,
            prevHash: prevHash);

        db.AuditEntries.Add(row);
        await db.SaveChangesAsync(ct);
    }
```

Add `using Kartova.SharedKernel.Multitenancy;` if not already present (it is — `ITenantContext` lives there). Update the class XML-doc's parenthetical that says the `System`-actor path is "deferred" — change it to note both `User` and `System` paths are now implemented (the `User` path reads `ICurrentUser`; the `System` path takes an explicit tenant and records a null actor).

- [ ] **Step 5: Run the test to verify it passes (and the existing writer tests still pass)**

Run: `cmd //c "dotnet test src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests --filter FullyQualifiedName~AuditWriterTests"`
Expected: PASS — `AppendSystem_writes_System_actor_row_with_null_actor_and_chains` green, and all pre-existing `AuditWriterTests` (User-path append, jsonb round-trip, rollback, RLS reject, tamper, concurrency, two-tenant) still green (regression guard on the extraction).

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.SharedKernel/Audit/IAuditWriter.cs src/Modules/Audit/Kartova.Audit.Infrastructure/AuditWriter.cs src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/AuditWriterTests.cs
git commit -m "feat(audit): add System-actor write path (AppendSystemAsync) (E-01.F-03.S-03)"
```

---

### Task 2: Extend the test fixture to expose `actor_type`

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFixture.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `KartovaApiFixture.AuditRowRecord` gains a trailing `string ActorType` property; `ReadAuditLogAsync` selects `actor_type`. Consumed by Task 3's sweep integration tests to assert `ActorType == "System"`.

This is a focused enabling change — folded into its own task because Task 1 and Task 3 are otherwise independently reviewable and this touches a shared fixture used by the already-shipped `AuditWiringTests`.

- [ ] **Step 1: Add `actor_type` to the read + record**

In `KartovaApiFixture.cs`, update `ReadAuditLogAsync`'s SQL to select `actor_type` last, and pass it to the record:

```csharp
        cmd.CommandText = """
            SELECT seq, action, actor_id, actor_display, target_type, target_id,
                   data::text, prev_hash, row_hash, actor_type
            FROM audit_log WHERE tenant_id = $1 ORDER BY seq
            """;
```

and the row construction:

```csharp
            rows.Add(new AuditRowRecord(
                r.GetInt64(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetGuid(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                (byte[])r[7], (byte[])r[8],
                r.GetString(9)));
```

and the record declaration (append `ActorType` at the end so existing positional construction elsewhere is unaffected — there is none; only `ReadAuditLogAsync` constructs it):

```csharp
    public sealed record AuditRowRecord(
        long Seq, string Action, Guid? ActorId, string? ActorDisplay,
        string TargetType, string TargetId, string? DataJson, byte[] PrevHash, byte[] RowHash,
        string ActorType);
```

- [ ] **Step 2: Verify the fixture project still builds and existing audit-wiring tests pass**

Run: `cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter FullyQualifiedName~AuditWiringTests"`
Expected: PASS — the three existing `AuditWiringTests` are unaffected (they reference `AuditRowRecord` only by named property; the new trailing property is additive).

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFixture.cs
git commit -m "test(audit): expose actor_type on AuditRowRecord fixture reader"
```

---

### Task 3: Refactor the invitation-expiry sweep to audit per-tenant as System

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.Application/OrganizationAuditActions.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/ExpireInvitationsHostedService.cs`
- Delete: `src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/ExpireInvitationsHostedServiceTests.cs`
- Test (create): `src/Modules/Organization/Kartova.Organization.IntegrationTests/InvitationExpirySweepAuditTests.cs`

**Interfaces:**
- Consumes: `IAuditWriter.AppendSystemAsync` (Task 1), `OrganizationAuditActions.InvitationExpired` (this task), `AuditTargetTypes.Invitation` (existing), `KartovaApiFixture.AuditRowRecord.ActorType` (Task 2), `ITenantScope` / `OrganizationDbContext` / `IKeycloakAdminClient` / `TimeProvider` resolved per-tenant, `AdminOrganizationDbContext` for enumeration, `Invitation.MarkExpired(TimeProvider)` (existing).
- Produces: an audited sweep — no new public type consumed downstream.

- [ ] **Step 1: Delete the obsolete InMemory unit tests**

```bash
git rm src/Modules/Organization/Kartova.Organization.Infrastructure.Tests/ExpireInvitationsHostedServiceTests.cs
```

Rationale (record in the commit message): the refactored sweep requires the real Postgres seam (`ITenantScope` open connection + `SET LOCAL`, RLS `INSERT WITH CHECK`, per-tenant hash chain, `pg_advisory_xact_lock`). The InMemory provider cannot model any of these — exactly why `AuditCheckpointHostedService` (the structural twin) has no unit tests and is covered only by integration tests. Coverage moves to `InvitationExpirySweepAuditTests` below.

- [ ] **Step 2: Add the taxonomy constant**

In `OrganizationAuditActions.cs`, add to the `OrganizationAuditActions` class (after `InvitationCreated`):

```csharp
    public const string InvitationExpired = "invitation.expired";
```

- [ ] **Step 3: Write the failing integration tests**

Create `src/Modules/Organization/Kartova.Organization.IntegrationTests/InvitationExpirySweepAuditTests.cs`:

```csharp
using System.Text.Json;
using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.Organization.Infrastructure;
using Kartova.Organization.Infrastructure.Admin;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Gate-5 real-seam tests for the invitation-expiry sweep's audit wiring (design §8):
/// the sweep records one System-actor <c>invitation.expired</c> row per expired invitation,
/// inside each tenant's RLS-scoped chain. Drives the public <c>ExpireDueAsync</c> work method
/// against the running API host's service provider (mirrors AuditCheckpointHostedServiceTests).
/// Seeded invitations carry a random KC user id that does not exist in the Keycloak container,
/// so the real <c>IKeycloakAdminClient.DeleteUserAsync</c> returns NotFound (swallowed) — which
/// also exercises the idempotent-delete path.
/// </summary>
[TestClass]
public sealed class InvitationExpirySweepAuditTests : OrganizationIntegrationTestBase
{
    private static ExpireInvitationsHostedService NewSweep() => new(
        Fx.Services.GetRequiredService<IServiceScopeFactory>(),
        Substitute.For<IDistributedLock>(),               // unused by ExpireDueAsync
        Fx.Services.GetRequiredService<TimeProvider>(),   // unused by ExpireDueAsync (timer only)
        NullLogger<ExpireInvitationsHostedService>.Instance);

    private static async Task RunSweepAsync()
    {
        using var scope = Fx.Services.CreateScope();
        await NewSweep().ExpireDueAsync(scope.ServiceProvider, CancellationToken.None);
    }

    private static async Task<InvitationStatus> ReadStatusAsync(Guid invitationId)
    {
        await using var db = new OrganizationDbContext(BypassOptions());
        return await db.Invitations
            .Where(i => EF.Property<Guid>(i, "_id") == invitationId)
            .Select(i => i.Status)
            .SingleAsync();
    }

    // --- Happy: a past-due invitation is expired and audited as System ---
    [TestMethod]
    public async Task Sweep_expires_pastdue_invitation_and_writes_System_audit_row()
    {
        var (_, tenantId) = await NewTenantAsync("expire-happy");
        var pastInvitedAt = DateTimeOffset.UtcNow.AddDays(-8);
        var invitationId = await Fx.SeedInvitationAsync(
            tenantId, "expiree@expire-happy.kartova.local", KartovaRoles.Member,
            invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
            invitedAt: pastInvitedAt, expiresAt: pastInvitedAt.AddDays(7)); // expired 1 day ago

        try
        {
            await RunSweepAsync();

            Assert.AreEqual(InvitationStatus.Expired, await ReadStatusAsync(invitationId));

            var rows = await Fx.ReadAuditLogAsync(tenantId);
            var row = rows.Single(r => r.Action == OrganizationAuditActions.InvitationExpired);
            Assert.AreEqual("System", row.ActorType);
            Assert.IsNull(row.ActorId, "System actor row must have NULL actor_id");
            Assert.AreEqual("System", row.ActorDisplay);
            Assert.AreEqual(AuditTargetTypes.Invitation, row.TargetType);
            Assert.AreEqual(invitationId.ToString(), row.TargetId);
            using var data = JsonDocument.Parse(row.DataJson!);
            Assert.AreEqual("expiree@expire-happy.kartova.local", data.RootElement.GetProperty("email").GetString());
            Assert.AreEqual(KartovaRoles.Member, data.RootElement.GetProperty("role").GetString());
        }
        finally
        {
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }

    // --- Multi-tenant: each expiry lands only in its own tenant's chain ---
    [TestMethod]
    public async Task Sweep_isolates_audit_rows_per_tenant()
    {
        var (_, tenantA) = await NewTenantAsync("expire-iso-a");
        var (_, tenantB) = await NewTenantAsync("expire-iso-b");
        var past = DateTimeOffset.UtcNow.AddDays(-8);
        var invA = await Fx.SeedInvitationAsync(tenantA, "a@expire-iso-a.kartova.local", KartovaRoles.Member,
            Guid.NewGuid(), Guid.NewGuid(), past, past.AddDays(7));
        var invB = await Fx.SeedInvitationAsync(tenantB, "b@expire-iso-b.kartova.local", KartovaRoles.Viewer,
            Guid.NewGuid(), Guid.NewGuid(), past, past.AddDays(7));

        try
        {
            await RunSweepAsync();

            var rowsA = await Fx.ReadAuditLogAsync(tenantA);
            var rowsB = await Fx.ReadAuditLogAsync(tenantB);

            var rowA = rowsA.Single(r => r.Action == OrganizationAuditActions.InvitationExpired);
            Assert.AreEqual(invA.ToString(), rowA.TargetId);
            Assert.IsFalse(rowsA.Any(r => r.TargetId == invB.ToString()), "tenant A chain must not contain tenant B's row");

            var rowB = rowsB.Single(r => r.Action == OrganizationAuditActions.InvitationExpired);
            Assert.AreEqual(invB.ToString(), rowB.TargetId);
            Assert.IsFalse(rowsB.Any(r => r.TargetId == invA.ToString()), "tenant B chain must not contain tenant A's row");
        }
        finally
        {
            await Fx.DeleteInvitationsForTenantAsync(tenantA);
            await Fx.DeleteInvitationsForTenantAsync(tenantB);
            await Fx.DeleteOrganizationsForTenantAsync(tenantA);
            await Fx.DeleteOrganizationsForTenantAsync(tenantB);
        }
    }

    // --- Negative: a not-yet-due invitation is left alone and writes no row ---
    [TestMethod]
    public async Task Sweep_leaves_future_invitation_pending_and_writes_no_row()
    {
        var (_, tenantId) = await NewTenantAsync("expire-future");
        var invitationId = await Fx.SeedInvitationAsync(
            tenantId, "future@expire-future.kartova.local", KartovaRoles.Member,
            invitedByUserId: Guid.NewGuid(), keycloakUserId: Guid.NewGuid(),
            invitedAt: DateTimeOffset.UtcNow, expiresAt: DateTimeOffset.UtcNow.AddDays(7)); // not due

        try
        {
            await RunSweepAsync();

            Assert.AreEqual(InvitationStatus.Pending, await ReadStatusAsync(invitationId));
            var rows = await Fx.ReadAuditLogAsync(tenantId);
            Assert.AreEqual(0, rows.Count(r => r.Action == OrganizationAuditActions.InvitationExpired),
                "a non-due invitation must not be audited");
        }
        finally
        {
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }

    // --- Idempotency: a second sweep does not re-expire or double-audit ---
    [TestMethod]
    public async Task Sweep_run_twice_does_not_double_audit()
    {
        var (_, tenantId) = await NewTenantAsync("expire-twice");
        var past = DateTimeOffset.UtcNow.AddDays(-8);
        await Fx.SeedInvitationAsync(tenantId, "twice@expire-twice.kartova.local", KartovaRoles.Member,
            Guid.NewGuid(), Guid.NewGuid(), past, past.AddDays(7));

        try
        {
            await RunSweepAsync();
            await RunSweepAsync(); // second tick: invitation is no longer Pending → nothing to do

            var rows = await Fx.ReadAuditLogAsync(tenantId);
            Assert.AreEqual(1, rows.Count(r => r.Action == OrganizationAuditActions.InvitationExpired),
                "the second sweep must not write a second invitation.expired row");
        }
        finally
        {
            await Fx.DeleteInvitationsForTenantAsync(tenantId);
            await Fx.DeleteOrganizationsForTenantAsync(tenantId);
        }
    }
}
```

- [ ] **Step 4: Run the new tests to verify they fail**

Run: `cmd //c "dotnet build src/Modules/Organization/Kartova.Organization.IntegrationTests"`
Expected: FAIL — `'OrganizationAuditActions' does not contain a definition for 'InvitationExpired'` is resolved by Step 2; the remaining failure after build is at runtime: the current sweep writes **no** audit row, so `rows.Single(... InvitationExpired)` throws (`Sequence contains no matching element`). (If Step 2 is already applied, the build succeeds and the tests fail at runtime as described.)

- [ ] **Step 5: Refactor the sweep**

Replace the body of `src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/ExpireInvitationsHostedService.cs` with:

```csharp
using Kartova.Organization.Application;
using Kartova.Organization.Domain;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Identity;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kartova.Organization.Infrastructure.Admin;

/// <summary>
/// Hourly leader-elected sweep that expires past-due pending invitations, deletes their
/// corresponding KeyCloak directory users, and records one tamper-evident
/// <c>invitation.expired</c> audit row per expiry as the <c>System</c> actor.
///
/// <para>Tenant enumeration is cross-tenant maintenance and uses the BYPASSRLS
/// <see cref="AdminOrganizationDbContext"/> (read-only). Each affected tenant is then processed
/// inside its own tenant scope via the app role (mirroring <c>AuditCheckpointHostedService</c>),
/// so the invitation update + audit append both pass the RLS WITH CHECK and ride one transaction
/// — the sweep cannot expire or audit the wrong tenant even by mistake (ADR-0018 + ADR-0090).
/// The periodic job is the transport adapter here: it owns Begin/Commit; the writer/handler never
/// touch the scope.</para>
/// </summary>
public sealed class ExpireInvitationsHostedService(
    IServiceScopeFactory scopes,
    IDistributedLock locks,
    TimeProvider clock,
    ILogger<ExpireInvitationsHostedService> logger)
    : LeaderElectedPeriodicService(scopes, locks, clock, logger)
{
    private readonly IServiceScopeFactory _scopes = scopes;
    private readonly ILogger<ExpireInvitationsHostedService> _logger = logger;

    protected override string LockName => "expire-invitations";
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override Task ExecuteLeaderWorkAsync(IServiceProvider services, CancellationToken ct)
        => ExpireDueAsync(services, ct);

    /// <summary>
    /// Exposed for direct integration testing — the base class wraps this in scope + lock
    /// setup, both of which are timing/integration concerns. <paramref name="services"/> must
    /// resolve <see cref="AdminOrganizationDbContext"/> for enumeration; per-tenant work runs
    /// in fresh scopes created from the injected <see cref="IServiceScopeFactory"/>.
    /// </summary>
    public async Task ExpireDueAsync(IServiceProvider services, CancellationToken ct)
    {
        var admin = services.GetRequiredService<AdminOrganizationDbContext>();
        var now = services.GetRequiredService<TimeProvider>().GetUtcNow();

        // Cross-tenant read: which tenants currently have a past-due pending invitation?
        // Materialize then dedupe in memory to avoid translating value-object projections.
        var dueTenantIds = (await admin.Invitations
                .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt < now)
                .AsNoTracking()
                .Select(i => i.TenantId)
                .ToListAsync(ct))
            .Select(t => t.Value)
            .Distinct()
            .ToList();

        int tenants = 0, expired = 0, failed = 0;
        foreach (var tenantId in dueTenantIds)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                expired += await ProcessTenantAsync(tenantId, ct);
                tenants++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Isolate per-tenant failures (KC outage, transient DB error): one tenant's
                // failure must not abort the sweep for the others. Its txn rolls back (nothing
                // expired or audited for it); the next hourly tick retries. Matches
                // AuditCheckpointHostedService's per-tenant isolation.
                failed++;
                _logger.LogError(ex, "Invitation-expiry sweep errored for tenant {TenantId}.", tenantId);
            }
        }

        if (expired > 0 || failed > 0)
            _logger.LogInformation(
                "Invitation-expiry sweep: {Expired} expired across {Tenants} tenant(s), {Failed} errored.",
                expired, tenants, failed);
    }

    private async Task<int> ProcessTenantAsync(Guid tenantId, CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var tenant = new TenantId(tenantId);

        // The periodic job is the transport adapter (ADR-0090): it owns Begin/Commit.
        var tenantScope = sp.GetRequiredService<ITenantScope>();
        await using var handle = await tenantScope.BeginAsync(tenant, ct);

        var db = sp.GetRequiredService<OrganizationDbContext>();
        var kc = sp.GetRequiredService<IKeycloakAdminClient>();
        var audit = sp.GetRequiredService<IAuditWriter>();
        var workClock = sp.GetRequiredService<TimeProvider>();
        var now = workClock.GetUtcNow();

        // Re-read through the RLS context: SET LOCAL scopes this to the current tenant, and the
        // Status re-filter ignores any invitation accepted/revoked since enumeration.
        var due = await db.Invitations
            .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt < now)
            .ToListAsync(ct);

        foreach (var inv in due)
        {
            if (inv.KeycloakUserId is { } kid)
            {
                try
                {
                    await kc.DeleteUserAsync(kid, ct);
                }
                catch (KeycloakAdminException ex) when (ex.Error == KeycloakAdminError.NotFound)
                {
                    // Idempotent: the KC user is already gone, which is the desired end state.
                }
                // Non-NotFound KC errors propagate, rolling back this tenant's txn; the
                // outer loop catches + isolates them. The KC delete already happened, but
                // the next tick re-deletes (NotFound swallowed) and retries — no partial state.
            }

            inv.MarkExpired(workClock);
            await audit.AppendSystemAsync(tenant, new AuditEntry(
                OrganizationAuditActions.InvitationExpired,
                AuditTargetTypes.Invitation,
                inv.Id.Value.ToString(),
                new Dictionary<string, string?> { ["email"] = inv.Email, ["role"] = inv.Role }), ct);
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        await handle.CommitAsync(ct);
        return due.Count;
    }
}
```

- [ ] **Step 6: Run the new integration tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests --filter FullyQualifiedName~InvitationExpirySweepAuditTests"`
Expected: PASS — all four tests green (happy/System-row, multi-tenant isolation, non-due negative, run-twice idempotency).

- [ ] **Step 7: Run the affected module unit + integration suites + arch tests**

Run: `cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.Infrastructure.Tests"`
Expected: PASS — the project compiles and all remaining tests pass after the obsolete sweep unit-test file was removed.

Run: `cmd //c "dotnet test tests/Kartova.ArchitectureTests"`
Expected: PASS — no new cross-module Infrastructure reference; `IAuditWriter` stays in SharedKernel (ADR-0082).

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Organization/Kartova.Organization.Application/OrganizationAuditActions.cs \
        src/Modules/Organization/Kartova.Organization.Infrastructure.Admin/ExpireInvitationsHostedService.cs \
        src/Modules/Organization/Kartova.Organization.IntegrationTests/InvitationExpirySweepAuditTests.cs
git commit -m "feat(audit): wire invitation-expiry sweep to System-actor audit log (E-01.F-03.S-03)

Refactor ExpireInvitationsHostedService to process each tenant inside its own
ITenantScope txn (RLS app role), writing one System-actor invitation.expired
row per expiry. Remove the InMemory unit tests (the sweep now needs the real
Postgres seam, like AuditCheckpointHostedService); coverage moves to
InvitationExpirySweepAuditTests."
```

---

### Task 4: Update the progress checklist

**Files:**
- Modify: `docs/product/CHECKLIST.md`

- [ ] **Step 1: Refine the E-01.F-03.S-03 note**

In `docs/product/CHECKLIST.md`, append to the existing `E-01.F-03.S-03` line's note: a Phase-2 follow-up clause recording that the System-actor write path (`AppendSystemAsync`) + the `invitation.expired` sweep are now wired (2026-06-18), and that **Catalog app events remain the last deferred chunk**.

- [ ] **Step 2: Commit**

```bash
git add docs/product/CHECKLIST.md
git commit -m "docs(checklist): System-actor + invitation-expiry sweep audit wired (E-01.F-03.S-03)"
```

---

## Definition of Done

Per CLAUDE.md's eight always-blocking gates + the conditional mutation gate. **Gate 6 (mutation) is BLOCKING** here — the slice changes Application/Infrastructure logic (writer actor/chain path + sweep per-tenant restructure). After Tasks 1–4:

1. Full solution build, `TreatWarningsAsErrors=true` (0 warnings).
2. Per-task subagent reviews (interleaved).
3. Full test suite green incl. real-seam integration (`AuditWriterTests`, `InvitationExpirySweepAuditTests`) + architecture tests.
4. Container build green (`images` CI job) — no Dockerfile/`COPY` change in this slice.
5. `/simplify` against the branch diff.
6. **Mutation loop (blocking):** `/misc:mutation-sentinel` → `/misc:test-generator` on `AuditWriter` + `ExpireInvitationsHostedService`; target ≥80%; document survivors.
7. `/superpowers:requesting-code-review` at slice boundary.
8. `/pr-review-toolkit:review-pr`.
9. `/deep-review` against the branch diff.

**Terminal re-verify:** after gate 9, re-run build + full suite and confirm green. Run `scripts/ci-local.sh` (or the `backend` subset) before push.

## Self-Review

- **Spec coverage:** §3 decision 1 (AppendSystemAsync + AppendCoreAsync) → Task 1. Decision 2 (System/null/"System") → Task 1 + asserted in Tasks 1 & 3. Decision 3 (enumerate BYPASSRLS, write RLS) → Task 3 Step 5. Decision 4 (one txn per tenant + isolation) → Task 3 `ProcessTenantAsync` + outer try/catch. Decision 5 (KC-before-commit, idempotent) → Task 3 loop. §4 writer surface → Task 1. §5 payload → Task 3 (`InvitationExpired` + data keys). §6 sweep shape → Task 3 Step 5. §8 gate-5 artifacts (happy/multi-tenant/negative/idempotency) → Task 3 Step 3; writer unit/integration → Task 1. §9 DoD + checklist update → Task 4 + DoD section. All covered.
- **Placeholder scan:** none — every code step shows full code; every run step shows command + expected output.
- **Type consistency:** `AppendSystemAsync(TenantId, AuditEntry, CancellationToken)` identical across interface (Task 1 Step 3), impl (Step 4), and call site (Task 3 Step 5). `AuditRowRecord.ActorType` (Task 2) matches the assertions in Task 3. `OrganizationAuditActions.InvitationExpired` defined (Task 3 Step 2) before use (Step 3/5). `AuditTargetTypes.Invitation` is the existing constant.
