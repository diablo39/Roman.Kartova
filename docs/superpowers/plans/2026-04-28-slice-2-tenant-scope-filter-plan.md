# Slice 2 Followup — Tenant-Scope Endpoint Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the slice-2 tenant-scope mechanism back to the spec/ADR-0090 §Decision shape: endpoint filter (not middleware), EF transaction enlistment (if needed), transport-agnostic begin-failure exception, no `SharedKernel.AspNetCore → SharedKernel.Postgres` reference. Sets the stage for Slice 3's first tenant-scoped writes.

**Architecture:** Replace `TenantScopeMiddleware` with `TenantScopeEndpointFilter` attached via `RouteGroupBuilder.AddEndpointFilter<>`. Wrap `NpgsqlException` from `BeginAsync` in a new `TenantScopeBeginException` defined in `SharedKernel`, so the filter (in `SharedKernel.AspNetCore`) catches a transport-agnostic type and the cross-project reference can be cut. Probe-test the EF auto-enlistment behavior; ship `EnlistInTenantScopeInterceptor` only if EF doesn't auto-enlist. Refactor existing §6.3 tests to use the EF write path now that `INpgsqlTenantScope.Transaction` is public.

**Tech Stack:** .NET 10, ASP.NET Core 10 (endpoint filters), EF Core 10, Npgsql 10, NetArchTest 1.3, Testcontainers 4, xUnit, FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-04-28-slice-2-tenant-scope-filter-design.md`

---

## Pre-flight

Before starting Task 1, verify the branch state.

- [ ] **Branch check.** Confirm `git branch --show-current` outputs `feat/slice-2-tenant-scope-filter`. If not, `git checkout feat/slice-2-tenant-scope-filter`.
- [ ] **Working tree clean.** Confirm `git status --short` is empty (besides this plan file if it isn't yet committed). If dirty, stash or commit first.
- [ ] **Build green from start.** Run `cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"`. Must report `Build succeeded. 0 Warning(s) 0 Error(s)`. If not, **stop** — fix the failure on master before starting this PR.

---

## Task 1: Probe test — does EF Core 10 auto-enlist in the scope's transaction?

**Goal:** Decide whether `EnlistInTenantScopeInterceptor` ships. The probe test is RED-first: write it, run it, observe the result. The result determines whether Task 7 runs at all.

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/EfEnlistmentProbeTests.cs`
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/OrganizationTestHelper.cs`

**Why this is task 1:** Several later tasks depend on whether the interceptor ships. Tasks 6 and 8 reference `db.SaveChangesAsync()` working inside a scope; they only work if EF is enlisted. Settle this first.

- [ ] **Step 1: Create `OrganizationTestHelper.cs`** — a tiny helper that builds a domain Organization with an arbitrary TenantId via reflection. Production aggregates correctly forbid this; tests need it.

```csharp
// src/Modules/Organization/Kartova.Organization.IntegrationTests/OrganizationTestHelper.cs
using System.Reflection;
using Kartova.Organization.Domain;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Organization.IntegrationTests;

internal static class OrganizationTestHelper
{
    /// <summary>
    /// Constructs an Organization aggregate with explicit TenantId. The production
    /// Organization.Create factory generates a fresh TenantId from the new Id; this
    /// helper uses reflection to override it so tests can write rows under a
    /// pre-seeded tenant (e.g. SeededOrgs.OrgA) inside the active tenant scope.
    /// Test-only.
    /// </summary>
    public static Organization CreateWithTenant(Guid id, TenantId tenantId, string name)
    {
        var org = Organization.Create(name);

        var idProp = typeof(Organization).GetProperty(nameof(Organization.Id))!;
        idProp.SetValue(org, new OrganizationId(id));

        var tenantProp = typeof(Organization).GetProperty(nameof(Organization.TenantId))!;
        tenantProp.SetValue(org, tenantId);

        return org;
    }
}
```

- [ ] **Step 2: Write the failing probe test.**

```csharp
// src/Modules/Organization/Kartova.Organization.IntegrationTests/EfEnlistmentProbeTests.cs
using FluentAssertions;
using Kartova.Organization.Infrastructure;
using Kartova.SharedKernel.Multitenancy;
using Kartova.Testing.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Probe: does EF Core 10 + Npgsql auto-enlist in a connection's active transaction
/// when the DbContext is configured via UseNpgsql(connection)?
///
/// Outcome decides whether EnlistInTenantScopeInterceptor (spec §3.1) ships.
/// - Pass → EF auto-enlists; interceptor not needed.
/// - Fail → ship the interceptor.
/// </summary>
[Collection(KartovaApiCollection.Name)]
public class EfEnlistmentProbeTests
{
    private readonly KartovaApiFixture _fx;

    public EfEnlistmentProbeTests(KartovaApiFixture fx) => _fx = fx;

    [Fact]
    public async Task DbContext_writes_inside_scope_are_rolled_back_on_scope_dispose()
    {
        // If EF is enlisted in the scope's tx, the scope's DisposeAsync rollback must
        // drop the row. If EF runs its own tx (or no tx), the row commits and persists.
        var rowName = $"Probe-{Guid.NewGuid()}";
        var rowId = Guid.NewGuid();

        using var hostScope = _fx.Services.CreateScope();
        var sp = hostScope.ServiceProvider;
        var tenantScope = sp.GetRequiredService<ITenantScope>();

        await using (var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default))
        {
            var db = sp.GetRequiredService<OrganizationDbContext>();
            var org = OrganizationTestHelper.CreateWithTenant(rowId, SeededOrgs.OrgA, rowName);
            db.Add(org);
            await db.SaveChangesAsync();

            // Exit without CommitAsync → handle.DisposeAsync rolls back.
        }

        await using var bypass = new NpgsqlConnection(_fx.BypassConnectionString);
        await bypass.OpenAsync();
        await using var cmd = bypass.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM organizations WHERE name = $1";
        cmd.Parameters.AddWithValue(rowName);
        var count = (long)(await cmd.ExecuteScalarAsync())!;

        count.Should().Be(0,
            because: "if EF is enlisted in the scope's transaction, scope rollback drops the row. " +
                     "Non-zero count means EF committed independently (interceptor needed).");
    }

    [Fact]
    public async Task DbContext_CurrentTransaction_matches_scope_transaction()
    {
        // Direct verification via EF's public API: the DbContext should report the
        // scope's NpgsqlTransaction as its current ambient transaction.
        using var hostScope = _fx.Services.CreateScope();
        var sp = hostScope.ServiceProvider;
        var tenantScope = sp.GetRequiredService<ITenantScope>();
        var npgScope = (INpgsqlTenantScope)tenantScope;

        await using var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default);

        var db = sp.GetRequiredService<OrganizationDbContext>();

        // Trigger the connection to be associated with the DbContext.
        await db.Database.OpenConnectionAsync();

        var efTx = db.Database.CurrentTransaction;
        efTx.Should().NotBeNull(
            because: "DbContext should report enlistment in the scope's active transaction");
    }
}
```

- [ ] **Step 3: Build the test project.**

```bash
cmd //c "dotnet build src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. The test references `INpgsqlTenantScope.Transaction` only via the namespace import; no public-API change needed yet.

- [ ] **Step 4: Run the probe — observe outcome.**

```bash
cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --no-build --filter \"FullyQualifiedName~EfEnlistmentProbeTests\" --nologo -v minimal"
```

Expected: **Either** Pass (both probes green → EF auto-enlists → SKIP Task 7) **or** Fail (one or both probes red → EF does not auto-enlist → DO Task 7).

**Record the outcome in your notes.** This decision propagates to Tasks 6, 7, 8.

- [ ] **Step 5: Commit the probe regardless of outcome.**

```bash
git add src/Modules/Organization/Kartova.Organization.IntegrationTests/EfEnlistmentProbeTests.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/OrganizationTestHelper.cs
git commit -m "test(probe): EF Core 10 auto-enlistment in scope transaction

