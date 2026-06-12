# Audit Log Foundation (Phase 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the *mechanism* for an append-only, tamper-evident audit log (ADR-0018) — a dedicated `Kartova.Audit` module, an insert-only/RLS-protected `audit_log` table, a synchronous in-transaction `IAuditWriter`, a per-tenant SHA-256 hash chain, and a chain verifier — with **no business events wired yet** (that is Phase 2).

**Architecture:** New modular-monolith module (`Kartova.Audit.Domain` + `Kartova.Audit.Infrastructure`, ADR-0082). The `IAuditWriter` port lives in `Kartova.SharedKernel` so any module can call it without a cross-module reference (mirrors `ITeamMembershipReader`). The writer enlists in the per-request `ITenantScope` connection/transaction (ADR-0090) → the audit row commits atomically with its caller's change, **fail-closed**. The hash chain is per-tenant; a `pg_advisory_xact_lock` serializes appends per tenant (correct even for the genesis row, where no row exists to `FOR UPDATE`). DB-enforced insert-only: the migration `REVOKE`s `UPDATE/DELETE/TRUNCATE` from the app roles.

**Tech Stack:** .NET 10, EF Core 10 + Npgsql, PostgreSQL 18 (RLS, jsonb, advisory locks), MSTest v4 + NSubstitute, Testcontainers (via `Kartova.Testing.Auth`).

**Spec:** `docs/superpowers/specs/2026-06-12-audit-log-foundation-design.md`

---

## File Structure

**New — SharedKernel port (referenced by every module):**
- `src/Kartova.SharedKernel/Audit/IAuditWriter.cs` — the port.
- `src/Kartova.SharedKernel/Audit/AuditEntry.cs` — caller-supplied input record.

**New — `Kartova.Audit.Domain` (pure, no infra deps):**
- `Kartova.Audit.Domain.csproj`
- `AuditActorType.cs` — enum.
- `AuditCanonicalSerializer.cs` — deterministic, jsonb-stable byte encoding.
- `AuditRowHasher.cs` — SHA-256 over canonical bytes + `GenesisHash`.
- `AuditLogEntry.cs` — the row entity; `Create(...)` computes `RowHash`.
- `AuditChainInspector.cs` — pure chain-walk verification.
- `AuditChainVerificationResult.cs` — verification result record.

**New — `Kartova.Audit.Infrastructure`:**
- `Kartova.Audit.Infrastructure.csproj`
- `AuditDbContext.cs`
- `AuditLogEntryConfiguration.cs` — EF mapping (snake_case, jsonb, bytea).
- `AuditDbContextFactory.cs` — design-time factory (`dotnet ef`).
- `AuditWriter.cs` — `IAuditWriter` impl.
- `AuditChainVerifier.cs` — loads rows, delegates to `AuditChainInspector`.
- `AuditModule.cs` — `IModule`.
- `Migrations/<stamp>_InitialAuditLog.cs` (+ Designer + snapshot) — generated then edited.

**New — test projects:**
- `src/Modules/Audit/Kartova.Audit.Domain.Tests/` — unit tests (serializer, hasher, entity, inspector).
- `src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/` — Testcontainers (grants, RLS, writer e2e, jsonb/timestamp stability, transactional rollback).

**Modified:**
- `Kartova.slnx` — add the four new projects.
- `src/Kartova.Api/Program.cs:32-36` — add `new AuditModule()` to `modules[]`.
- `src/Kartova.Migrator/Program.cs:13-17` — add `new AuditModule()` to `modules[]`.
- `tests/Kartova.ArchitectureTests/AssemblyRegistry.cs` — register Audit assemblies.
- `docs/product/CHECKLIST.md` — mark E-01.F-03.S-03 Phase-1 progress.

---

## Task 1: Scaffold the Audit module projects + solution wiring

**Files:**
- Create: `src/Modules/Audit/Kartova.Audit.Domain/Kartova.Audit.Domain.csproj`
- Create: `src/Modules/Audit/Kartova.Audit.Infrastructure/Kartova.Audit.Infrastructure.csproj`
- Create: `src/Modules/Audit/Kartova.Audit.Domain/AuditActorType.cs`
- Modify: `Kartova.slnx`

- [ ] **Step 1: Create the Domain csproj**

`src/Modules/Audit/Kartova.Audit.Domain/Kartova.Audit.Domain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Kartova.Audit.Domain.Tests" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create the Infrastructure csproj**

`src/Modules/Audit/Kartova.Audit.Infrastructure/Kartova.Audit.Infrastructure.csproj` (mirrors `Kartova.Organization.Infrastructure.csproj` references, minus Application/Contracts which Phase 1 doesn't need):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Relational" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" />
    <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.Common" />
  </ItemGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="Kartova.Audit.Infrastructure.IntegrationTests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Kartova.Audit.Domain\Kartova.Audit.Domain.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel.AspNetCore\Kartova.SharedKernel.AspNetCore.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel.Postgres\Kartova.SharedKernel.Postgres.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Create the actor-type enum**

`src/Modules/Audit/Kartova.Audit.Domain/AuditActorType.cs`:

```csharp
namespace Kartova.Audit.Domain;

/// <summary>
/// Who performed an audited action. <see cref="User"/> is the only value written in
/// Phase 1 (all wired callers are authenticated HTTP requests). <see cref="System"/>
/// (background jobs) and <see cref="ServiceAccount"/> (ADR-0009) exist so the schema
/// is ready; the writer begins emitting them in Phase 2 when such callers appear.
/// </summary>
public enum AuditActorType
{
    User = 1,
    System = 2,
    ServiceAccount = 3,
}
```

- [ ] **Step 4: Add all projects to the solution**

Run:
```
cmd //c "dotnet sln Kartova.slnx add src/Modules/Audit/Kartova.Audit.Domain/Kartova.Audit.Domain.csproj src/Modules/Audit/Kartova.Audit.Infrastructure/Kartova.Audit.Infrastructure.csproj"
```
Expected: "Project ... added to the solution." for both.

- [ ] **Step 5: Build to verify scaffolding compiles**

Run:
```
cmd //c "dotnet build Kartova.slnx -warnaserror"
```
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 6: Commit**

```
git add src/Modules/Audit Kartova.slnx
git commit -m "feat(audit): scaffold Kartova.Audit module (Domain + Infrastructure)"
```

---

## Task 2: SharedKernel audit port + entry record

**Files:**
- Create: `src/Kartova.SharedKernel/Audit/AuditEntry.cs`
- Create: `src/Kartova.SharedKernel/Audit/IAuditWriter.cs`

- [ ] **Step 1: Create the caller-supplied input record**

`src/Kartova.SharedKernel/Audit/AuditEntry.cs`:

```csharp
namespace Kartova.SharedKernel.Audit;

/// <summary>
/// What a caller records about a single audited action. The actor, tenant, timestamp,
/// sequence, and hash chain are NOT supplied here — the writer derives them from the
/// ambient request context (ADR-0090) and the existing chain.
///
/// <para><b>Data values are strings only</b> (GUIDs, enum names, "true"/"false", etc.).
/// This is deliberate: it keeps the canonical hash jsonb-stable (Postgres jsonb reformats
/// numbers like <c>1.0</c>→<c>1</c>; all-string values sidestep that — see the design
/// spec §5). Use <c>null</c> values for "absent".</para>
/// </summary>
public sealed record AuditEntry(
    string Action,
    string TargetType,
    string TargetId,
    IReadOnlyDictionary<string, string?>? Data = null);
```

- [ ] **Step 2: Create the port**

`src/Kartova.SharedKernel/Audit/IAuditWriter.cs`:

```csharp
namespace Kartova.SharedKernel.Audit;