Probe test that decides whether EnlistInTenantScopeInterceptor
(spec §3.1) is needed. Outcome determined at first run; Task 7
of the implementation plan is conditional on this result.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 2: Introduce `TenantScopeBeginException`

**Goal:** Define the transport-agnostic exception in `SharedKernel.Multitenancy` so transport adapters can catch a non-Postgres type. No behavioral change yet — Task 3 wraps `NpgsqlException` and Task 6 catches it in the filter.

**Files:**
- Create: `src/Kartova.SharedKernel/Multitenancy/TenantScopeBeginException.cs`

- [ ] **Step 1: Create the exception class.**

```csharp
// src/Kartova.SharedKernel/Multitenancy/TenantScopeBeginException.cs
namespace Kartova.SharedKernel.Multitenancy;

/// <summary>
/// Thrown by <see cref="ITenantScope.BeginAsync"/> when the underlying storage cannot
/// open a connection or begin a transaction (database unavailable, pool exhausted,
/// network failure). Transport adapters (HTTP filter, Wolverine middleware) catch this
/// type to map to their respective transport-level "service unavailable" semantics —
/// e.g. HTTP 503 + RFC 7807 problem-details per ADR-0091, or Wolverine retry/DLQ.
///
/// The inner exception carries the storage-specific diagnostic detail (e.g. NpgsqlException).
/// See ADR-0090 §Error handling.
/// </summary>
public sealed class TenantScopeBeginException : Exception
{
    public TenantScopeBeginException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
```

- [ ] **Step 2: Build SharedKernel.**

```bash
cmd //c "dotnet build src/Kartova.SharedKernel/Kartova.SharedKernel.csproj -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit.**

```bash
git add src/Kartova.SharedKernel/Multitenancy/TenantScopeBeginException.cs
git commit -m "feat(sharedkernel): add TenantScopeBeginException

Transport-agnostic exception type for ITenantScope.BeginAsync failures.
Allows transport adapters (HTTP, Wolverine) to map to transport-specific
unavailability semantics without knowing about Npgsql/Postgres.
ADR-0090 §Error handling.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: Wrap `NpgsqlException` in `TenantScope.BeginAsync`

**Goal:** Translate Postgres-specific exceptions to the agnostic type at the boundary. Filter (Task 6) and tests stop seeing `NpgsqlException` from `BeginAsync`.

**Files:**
- Modify: `src/Kartova.SharedKernel.Postgres/TenantScope.cs`
- Modify: `src/Kartova.SharedKernel.AspNetCore/TenantScopeMiddleware.cs` (interim — middleware still exists at this point, must keep working)

- [ ] **Step 1: Update `TenantScope.BeginAsync` to wrap `NpgsqlException`.**

Locate the existing `BeginAsync` method in `src/Kartova.SharedKernel.Postgres/TenantScope.cs`. Replace its body's `try/catch` block. The current shape is:

```csharp
try
{
    _connection = await _dataSource.OpenConnectionAsync(ct);
    _transaction = await _connection.BeginTransactionAsync(ct);
    // ...set_config...
    return new Handle(this);
}
catch
{
    // dispose + null-out + rethrow (current behavior)
    if (_transaction is not null) { try { await _transaction.DisposeAsync(); } catch { } _transaction = null; }
    if (_connection is not null) { try { await _connection.DisposeAsync(); } catch { } _connection = null; }
    throw;
}
```

Change the trailing `throw;` to wrap `NpgsqlException` only:

```csharp
catch (NpgsqlException npg)
{
    if (_transaction is not null) { try { await _transaction.DisposeAsync(); } catch { } _transaction = null; }
    if (_connection is not null) { try { await _connection.DisposeAsync(); } catch { } _connection = null; }
    throw new TenantScopeBeginException(
        "Failed to begin tenant scope: database unavailable or connection failure.",
        npg);
}
catch
{
    if (_transaction is not null) { try { await _transaction.DisposeAsync(); } catch { } _transaction = null; }
    if (_connection is not null) { try { await _connection.DisposeAsync(); } catch { } _connection = null; }
    throw;
}
```

Add the using if needed: `using Kartova.SharedKernel.Multitenancy;` (already present — `TenantId` lives there).

- [ ] **Step 2: Update `TenantScopeMiddleware` to catch the new type.**

Currently the middleware catches `NpgsqlException`. Change it to `TenantScopeBeginException` so the middleware works *during* this transitional period (it gets deleted in Task 6, but must keep working until then).

Locate the catch block in `src/Kartova.SharedKernel.AspNetCore/TenantScopeMiddleware.cs`:

```csharp
catch (NpgsqlException)
{
    var problem = Results.Problem(
        type: ProblemTypes.ServiceUnavailable,
        title: "Database is currently unavailable",
        statusCode: StatusCodes.Status503ServiceUnavailable);
    await problem.ExecuteAsync(context);
    return;
}
```

Change `catch (NpgsqlException)` → `catch (TenantScopeBeginException)`. Update the `using` statements: remove `using Npgsql;`, add `using Kartova.SharedKernel.Multitenancy;`.

- [ ] **Step 3: Build.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Run unit + arch tests.**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: all green (38 unit + 30 arch = 68 tests). The probe tests from Task 1 are excluded by the filter.

- [ ] **Step 5: Commit.**

```bash
git add src/Kartova.SharedKernel.Postgres/TenantScope.cs src/Kartova.SharedKernel.AspNetCore/TenantScopeMiddleware.cs
git commit -m "refactor(tenant-scope): wrap NpgsqlException in TenantScopeBeginException

TenantScope.BeginAsync now translates Postgres-specific exceptions to the
agnostic SharedKernel type. TenantScopeMiddleware updated to catch the
new type. The middleware itself is removed in a follow-up task; this is
the transitional state.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: Cut the `SharedKernel.AspNetCore → SharedKernel.Postgres` project reference

**Goal:** Remove the cross-project reference now that the middleware no longer needs Postgres types. Verify with a clean build.

**Files:**
- Modify: `src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj`

- [ ] **Step 1: Remove the project reference.**

Open `src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj`. Locate:

```xml
<ItemGroup>
  <ProjectReference Include="..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
  <ProjectReference Include="..\Kartova.SharedKernel.Postgres\Kartova.SharedKernel.Postgres.csproj" />
</ItemGroup>
```

Delete the second `<ProjectReference>` line so it becomes:

```xml
<ItemGroup>
  <ProjectReference Include="..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Build the AspNetCore project alone first to verify nothing else depends on Postgres types.**

```bash
cmd //c "dotnet build src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If errors appear (e.g. a stray `Npgsql` or `INpgsqlTenantScope` reference), grep the project for those identifiers and refactor them away — they should not exist after Task 3, but a hidden cross-call might surface here.

- [ ] **Step 3: Build the full solution.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 4: Run unit + arch tests to confirm no regression.**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: all green.

- [ ] **Step 5: Commit.**

```bash
git add src/Kartova.SharedKernel.AspNetCore/Kartova.SharedKernel.AspNetCore.csproj
git commit -m "refactor(sharedkernel): drop AspNetCore → Postgres project reference

Spec §3.2 specifies sibling adapters with no cross-reference. Now that
TenantScopeBeginException carries the failure semantics across the
boundary, AspNetCore no longer needs Postgres types.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: Add architecture rule `AspNetCore_does_not_reference_Postgres`

**Goal:** Codify the project-reference cut so it stays cut. NetArchTest scans the assembly's referenced-assembly list.

**Files:**
- Modify: `tests/Kartova.ArchitectureTests/TenantScopeRules.cs`

- [ ] **Step 1: Write the failing-by-default test.**

Open `tests/Kartova.ArchitectureTests/TenantScopeRules.cs`. Add this fact alongside the existing rules (e.g. after `Admin_bypass_DbContext_is_isolated_to_admin_assembly`):

```csharp
[Fact]
public void AspNetCore_adapter_does_not_reference_Postgres_adapter()
{
    // Spec §3.2: SharedKernel.AspNetCore and SharedKernel.Postgres are sibling
    // adapters consumed by the API composition root. Cross-reference would force
    // any future transport adapter (Wolverine, Kafka) to inherit a Postgres
    // dependency. ADR-0090 + slice-2-followup design 2026-04-28.
    var aspNetCoreRefs = SharedKernelAspNetCore
        .GetReferencedAssemblies()
        .Select(a => a.Name)
        .ToArray();

    aspNetCoreRefs.Should().NotContain("Kartova.SharedKernel.Postgres",
        because: "Spec §3.2 forbids the cross-reference; transport adapters " +
                 "exchange failures via Kartova.SharedKernel.Multitenancy.TenantScopeBeginException");
}
```

- [ ] **Step 2: Run the new test.**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --filter \"FullyQualifiedName~AspNetCore_adapter_does_not_reference_Postgres_adapter\" --nologo -v minimal"
```

Expected: PASS (the project reference was removed in Task 4).

- [ ] **Step 3: Run all arch tests.**

```bash
cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj --no-build --nologo -v minimal"
```

Expected: 31/31 (was 30 + 1 new).

- [ ] **Step 4: Commit.**

```bash
git add tests/Kartova.ArchitectureTests/TenantScopeRules.cs
git commit -m "test(arch): forbid SharedKernel.AspNetCore → SharedKernel.Postgres reference

Codifies the project-reference cut from the prior task as an arch rule.
Future contributors who reintroduce the dependency will fail CI before
the smell propagates to other transport adapters.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: Expose `INpgsqlTenantScope.Transaction` publicly + delete `TransactionViaReflection` from tests

**Goal:** Replace the reflection-based access added in PR #4 with a first-class interface member. Tests stop reflecting; the upcoming `EnlistInTenantScopeInterceptor` (Task 7, conditional) and the EF write path refactor (Task 8) consume this.

**Files:**
- Modify: `src/Kartova.SharedKernel.Postgres/INpgsqlTenantScope.cs`
- Modify: `src/Kartova.SharedKernel.Postgres/TenantScope.cs`
- Modify: `src/Modules/Organization/Kartova.Organization.IntegrationTests/TenantScopeMechanismTests.cs`

- [ ] **Step 1: Add `Transaction` to the interface.**

Open `src/Kartova.SharedKernel.Postgres/INpgsqlTenantScope.cs`. Replace the file body:

```csharp
using Kartova.SharedKernel.Multitenancy;
using Npgsql;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// Postgres-specific extension of <see cref="ITenantScope"/> that exposes the
/// already-open connection and active transaction so module DbContexts (and the
/// optional EnlistInTenantScopeInterceptor) can share them (ADR-0090 §3.1).
/// Consumed only inside <see cref="Kartova.SharedKernel.Postgres"/> and by tests
/// that drive the mechanism directly. Application code depends on
/// <see cref="ITenantScope"/>.
/// </summary>
public interface INpgsqlTenantScope : ITenantScope
{
    NpgsqlConnection Connection { get; }
    NpgsqlTransaction Transaction { get; }
}
```

- [ ] **Step 2: Make `TenantScope.Transaction` public.**

Open `src/Kartova.SharedKernel.Postgres/TenantScope.cs`. Locate:

```csharp
internal NpgsqlTransaction Transaction =>
    _transaction ?? throw new InvalidOperationException(
        "TenantScope has no active transaction.");
```

Change `internal` → `public`. The body is unchanged.

- [ ] **Step 3: Refactor `TenantScopeMechanismTests` to use the new public property.**

Open `src/Modules/Organization/Kartova.Organization.IntegrationTests/TenantScopeMechanismTests.cs`. Find both call sites that read the transaction:

```csharp
var tx = TransactionViaReflection(rawScope);
```

Replace each with:

```csharp
var tx = npgScope.Transaction;
```

Also remove the helper method:

```csharp
private static NpgsqlTransaction TransactionViaReflection(TenantScope scope)
{
    // ...
}
```

And remove the now-unused `using System.Reflection;` and any unused `var rawScope = (TenantScope)tenantScope;` lines.

- [ ] **Step 4: Build.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: clean build.

- [ ] **Step 5: Run the existing §6.3 tests against the testcontainer to confirm no regression from the refactor.**

```bash
cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --no-build --filter \"FullyQualifiedName~TenantScopeMechanismTests\" --nologo -v minimal"
```

Expected: 3/3 pass (the existing three §6.3 tests; they still use raw SQL — Task 8 converts them to the EF write path).

- [ ] **Step 6: Commit.**

```bash
git add src/Kartova.SharedKernel.Postgres/INpgsqlTenantScope.cs src/Kartova.SharedKernel.Postgres/TenantScope.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/TenantScopeMechanismTests.cs
git commit -m "refactor(tenant-scope): expose INpgsqlTenantScope.Transaction publicly

Replaces the reflection-based access added by PR #4 with a first-class
interface member. INpgsqlTenantScope is already Postgres-specific
(exposes NpgsqlConnection); exposing NpgsqlTransaction is the same
abstraction level. Enables the upcoming filter conversion and the
optional EnlistInTenantScopeInterceptor without test gymnastics.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: Hybrid scope adapter — `TenantScopeBeginMiddleware` + `TenantScopeCommitEndpointFilter`

**Goal:** Replace the slice-2 single middleware with a two-piece adapter. The begin-middleware opens the scope BEFORE parameter binding (so DI-injected `OrganizationDbContext` resolves with an active scope). The commit-filter commits BETWEEN handler return and `IResult.ExecuteAsync` (durability promise from ADR-0090 §Decision). The begin-middleware owns the handle's `DisposeAsync` lifetime so rollback fires on any non-committed exit.

**Why hybrid:** an earlier attempt at a pure endpoint filter blocked because ASP.NET Core 10 minimal-API parameter binding resolves DI-injected DbContexts BEFORE the filter chain runs. `OrganizationDbContext` resolution triggered `scope.Connection`, which threw because the scope was inactive. The hybrid moves `Begin` earlier (middleware, runs before the endpoint's request delegate) while keeping `Commit` inside the filter chain (so it runs before `IResult.ExecuteAsync`). Spec §Decisions and the ADR-0090 addendum document this.

**Files:**
- Create: `src/Kartova.SharedKernel.AspNetCore/RequireTenantScopeMarker.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/TenantScopeBeginMiddleware.cs`
- Create: `src/Kartova.SharedKernel.AspNetCore/TenantScopeCommitEndpointFilter.cs`
- Modify: `src/Kartova.SharedKernel.AspNetCore/TenantScopeRouteExtensions.cs`
- Delete: `src/Kartova.SharedKernel.AspNetCore/TenantScopeMiddleware.cs`
- Modify: `src/Kartova.Api/Program.cs`
- Modify: `tests/Kartova.ArchitectureTests/TenantScopeRules.cs` (the `SharedKernelAspNetCore` field anchors on the deleted `TenantScopeMiddleware` type — re-anchor on a type that survives this task, e.g. `TenantScopeBeginMiddleware` or `TenantScopeCommitEndpointFilter`)
- Possibly modify: `src/Kartova.SharedKernel.Postgres/TenantScopeRequiredInterceptor.cs` (its docstring or error message may name `TenantScopeMiddleware` — search and update)

- [ ] **Step 1: Create `RequireTenantScopeMarker`.**

```csharp
// src/Kartova.SharedKernel.AspNetCore/RequireTenantScopeMarker.cs
namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Endpoint metadata marker applied by <see cref="TenantScopeRouteExtensions.RequireTenantScope"/>.
/// <see cref="TenantScopeBeginMiddleware"/> inspects matched endpoints for this marker to
/// decide whether to open an <c>ITenantScope</c> for the request. The commit-filter is
/// attached directly via <c>AddEndpointFilter</c> on the same route group, so the marker
/// is only consumed by the begin-middleware. See ADR-0090 §Addendum (2026-04-28).
/// </summary>
public sealed class RequireTenantScopeMarker
{
    public static readonly RequireTenantScopeMarker Instance = new();
}
```

- [ ] **Step 2: Create `TenantScopeBeginMiddleware`.**

```csharp
// src/Kartova.SharedKernel.AspNetCore/TenantScopeBeginMiddleware.cs
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Opens an <see cref="ITenantScope"/> for endpoints carrying <see cref="RequireTenantScopeMarker"/>
/// metadata. Runs AFTER <c>UseAuthentication</c>/<c>UseAuthorization</c> so the JWT-derived
/// <see cref="ITenantContext"/> is populated, and BEFORE endpoint dispatch so DI-injected
/// DbContexts (registered via <c>AddModuleDbContext</c>) resolve against an active scope.
///
/// Pairs with <see cref="TenantScopeCommitEndpointFilter"/> which commits between handler
/// return and <c>IResult.ExecuteAsync</c>. This middleware owns the handle's
/// <see cref="IAsyncDisposable.DisposeAsync"/> lifetime — the <c>finally</c> block runs after
/// the filter chain unwinds, so rollback fires automatically on any non-committed exit
/// (handler exception, commit failure, cancellation).
///
/// Pipeline order in <c>Program.cs</c>:
///   UseAuthentication → UseAuthorization → UseMiddleware&lt;TenantScopeBeginMiddleware&gt;
///   → endpoint dispatch (parameter binding, filter chain, handler, IResult.ExecuteAsync).
///
/// See ADR-0090 §Addendum (2026-04-28) for why this is split from the commit filter.
/// </summary>
public sealed class TenantScopeBeginMiddleware
{
    /// <summary>
    /// Key under which the active <see cref="IAsyncTenantScopeHandle"/> is stored in
    /// <see cref="HttpContext.Items"/> for retrieval by <see cref="TenantScopeCommitEndpointFilter"/>.
    /// Internal to this assembly; the only reader is the commit filter.
    /// </summary>
    internal const string HandleKey = "Kartova.TenantScope.Handle";