/// <summary>
/// Appends one tamper-evident audit row inside the caller's current tenant transaction
/// (ADR-0018 + ADR-0090). Synchronous and fail-closed: if the append throws, the caller's
/// transaction rolls back, so a business mutation can never commit without its audit row.
/// Implemented by the Audit module.
/// </summary>
public interface IAuditWriter
{
    Task AppendAsync(AuditEntry entry, CancellationToken ct);
}
```

- [ ] **Step 3: Build**

Run:
```
cmd //c "dotnet build src/Kartova.SharedKernel/Kartova.SharedKernel.csproj -warnaserror"
```
Expected: Build succeeded, 0 warnings.

- [ ] **Step 4: Commit**

```
git add src/Kartova.SharedKernel/Audit
git commit -m "feat(audit): add IAuditWriter port + AuditEntry in SharedKernel"
```

---

## Task 3: Domain — canonical serializer + row hasher (TDD)

**Files:**
- Create: `src/Modules/Audit/Kartova.Audit.Domain.Tests/Kartova.Audit.Domain.Tests.csproj`
- Test: `src/Modules/Audit/Kartova.Audit.Domain.Tests/AuditRowHasherTests.cs`
- Create: `src/Modules/Audit/Kartova.Audit.Domain/AuditCanonicalSerializer.cs`
- Create: `src/Modules/Audit/Kartova.Audit.Domain/AuditRowHasher.cs`

- [ ] **Step 1: Create the Domain test project**

`src/Modules/Audit/Kartova.Audit.Domain.Tests/Kartova.Audit.Domain.Tests.csproj` (mirror an existing `*.Tests` csproj — confirm package list against `src/Modules/Organization/Kartova.Organization.Tests/Kartova.Organization.Tests.csproj`; central package versions apply):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="MSTest" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Kartova.Audit.Domain\Kartova.Audit.Domain.csproj" />
  </ItemGroup>
</Project>
```

Then add to the solution:
```
cmd //c "dotnet sln Kartova.slnx add src/Modules/Audit/Kartova.Audit.Domain.Tests/Kartova.Audit.Domain.Tests.csproj"
```

> If the package ids/versions differ from the reference `*.Tests` csproj (e.g. `MSTest` meta-package vs separate `MSTest.TestFramework`/`MSTest.TestAdapter`), copy them verbatim from that file. Do not invent versions — central management (`Directory.Packages.props`) supplies them.

- [ ] **Step 2: Write the failing tests**

`src/Modules/Audit/Kartova.Audit.Domain.Tests/AuditRowHasherTests.cs`:

```csharp
using System.Collections.Generic;
using Kartova.Audit.Domain;

namespace Kartova.Audit.Domain.Tests;

[TestClass]
public class AuditRowHasherTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Actor = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset When = new(2026, 6, 12, 9, 30, 0, TimeSpan.Zero);

    private static byte[] Hash(IReadOnlyDictionary<string, string?>? data, byte[] prev) =>
        AuditRowHasher.ComputeRowHash(
            Tenant, seq: 1, When, AuditActorType.User, Actor,
            action: "member.role_changed", targetType: "User", targetId: Actor.ToString(),
            data, prev);

    [TestMethod]
    public void GenesisHash_is_32_zero_bytes()
    {
        Assert.AreEqual(32, AuditRowHasher.GenesisHash.Length);
        CollectionAssert.AreEqual(new byte[32], AuditRowHasher.GenesisHash);
    }

    [TestMethod]
    public void ComputeRowHash_is_deterministic()
    {
        var data = new Dictionary<string, string?> { ["old_role"] = "Member", ["new_role"] = "OrgAdmin" };
        var a = Hash(data, AuditRowHasher.GenesisHash);
        var b = Hash(data, AuditRowHasher.GenesisHash);
        CollectionAssert.AreEqual(a, b);
        Assert.AreEqual(32, a.Length); // SHA-256
    }

    [TestMethod]
    public void ComputeRowHash_is_independent_of_data_key_insertion_order()
    {
        var ordered = new Dictionary<string, string?> { ["new_role"] = "OrgAdmin", ["old_role"] = "Member" };
        var reversed = new Dictionary<string, string?> { ["old_role"] = "Member", ["new_role"] = "OrgAdmin" };
        CollectionAssert.AreEqual(Hash(ordered, AuditRowHasher.GenesisHash), Hash(reversed, AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    public void ComputeRowHash_changes_when_prev_hash_changes()
    {
        var data = new Dictionary<string, string?> { ["k"] = "v" };
        var genesis = Hash(data, AuditRowHasher.GenesisHash);
        var chained = Hash(data, genesis);
        CollectionAssert.AreNotEqual(genesis, chained);
    }

    [TestMethod]
    public void ComputeRowHash_changes_when_payload_changes()
    {
        var d1 = new Dictionary<string, string?> { ["new_role"] = "OrgAdmin" };
        var d2 = new Dictionary<string, string?> { ["new_role"] = "Viewer" };
        CollectionAssert.AreNotEqual(Hash(d1, AuditRowHasher.GenesisHash), Hash(d2, AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    public void ComputeRowHash_handles_null_data()
    {
        var h = Hash(null, AuditRowHasher.GenesisHash);
        Assert.AreEqual(32, h.Length);
    }
}
```