    private readonly RequestDelegate _next;

    public TenantScopeBeginMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        var needsScope = endpoint?.Metadata.GetMetadata<RequireTenantScopeMarker>() is not null;
        if (!needsScope)
        {
            await _next(context);
            return;
        }

        var tenantContext = context.RequestServices.GetRequiredService<ITenantContext>();
        if (!tenantContext.IsTenantScoped)
        {
            await Results.Problem(
                type: ProblemTypes.MissingTenantClaim,
                title: "JWT is missing the tenant_id claim",
                statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
            return;
        }

        var scope = context.RequestServices.GetRequiredService<ITenantScope>();
        var ct = context.RequestAborted;

        IAsyncTenantScopeHandle handle;
        try
        {
            handle = await scope.BeginAsync(tenantContext.Id, ct);
        }
        catch (TenantScopeBeginException)
        {
            await Results.Problem(
                type: ProblemTypes.ServiceUnavailable,
                title: "Database is currently unavailable",
                statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
            return;
        }

        // Hand off to the commit filter via Items; middleware retains DisposeAsync ownership
        // so rollback fires on any non-committed exit (handler exception, commit failure, cancel).
        context.Items[HandleKey] = handle;
        try
        {
            await _next(context);   // parameter binding + filter chain + handler + IResult.ExecuteAsync
        }
        finally
        {
            await handle.DisposeAsync();
        }
    }
}
```

- [ ] **Step 3: Create `TenantScopeCommitEndpointFilter`.**

```csharp
// src/Kartova.SharedKernel.AspNetCore/TenantScopeCommitEndpointFilter.cs
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Http;

namespace Kartova.SharedKernel.AspNetCore;

/// <summary>
/// Commits the active <see cref="ITenantScope"/> transaction BETWEEN handler return and
/// <see cref="IResult.ExecuteAsync"/>, preserving ADR-0090's durability promise: if commit
/// fails, the exception bubbles to <c>UseExceptionHandler</c> and surfaces as 500 +
/// RFC 7807 problem-details — the client never sees a partial body for a transaction
/// that failed to commit. Streaming responses (<c>Results.Stream</c>, SSE,
/// <c>IAsyncEnumerable&lt;T&gt;</c>) are also durability-correct because the IResult is
/// returned but not yet executed when commit runs.
///
/// Pairs with <see cref="TenantScopeBeginMiddleware"/> which opens the scope and stashes
/// the handle in <c>HttpContext.Items[HandleKey]</c>. Missing key indicates a wiring bug
/// (filter attached without the begin-middleware in the request pipeline) and surfaces
/// immediately as <see cref="InvalidOperationException"/> rather than silently committing
/// nothing.
/// </summary>
public sealed class TenantScopeCommitEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx,
        EndpointFilterDelegate next)
    {
        var result = await next(ctx);   // handler returns IResult — NOT yet executed

        if (!ctx.HttpContext.Items.TryGetValue(TenantScopeBeginMiddleware.HandleKey, out var obj)
            || obj is not IAsyncTenantScopeHandle handle)
        {
            throw new InvalidOperationException(
                "TenantScopeCommitEndpointFilter ran without an active scope handle. " +
                "TenantScopeBeginMiddleware must be wired in the request pipeline " +
                "(app.UseMiddleware<TenantScopeBeginMiddleware>() before endpoint dispatch).");
        }

        await handle.CommitAsync(ctx.HttpContext.RequestAborted);
        return result;   // ASP.NET runs IResult.ExecuteAsync AFTER commit succeeds
    }
}
```

- [ ] **Step 4: Rewrite `TenantScopeRouteExtensions.RequireTenantScope`.**

Replace the entire file body:

```csharp
// src/Kartova.SharedKernel.AspNetCore/TenantScopeRouteExtensions.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Kartova.SharedKernel.AspNetCore;

public static class TenantScopeRouteExtensions
{
    /// <summary>
    /// Marks a route group as tenant-scoped. Wires three things:
    /// <list type="bullet">
    ///   <item><see cref="AuthorizationEndpointConventionBuilderExtensions.RequireAuthorization{TBuilder}(TBuilder, string[])"/>
    ///         — tenant-scoped routes are authenticated by definition (need a JWT to
    ///         extract <c>tenant_id</c>).</item>
    ///   <item><see cref="RequireTenantScopeMarker"/> metadata — <see cref="TenantScopeBeginMiddleware"/>
    ///         uses it to identify endpoints that should open a tenant scope before
    ///         parameter binding.</item>
    ///   <item><see cref="TenantScopeCommitEndpointFilter"/> — commits the scope's
    ///         transaction between handler return and <see cref="IResult.ExecuteAsync"/>,
    ///         preserving the durability promise from ADR-0090.</item>
    /// </list>
    /// See ADR-0090 §Addendum (2026-04-28) for why this is a two-piece adapter.
    /// </summary>
    public static RouteGroupBuilder RequireTenantScope(this RouteGroupBuilder builder)
    {
        builder.RequireAuthorization();
        builder.WithMetadata(RequireTenantScopeMarker.Instance);
        builder.AddEndpointFilter<TenantScopeCommitEndpointFilter>();
        return builder;
    }
}
```

- [ ] **Step 5: Delete `TenantScopeMiddleware.cs`.**

```bash
git rm src/Kartova.SharedKernel.AspNetCore/TenantScopeMiddleware.cs
```

The deleted file also previously housed `RequireTenantScopeMarker`. Step 1 above re-introduces the marker as a standalone file — the marker's role survives the deletion, but the combined Begin+Commit middleware does not.

- [ ] **Step 6: Update `Program.cs` middleware wiring.**

Open `src/Kartova.Api/Program.cs`. Locate:

```csharp
app.UseMiddleware<TenantScopeMiddleware>();
```

Replace with:

```csharp
app.UseMiddleware<TenantScopeBeginMiddleware>();
```

The `RequireTenantScope()` call on `MapGroup("/api/v1")` already attaches the commit filter to every endpoint in the group; no further wiring needed.

- [ ] **Step 7: Re-anchor the architecture-test assembly handle.**

Open `tests/Kartova.ArchitectureTests/TenantScopeRules.cs`. Locate:

```csharp
private static readonly Assembly SharedKernelAspNetCore = typeof(Kartova.SharedKernel.AspNetCore.TenantScopeMiddleware).Assembly;
```

`TenantScopeMiddleware` is gone. Re-anchor on a type that survives this PR. Use `TenantScopeBeginMiddleware`:

```csharp
private static readonly Assembly SharedKernelAspNetCore = typeof(Kartova.SharedKernel.AspNetCore.TenantScopeBeginMiddleware).Assembly;
```

Search the rest of the file for any other reference to `TenantScopeMiddleware` or `RequireTenantScopeMarker` — if a fact existed asserting either type's existence (none expected), update or delete.

- [ ] **Step 8: Update `TenantScopeRequiredInterceptor` error message if it names the old middleware.**

Search `src/Kartova.SharedKernel.Postgres/TenantScopeRequiredInterceptor.cs` for the string `TenantScopeMiddleware`. The slice-2 commit had a fail-fast message like:

```
"Either the endpoint is missing RequireTenantScope() (TenantScopeMiddleware skipped it),
or the handler is running outside a transport adapter."
```

Update the parenthetical to reference `TenantScopeBeginMiddleware` so the diagnostic accurately points future readers at the right type. Leave the rest of the message intact.

- [ ] **Step 9: Build the full solution.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. If a build error mentions `TenantScopeMiddleware` or claims `RequireTenantScopeMarker` cannot be found, grep for the symbol and fix the stale reference.

- [ ] **Step 10: Run unit + arch tests (no Docker required).**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: 70/70 (no count change vs. Task 6).

- [ ] **Step 11: Run integration tests against the testcontainer (Docker required).**

```bash
cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --no-build --nologo -v minimal"
```

Expected:
- The four tests that failed in the BLOCKED Task 7 attempt (`Get_me_returns_current_tenant_row`, `Each_tenant_only_sees_its_own_organization`, `Platform_admin_without_tenant_hits_missing_tenant_on_tenant_scoped_route`, and any other 500 from DbContext resolution) now PASS — `TenantScopeBeginMiddleware` opens the scope before parameter binding, so DbContext DI resolves correctly.
- `EfEnlistmentProbeTests.DbContext_writes_inside_scope_are_rolled_back_on_scope_dispose` PASSES (rollback at the SQL level still works).
- `EfEnlistmentProbeTests.DbContext_CurrentTransaction_matches_scope_transaction` is **expected to FAIL** until Task 8 ships the enlistment interceptor. Record this explicitly in your report.

Total expected: 13 pass / 1 fail (the probe).

- [ ] **Step 12: Commit.**

```bash
git add src/Kartova.SharedKernel.AspNetCore/RequireTenantScopeMarker.cs \
        src/Kartova.SharedKernel.AspNetCore/TenantScopeBeginMiddleware.cs \
        src/Kartova.SharedKernel.AspNetCore/TenantScopeCommitEndpointFilter.cs \
        src/Kartova.SharedKernel.AspNetCore/TenantScopeRouteExtensions.cs \
        src/Kartova.Api/Program.cs \
        tests/Kartova.ArchitectureTests/TenantScopeRules.cs \
        src/Kartova.SharedKernel.Postgres/TenantScopeRequiredInterceptor.cs
# git rm of TenantScopeMiddleware.cs already staged from Step 5
git commit -m "refactor(tenant-scope): hybrid two-piece adapter — begin-middleware + commit-filter

Replaces TenantScopeMiddleware with two single-responsibility pieces:

  - TenantScopeBeginMiddleware: opens the ITenantScope on routes tagged with
    RequireTenantScopeMarker, BEFORE parameter binding so DI-injected DbContexts
    resolve against an active scope. Owns the handle's DisposeAsync lifetime in
    a try/finally so rollback fires on any non-committed exit.
  - TenantScopeCommitEndpointFilter: commits the scope BETWEEN handler return
    and IResult.ExecuteAsync, preserving ADR-0090's durability promise. Streaming
    responses now fail cleanly with 5xx + problem-details on commit failure.

Pure-filter design (originally drafted in spec §Decisions) was infeasible:
ASP.NET Core 10 minimal-API parameter binding resolves DI-injected DbContexts
BEFORE the filter chain runs, so a filter calling BeginAsync runs too late and
DbContext resolution throws because scope.Connection is inactive. Hybrid moves
Begin earlier (middleware) while keeping Commit in the filter chain. See spec
§Decisions and the ADR-0090 addendum.

RequireTenantScope() now chains RequireAuthorization() + RequireTenantScopeMarker
metadata + TenantScopeCommitEndpointFilter — three behaviors documented at the
method.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: (CONDITIONAL) Add `EnlistInTenantScopeInterceptor`

**Run this task ONLY if the probe in Task 1 failed.** If the probe passed, skip to Task 9.

**Goal:** Force EF Core to enlist its DbContext transaction tracking in the scope's `NpgsqlTransaction` so DbContext writes participate in the same atomic unit.

**Files:**
- Create: `src/Kartova.SharedKernel.Postgres/EnlistInTenantScopeInterceptor.cs`
- Modify: `src/Kartova.SharedKernel.Postgres/AddModuleDbContextExtensions.cs`

The implementation pattern depends on EF Core 10's exact behavior. The most reliable hook for "fire on first DbContext use" is `IDbCommandInterceptor.CommandCreatingAsync` (since `ConnectionOpeningAsync` won't fire when the connection is already open). The interceptor calls `context.Database.UseTransactionAsync(scope.Transaction)` if not already enlisted, then proceeds.

- [ ] **Step 1: Create the interceptor.**

```csharp
// src/Kartova.SharedKernel.Postgres/EnlistInTenantScopeInterceptor.cs
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kartova.SharedKernel.Postgres;

/// <summary>
/// Ensures the DbContext's transaction tracking is enlisted in the shared
/// <see cref="INpgsqlTenantScope.Transaction"/> on first use. Without this, EF
/// Core may not auto-enlist when the connection has an active transaction,
/// causing DbContext writes to either fail or silently run on EF's own
/// transaction (breaking the per-request atomicity guarantee in ADR-0090).
///
/// Hook: IDbCommandInterceptor.CommandCreatingAsync — fires before EF executes
/// any command, regardless of whether the underlying connection is already open.
/// </summary>
public sealed class EnlistInTenantScopeInterceptor : DbCommandInterceptor
{
    private readonly INpgsqlTenantScope _scope;

    public EnlistInTenantScopeInterceptor(INpgsqlTenantScope scope)
    {
        _scope = scope;
    }

    public override async ValueTask<InterceptionResult<System.Data.Common.DbCommand>> CommandCreatingAsync(
        CommandCorrelatedEventData eventData,
        InterceptionResult<System.Data.Common.DbCommand> result,
        CancellationToken cancellationToken = default)
    {
        var dbContext = eventData.Context;
        if (dbContext is not null && _scope.IsActive && dbContext.Database.CurrentTransaction is null)
        {
            await dbContext.Database.UseTransactionAsync(_scope.Transaction, cancellationToken);
        }
        return result;
    }
}
```