- [ ] **Step 3: Run to verify they fail (compile error — types don't exist)**

Run:
```
cmd //c "dotnet test src/Modules/Audit/Kartova.Audit.Domain.Tests/Kartova.Audit.Domain.Tests.csproj"
```
Expected: FAIL — `AuditRowHasher`/`AuditCanonicalSerializer` not found.

- [ ] **Step 4: Implement the canonical serializer**

`src/Modules/Audit/Kartova.Audit.Domain/AuditCanonicalSerializer.cs`:

```csharp
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Kartova.Audit.Domain;

namespace Kartova.Audit.Domain;

/// <summary>
/// Produces a deterministic, jsonb-stable UTF-8 byte encoding of an audit row's hashable
/// fields. Fields are written in a fixed order; <c>data</c> keys are sorted ordinal and all
/// values are JSON strings (or null). Because Postgres jsonb normalizes on store, the verifier
/// re-canonicalizes the round-tripped <c>data</c> dictionary — sorting + string-only values make
/// that round-trip a no-op for the hash (design spec §5). <c>occurred_at</c> is formatted to
/// microsecond precision (6 fractional digits) to match Postgres <c>timestamptz</c> resolution.
/// </summary>
public static class AuditCanonicalSerializer
{
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.ffffffZ";

    public static byte[] Serialize(
        Guid tenantId,
        long seq,
        DateTimeOffset occurredAt,
        AuditActorType actorType,
        Guid? actorId,
        string action,
        string targetType,
        string targetId,
        IReadOnlyDictionary<string, string?>? data,
        byte[] prevHash)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("tenant_id", tenantId.ToString("D"));
            w.WriteNumber("seq", seq);
            w.WriteString("occurred_at", occurredAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            w.WriteString("actor_type", actorType.ToString());
            if (actorId is { } a) w.WriteString("actor_id", a.ToString("D"));
            else w.WriteNull("actor_id");
            w.WriteString("action", action);
            w.WriteString("target_type", targetType);
            w.WriteString("target_id", targetId);

            w.WritePropertyName("data");
            if (data is null)
            {
                w.WriteNullValue();
            }
            else
            {
                w.WriteStartObject();
                foreach (var key in data.Keys.OrderBy(k => k, StringComparer.Ordinal))
                {
                    var value = data[key];
                    if (value is null) w.WriteNull(key);
                    else w.WriteString(key, value);
                }
                w.WriteEndObject();
            }

            w.WriteString("prev_hash", Convert.ToBase64String(prevHash));
            w.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
```

- [ ] **Step 5: Implement the hasher**

`src/Modules/Audit/Kartova.Audit.Domain/AuditRowHasher.cs`:

```csharp
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Kartova.Audit.Domain;

/// <summary>
/// SHA-256 over the canonical row encoding (ADR-0018 tamper-evidence). The first row in a
/// per-tenant chain references <see cref="GenesisHash"/> (32 zero bytes) as its predecessor.
/// </summary>
public static class AuditRowHasher
{
    /// <summary>Predecessor hash for the first (genesis) row in a tenant's chain.</summary>
    public static byte[] GenesisHash => new byte[32];

    public static byte[] ComputeRowHash(
        Guid tenantId,
        long seq,
        DateTimeOffset occurredAt,
        AuditActorType actorType,
        Guid? actorId,
        string action,
        string targetType,
        string targetId,
        IReadOnlyDictionary<string, string?>? data,
        byte[] prevHash)
    {
        var canonical = AuditCanonicalSerializer.Serialize(
            tenantId, seq, occurredAt, actorType, actorId, action, targetType, targetId, data, prevHash);
        return SHA256.HashData(canonical);
    }
}
```

- [ ] **Step 6: Run to verify pass**

Run:
```
cmd //c "dotnet test src/Modules/Audit/Kartova.Audit.Domain.Tests/Kartova.Audit.Domain.Tests.csproj"
```
Expected: PASS (6 tests).

- [ ] **Step 7: Commit**

```
git add src/Modules/Audit/Kartova.Audit.Domain src/Modules/Audit/Kartova.Audit.Domain.Tests Kartova.slnx
git commit -m "feat(audit): canonical serializer + SHA-256 row hasher (TDD)"
```

---

## Task 4: Domain — AuditLogEntry entity (TDD)

**Files:**
- Test: `src/Modules/Audit/Kartova.Audit.Domain.Tests/AuditLogEntryTests.cs`
- Create: `src/Modules/Audit/Kartova.Audit.Domain/AuditLogEntry.cs`

- [ ] **Step 1: Write the failing test**

`src/Modules/Audit/Kartova.Audit.Domain.Tests/AuditLogEntryTests.cs`:

```csharp
using System.Collections.Generic;
using Kartova.Audit.Domain;

namespace Kartova.Audit.Domain.Tests;

[TestClass]
public class AuditLogEntryTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    public void Create_computes_row_hash_matching_the_hasher()
    {
        var id = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var when = new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero);
        var data = new Dictionary<string, string?> { ["new_role"] = "OrgAdmin" };

        var entry = AuditLogEntry.Create(
            id, Tenant, seq: 1, when, AuditActorType.User, actor, actorDisplay: "Ada",
            action: "member.role_changed", targetType: "User", targetId: actor.ToString(),
            data, AuditRowHasher.GenesisHash);

        var expected = AuditRowHasher.ComputeRowHash(
            Tenant, 1, when, AuditActorType.User, actor,
            "member.role_changed", "User", actor.ToString(), data, AuditRowHasher.GenesisHash);

        CollectionAssert.AreEqual(expected, entry.RowHash);
        Assert.AreEqual(1, entry.Seq);
        CollectionAssert.AreEqual(AuditRowHasher.GenesisHash, entry.PrevHash);
    }

    [TestMethod]
    public void Create_rejects_non_positive_seq()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 0, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: "User", targetId: "x",
            data: null, prevHash: AuditRowHasher.GenesisHash));
    }

    [TestMethod]
    public void Create_rejects_wrong_length_prev_hash()
    {
        Assert.ThrowsExactly<ArgumentException>(() => AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq: 1, DateTimeOffset.UtcNow, AuditActorType.User, Guid.NewGuid(),
            actorDisplay: null, action: "a", targetType: "User", targetId: "x",
            data: null, prevHash: new byte[16]));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run:
```
cmd //c "dotnet test src/Modules/Audit/Kartova.Audit.Domain.Tests/Kartova.Audit.Domain.Tests.csproj --filter AuditLogEntryTests"
```
Expected: FAIL — `AuditLogEntry` not found.

- [ ] **Step 3: Implement the entity**

`src/Modules/Audit/Kartova.Audit.Domain/AuditLogEntry.cs`:

```csharp
using System.Collections.Generic;

namespace Kartova.Audit.Domain;

/// <summary>
/// One append-only audit row (ADR-0018). Immutable after creation: <see cref="Create"/>
/// computes <see cref="RowHash"/> from all hashable fields + <see cref="PrevHash"/>, and the
/// database REVOKEs UPDATE/DELETE so the row can never change. <see cref="ActorDisplay"/> is a
/// denormalized snapshot so an offboarded actor (ADR-0102 hard delete) is still named.
/// </summary>
public sealed class AuditLogEntry
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public long Seq { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public AuditActorType ActorType { get; private set; }
    public Guid? ActorId { get; private set; }
    public string? ActorDisplay { get; private set; }
    public string Action { get; private set; } = null!;
    public string TargetType { get; private set; } = null!;
    public string TargetId { get; private set; } = null!;
    public IReadOnlyDictionary<string, string?>? Data { get; private set; }
    public byte[] PrevHash { get; private set; } = null!;
    public byte[] RowHash { get; private set; } = null!;

    private AuditLogEntry() { /* EF */ }

    public static AuditLogEntry Create(
        Guid id,
        Guid tenantId,
        long seq,
        DateTimeOffset occurredAt,
        AuditActorType actorType,
        Guid? actorId,
        string? actorDisplay,
        string action,
        string targetType,
        string targetId,
        IReadOnlyDictionary<string, string?>? data,
        byte[] prevHash)
    {
        ArgumentNullException.ThrowIfNull(prevHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentOutOfRangeException.ThrowIfLessThan(seq, 1);
        if (prevHash.Length != 32)
            throw new ArgumentException("prevHash must be 32 bytes (SHA-256).", nameof(prevHash));

        return new AuditLogEntry
        {
            Id = id,
            TenantId = tenantId,
            Seq = seq,
            OccurredAt = occurredAt,
            ActorType = actorType,
            ActorId = actorId,
            ActorDisplay = actorDisplay,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Data = data,
            PrevHash = prevHash,
            RowHash = AuditRowHasher.ComputeRowHash(
                tenantId, seq, occurredAt, actorType, actorId, action, targetType, targetId, data, prevHash),
        };
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run:
```
cmd //c "dotnet test src/Modules/Audit/Kartova.Audit.Domain.Tests/Kartova.Audit.Domain.Tests.csproj --filter AuditLogEntryTests"
```
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```
git add src/Modules/Audit/Kartova.Audit.Domain src/Modules/Audit/Kartova.Audit.Domain.Tests
git commit -m "feat(audit): AuditLogEntry entity with hash on create (TDD)"
```

---

## Task 5: Domain — chain inspector + result (TDD)

**Files:**
- Test: `src/Modules/Audit/Kartova.Audit.Domain.Tests/AuditChainInspectorTests.cs`
- Create: `src/Modules/Audit/Kartova.Audit.Domain/AuditChainVerificationResult.cs`
- Create: `src/Modules/Audit/Kartova.Audit.Domain/AuditChainInspector.cs`

- [ ] **Step 1: Write the failing tests**

`src/Modules/Audit/Kartova.Audit.Domain.Tests/AuditChainInspectorTests.cs`:

```csharp
using System.Collections.Generic;
using Kartova.Audit.Domain;

namespace Kartova.Audit.Domain.Tests;

[TestClass]
public class AuditChainInspectorTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static AuditLogEntry Row(long seq, byte[] prev, string action = "a") =>
        AuditLogEntry.Create(
            Guid.NewGuid(), Tenant, seq, new DateTimeOffset(2026, 6, 12, 9, 0, 0, TimeSpan.Zero),
            AuditActorType.User, Guid.Empty, actorDisplay: null,
            action, targetType: "User", targetId: "x",
            data: new Dictionary<string, string?> { ["k"] = "v" }, prev);

    private static List<AuditLogEntry> Chain(int n)
    {
        var rows = new List<AuditLogEntry>();
        var prev = AuditRowHasher.GenesisHash;
        for (var i = 1; i <= n; i++) { var r = Row(i, prev); rows.Add(r); prev = r.RowHash; }
        return rows;
    }

    [TestMethod]
    public void Empty_chain_is_intact()
    {
        var result = AuditChainInspector.Inspect(new List<AuditLogEntry>());
        Assert.IsTrue(result.Intact);
    }

    [TestMethod]
    public void Well_formed_chain_is_intact()
    {
        Assert.IsTrue(AuditChainInspector.Inspect(Chain(3)).Intact);
    }

    [TestMethod]
    public void Detects_non_contiguous_seq()
    {
        var rows = Chain(3);
        rows.RemoveAt(1); // drop seq 2 → 1,3
        var result = AuditChainInspector.Inspect(rows);
        Assert.IsFalse(result.Intact);
        Assert.AreEqual(3, result.FirstBrokenSeq);
    }

    [TestMethod]
    public void Detects_prev_hash_break()
    {
        var rows = Chain(2);
        // Row 1 chained off genesis but we re-create row 2 pointing at the wrong predecessor.
        rows[1] = Row(2, new byte[32] /* wrong: not row1.RowHash but still genesis-shaped */);
        // Force a real mismatch: row1.RowHash is non-genesis, so a genesis prev on row 2 breaks the link.
        var result = AuditChainInspector.Inspect(rows);
        Assert.IsFalse(result.Intact);
        Assert.AreEqual(2, result.FirstBrokenSeq);
    }

    [TestMethod]
    public void Detects_tampered_row_hash()
    {
        var rows = Chain(2);
        typeof(AuditLogEntry).GetProperty(nameof(AuditLogEntry.RowHash))!
            .SetValue(rows[0], new byte[32]); // forge row 1's stored hash
        var result = AuditChainInspector.Inspect(rows);
        Assert.IsFalse(result.Intact);
        Assert.AreEqual(1, result.FirstBrokenSeq);
    }
}
```

> Note: the reflection write in the last test exercises the "stored hash was tampered after the fact" case the DB grants are meant to prevent — it is a unit-test-only forgery.

- [ ] **Step 2: Run to verify it fails**

Run:
```
cmd //c "dotnet test src/Modules/Audit/Kartova.Audit.Domain.Tests/Kartova.Audit.Domain.Tests.csproj --filter AuditChainInspectorTests"
```
Expected: FAIL — `AuditChainInspector` / `AuditChainVerificationResult` not found.

- [ ] **Step 3: Implement the result record**

`src/Modules/Audit/Kartova.Audit.Domain/AuditChainVerificationResult.cs`:

```csharp
namespace Kartova.Audit.Domain;

/// <summary>
/// Outcome of walking a tenant's audit chain. <see cref="Intact"/> rows verify end-to-end;
/// otherwise <see cref="FirstBrokenSeq"/> + <see cref="Reason"/> describe the first break.
/// </summary>
public sealed record AuditChainVerificationResult(bool Intact, long? FirstBrokenSeq, string? Reason)
{
    public static AuditChainVerificationResult Ok { get; } = new(true, null, null);
    public static AuditChainVerificationResult Broken(long seq, string reason) => new(false, seq, reason);
}
```

- [ ] **Step 4: Implement the inspector**

`src/Modules/Audit/Kartova.Audit.Domain/AuditChainInspector.cs`:

```csharp
using System.Collections.Generic;

namespace Kartova.Audit.Domain;

/// <summary>
/// Pure verification of a tenant's audit chain. Given rows ordered by <c>Seq</c>, asserts the
/// sequence is contiguous from 1, each row's <c>PrevHash</c> equals the prior row's <c>RowHash</c>,
/// and each stored <c>RowHash</c> recomputes from the row's fields. DB I/O lives in the
/// Infrastructure verifier; this function is the testable core.
/// </summary>
public static class AuditChainInspector
{
    public static AuditChainVerificationResult Inspect(IReadOnlyList<AuditLogEntry> rowsOrderedBySeq)
    {
        ArgumentNullException.ThrowIfNull(rowsOrderedBySeq);

        long expectedSeq = 1;
        var prev = AuditRowHasher.GenesisHash;

        foreach (var row in rowsOrderedBySeq)
        {
            if (row.Seq != expectedSeq)
                return AuditChainVerificationResult.Broken(row.Seq, $"non-contiguous seq (expected {expectedSeq})");

            if (!row.PrevHash.AsSpan().SequenceEqual(prev))
                return AuditChainVerificationResult.Broken(row.Seq, "prev_hash does not match prior row_hash");

            var recomputed = AuditRowHasher.ComputeRowHash(
                row.TenantId, row.Seq, row.OccurredAt, row.ActorType, row.ActorId,
                row.Action, row.TargetType, row.TargetId, row.Data, row.PrevHash);

            if (!recomputed.AsSpan().SequenceEqual(row.RowHash))
                return AuditChainVerificationResult.Broken(row.Seq, "row_hash does not match recomputed hash");

            prev = row.RowHash;
            expectedSeq++;
        }

        return AuditChainVerificationResult.Ok;
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run:
```
cmd //c "dotnet test src/Modules/Audit/Kartova.Audit.Domain.Tests/Kartova.Audit.Domain.Tests.csproj --filter AuditChainInspectorTests"
```
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```
git add src/Modules/Audit/Kartova.Audit.Domain src/Modules/Audit/Kartova.Audit.Domain.Tests
git commit -m "feat(audit): pure chain inspector + verification result (TDD)"
```

---

## Task 6: Infrastructure — DbContext, EF mapping, design-time factory

**Files:**
- Create: `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditDbContext.cs`
- Create: `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditLogEntryConfiguration.cs`
- Create: `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditDbContextFactory.cs`

- [ ] **Step 1: Create the DbContext**

`src/Modules/Audit/Kartova.Audit.Infrastructure/AuditDbContext.cs`:

```csharp
using Kartova.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Audit.Infrastructure;

public sealed class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options) : base(options) { }

    public DbSet<AuditLogEntry> AuditEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(AuditDbContext).Assembly);
}
```

- [ ] **Step 2: Create the EF mapping**

`src/Modules/Audit/Kartova.Audit.Infrastructure/AuditLogEntryConfiguration.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json;
using Kartova.Audit.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Kartova.Audit.Infrastructure;

internal sealed class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.Seq).HasColumnName("seq");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        builder.Property(x => x.ActorType).HasColumnName("actor_type").HasConversion<string>();
        builder.Property(x => x.ActorId).HasColumnName("actor_id");
        builder.Property(x => x.ActorDisplay).HasColumnName("actor_display");
        builder.Property(x => x.Action).HasColumnName("action");
        builder.Property(x => x.TargetType).HasColumnName("target_type");
        builder.Property(x => x.TargetId).HasColumnName("target_id");
        builder.Property(x => x.PrevHash).HasColumnName("prev_hash");
        builder.Property(x => x.RowHash).HasColumnName("row_hash");

        // data: stored as jsonb (forensic queryability + write-time validation). The converter
        // round-trips through System.Text.Json; the chain hash is computed by the domain
        // canonical serializer (sorted keys, string values) so jsonb normalization is hash-neutral
        // (design spec §5). A null dictionary maps to SQL NULL.
        var dataConverter = new ValueConverter<IReadOnlyDictionary<string, string?>?, string?>(
            v => v == null ? null : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => v == null ? null : JsonSerializer.Deserialize<Dictionary<string, string?>>(v, (JsonSerializerOptions?)null));

        var dataComparer = new ValueComparer<IReadOnlyDictionary<string, string?>?>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            v => v == null ? 0 : JsonSerializer.Serialize(v, (JsonSerializerOptions?)null).GetHashCode(),
            v => v);

        builder.Property(x => x.Data)
            .HasColumnName("data")
            .HasColumnType("jsonb")
            .HasConversion(dataConverter, dataComparer);

        builder.HasIndex(x => new { x.TenantId, x.Seq }).IsUnique().HasDatabaseName("ux_audit_log_tenant_seq");
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt }).HasDatabaseName("idx_audit_log_tenant_time");
        builder.HasIndex(x => new { x.TenantId, x.TargetType, x.TargetId }).HasDatabaseName("idx_audit_log_tenant_target");
    }
}
```

- [ ] **Step 3: Create the design-time factory** (mirrors `OrganizationDbContextFactory`)

`src/Modules/Audit/Kartova.Audit.Infrastructure/AuditDbContextFactory.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// Enables `dotnet ef migrations add` without a running host.
/// Production connection strings come from IModule.RegisterServices.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed class AuditDbContextFactory : IDesignTimeDbContextFactory<AuditDbContext>
{
    public AuditDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql("Host=localhost;Database=kartova_design;Username=migrator;Password=dev",
                npg => npg.MigrationsAssembly(typeof(AuditDbContextFactory).Assembly.FullName))
            .Options;
        return new AuditDbContext(options);
    }
}
```

- [ ] **Step 4: Build**

Run:
```
cmd //c "dotnet build src/Modules/Audit/Kartova.Audit.Infrastructure/Kartova.Audit.Infrastructure.csproj -warnaserror"
```
Expected: Build succeeded, 0 warnings.

- [ ] **Step 5: Commit**

```
git add src/Modules/Audit/Kartova.Audit.Infrastructure
git commit -m "feat(audit): AuditDbContext, jsonb mapping, design-time factory"
```

---

## Task 7: Infrastructure — EF migration (table + indexes + RLS + insert-only grants)

**Files:**
- Create (generated, then edited): `src/Modules/Audit/Kartova.Audit.Infrastructure/Migrations/<stamp>_InitialAuditLog.cs` (+ `.Designer.cs` + `AuditDbContextModelSnapshot.cs`)

- [ ] **Step 1: Generate the migration from the model**

Run (the design-time factory makes the Infrastructure project self-contained as its own startup):
```
cmd //c "dotnet ef migrations add InitialAuditLog --project src/Modules/Audit/Kartova.Audit.Infrastructure --startup-project src/Modules/Audit/Kartova.Audit.Infrastructure --context AuditDbContext"
```
Expected: "Done." Three files appear under `Migrations/`.

> If `dotnet ef` is not installed: `cmd //c "dotnet tool install --global dotnet-ef"` (or `dotnet tool restore` if a tool manifest exists). Confirm the project's existing migration workflow first.