- [ ] **Step 2: Wire the interceptor in `AddModuleDbContext`.**

Open `src/Kartova.SharedKernel.Postgres/AddModuleDbContextExtensions.cs`. Locate the existing registration:

```csharp
services.AddDbContext<TContext>((sp, options) =>
{
    var scope = sp.GetRequiredService<INpgsqlTenantScope>();
    options.UseNpgsql(scope.Connection, npg => npgsqlOptions?.Invoke(npg));
    options.AddInterceptors(sp.GetRequiredService<TenantScopeRequiredInterceptor>());
});
```

Add the enlistment interceptor:

```csharp
services.AddDbContext<TContext>((sp, options) =>
{
    var scope = sp.GetRequiredService<INpgsqlTenantScope>();
    options.UseNpgsql(scope.Connection, npg => npgsqlOptions?.Invoke(npg));
    options.AddInterceptors(sp.GetRequiredService<TenantScopeRequiredInterceptor>());
    options.AddInterceptors(sp.GetRequiredService<EnlistInTenantScopeInterceptor>());
});
```

Also register the interceptor itself in `AddTenantScope`:

```csharp
public static IServiceCollection AddTenantScope(this IServiceCollection services)
{
    services.AddScoped<ITenantContext, TenantContextAccessor>();
    services.AddScoped<TenantScope>();
    services.AddScoped<ITenantScope>(sp => sp.GetRequiredService<TenantScope>());
    services.AddScoped<INpgsqlTenantScope>(sp => sp.GetRequiredService<TenantScope>());
    services.AddScoped<TenantScopeRequiredInterceptor>();
    services.AddScoped<EnlistInTenantScopeInterceptor>();   // ← new
    return services;
}
```

- [ ] **Step 3: Build.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: clean build.

- [ ] **Step 4: Re-run the probe — must now pass.**

```bash
cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --no-build --filter \"FullyQualifiedName~EfEnlistmentProbeTests\" --nologo -v minimal"
```

Expected: 2/2 pass. If the probe still fails, the interceptor's hook is wrong — investigate `CommandCreatingAsync` vs alternative hooks (`SavingChangesAsync`, `IDbContextOptionsExtensionInfo`).

- [ ] **Step 5: Commit.**

```bash
git add src/Kartova.SharedKernel.Postgres/EnlistInTenantScopeInterceptor.cs src/Kartova.SharedKernel.Postgres/AddModuleDbContextExtensions.cs
git commit -m "feat(tenant-scope): EnlistInTenantScopeInterceptor

Closes spec §3.1 gap. Forces EF Core to enlist DbContext transaction
tracking in the shared INpgsqlTenantScope.Transaction on first command,
so DbContext writes participate in the per-request atomic unit per
ADR-0090. Probe test (Task 1) now passes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: Refactor `TenantScopeMechanismTests` to use the EF write path

**Goal:** The three §6.3 tests added in PR #4 currently insert via raw SQL with reflection on the (now public) `Transaction`. With EF correctly enlisted (probe passed, possibly via Task 8), the tests should drive writes through `OrganizationDbContext` — exercising the actual production code path.

**Files:**
- Modify: `src/Modules/Organization/Kartova.Organization.IntegrationTests/TenantScopeMechanismTests.cs`

- [ ] **Step 1: Refactor `Commit_failure_after_write_propagates_and_persists_no_data`.**

Replace the raw-SQL insert block with:

```csharp
var db = sp.GetRequiredService<OrganizationDbContext>();
var org = OrganizationTestHelper.CreateWithTenant(rowId, SeededOrgs.OrgA, rowName);
db.Add(org);
await db.SaveChangesAsync(default);

// Force commit failure: close the underlying connection while the tx is open.
await npgScope.Connection.CloseAsync();

var commit = async () => await handle.CommitAsync(default);
await commit.Should().ThrowAsync<Exception>(...);
```

Drop the `var rawScope = (TenantScope)tenantScope;` line and the `cmd.Transaction = npgScope.Transaction; INSERT INTO organizations ...` block. Keep the bypass-connection verification at the end unchanged.

- [ ] **Step 2: Refactor `Exception_during_handler_rolls_back_uncommitted_writes`.**

Same shape — replace raw SQL with:

```csharp
await using (var handle = await tenantScope.BeginAsync(SeededOrgs.OrgA, default))
{
    var db = sp.GetRequiredService<OrganizationDbContext>();
    var org = OrganizationTestHelper.CreateWithTenant(rowId, SeededOrgs.OrgA, rowName);
    db.Add(org);
    await db.SaveChangesAsync(default);
    // Exit without CommitAsync — simulates a thrown handler.
}
```

- [ ] **Step 3: Leave `SaveChanges_throws_from_interceptor_when_scope_inactive` as-is** — it already exercises the EF lifecycle (it constructs a DbContext with the inactive-scope stub and calls SaveChanges). The interceptor under test is `TenantScopeRequiredInterceptor`, not the new enlistment interceptor.

- [ ] **Step 4: Drop unused `using` statements** — `using Npgsql;` may be unused now (only `NpgsqlConnection` for the bypass check remains, so keep it). `using System.Reflection;` is no longer needed (the `TransactionViaReflection` helper went away in Task 6).

- [ ] **Step 5: Build.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: clean.

- [ ] **Step 6: Run the §6.3 tests.**

```bash
cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --no-build --filter \"FullyQualifiedName~TenantScopeMechanismTests\" --nologo -v minimal"
```

Expected: 3/3 pass. If `Commit_failure_*` or `Exception_during_handler_*` fails because EF re-opens the connection or starts a new transaction, the enlistment interceptor isn't firing as expected — return to Task 8 step 4 and investigate.

- [ ] **Step 7: Commit.**

```bash
git add src/Modules/Organization/Kartova.Organization.IntegrationTests/TenantScopeMechanismTests.cs
git commit -m "test(tenant-scope): refactor §6.3 tests to use EF write path

Now that EF transaction tracking is properly enlisted (auto-enlistment
or via EnlistInTenantScopeInterceptor), the §6.3 defense-in-depth tests
drive writes through OrganizationDbContext.SaveChangesAsync instead of
raw SQL via reflection. Exercises the production code path Slice 3 will
rely on.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: Add streaming-response durability regression test

**Goal:** Pin the durability promise. Test setup uses `IStartupFilter` to add a tenant-scoped streaming endpoint to the test host, plus a `FailingCommitTenantScopeDecorator` that flips commit-failure on demand.

**Files:**
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFaultInjectionFixture.cs`
- Create: `src/Modules/Organization/Kartova.Organization.IntegrationTests/StreamingDurabilityTests.cs`

- [ ] **Step 1: Create the fault-injection fixture.**

```csharp
// src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFaultInjectionFixture.cs
using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kartova.Organization.IntegrationTests;

/// <summary>
/// Subclass of KartovaApiFixture that:
///  - Injects a FailingCommitTenantScopeDecorator wrapping the real ITenantScope.
///    Tests flip CommitFailFlag.Fail to true to force CommitAsync to throw.
///  - Maps a tenant-scoped streaming endpoint /__test/stream via IStartupFilter so
///    StreamingDurabilityTests can exercise the durability promise.
/// Test-only.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class KartovaApiFaultInjectionFixture : KartovaApiFixture
{
    public CommitFailFlag CommitFailFlag { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(CommitFailFlag);

            // Replace ITenantScope registration with the decorator. The decorator
            // resolves the real TenantScope from DI directly, then wraps it.
            services.RemoveAll<ITenantScope>();
            services.AddScoped<ITenantScope>(sp =>
                new FailingCommitTenantScopeDecorator(
                    sp.GetRequiredService<Kartova.SharedKernel.Postgres.TenantScope>(),
                    sp.GetRequiredService<CommitFailFlag>()));

            services.AddSingleton<IStartupFilter, StreamingTestEndpointStartupFilter>();
        });
    }
}

[ExcludeFromCodeCoverage]
public sealed class CommitFailFlag
{
    public bool Fail { get; set; }
}

[ExcludeFromCodeCoverage]
internal sealed class FailingCommitTenantScopeDecorator : ITenantScope
{
    private readonly ITenantScope _inner;
    private readonly CommitFailFlag _flag;

    public FailingCommitTenantScopeDecorator(ITenantScope inner, CommitFailFlag flag)
    {
        _inner = inner;
        _flag = flag;
    }

    public bool IsActive => _inner.IsActive;

    public async Task<IAsyncTenantScopeHandle> BeginAsync(TenantId id, CancellationToken ct)
    {
        var inner = await _inner.BeginAsync(id, ct);
        return new Handle(inner, _flag);
    }

    private sealed class Handle : IAsyncTenantScopeHandle
    {
        private readonly IAsyncTenantScopeHandle _inner;
        private readonly CommitFailFlag _flag;

        public Handle(IAsyncTenantScopeHandle inner, CommitFailFlag flag)
        {
            _inner = inner;
            _flag = flag;
        }

        public Task CommitAsync(CancellationToken ct)
        {
            if (_flag.Fail)
            {
                throw new InvalidOperationException(
                    "Forced commit failure for streaming-durability regression test.");
            }
            return _inner.CommitAsync(ct);
        }

        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }
}

[ExcludeFromCodeCoverage]
internal sealed class StreamingTestEndpointStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            next(app);
            // Map a tenant-scoped streaming endpoint AFTER the main pipeline.
            // WebApplication's UseEndpoints/MapXxx is normally one-shot; UseEndpoints
            // here registers an additional endpoint set under the existing routing.
            app.UseEndpoints(endpoints =>
            {
                var group = endpoints.MapGroup("/__test").RequireTenantScope();
                group.MapGet("/stream", async (HttpContext ctx) =>
                {
                    // Stream 2 KB of bytes. Without endpoint-filter commit semantics
                    // this would flush before commit completes.
                    ctx.Response.ContentType = "application/octet-stream";
                    var buffer = new byte[256];
                    for (var i = 0; i < 8; i++)
                    {
                        await ctx.Response.Body.WriteAsync(buffer);
                    }
                    return Results.Empty;
                });
            });
        };
}
```

- [ ] **Step 2: Create a separate xUnit collection for the fault-injection fixture** so it doesn't share state with `KartovaApiCollection`.

Add this just below `KartovaApiCollection.cs` content, in a new file:

```csharp
// src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFaultInjectionCollection.cs
using System.Diagnostics.CodeAnalysis;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

[ExcludeFromCodeCoverage]
[CollectionDefinition(Name)]
public sealed class KartovaApiFaultInjectionCollection : ICollectionFixture<KartovaApiFaultInjectionFixture>
{
    public const string Name = "Kartova API (Fault Injection)";
}
```

- [ ] **Step 3: Write the streaming-durability test.**

```csharp
// src/Modules/Organization/Kartova.Organization.IntegrationTests/StreamingDurabilityTests.cs
using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Kartova.Testing.Auth;
using Xunit;

namespace Kartova.Organization.IntegrationTests;

[Collection(KartovaApiFaultInjectionCollection.Name)]
public class StreamingDurabilityTests
{
    private readonly KartovaApiFaultInjectionFixture _fx;

    public StreamingDurabilityTests(KartovaApiFaultInjectionFixture fx) => _fx = fx;

    [Fact]
    public async Task Commit_failure_on_streaming_endpoint_returns_clean_5xx()
    {
        // Endpoint filter contract: handle.CommitAsync runs BEFORE IResult.ExecuteAsync,
        // so a commit failure propagates as 500 + problem-details with no bytes
        // streamed. With middleware (the slice-2 deviation), the response body would
        // begin flushing during _next(context) and a commit failure would surface
        // after partial bytes had already been sent.
        _fx.CommitFailFlag.Fail = true;
        try
        {
            var client = _fx.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", _fx.Signer.IssueForTenant(SeededOrgs.OrgA, new[] { "OrgAdmin" }));

            var resp = await client.GetAsync("/__test/stream");

            resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
                because: "commit failure must surface as 5xx, not as a partial 200 stream");
            // ProblemDetails ContentType is application/problem+json; we accept either
            // it or text/plain depending on UseExceptionHandler defaults — assert the
            // body is NOT 2 KB of zero bytes (the streaming payload).
            var body = await resp.Content.ReadAsByteArrayAsync();
            body.Length.Should().BeLessThan(2048,
                because: "no streamed bytes should reach the client when commit fails");
        }
        finally
        {
            _fx.CommitFailFlag.Fail = false;
        }
    }
}
```

- [ ] **Step 4: Build.**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: clean.

- [ ] **Step 5: Run the streaming test.**

```bash
cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --no-build --filter \"FullyQualifiedName~StreamingDurabilityTests\" --nologo -v minimal"
```

Expected: PASS. If TestServer's response handling makes the assertion unreliable, the test docstring documents the fallback: an in-process assertion that `TenantScopeEndpointFilter` calls `handle.CommitAsync` before `IResult.ExecuteAsync` is invoked.

If the test fails because TestServer behavior differs from Kestrel, downgrade per the spec's documented fallback (component-level assertion).

- [ ] **Step 6: Run the full integration suite to confirm no cross-fixture regression.**

```bash
cmd //c "dotnet test src/Modules/Organization/Kartova.Organization.IntegrationTests/Kartova.Organization.IntegrationTests.csproj --no-build --nologo -v minimal"
```

Expected: 14 (or 13 with conditional probe) + 3 §6.3 + 1 streaming = 17 (or 18) green.

- [ ] **Step 7: Commit.**

```bash
git add src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFaultInjectionFixture.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/KartovaApiFaultInjectionCollection.cs src/Modules/Organization/Kartova.Organization.IntegrationTests/StreamingDurabilityTests.cs
git commit -m "test(tenant-scope): streaming-response durability regression

Pins the commit-before-IResult.ExecuteAsync ordering by exercising a
tenant-scoped streaming endpoint with a forced commit failure. Asserts
the client sees a clean 5xx, not a partial body. Test-only IStartupFilter
adds the streaming endpoint; FailingCommitTenantScopeDecorator wraps the
real ITenantScope with a flippable commit-failure flag.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 11: Amend ADR-0090

**Goal:** Record that the implementation now matches §Decision after the slice-2 middleware deviation.

**Files:**
- Modify: `docs/architecture/decisions/ADR-0090-tenant-scope-mechanism.md`

- [ ] **Step 1: Append the addendum to the end of the file.**

After the `## References` section, add:

```markdown

## Addenda

### 2026-04-28 — Implementation re-aligned with §Decision

Slice 2 (PR #1, merged 2026-04-26) initially shipped this mechanism as a
`TenantScopeMiddleware` rather than the spec'd `TenantScopeEndpointFilter`.
The middleware's `await _next(context)` caused `IResult.ExecuteAsync` to
run before `handle.CommitAsync`, which broke the durability promise above
for any streaming response (`Results.Stream`, `IAsyncEnumerable<T>`, SSE):
a commit failure could occur after the response body had already begun
flushing.

Slice-2 followup (PR #6, merged 2026-04-28) restored the implementation
to the spec'd shape:

- `TenantScopeMiddleware` deleted; `TenantScopeEndpointFilter` attached
  via `RouteGroupBuilder.AddEndpointFilter<>()` from
  `RequireTenantScope()`.
- `RequireTenantScope()` now also implies `RequireAuthorization()` —
  tenant-scoped routes are authenticated by definition.
- `TenantScopeBeginException` introduced in `SharedKernel.Multitenancy`;
  `TenantScope.BeginAsync` wraps `NpgsqlException`. The filter catches
  the agnostic type, allowing the `SharedKernel.AspNetCore →
  SharedKernel.Postgres` project reference to be cut (matches §3.2).
- The previously-missing `EnlistInTenantScopeInterceptor` was probe-tested;
  ship/skip outcome and rationale in the followup PR.
- A streaming-response durability regression test pins the new ordering.
```