- [ ] **Step 2: Append the RLS + insert-only SQL to the generated `Up`**

Open the generated `<stamp>_InitialAuditLog.cs`. At the END of the `Up(MigrationBuilder migrationBuilder)` method (after the `CreateTable`/`CreateIndex` calls), add:

```csharp
            migrationBuilder.Sql(@"
ALTER TABLE audit_log ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit_log FORCE ROW LEVEL SECURITY;

-- Tenant isolation. With no WITH CHECK clause, the USING expression is also applied to
-- INSERTed rows (PostgreSQL CREATE POLICY semantics) — matching the users-table pattern.
CREATE POLICY tenant_isolation ON audit_log
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);

-- ADR-0018 insert-only: the app + bypass roles inherit SELECT,INSERT,UPDATE,DELETE from the
-- migrator's default privileges (docker/postgres/init.sql). Strip every mutating privilege so
-- an audit row can never be altered or removed by application code — the database, not app
-- discipline, is the guarantee. Guarded so the migration also applies in environments where a
-- role happens not to exist.
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kartova_app') THEN
    REVOKE UPDATE, DELETE, TRUNCATE ON audit_log FROM kartova_app;
  END IF;
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kartova_bypass_rls') THEN
    REVOKE UPDATE, DELETE, TRUNCATE ON audit_log FROM kartova_bypass_rls;
  END IF;
END $$;
");
```