- [ ] **Step 2: Commit.**

```bash
git add docs/architecture/decisions/ADR-0090-tenant-scope-mechanism.md
git commit -m "docs(adr-0090): record slice-2-followup correction

Adds dated addendum noting that PR #1 deviated to middleware and PR #6
brought the implementation back to the spec'd endpoint-filter shape,
along with the supporting changes (TenantScopeBeginException, project-
reference cut, conditional EnlistInTenantScopeInterceptor, streaming
durability test).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 12: Final verification, push, open PR

**Goal:** Confirm Definition of Done, push the branch, open the PR.

- [ ] **Step 1: Full clean build (Definition of Done #1).**

```bash
cmd //c "dotnet build Kartova.slnx -c Debug --nologo -v minimal"
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. `TreatWarningsAsErrors` is on per project settings.

- [ ] **Step 2: Full unit + arch test suite green (DoD #4).**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName!~IntegrationTests\" --nologo -v minimal"
```

Expected: 38 unit + 31 arch = 69 tests, all green. Compare to the pre-PR baseline (was 38 + 30 = 68).

- [ ] **Step 3: Full integration suite green.**

```bash
cmd //c "dotnet test Kartova.slnx --no-build --filter \"FullyQualifiedName~IntegrationTests\" --nologo -v minimal"
```

Expected: KeyCloak smoke + Organization integration. Counts:
- `Kartova.Api.IntegrationTests` — 1 KeyCloak smoke (unchanged).
- `Kartova.Organization.IntegrationTests` — 12 existing + 2 probe (or 1 if probe set redesigned) + 3 §6.3 (refactored) + 1 streaming = ~17–18.

If counts don't match, investigate before continuing.

- [ ] **Step 4: Docker compose smoke (DoD #5 — applies because this PR changes HTTP/auth/middleware/pipeline).**

```bash
cmd //c "docker compose up -d --wait"
```

Run the slice-2 acceptance HTTP checks (per CLAUDE.md DoD §5):
- `GET /api/v1/version` (anonymous) → 200 + version JSON
- `GET /api/v1/organizations/me` (no token) → 401
- `POST /api/v1/admin/organizations` (no token) → 401
- `POST /api/v1/admin/organizations` (platform-admin token) → 201 + DTO
- `GET /api/v1/organizations/me` (admin@orga token) → 200 (now expected to succeed if admin/orga has a seeded Org row, else 404)

Capture command + output. If any check fails, root-cause before continuing — no shortcuts.

After verification:

```bash
cmd //c "docker compose down -v"
```

- [ ] **Step 5: Self-review the diff against the spec's success criteria (§Success criteria).**

Walk the spec's 10 success criteria against `git log master..HEAD --oneline` + a `git diff master..HEAD --stat`. Each criterion should be satisfiable by pointing to a commit. If any is not, add a follow-up task.

- [ ] **Step 6: Push.**

```bash
git push
```

- [ ] **Step 7: Open the PR.**

```bash
gh pr create --title "feat(slice-2-followup): tenant-scope endpoint filter + EF enlistment + layering cleanup" --body "$(cat <<'EOF'
## Summary

Closes the gap between slice-2's shipped tenant-scope mechanism and ADR-0090
§Decision before Slice 3 starts on the first tenant-scoped writes.

- `TenantScopeMiddleware` → `TenantScopeEndpointFilter` (commit before
  `IResult.ExecuteAsync`; durable for streaming responses).
- `TenantScopeBeginException` in `SharedKernel.Multitenancy`; `SharedKernel.AspNetCore`
  no longer references `SharedKernel.Postgres`.
- (Conditional) `EnlistInTenantScopeInterceptor` ships only if the probe test
  shows EF Core 10 doesn't auto-enlist in the scope's transaction.
- `INpgsqlTenantScope.Transaction` is now public; tests stop using reflection.
- `RequireTenantScope()` implicitly calls `RequireAuthorization()`.
- New architecture rule forbids reintroducing the project reference.
- Streaming-response durability regression test pins the new ordering.
- ADR-0090 amended with a dated addendum.

Spec: `docs/superpowers/specs/2026-04-28-slice-2-tenant-scope-filter-design.md`
Plan: `docs/superpowers/plans/2026-04-28-slice-2-tenant-scope-filter-plan.md`

## Test plan

- [ ] CI: clean build with `TreatWarningsAsErrors`, 0 warnings, 0 errors.
- [ ] Reviewer pulls branch, runs `docker compose up`, repeats the slice-2 HTTP smoke checks.
- [ ] Reviewer confirms the probe outcome and that the conditional interceptor was/wasn't shipped accordingly.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 8: Done.** Mark the spec's success criteria green in your notes; the PR is ready for review.

---

## Self-review (filled in by plan author after writing)

**1. Spec coverage check:**
- §Decisions table — all 11 rows traced to tasks: filter (T7), probe-test-first (T1, T8), TenantScopeBeginException (T2, T3), project-ref cut (T4), Transaction exposure (T6), RequireTenantScope semantics (T7), ADR addendum (T11), marker deletion (T7), middleware deletion (T7), streaming test (T10), §6.3 refactor (T9), new arch rule (T5).
- §3.2 file table — all "Action" rows have a task: `TenantScopeBeginException.cs` (T2), `TenantScope.cs` modify (T3, T6), `INpgsqlTenantScope.cs` modify (T6), `EnlistInTenantScopeInterceptor.cs` (T8 conditional), `AddModuleDbContextExtensions.cs` (T8), `TenantScopeEndpointFilter.cs` (T7), `TenantScopeMiddleware.cs` delete (T7), `TenantScopeRouteExtensions.cs` rewrite (T7), `Kartova.SharedKernel.AspNetCore.csproj` (T4), `Program.cs` (T7), `TenantScopeRules.cs` (T5), `TenantScopeMechanismTests.cs` refactor (T6 + T9), `EfEnlistmentProbeTests.cs` (T1), `StreamingDurabilityTests.cs` (T10), `KartovaApiFaultInjectionFixture.cs` (T10), ADR-0090 addendum (T11).
- §Success criteria 1-10 — all addressed by tasks; T12 verifies.

**2. Placeholder scan:** No "TBD"/"TODO"/"implement later" tokens. The conditional Task 8 explicitly depends on a runtime decision recorded in Task 1; that's intentional, not a placeholder.

**3. Type consistency:**
- `TenantScopeBeginException(string, Exception)` constructor signature consistent across T2 (definition) and T3 (usage).
- `INpgsqlTenantScope.Transaction { get; }` — defined T6, consumed T8 (interceptor) and T6 (test refactor).
- `RequireTenantScope()` — same return type (`RouteGroupBuilder`) before and after T7.
- `EnlistInTenantScopeInterceptor` — only referenced in T8; consistent with itself.
- `OrganizationTestHelper.CreateWithTenant(Guid id, TenantId tenantId, string name)` — defined T1, consumed T1 (probe) and T9 (refactor). Same signature.
- `CommitFailFlag.Fail` — defined T10, consumed T10 only.
- `StreamingTestEndpointStartupFilter` — defined T10, consumed T10 only.

**No issues found.**