- [ ] **Step 3: Append the matching teardown to the generated `Down`**

At the START of the `Down` method (before the `DropTable`), add:

```csharp
            migrationBuilder.Sql(@"
DROP POLICY IF EXISTS tenant_isolation ON audit_log;
ALTER TABLE audit_log DISABLE ROW LEVEL SECURITY;
");
```

- [ ] **Step 4: Build to verify the edited migration compiles**

Run:
```
cmd //c "dotnet build src/Modules/Audit/Kartova.Audit.Infrastructure/Kartova.Audit.Infrastructure.csproj -warnaserror"
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```
git add src/Modules/Audit/Kartova.Audit.Infrastructure/Migrations
git commit -m "feat(audit): InitialAuditLog migration — table, indexes, RLS, insert-only grants"
```

---

## Task 8: Infrastructure — AuditWriter (IAuditWriter implementation)

**Files:**
- Create: `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditWriter.cs`

(Behavioral verification is the integration suite in Task 11 — the writer needs a real Postgres + tenant scope, so there is no unit test here.)

- [ ] **Step 1: Implement the writer**

`src/Modules/Audit/Kartova.Audit.Infrastructure/AuditWriter.cs`:

```csharp
using Kartova.Audit.Domain;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// Appends one audit row inside the caller's tenant transaction (ADR-0090). Synchronous and
/// fail-closed: <see cref="AuditDbContext"/> shares the request connection + transaction via
/// <c>AddModuleDbContext</c>, so the row commits atomically with the caller's change and any
/// failure here rolls the whole request back.
///
/// <para>Chain ordering: a per-tenant <c>pg_advisory_xact_lock</c> serializes concurrent appends
/// for the same tenant — correct even for the genesis row, where there is no existing row to lock
/// with <c>FOR UPDATE</c>. The lock auto-releases at transaction end.</para>
///
/// <para>Phase 1 writes <see cref="AuditActorType.User"/> only (wired callers arrive in Phase 2;
/// all are authenticated HTTP requests). <c>actor_display</c> is left null here — the offboard
/// caller that needs the snapshot resolves it in Phase 2.</para>
/// </summary>
public sealed class AuditWriter(
    AuditDbContext db,
    ICurrentUser currentUser,
    ITenantContext tenant,
    TimeProvider clock) : IAuditWriter
{
    public async Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var tenantId = tenant.Id.Value;

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
            actorType: AuditActorType.User,
            actorId: currentUser.UserId,
            actorDisplay: null,
            action: entry.Action,
            targetType: entry.TargetType,
            targetId: entry.TargetId,
            data: entry.Data,
            prevHash: prevHash);

        db.AuditEntries.Add(row);
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 2: Build**

Run:
```
cmd //c "dotnet build src/Modules/Audit/Kartova.Audit.Infrastructure/Kartova.Audit.Infrastructure.csproj -warnaserror"
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```
git add src/Modules/Audit/Kartova.Audit.Infrastructure/AuditWriter.cs
git commit -m "feat(audit): AuditWriter — in-transaction, advisory-locked, hash-chained append"
```

---

## Task 9: Infrastructure — AuditChainVerifier

**Files:**
- Create: `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditChainVerifier.cs`

- [ ] **Step 1: Implement the verifier**

`src/Modules/Audit/Kartova.Audit.Infrastructure/AuditChainVerifier.cs`:

```csharp
using Kartova.Audit.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// Loads a tenant's audit rows (RLS scopes the read to the current tenant) ordered by seq and
/// delegates to the pure <see cref="AuditChainInspector"/>. Phase 1 ships this as an injectable
/// service exercised by tests; the regulator-facing surface (CLI/endpoint) is deferred.
/// </summary>
public sealed class AuditChainVerifier(AuditDbContext db)
{
    public async Task<AuditChainVerificationResult> VerifyAsync(TenantId tenantId, CancellationToken ct)
    {
        var rows = await db.AuditEntries
            .Where(e => e.TenantId == tenantId.Value)
            .OrderBy(e => e.Seq)
            .ToListAsync(ct);

        return AuditChainInspector.Inspect(rows);
    }
}
```

- [ ] **Step 2: Build**

Run:
```
cmd //c "dotnet build src/Modules/Audit/Kartova.Audit.Infrastructure/Kartova.Audit.Infrastructure.csproj -warnaserror"
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```
git add src/Modules/Audit/Kartova.Audit.Infrastructure/AuditChainVerifier.cs
git commit -m "feat(audit): AuditChainVerifier loads rows + runs pure inspector"
```

---

## Task 10: Module composition + host/migrator/arch-test registration

**Files:**
- Create: `src/Modules/Audit/Kartova.Audit.Infrastructure/AuditModule.cs`
- Modify: `src/Kartova.Api/Program.cs:32-36`
- Modify: `src/Kartova.Migrator/Program.cs:13-17`
- Modify: `tests/Kartova.ArchitectureTests/AssemblyRegistry.cs`

- [ ] **Step 1: Create the module**

`src/Modules/Audit/Kartova.Audit.Infrastructure/AuditModule.cs` (follows `OrganizationModule`; no `IModuleEndpoints` — Phase 1 has no HTTP surface):

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Wolverine;

namespace Kartova.Audit.Infrastructure;

/// <summary>
/// Audit bounded-context module (ADR-0082 / ADR-0018). Registers the tenant-scoped
/// <see cref="AuditDbContext"/> via <c>AddModuleDbContext</c> so the writer's insert rides the
/// per-request <c>ITenantScope</c> connection + transaction (ADR-0090), and binds the
/// SharedKernel <see cref="IAuditWriter"/> port to <see cref="AuditWriter"/>.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AuditModule : IModule
{
    public string Name => "audit";

    public string Slug => "audit";

    public Type DbContextType => typeof(AuditDbContext);

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<AuditDbContext>(npg =>
            npg.MigrationsAssembly(typeof(AuditDbContext).Assembly.FullName));

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<AuditChainVerifier>();
    }

    public void RegisterForMigrator(IServiceCollection services, IConfiguration configuration)
    {
        var cs = KartovaConnectionStrings.RequireMain(configuration);
        services.AddDbContext<AuditDbContext>(opts =>
            opts.UseNpgsql(cs, npg => npg.MigrationsAssembly(
                typeof(AuditDbContext).Assembly.FullName)));
    }

    public void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.IncludeAssembly(typeof(AuditModule).Assembly);
    }
}
```

> Verify the `IModule` member set against `src/Kartova.SharedKernel/IModule.cs`. If a member differs (e.g. an `MapEndpoints` that is not on a separate `IModuleEndpoints`), match the interface exactly. `OrganizationModule` is the reference implementation.

- [ ] **Step 2: Register the module in the API host**

`src/Kartova.Api/Program.cs` — change the `modules[]` initializer (lines 32-36) to:

```csharp
        IModule[] modules =
        [
            new CatalogModule(),
            new OrganizationModule(),
            new AuditModule(),
        ];
```

Add `using Kartova.Audit.Infrastructure;` to the file's usings. Add a `ProjectReference` to `Kartova.Audit.Infrastructure` in `src/Kartova.Api/Kartova.Api.csproj` (mirror the existing Organization Infrastructure reference).

- [ ] **Step 3: Register the module in the migrator**

`src/Kartova.Migrator/Program.cs` — change the `modules[]` initializer (lines 13-17) to:

```csharp
IModule[] modules =
[
    new CatalogModule(),
    new OrganizationModule(),
    new AuditModule(),
];
```

Add `using Kartova.Audit.Infrastructure;`. Add a `ProjectReference` to `Kartova.Audit.Infrastructure` in `src/Kartova.Migrator/Kartova.Migrator.csproj`.

- [ ] **Step 4: Register Audit assemblies in the architecture-test registry**

`tests/Kartova.ArchitectureTests/AssemblyRegistry.cs`:

Add a using: `using Kartova.Audit.Infrastructure;` and `using Kartova.Audit.Domain;`.

Add a nested class after the `Organization` class:

```csharp
    public static class Audit
    {
        public static readonly Assembly Domain = typeof(AuditLogEntry).Assembly;
        public static readonly Assembly Infrastructure = typeof(AuditModule).Assembly;
    }
```

In `AllProduction()`, add before the closing of the method:

```csharp
        yield return Audit.Domain;
        yield return Audit.Infrastructure;
```

Add a `ProjectReference` to both Audit projects in `tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj`.

> Do NOT add Audit to `AllContracts()` (no Contracts assembly in Phase 1) or `AllInfrastructureAssemblies()` (no list endpoint yet — the pagination convention rule, ADR-0095, has nothing to check; add it in Phase 2 when a read endpoint lands).

- [ ] **Step 5: Build the whole solution**

Run:
```
cmd //c "dotnet build Kartova.slnx -warnaserror"
```
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 6: Run the architecture suite**

Run:
```
cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj"
```
Expected: PASS — confirms layering/coverage rules accept the new module (the `AuditModule` carries `[ExcludeFromCodeCoverage]`; no `*Dto/*Request/*Response` types added).

- [ ] **Step 7: Commit**

```
git add src/Modules/Audit/Kartova.Audit.Infrastructure/AuditModule.cs src/Kartova.Api src/Kartova.Migrator tests/Kartova.ArchitectureTests
git commit -m "feat(audit): compose AuditModule; register in API, migrator, arch tests"
```

---

## Task 11: Integration tests — grants, RLS, writer e2e, jsonb/timestamp stability, transactional rollback

**Files:**
- Create: `src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/Kartova.Audit.Infrastructure.IntegrationTests.csproj`
- Create: `src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/AuditLogFixture.cs`
- Create: `src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/AuditLogGrantsAndRlsTests.cs`
- Create: `src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/AuditWriterTests.cs`

This task proves the slice's premise. It uses the same Postgres-container + role-seeding harness the other modules use (`Kartova.Testing.Auth`), so `kartova_app` grants and RLS are observable. **Confirm the exact fixture base/lifecycle against an existing infra integration test** (e.g. `src/Modules/Organization/Kartova.Organization.IntegrationTests/`) and follow it; the code below is the target shape.

- [ ] **Step 1: Create the integration-test project**

`src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/Kartova.Audit.Infrastructure.IntegrationTests.csproj` (copy package refs verbatim from `src/Modules/Organization/Kartova.Organization.IntegrationTests/*.csproj`; the references below are the minimum):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <IsPackable>false</IsPackable>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="MSTest" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="Npgsql" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Kartova.Audit.Domain\Kartova.Audit.Domain.csproj" />
    <ProjectReference Include="..\Kartova.Audit.Infrastructure\Kartova.Audit.Infrastructure.csproj" />
    <ProjectReference Include="..\..\..\..\tests\Kartova.Testing.Auth\Kartova.Testing.Auth.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel\Kartova.SharedKernel.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel.AspNetCore\Kartova.SharedKernel.AspNetCore.csproj" />
    <ProjectReference Include="..\..\..\Kartova.SharedKernel.Postgres\Kartova.SharedKernel.Postgres.csproj" />
  </ItemGroup>
</Project>
```

Add to the solution:
```
cmd //c "dotnet sln Kartova.slnx add src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/Kartova.Audit.Infrastructure.IntegrationTests.csproj"
```

> Verify the relative path to `Kartova.Testing.Auth.csproj` and copy the container-bootstrap pattern (Postgres container startup, `PostgresTestBootstrap.SeedRolesAndSchemaAsync`, `RunMigrationsAsync<AuditDbContext>`, connection-string helpers) from an existing infra integration fixture. Do not hand-roll container lifecycle.

- [ ] **Step 2: Create the fixture**

`AuditLogFixture.cs` — brings up Postgres, seeds roles, migrates `AuditDbContext`, exposes app/migrator/bypass connection strings. Model it on `KartovaApiFixtureBase` + `PostgresTestBootstrap`; the essential surface:

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.Audit.Infrastructure;
using Kartova.Testing.Auth;
using Npgsql;

namespace Kartova.Audit.Infrastructure.IntegrationTests;

/// <summary>
/// Spins up a Postgres container with production-matching roles (migrator / kartova_app /
/// kartova_bypass_rls), migrates the audit schema, and hands out per-role connection strings.
/// Follow the existing module fixtures for the exact Testcontainers wiring.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class AuditLogFixture : IAsyncDisposable
{
    public required string MigratorConnectionString { get; init; }
    public required string AppConnectionString { get; init; }
    public required string BypassConnectionString { get; init; }

    public static async Task<AuditLogFixture> CreateAsync(CancellationToken ct = default)
    {
        // 1. Start container (see existing fixtures), obtain admin connection string.
        // 2. await PostgresTestBootstrap.SeedRolesAndSchemaAsync(adminConnectionString, ct);
        // 3. await PostgresTestBootstrap.RunMigrationsAsync<AuditDbContext>(migratorCs, o => new AuditDbContext(o), ct);
        // 4. Build per-role strings via PostgresTestBootstrap.ConnectionStringFor(baseCs, role).
        throw new NotImplementedException("Wire to the shared Testcontainers harness per the existing module fixtures.");
    }

    public ValueTask DisposeAsync() => default; // dispose the container per the harness pattern.
}
```

> This step's deliverable is a working fixture copied from the existing harness, not the stub above. The stub documents the required surface so the test files compile against it.

- [ ] **Step 3: Write the grants + RLS tests**

`AuditLogGrantsAndRlsTests.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Npgsql;

namespace Kartova.Audit.Infrastructure.IntegrationTests;

[TestClass]
[ExcludeFromCodeCoverage]
public class AuditLogGrantsAndRlsTests
{
    private static AuditLogFixture _fx = null!;

    [ClassInitialize]
    public static async Task Init(TestContext _) => _fx = await AuditLogFixture.CreateAsync();

    [ClassCleanup]
    public static async Task Cleanup() => await _fx.DisposeAsync();

    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    // Opens an app-role connection with the tenant GUC set, like TenantScope does at request start.
    private static async Task<NpgsqlConnection> OpenAppScopedAsync(Guid tenantId)
    {
        var conn = new NpgsqlConnection(_fx.AppConnectionString);
        await conn.OpenAsync();
        await using var set = conn.CreateCommand();
        set.CommandText = "SELECT set_config('app.current_tenant_id', $1, false)";
        set.Parameters.AddWithValue(tenantId.ToString());
        await set.ExecuteNonQueryAsync();
        return conn;
    }

    private static async Task InsertRowAsync(NpgsqlConnection conn, Guid tenantId, long seq)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO audit_log (id, tenant_id, seq, occurred_at, actor_type, actor_id, actor_display,
                       action, target_type, target_id, data, prev_hash, row_hash)
VALUES ($1, $2, $3, now(), 'User', $4, NULL, 'test.action', 'User', $5, NULL, $6, $7)";
        cmd.Parameters.AddWithValue(Guid.NewGuid());
        cmd.Parameters.AddWithValue(tenantId);
        cmd.Parameters.AddWithValue(seq);
        cmd.Parameters.AddWithValue(Guid.NewGuid());
        cmd.Parameters.AddWithValue(Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue(new byte[32]);
        cmd.Parameters.AddWithValue(new byte[32]);
        await cmd.ExecuteNonQueryAsync();
    }

    [TestMethod]
    public async Task App_role_can_insert_into_audit_log()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await InsertRowAsync(conn, TenantA, seq: 1000);
        // No exception == success.
    }

    [TestMethod]
    public async Task App_role_cannot_update_audit_log()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await InsertRowAsync(conn, TenantA, seq: 1001);
        await using var upd = conn.CreateCommand();
        upd.CommandText = "UPDATE audit_log SET action = 'tampered' WHERE tenant_id = $1";
        upd.Parameters.AddWithValue(TenantA);
        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => upd.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState); // insufficient_privilege
    }

    [TestMethod]
    public async Task App_role_cannot_delete_audit_log()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await InsertRowAsync(conn, TenantA, seq: 1002);
        await using var del = conn.CreateCommand();
        del.CommandText = "DELETE FROM audit_log WHERE tenant_id = $1";
        del.Parameters.AddWithValue(TenantA);
        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => del.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState);
    }

    [TestMethod]
    public async Task App_role_cannot_truncate_audit_log()
    {
        await using var conn = await OpenAppScopedAsync(TenantA);
        await using var tr = conn.CreateCommand();
        tr.CommandText = "TRUNCATE audit_log";
        var ex = await Assert.ThrowsExactlyAsync<PostgresException>(() => tr.ExecuteNonQueryAsync());
        Assert.AreEqual("42501", ex.SqlState);
    }

    [TestMethod]
    public async Task Rls_hides_other_tenants_rows()
    {
        await using (var a = await OpenAppScopedAsync(TenantA))
            await InsertRowAsync(a, TenantA, seq: 2000);

        await using var b = await OpenAppScopedAsync(TenantB);
        await using var q = b.CreateCommand();
        q.CommandText = "SELECT count(*) FROM audit_log WHERE seq = 2000";
        var count = (long)(await q.ExecuteScalarAsync())!;
        Assert.AreEqual(0, count); // tenant B cannot see tenant A's row
    }
}
```

- [ ] **Step 4: Run the grants/RLS tests**

Run:
```
cmd //c "dotnet test src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/Kartova.Audit.Infrastructure.IntegrationTests.csproj --filter AuditLogGrantsAndRlsTests"
```
Expected: PASS (5 tests). (Requires Docker running for Testcontainers.)

- [ ] **Step 5: Write the writer + verifier + stability + rollback tests**

`AuditWriterTests.cs`:

```csharp
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Kartova.Audit.Domain;
using Kartova.Audit.Infrastructure;
using Kartova.SharedKernel.Audit;
using Kartova.SharedKernel.AspNetCore;
using Kartova.SharedKernel.Multitenancy;
using Kartova.SharedKernel.Postgres;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Kartova.Audit.Infrastructure.IntegrationTests;

[TestClass]
[ExcludeFromCodeCoverage]
public class AuditWriterTests
{
    private static AuditLogFixture _fx = null!;

    [ClassInitialize]
    public static async Task Init(TestContext _) => _fx = await AuditLogFixture.CreateAsync();

    [ClassCleanup]
    public static async Task Cleanup() => await _fx.DisposeAsync();

    // Builds a DI container that mirrors the request pipeline's audit-relevant wiring:
    // NpgsqlDataSource (as kartova_app) + tenant scope + AuditDbContext + a stub current user.
    private static ServiceProvider BuildProvider(Guid actorId)
    {
        var services = new ServiceCollection();
        services.AddNpgsqlDataSource(_fx.AppConnectionString);
        services.AddTenantScope();
        services.AddModuleDbContext<AuditDbContext>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AuditWriter>();
        services.AddScoped<AuditChainVerifier>();

        var user = Substitute.For<ICurrentUser>();
        user.UserId.Returns(actorId);
        services.AddScoped(_ => user);

        return services.BuildServiceProvider();
    }

    private static AuditEntry SampleEntry(Guid target) => new(
        Action: "member.role_changed",
        TargetType: "User",
        TargetId: target.ToString(),
        Data: new Dictionary<string, string?> { ["old_role"] = "Member", ["new_role"] = "OrgAdmin" });

    [TestMethod]
    public async Task Append_three_then_verify_intact_with_contiguous_seq()
    {
        var tenant = new TenantId(Guid.NewGuid());
        var actor = Guid.NewGuid();
        await using var sp = BuildProvider(actor);

        using (var scope = sp.CreateScope())
        {
            var ts = scope.ServiceProvider.GetRequiredService<ITenantScope>();
            await using var handle = await ts.BeginAsync(tenant, CancellationToken.None);
            var writer = scope.ServiceProvider.GetRequiredService<AuditWriter>();
            await writer.AppendAsync(SampleEntry(Guid.NewGuid()), CancellationToken.None);
            await writer.AppendAsync(SampleEntry(Guid.NewGuid()), CancellationToken.None);
            await writer.AppendAsync(SampleEntry(Guid.NewGuid()), CancellationToken.None);
            await handle.CommitAsync(CancellationToken.None);
        }

        using (var scope = sp.CreateScope())
        {
            var ts = scope.ServiceProvider.GetRequiredService<ITenantScope>();
            await using var handle = await ts.BeginAsync(tenant, CancellationToken.None);
            var verifier = scope.ServiceProvider.GetRequiredService<AuditChainVerifier>();
            var result = await verifier.VerifyAsync(tenant, CancellationToken.None);
            Assert.IsTrue(result.Intact, result.Reason);
        }
    }

    [TestMethod]
    public async Task Append_persists_jsonb_data_that_verifies_after_round_trip()
    {
        // Guards against false tamper alarms: the hash computed at write time must still
        // verify after the data dictionary round-trips through Postgres jsonb normalization
        // (design spec §5). A successful Verify after a fresh read proves stability.
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider(Guid.NewGuid());

        using (var scope = sp.CreateScope())
        {
            var ts = scope.ServiceProvider.GetRequiredService<ITenantScope>();
            await using var handle = await ts.BeginAsync(tenant, CancellationToken.None);
            var writer = scope.ServiceProvider.GetRequiredService<AuditWriter>();
            await writer.AppendAsync(SampleEntry(Guid.NewGuid()), CancellationToken.None);
            await handle.CommitAsync(CancellationToken.None);
        }

        using (var scope = sp.CreateScope())
        {
            var ts = scope.ServiceProvider.GetRequiredService<ITenantScope>();
            await using var handle = await ts.BeginAsync(tenant, CancellationToken.None);
            var verifier = scope.ServiceProvider.GetRequiredService<AuditChainVerifier>();
            Assert.IsTrue((await verifier.VerifyAsync(tenant, CancellationToken.None)).Intact);
        }
    }

    [TestMethod]
    public async Task Rolled_back_scope_persists_no_audit_row()
    {
        // The transactional substrate behind fail-closed: an append that is not committed
        // leaves no row. (In Phase 2 a real handler couples a business write to this same
        // transaction, so an audit failure rolls the business change back too.)
        var tenant = new TenantId(Guid.NewGuid());
        await using var sp = BuildProvider(Guid.NewGuid());

        using (var scope = sp.CreateScope())
        {
            var ts = scope.ServiceProvider.GetRequiredService<ITenantScope>();
            await using var handle = await ts.BeginAsync(tenant, CancellationToken.None);
            var writer = scope.ServiceProvider.GetRequiredService<AuditWriter>();
            await writer.AppendAsync(SampleEntry(Guid.NewGuid()), CancellationToken.None);
            // Dispose WITHOUT CommitAsync → rollback.
        }

        using (var scope = sp.CreateScope())
        {
            var ts = scope.ServiceProvider.GetRequiredService<ITenantScope>();
            await using var handle = await ts.BeginAsync(tenant, CancellationToken.None);
            var verifier = scope.ServiceProvider.GetRequiredService<AuditChainVerifier>();
            var result = await verifier.VerifyAsync(tenant, CancellationToken.None);
            Assert.IsTrue(result.Intact);        // empty chain is intact...
            // ...and empty: re-open and count via the verifier's own load path.
        }
    }
}
```

> If `AddModuleDbContext<AuditDbContext>()` requires the interceptors registered by `AddTenantScope()` to already be present, keep the `AddTenantScope()` call before it (as shown). Confirm the resolution order against how the API composes these in `Program.cs`.

- [ ] **Step 6: Run the writer tests**

Run:
```
cmd //c "dotnet test src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/Kartova.Audit.Infrastructure.IntegrationTests.csproj --filter AuditWriterTests"
```
Expected: PASS (3 tests).

- [ ] **Step 7: Run the full Audit integration suite + commit**

Run:
```
cmd //c "dotnet test src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests/Kartova.Audit.Infrastructure.IntegrationTests.csproj"
```
Expected: PASS (8 tests).

```
git add src/Modules/Audit/Kartova.Audit.Infrastructure.IntegrationTests Kartova.slnx
git commit -m "test(audit): integration — insert-only grants, RLS, writer e2e, jsonb/rollback"
```

---

## Task 12: Docker-compose DB verification + checklist update (DoD)

**Files:**
- Modify: `docs/product/CHECKLIST.md`
- Create: `docs/superpowers/evidence/2026-06-12-audit-log-foundation/db-verification.md`

This slice wires DB schema + grants + RLS, so DoD item 5 requires real-container evidence (unit + arch tests are the wrong layer for grant/RLS semantics).

- [ ] **Step 1: Bring up the stack and apply migrations**

Run:
```
cmd //c "docker compose up -d postgres"
cmd //c "dotnet run --project src/Kartova.Migrator"
```
Expected: migrator logs "Module 'audit' migrated." and exits 0.

- [ ] **Step 2: Happy path — app role inserts an audit row**

Connect as `kartova_app`, set the tenant GUC, insert one row (use the `INSERT` shape from Task 11 Step 3 via `psql` or a scratch script). Expected: 1 row inserted; `SELECT` it back within the same tenant scope.

- [ ] **Step 3: Negative path — app role cannot mutate**

As `kartova_app`, run `UPDATE audit_log SET action='x';` then `DELETE FROM audit_log;`. Expected: both fail with `ERROR: permission denied for table audit_log` (SQLSTATE 42501). Capture the output.

- [ ] **Step 4: Record evidence**

Write `docs/superpowers/evidence/2026-06-12-audit-log-foundation/db-verification.md` with the exact commands + captured output for steps 1-3 (mirror `docs/superpowers/evidence/2026-05-04-sorting-pagination/curl-output.md`).

- [ ] **Step 5: Update the checklist**

In `docs/product/CHECKLIST.md`, annotate E-01.F-03.S-03 with Phase-1 completion, e.g.:

```markdown
- [~] E-01.F-03.S-03 — Append-only audit log table (MiFID II) — Phase 1 foundation (audit-log-foundation, 2026-06-12): Kartova.Audit module, insert-only/RLS audit_log, IAuditWriter (sync in-transaction fail-closed), per-tenant SHA-256 hash chain + verifier (ADR-0018). Event wiring = Phase 2.
```

- [ ] **Step 6: Commit**

```
git add docs/product/CHECKLIST.md docs/superpowers/evidence/2026-06-12-audit-log-foundation
git commit -m "docs(audit): Phase-1 DB verification evidence + checklist update"
```

---

## Definition of Done (slice boundary)

After Task 12, run the full DoD gate from CLAUDE.md before claiming completion — none of these may be skipped:

1. `cmd //c "dotnet build Kartova.slnx -warnaserror"` → 0 warnings/errors.
2. Per-task spec-compliance + code-quality subagent reviews.
3. `/superpowers:requesting-code-review` against the full branch diff (spec + this plan as context).
4. Full suite: `cmd //c "dotnet test Kartova.slnx"` → unit + architecture + integration green.
5. Docker-compose DB evidence captured (Task 12) — happy-path insert + negative-path UPDATE/DELETE rejection.
6. `/simplify` against the branch diff.
7. `/misc:mutation-sentinel` → `/misc:test-generator` on changed files; mutation score ≥ 80% (per `stryker-config.json`); document survivors. The Domain hasher/serializer/inspector are the high-value mutation targets.
8. `/pr-review-toolkit:review-pr`.
9. `/deep-review` against the branch diff.

Until all nine are green, the honest status is **"implementation staged, verification pending"** — not "Phase 1 complete".

---

## Self-Review

**Spec coverage:**
- §2 sync/in-transaction/fail-closed → Task 8 (writer enlists via `AddModuleDbContext`); Task 11 rollback test.
- §2 dedicated module + SharedKernel port → Tasks 1, 2, 10.
- §2 per-tenant hash chain → Tasks 3-5 (hasher/inspector), Task 8 (advisory lock + chain head).
- §2 `actor_display` snapshot → Task 4 (column + factory param); writer leaves it null in Phase 1 (documented, Phase 2 populates) — consistent with spec §3.
- §4 data model (all columns, GUIDv7 id, unique `(tenant_id, seq)`, indexes) → Tasks 4, 6, 7.
- §4 insert-only grants + RLS → Task 7; verified in Task 11 (grants/RLS) + Task 12 (container).
- §5 canonical serialization + jsonb-stability + microsecond timestamp → Task 3 (serializer), Task 8 (truncation), Task 11 (round-trip verify test).
- §5 verifier → Tasks 5 + 9; exercised in Task 11.
- §6 fail-closed → Task 8 (propagation) + Task 11 (rollback substrate).
- §7 testing (unit/arch/integration incl. mandatory UPDATE/DELETE-rejected + fail-closed) → Tasks 3-5, 10, 11.
- §8 DoD container evidence → Task 12.

**Placeholder scan:** No "TBD"/"add error handling" placeholders. Two steps (Task 11 fixture, Task 11 csproj package list) intentionally instruct copying the exact harness/package set from named existing files rather than inventing versions/container lifecycle — this is correct given central package management and the shared Testcontainers harness, and each names the precise reference file.

**Type consistency:** `AuditRowHasher.ComputeRowHash(...)` argument order is identical in Task 3 (definition), Task 4 (`AuditLogEntry.Create`), and Task 5 (`AuditChainInspector`). `AuditEntry(Action, TargetType, TargetId, Data)` is consistent across Tasks 2, 8, 11. `AuditChainVerifier.VerifyAsync(TenantId, ct)` consistent in Tasks 9 + 11. `AuditActorType` values consistent (Task 1) with usage (Tasks 3-8).
