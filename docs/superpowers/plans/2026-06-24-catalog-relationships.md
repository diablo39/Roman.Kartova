# Catalog — Manual Entity Relationships (Slice 1a, backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add directed, origin-stamped manual relationships (`depends-on`, `part-of`) between catalog entities as a new Catalog aggregate, exposed as `POST` / `GET`-by-entity / `DELETE` REST endpoints under tenant RLS, with audit.

**Architecture:** Single polymorphic `relationships` table in the existing Catalog module (Approach A): each row is `(tenant_id, source{kind,id}, type, target{kind,id}, origin, created_by, created_at)`. `*_kind` is an open string discriminator so future + custom entity kinds need no schema change. Source-side authorization (you declare edges on entities you can edit), resolved through one `ICatalogEntityLookup` port that also validates endpoint existence and supplies display names. Mirrors the proven Application/Service slice mechanics: direct-dispatch handlers (ADR-0093), `ITenantScope` hybrid filter (ADR-0090), cursor pagination (ADR-0095), fail-closed audit.

**Tech Stack:** .NET 10 / C#, EF Core (PostgreSQL/Npgsql, RLS), ASP.NET Core minimal APIs, MSTest v4 + NSubstitute, Testcontainers (`KartovaApiFixtureBase`), real `JwtBearer`.

## Global Constraints

- Solution file: `Kartova.slnx`. Build with `TreatWarningsAsErrors=true` — **0 warnings**.
- **Serena-first** code edits: use `replace_symbol_body` / `insert_*` / `replace_content`; the PreToolUse guard hard-blocks built-in `Edit`/`Write` on `.cs`. Bypass for a true fallback: `SERENA_GUARD=0`.
- Module boundaries (ADR-0082): no cross-module internal refs; only `Kartova.Catalog.Contracts` is public.
- Routes (ADR-0092): `/api/v1/catalog/<collection>`. Verbs (ADR-0096): `POST` create, `DELETE` remove; no `PATCH`. UUID ids only (ADR-0098).
- Errors (ADR-0091): `application/problem+json`. Pagination (ADR-0095): `CursorPage<T>`, `sortBy`/`sortOrder` from a per-resource allowlist, default 50 / max 200.
- Coverage exclusion (CLAUDE.md): every `*.Contracts` DTO carries `[ExcludeFromCodeCoverage]`.
- Migrations run only via `Kartova.Migrator` (ADR-0085) — never at app startup.
- Tenant id + creator id come from `ITenantContext` / `ICurrentUser`, **never** the request payload (ADR-0090).
- DoD: the eight always-blocking gates + conditional mutation gate from CLAUDE.md apply. Mutation gate (6) **is blocking** (diff touches Domain + Application logic). Run `scripts/ci-local.sh` green before push.
- Windows shell: PowerShell or `cmd //c` wrappers for `dotnet`. Multi-line git messages: PowerShell + multiple `-m`.

---

## File Structure

**Domain (`src/Modules/Catalog/Kartova.Catalog.Domain/`)** — new value objects + aggregate, no external deps:
- `RelationshipId.cs`, `EntityKind.cs`, `RelationshipType.cs`, `RelationshipOrigin.cs`, `EntityRef.cs`, `RelationshipTypeRules.cs`, `Relationship.cs`

**Application (`…/Kartova.Catalog.Application/`)** — port + command/query records:
- `ICatalogEntityLookup.cs`, `CreateRelationshipCommand.cs`, `RelationshipDirection.cs`, `ListRelationshipsForEntityQuery.cs`, `RelationshipResponseExtensions.cs`
- modify `CatalogAuditActions.cs`

**Contracts (`…/Kartova.Catalog.Contracts/`)** — public DTOs:
- `CreateRelationshipRequest.cs`, `EntityRefDto.cs`, `RelationshipResponse.cs`, `RelationshipSortField.cs`

**Infrastructure (`…/Kartova.Catalog.Infrastructure/`)** — EF + handlers + wiring:
- `EfRelationshipConfiguration.cs`, `CatalogEntityLookup.cs`, `CreateRelationshipHandler.cs`, `ListRelationshipsForEntityHandler.cs`, `DeleteRelationshipHandler.cs`, `RelationshipSortSpecs.cs`, `Migrations/<ts>_AddRelationships.cs`
- modify `CatalogDbContext.cs`, `CatalogModule.cs`, `CatalogEndpointDelegates.cs`

**SharedKernel (`src/Kartova.SharedKernel/Multitenancy/`)** — modify `KartovaPermissions.cs`, `KartovaRolePermissions.cs`

**Tests** — `Kartova.Catalog.Tests/RelationshipTests.cs`; `Kartova.Catalog.IntegrationTests/{CreateRelationshipTests,ListRelationshipsTests,DeleteRelationshipTests}.cs`; modify `CatalogPermissionMatrixTests.cs`, `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs`

---

## Task 1: Domain primitives — enums, EntityRef, type-pair rules

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/RelationshipId.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/EntityKind.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/RelationshipType.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/RelationshipOrigin.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/EntityRef.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/RelationshipTypeRules.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/RelationshipTests.cs`

**Interfaces:**
- Produces: `RelationshipId(Guid Value)` + `New()`; enums `EntityKind {Application, Service}`, `RelationshipType {DependsOn, ProvidesApiFor, ConsumesApiFrom, PublishesTo, SubscribesFrom, DeployedOn, PartOf}`, `RelationshipOrigin {Manual, Scan, Agent}`; `readonly record struct EntityRef(EntityKind Kind, Guid Id)`; `static RelationshipTypeRules.IsCreatable(RelationshipType)` + `IsAllowedPair(RelationshipType, EntityKind source, EntityKind target)`.

- [ ] **Step 1: Write the failing tests**

Create `RelationshipTests.cs`:

```csharp
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public class RelationshipTests
{
    [TestMethod]
    public void EntityRef_rejects_empty_id()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new EntityRef(EntityKind.Service, Guid.Empty));
    }

    [TestMethod]
    public void EntityRef_value_equality_holds()
    {
        var id = Guid.NewGuid();
        Assert.AreEqual(new EntityRef(EntityKind.Service, id), new EntityRef(EntityKind.Service, id));
        Assert.AreNotEqual(new EntityRef(EntityKind.Service, id), new EntityRef(EntityKind.Application, id));
    }

    [TestMethod]
    public void IsCreatable_only_dependsOn_and_partOf()
    {
        Assert.IsTrue(RelationshipTypeRules.IsCreatable(RelationshipType.DependsOn));
        Assert.IsTrue(RelationshipTypeRules.IsCreatable(RelationshipType.PartOf));
        foreach (var t in new[] { RelationshipType.ProvidesApiFor, RelationshipType.ConsumesApiFrom,
                     RelationshipType.PublishesTo, RelationshipType.SubscribesFrom, RelationshipType.DeployedOn })
            Assert.IsFalse(RelationshipTypeRules.IsCreatable(t), $"{t} must not be creatable in slice 1a");
    }

    [DataTestMethod]
    // depends-on: any of {App,Service} → any of {App,Service}
    [DataRow(RelationshipType.DependsOn, EntityKind.Service, EntityKind.Service, true)]
    [DataRow(RelationshipType.DependsOn, EntityKind.Application, EntityKind.Service, true)]
    [DataRow(RelationshipType.DependsOn, EntityKind.Service, EntityKind.Application, true)]
    [DataRow(RelationshipType.DependsOn, EntityKind.Application, EntityKind.Application, true)]
    // part-of: Service → Application ONLY
    [DataRow(RelationshipType.PartOf, EntityKind.Service, EntityKind.Application, true)]
    [DataRow(RelationshipType.PartOf, EntityKind.Application, EntityKind.Service, false)]
    [DataRow(RelationshipType.PartOf, EntityKind.Service, EntityKind.Service, false)]
    [DataRow(RelationshipType.PartOf, EntityKind.Application, EntityKind.Application, false)]
    public void IsAllowedPair_matrix(RelationshipType type, EntityKind source, EntityKind target, bool expected)
    {
        Assert.AreEqual(expected, RelationshipTypeRules.IsAllowedPair(type, source, target));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter RelationshipTests"`
Expected: build failure / FAIL — `EntityRef`, `RelationshipTypeRules` not defined.

- [ ] **Step 3: Create the enums + `RelationshipId`**

`RelationshipId.cs`:
```csharp
namespace Kartova.Catalog.Domain;

public readonly record struct RelationshipId(Guid Value)
{
    public static RelationshipId New() => new(Guid.NewGuid());
}
```
`EntityKind.cs`:
```csharp
namespace Kartova.Catalog.Domain;

public enum EntityKind { Application, Service }
```
`RelationshipType.cs`:
```csharp
namespace Kartova.Catalog.Domain;

public enum RelationshipType
{
    DependsOn,
    ProvidesApiFor,
    ConsumesApiFrom,
    PublishesTo,
    SubscribesFrom,
    DeployedOn,
    PartOf,
}
```
`RelationshipOrigin.cs`:
```csharp
namespace Kartova.Catalog.Domain;

public enum RelationshipOrigin { Manual, Scan, Agent }
```

- [ ] **Step 4: Create `EntityRef` and `RelationshipTypeRules`**

`EntityRef.cs`:
```csharp
namespace Kartova.Catalog.Domain;

public readonly record struct EntityRef
{
    public EntityKind Kind { get; }
    public Guid Id { get; }

    public EntityRef(EntityKind kind, Guid id)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentException("unknown entity kind", nameof(kind));
        if (id == Guid.Empty) throw new ArgumentException("entity id required", nameof(id));
        Kind = kind;
        Id = id;
    }
}
```
`RelationshipTypeRules.cs`:
```csharp
namespace Kartova.Catalog.Domain;

public static class RelationshipTypeRules
{
    public static bool IsCreatable(RelationshipType type)
        => type is RelationshipType.DependsOn or RelationshipType.PartOf;

    public static bool IsAllowedPair(RelationshipType type, EntityKind source, EntityKind target) => type switch
    {
        RelationshipType.DependsOn => true,
        RelationshipType.PartOf => source == EntityKind.Service && target == EntityKind.Application,
        _ => false,
    };
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter RelationshipTests"`
Expected: PASS (12 cases).

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain src/Modules/Catalog/Kartova.Catalog.Tests/RelationshipTests.cs
git commit -m "feat(catalog): relationship domain primitives (enums, EntityRef, type-pair rules)"
```

---

## Task 2: `Relationship` aggregate

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/Relationship.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/RelationshipTests.cs` (add methods)

**Interfaces:**
- Consumes: Task 1 types; `TenantId` (`Kartova.SharedKernel`, `new TenantId(Guid)` / `.Value`); `ITenantOwned`.
- Produces: `Relationship.CreateManual(EntityRef source, EntityRef target, RelationshipType type, Guid createdByUserId, TenantId tenantId, TimeProvider clock)` and a `DateTimeOffset createdAt` overload; readonly props `Id (RelationshipId), TenantId, Source, Target, Type, Origin, CreatedByUserId, CreatedAt`.

- [ ] **Step 1: Write the failing tests** (append to `RelationshipTests.cs`)

```csharp
    private static EntityRef Svc(Guid id) => new(EntityKind.Service, id);
    private static EntityRef App(Guid id) => new(EntityKind.Application, id);

    [TestMethod]
    public void CreateManual_dependsOn_sets_fields_and_manual_origin()
    {
        var src = Svc(Guid.NewGuid());
        var tgt = Svc(Guid.NewGuid());
        var creator = Guid.NewGuid();
        var rel = Relationship.CreateManual(src, tgt, RelationshipType.DependsOn, creator,
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System);

        Assert.AreEqual(src, rel.Source);
        Assert.AreEqual(tgt, rel.Target);
        Assert.AreEqual(RelationshipType.DependsOn, rel.Type);
        Assert.AreEqual(RelationshipOrigin.Manual, rel.Origin);
        Assert.AreEqual(creator, rel.CreatedByUserId);
        Assert.AreNotEqual(Guid.Empty, rel.Id.Value);
    }

    [TestMethod]
    public void CreateManual_partOf_service_to_application_is_valid()
    {
        var rel = Relationship.CreateManual(Svc(Guid.NewGuid()), App(Guid.NewGuid()),
            RelationshipType.PartOf, Guid.NewGuid(),
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System);
        Assert.AreEqual(RelationshipType.PartOf, rel.Type);
    }

    [TestMethod]
    public void CreateManual_rejects_self_reference()
    {
        var same = Svc(Guid.NewGuid());
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            same, same, RelationshipType.DependsOn, Guid.NewGuid(),
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System));
    }

    [TestMethod]
    public void CreateManual_rejects_non_creatable_type()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            Svc(Guid.NewGuid()), Svc(Guid.NewGuid()), RelationshipType.PublishesTo, Guid.NewGuid(),
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System));
    }

    [TestMethod]
    public void CreateManual_rejects_disallowed_pair_partOf_app_to_service()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            App(Guid.NewGuid()), Svc(Guid.NewGuid()), RelationshipType.PartOf, Guid.NewGuid(),
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System));
    }

    [TestMethod]
    public void CreateManual_rejects_empty_creator()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            Svc(Guid.NewGuid()), Svc(Guid.NewGuid()), RelationshipType.DependsOn, Guid.Empty,
            new Kartova.SharedKernel.Multitenancy.TenantId(Guid.NewGuid()), TimeProvider.System));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter RelationshipTests"`
Expected: FAIL — `Relationship` not defined.

- [ ] **Step 3: Implement `Relationship.cs`**

> Use the project's `TenantId` namespace as seen in `Service.cs` — confirm it is `Kartova.SharedKernel.Multitenancy`; adjust the `using` if the codebase differs.

```csharp
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Domain;

public sealed class Relationship : ITenantOwned
{
    private Guid _id;

    public RelationshipId Id => new(_id);
    public TenantId TenantId { get; private set; }
    public EntityRef Source { get; private set; }
    public EntityRef Target { get; private set; }
    public RelationshipType Type { get; private set; }
    public RelationshipOrigin Origin { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Relationship() { } // EF

    public static Relationship CreateManual(
        EntityRef source, EntityRef target, RelationshipType type,
        Guid createdByUserId, TenantId tenantId, TimeProvider clock)
        => CreateManual(source, target, type, createdByUserId, tenantId, clock.GetUtcNow());

    public static Relationship CreateManual(
        EntityRef source, EntityRef target, RelationshipType type,
        Guid createdByUserId, TenantId tenantId, DateTimeOffset createdAt)
    {
        if (source == target)
            throw new ArgumentException("a relationship cannot reference the same entity as source and target", nameof(target));
        if (!RelationshipTypeRules.IsCreatable(type))
            throw new ArgumentException($"relationship type '{type}' is not yet available", nameof(type));
        if (!RelationshipTypeRules.IsAllowedPair(type, source.Kind, target.Kind))
            throw new ArgumentException($"'{type}' is not valid from {source.Kind} to {target.Kind}", nameof(type));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("createdByUserId required", nameof(createdByUserId));

        return new Relationship
        {
            _id = RelationshipId.New().Value,
            TenantId = tenantId,
            Source = source,
            Target = target,
            Type = type,
            Origin = RelationshipOrigin.Manual,
            CreatedByUserId = createdByUserId,
            CreatedAt = createdAt,
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter RelationshipTests"`
Expected: PASS (all Task 1 + Task 2 cases).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/Relationship.cs src/Modules/Catalog/Kartova.Catalog.Tests/RelationshipTests.cs
git commit -m "feat(catalog): Relationship aggregate with CreateManual invariants"
```

---

## Task 3: Contracts (DTOs + sort field)

**Files:**
- Create: `…/Kartova.Catalog.Contracts/CreateRelationshipRequest.cs`
- Create: `…/Kartova.Catalog.Contracts/EntityRefDto.cs`
- Create: `…/Kartova.Catalog.Contracts/RelationshipResponse.cs`
- Create: `…/Kartova.Catalog.Contracts/RelationshipSortField.cs`

**Interfaces:**
- Consumes: `EntityKind`, `RelationshipType`, `RelationshipOrigin` (Domain — Contracts already references Domain enums, as `ServiceResponse` does for `Protocol`/`HealthStatus`).
- Produces: `CreateRelationshipRequest`, `EntityRefDto`, `RelationshipResponse`, `RelationshipSortField {CreatedAt, Type}`.

- [ ] **Step 1: Create the DTOs** (no test — DTOs are `[ExcludeFromCodeCoverage]` per CLAUDE.md; verified by build)

`CreateRelationshipRequest.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record CreateRelationshipRequest(
    EntityKind SourceKind, Guid SourceId, RelationshipType Type, EntityKind TargetKind, Guid TargetId);
```
`EntityRefDto.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record EntityRefDto(EntityKind Kind, Guid Id, string DisplayName);
```
`RelationshipResponse.cs`:
```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record RelationshipResponse(
    Guid Id, EntityRefDto Source, EntityRefDto Target,
    RelationshipType Type, RelationshipOrigin Origin,
    Guid CreatedByUserId, DateTimeOffset CreatedAt);
```
`RelationshipSortField.cs`:
```csharp
namespace Kartova.Catalog.Contracts;

public enum RelationshipSortField { CreatedAt, Type }
```

- [ ] **Step 2: Build**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Contracts"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Contracts
git commit -m "feat(catalog): relationship contracts (request, response, sort field)"
```

---

## Task 4: Permission + role map

**Files:**
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs`
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs`
- Test: `tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs`

**Interfaces:**
- Produces: `KartovaPermissions.CatalogRelationshipsWrite = "catalog.relationships.write"` (also added to `All`); mapped to `Member` + `OrgAdmin`.

- [ ] **Step 1: Write the failing test** (append a method to `KartovaRolePermissionsTests.cs`, mirroring the existing `CatalogServicesRegister` assertions)

```csharp
    [TestMethod]
    public void RelationshipsWrite_granted_to_member_and_orgadmin_not_viewer()
    {
        Assert.IsTrue(KartovaRolePermissions.ForRole(KartovaRoles.Member)
            .Contains(KartovaPermissions.CatalogRelationshipsWrite));
        Assert.IsTrue(KartovaRolePermissions.ForRole(KartovaRoles.OrgAdmin)
            .Contains(KartovaPermissions.CatalogRelationshipsWrite));
        Assert.IsFalse(KartovaRolePermissions.ForRole(KartovaRoles.Viewer)
            .Contains(KartovaPermissions.CatalogRelationshipsWrite));
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Tests --filter KartovaRolePermissionsTests"`
Expected: FAIL — `CatalogRelationshipsWrite` not defined.

- [ ] **Step 3: Add the permission constant**

In `KartovaPermissions.cs`, add the constant next to `CatalogServicesRegister` and include it in the `All` collection (mirror exactly how `CatalogServicesRegister` is declared and listed):
```csharp
public const string CatalogRelationshipsWrite = "catalog.relationships.write";
```

- [ ] **Step 4: Map it to Member + OrgAdmin**

In `KartovaRolePermissions.cs`, add `KartovaPermissions.CatalogRelationshipsWrite,` to **both** the `[KartovaRoles.Member]` and `[KartovaRoles.OrgAdmin]` permission arrays (alongside `CatalogServicesRegister`). Do **not** add it to `Viewer`.

- [ ] **Step 5: Run tests to verify they pass**

Run: `cmd //c "dotnet test tests/Kartova.SharedKernel.Tests --filter KartovaRolePermissionsTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs tests/Kartova.SharedKernel.Tests/KartovaRolePermissionsTests.cs
git commit -m "feat(catalog): catalog.relationships.write permission (Member + OrgAdmin)"
```

---

## Task 5: EF configuration + migration + DbSet

**Files:**
- Create: `…/Kartova.Catalog.Infrastructure/EfRelationshipConfiguration.cs`
- Modify: `…/Kartova.Catalog.Infrastructure/CatalogDbContext.cs`
- Create: `…/Kartova.Catalog.Infrastructure/Migrations/<ts>_AddRelationships.cs` (generated, then hand-edit RLS)

**Interfaces:**
- Consumes: `Relationship` aggregate (Task 2).
- Produces: `CatalogDbContext.Relationships` `DbSet<Relationship>`; `relationships` table under RLS.

- [ ] **Step 1: Create `EfRelationshipConfiguration.cs`**

Mirrors `EfServiceConfiguration` (field-backed `_id`, `TenantId` conversion, indexes) with two `ComplexProperty` value objects and **no `xmin`/Version** (create+delete only). See the spec's EF caveat: if EF rejects the `record struct` complex type, fall back to inline scalar properties (`b.Property<EntityKind>("SourceKind")...`) — same columns, same behavior.

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kartova.Catalog.Infrastructure;

public sealed class EfRelationshipConfiguration : IEntityTypeConfiguration<Relationship>
{
    internal const string IdFieldName = "_id";

    public void Configure(EntityTypeBuilder<Relationship> b)
    {
        b.ToTable("relationships");

        b.Property<Guid>(IdFieldName)
            .HasField(IdFieldName)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasKey(IdFieldName);
        b.Ignore(x => x.Id);

        b.Property(x => x.TenantId)
            .HasConversion(v => v.Value, v => new TenantId(v))
            .HasColumnName("tenant_id")
            .IsRequired();

        b.ComplexProperty(x => x.Source, s =>
        {
            s.Property(p => p.Kind).HasColumnName("source_kind").HasConversion<string>().HasMaxLength(64).IsRequired();
            s.Property(p => p.Id).HasColumnName("source_id").IsRequired();
        });
        b.ComplexProperty(x => x.Target, t =>
        {
            t.Property(p => p.Kind).HasColumnName("target_kind").HasConversion<string>().HasMaxLength(64).IsRequired();
            t.Property(p => p.Id).HasColumnName("target_id").IsRequired();
        });

        b.Property(x => x.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(64).IsRequired();
        b.Property(x => x.Origin).HasColumnName("origin").HasConversion<string>().HasMaxLength(32).IsRequired();
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        b.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        b.HasIndex("tenant_id", "source_kind", "source_id").HasDatabaseName("ix_relationships_tenant_source");
        b.HasIndex("tenant_id", "target_kind", "target_id").HasDatabaseName("ix_relationships_tenant_target");
        b.HasIndex("tenant_id", "source_kind", "source_id", "type", "target_kind", "target_id")
            .IsUnique().HasDatabaseName("ux_relationships_edge");
    }
}
```

> If `ComplexProperty` columns can't be referenced by name in `HasIndex` (EF version dependent), declare the indexes inside the migration's `Up` via `migrationBuilder.CreateIndex` on the literal column names instead — the column names are fixed above.

- [ ] **Step 2: Wire the DbSet**

In `CatalogDbContext.cs`: add `public DbSet<Relationship> Relationships => Set<Relationship>();` (match the existing `Services` DbSet style) and ensure configuration is applied — if the context uses `ApplyConfigurationsFromAssembly`, nothing else is needed; otherwise add `modelBuilder.ApplyConfiguration(new EfRelationshipConfiguration());` next to the Service one.

- [ ] **Step 3: Generate the migration**

Run (from repo root; mirror how prior catalog migrations were generated):
```
cmd //c "dotnet ef migrations add AddRelationships --project src/Modules/Catalog/Kartova.Catalog.Infrastructure --startup-project src/Kartova.Migrator -o Migrations"
```
Expected: creates `Migrations/<ts>_AddRelationships.cs` with the `relationships` table + the three indexes.

- [ ] **Step 4: Hand-add RLS to the migration**

In the generated `Up(...)`, append (verbatim pattern from `AddServices`):
```csharp
migrationBuilder.Sql(@"
ALTER TABLE relationships ENABLE ROW LEVEL SECURITY;
ALTER TABLE relationships FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON relationships
  USING (tenant_id = current_setting('app.current_tenant_id')::uuid);
");
```
In `Down(...)`, before the `DropTable`, add:
```csharp
migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON relationships;");
```

- [ ] **Step 5: Build + verify the migration compiles & the model snapshot updated**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure"`
Expected: 0 warnings, 0 errors. (Behavioral round-trip — incl. the `EntityRef` complex-type caveat — is proven by the Task 6 integration tests against the real Testcontainer.)

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfRelationshipConfiguration.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations
git commit -m "feat(catalog): relationships table EF config + migration with RLS"
```

---

## Task 6: Create endpoint (lookup + handler + delegate + wiring + tests)

**Files:**
- Create: `…/Kartova.Catalog.Application/ICatalogEntityLookup.cs`
- Create: `…/Kartova.Catalog.Application/CreateRelationshipCommand.cs`
- Create: `…/Kartova.Catalog.Application/RelationshipResponseExtensions.cs`
- Modify: `…/Kartova.Catalog.Application/CatalogAuditActions.cs`
- Create: `…/Kartova.Catalog.Infrastructure/CatalogEntityLookup.cs`
- Create: `…/Kartova.Catalog.Infrastructure/CreateRelationshipHandler.cs`
- Modify: `…/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (add `CreateRelationshipAsync`)
- Modify: `…/Kartova.Catalog.Infrastructure/CatalogModule.cs` (map `POST`; register handler + lookup)
- Test: `…/Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs`
- Modify: `…/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs` (POST row)

**Interfaces:**
- Consumes: Tasks 1–5; `RegisterServiceHandler` shape (`Handle(cmd, CatalogDbContext, ITenantContext, ICurrentUser, IAuditWriter, CancellationToken)`), `AuditEntry(action, targetType, targetId, Dictionary<string,string?>)`, `RegisterServiceAsync` delegate (claim + membership-gate plumbing to mirror), `Fx.CreateAuthenticatedClientAsync`, `Fx.SeedTeamInOrganizationAsync`, `Fx.TenantIdForEmail`, `KartovaApiFixtureBase.WireJson`, the org-A/org-B/member user constants from the integration-test base class.
- Produces: `ICatalogEntityLookup.Find(EntityKind, Guid, CancellationToken) → EntityLookupResult?`; `record EntityLookupResult(Guid TeamId, string DisplayName)`; `CreateRelationshipCommand(EntityRef Source, EntityRef Target, RelationshipType Type)`; `RelationshipResponse ToResponse(this Relationship, EntityRefDto source, EntityRefDto target)`; `CreateRelationshipHandler.Handle(...)`; `CatalogEndpointDelegates.CreateRelationshipAsync`.

- [ ] **Step 1: Define the port + command + audit constants + response extension**

`ICatalogEntityLookup.cs`:
```csharp
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public interface ICatalogEntityLookup
{
    Task<EntityLookupResult?> Find(EntityKind kind, Guid id, CancellationToken ct);
}

public sealed record EntityLookupResult(Guid TeamId, string DisplayName);
```
`CreateRelationshipCommand.cs`:
```csharp
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public sealed record CreateRelationshipCommand(EntityRef Source, EntityRef Target, RelationshipType Type);
```
In `CatalogAuditActions.cs` add (mirror `ServiceRegistered`):
```csharp
public const string RelationshipCreated = "relationship.created";
public const string RelationshipRemoved = "relationship.removed";
```
and in `CatalogAuditTargetTypes`: `public const string Relationship = "Relationship";`

`RelationshipResponseExtensions.cs`:
```csharp
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public static class RelationshipResponseExtensions
{
    public static RelationshipResponse ToResponse(this Relationship r, EntityRefDto source, EntityRefDto target)
        => new(r.Id.Value, source, target, r.Type, r.Origin, r.CreatedByUserId, r.CreatedAt);
}
```

- [ ] **Step 2: Implement the lookup**

`CatalogEntityLookup.cs` — one RLS-scoped query per kind over `CatalogDbContext`:
```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

public sealed class CatalogEntityLookup(CatalogDbContext db) : ICatalogEntityLookup
{
    public async Task<EntityLookupResult?> Find(EntityKind kind, Guid id, CancellationToken ct) => kind switch
    {
        EntityKind.Application => await db.Applications
            .Where(a => a.Id.Value == id)
            .Select(a => new EntityLookupResult(a.TeamId, a.DisplayName))
            .SingleOrDefaultAsync(ct),
        EntityKind.Service => await db.Services
            .Where(s => s.Id.Value == id)
            .Select(s => new EntityLookupResult(s.TeamId, s.DisplayName))
            .SingleOrDefaultAsync(ct),
        _ => null,
    };
}
```
> If EF can't translate `a.Id.Value == id` (computed property), filter on the backing field via `EF.Property<Guid>(a, "_id") == id`, matching however `GetServiceByIdHandler` queries by id.

- [ ] **Step 3: Implement the handler** (mirrors `RegisterServiceHandler`)

`CreateRelationshipHandler.cs`:
```csharp
using Kartova.Audit;                 // AuditEntry / IAuditWriter — match RegisterServiceHandler's usings
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

public sealed class CreateRelationshipHandler(TimeProvider clock)
{
    public async Task<RelationshipResponse> Handle(
        CreateRelationshipCommand cmd,
        EntityRefDto source,            // resolved + display-enriched by the delegate
        EntityRefDto target,
        CatalogDbContext db,
        ITenantContext tenant,
        ICurrentUser user,
        IAuditWriter audit,
        CancellationToken ct)
    {
        var rel = Relationship.CreateManual(cmd.Source, cmd.Target, cmd.Type, user.UserId, tenant.Id, clock);

        db.Relationships.Add(rel);
        await db.SaveChangesAsync(ct);

        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.RelationshipCreated,
            CatalogAuditTargetTypes.Relationship,
            rel.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["sourceKind"] = cmd.Source.Kind.ToString(),
                ["sourceId"] = cmd.Source.Id.ToString(),
                ["type"] = cmd.Type.ToString(),
                ["targetKind"] = cmd.Target.Kind.ToString(),
                ["targetId"] = cmd.Target.Id.ToString(),
            }), ct);

        return rel.ToResponse(source, target);
    }
}
```
> The unique index `ux_relationships_edge` backstops a duplicate race; the delegate does an explicit pre-check (Step 4) for a clean 409 ProblemDetails. (Catching `DbUpdateException` for the race and mapping to 409 is acceptable but the pre-check is the primary path.)

- [ ] **Step 4: Add the `CreateRelationshipAsync` delegate** (in `CatalogEndpointDelegates.cs`)

Mirror `RegisterServiceAsync` for the **claim gate + team-membership gate** plumbing (same authorization helper it calls, passing the resolved **source** team id). The relationship-specific body:
```csharp
internal static async Task<IResult> CreateRelationshipAsync(
    CreateRelationshipRequest req,
    ICatalogEntityLookup lookup,
    CreateRelationshipHandler handler,
    CatalogDbContext db,
    ITenantContext tenant,
    ICurrentUser user,
    IAuditWriter audit,
    /* + the same authorization/HttpContext params RegisterServiceAsync uses for the membership gate */
    CancellationToken ct)
{
    var source = new EntityRef(req.SourceKind, req.SourceId);   // throws ArgumentException -> 400 via DomainValidationExceptionHandler
    var target = new EntityRef(req.TargetKind, req.TargetId);

    var sourceInfo = await lookup.Find(source.Kind, source.Id, ct);
    if (sourceInfo is null)
        return Results.Problem(type: ProblemTypes.InvalidSourceEntity, title: "Invalid source entity",
            detail: "The source entity does not exist in this tenant.", statusCode: StatusCodes.Status422UnprocessableEntity);

    // MEMBERSHIP GATE: mirror RegisterServiceAsync — OrgAdmin bypass OR member of sourceInfo.TeamId, else 403.
    // (Use the exact authorization call RegisterServiceAsync uses, with teamId = sourceInfo.TeamId.)

    var targetInfo = await lookup.Find(target.Kind, target.Id, ct);
    if (targetInfo is null)
        return Results.Problem(type: ProblemTypes.InvalidTargetEntity, title: "Invalid target entity",
            detail: "The target entity does not exist in this tenant.", statusCode: StatusCodes.Status422UnprocessableEntity);

    // Duplicate pre-check (same tenant scope is implied by RLS on the open connection).
    var exists = await db.Relationships.AnyAsync(r =>
        r.Source.Kind == source.Kind && r.Source.Id == source.Id &&
        r.Type == req.Type &&
        r.Target.Kind == target.Kind && r.Target.Id == target.Id, ct);
    if (exists)
        return Results.Problem(type: ProblemTypes.RelationshipAlreadyExists, title: "Relationship already exists",
            detail: "An identical relationship already exists.", statusCode: StatusCodes.Status409Conflict);

    var srcDto = new EntityRefDto(source.Kind, source.Id, sourceInfo.DisplayName);
    var tgtDto = new EntityRefDto(target.Kind, target.Id, targetInfo.DisplayName);
    var cmd = new CreateRelationshipCommand(source, target, req.Type);

    var response = await handler.Handle(cmd, srcDto, tgtDto, db, tenant, user, audit, ct);
    return Results.Created($"/api/v1/catalog/relationships/{response.Id}", response);
}
```
> Add `InvalidSourceEntity`, `InvalidTargetEntity`, `RelationshipAlreadyExists` to the project's `ProblemTypes` constants (mirror `InvalidHealthFilter`). If `r.Source.Kind == ...` can't be translated through the complex property, query the literal columns via `EF.Property<string>(r, "source_kind")` / map `EntityKind` to its string name — the unique index still backstops the race.

- [ ] **Step 5: Map the route + register services** (in `CatalogModule.cs`)

Add to the tenant-scoped catalog route group (mirror the `services` `MapPost`):
```csharp
group.MapPost("/relationships", CatalogEndpointDelegates.CreateRelationshipAsync)
     /* .RequirePermission(KartovaPermissions.CatalogRelationshipsWrite) — use the exact helper the services POST uses */;
```
Register in DI (mirror the service handler registrations): `services.AddScoped<CreateRelationshipHandler>();` and `services.AddScoped<ICatalogEntityLookup, CatalogEntityLookup>();`.

- [ ] **Step 6: Write the failing integration tests**

`CreateRelationshipTests.cs` — inherit the same base class `RegisterServiceTests` uses (provides `Fx` + user constants). Source/target entities are created through the existing catalog POST endpoints as setup.

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class CreateRelationshipTests : /* same base as RegisterServiceTests */
{
    private static object Rel(EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid) => new
    { sourceKind = sk, sourceId = sid, type = t, targetKind = tk, targetId = tid };

    // Creates a service via the API and returns its id. teamId must be one the caller may register under.
    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        { displayName = name, description = "x", teamId, endpoints = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    [TestMethod]
    public async Task POST_dependsOn_between_two_services_returns_201_and_manual_origin()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team");
        var a = await SeedServiceAsync(client, teamId, "svc-a");
        var b = await SeedServiceAsync(client, teamId, "svc-b");

        var resp = await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(RelationshipOrigin.Manual, body!.Origin);
        Assert.AreEqual(a, body.Source.Id);
        Assert.AreEqual("svc-b", body.Target.DisplayName);
    }

    [TestMethod]
    public async Task POST_self_reference_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team");
        var a = await SeedServiceAsync(client, teamId, "svc-self");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, a));
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_non_creatable_type_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team");
        var a = await SeedServiceAsync(client, teamId, "svc-x");
        var b = await SeedServiceAsync(client, teamId, "svc-y");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.PublishesTo, EntityKind.Service, b));
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_unknown_target_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team");
        var a = await SeedServiceAsync(client, teamId, "svc-known");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_duplicate_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team");
        var a = await SeedServiceAsync(client, teamId, "svc-d1");
        var b = await SeedServiceAsync(client, teamId, "svc-d2");
        var payload = Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b);
        Assert.AreEqual(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/catalog/relationships", payload)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/catalog/relationships", payload)).StatusCode);
    }

    [TestMethod]
    public async Task POST_without_token_returns_401()
    {
        var client = Fx.CreateAnonymousClient();
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, Guid.NewGuid(), RelationshipType.DependsOn, EntityKind.Service, Guid.NewGuid()));
        Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_by_member_not_in_source_team_returns_403()
    {
        // OrgAdmin seeds team + source/target; a Member NOT in that team attempts to declare the edge.
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Restricted");
        var a = await SeedServiceAsync(admin, teamId, "svc-r1");
        var b = await SeedServiceAsync(admin, teamId, "svc-r2");
        var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
        var resp = await member.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));
        Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_cross_tenant_target_returns_422()
    {
        // Target lives in Org B; Org A caller cannot see it (RLS) → 422.
        var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "B Team");
        var bSvc = await SeedServiceAsync(orgB, teamB, "b-svc");

        var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamA = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "A Team");
        var aSvc = await SeedServiceAsync(orgA, teamA, "a-svc");

        var resp = await orgA.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, aSvc, RelationshipType.DependsOn, EntityKind.Service, bSvc));
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_sets_CreatedByUserId_to_caller_sub()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team");
        var a = await SeedServiceAsync(client, teamId, "svc-c1");
        var b = await SeedServiceAsync(client, teamId, "svc-c2");
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/relationships",
            Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(await Fx.GetSubClaimAsync(OrgAUser), body!.CreatedByUserId);
    }
}
```
> Confirm the exact base-class name and user constants (`OrgAUser`, `OrgBUser`, member email, `GetSubClaimAsync`) against `RegisterServiceTests.cs` and adjust. If `part-of` (Service→Application) deserves explicit coverage, add a happy-path test that seeds an Application target.

- [ ] **Step 7: Add the permission-matrix row**

In `CatalogPermissionMatrixTests.cs`, add a row asserting `POST /api/v1/catalog/relationships` requires `catalog.relationships.write` (mirror the service POST row).

- [ ] **Step 8: Run to verify failure, then implement to green**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter CreateRelationshipTests"`
Expected first run: FAIL (route 404 / types missing). After Steps 1–7 implemented: PASS. Also run `CatalogPermissionMatrixTests` green.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application src/Modules/Catalog/Kartova.Catalog.Infrastructure src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs
git commit -m "feat(catalog): POST /catalog/relationships with source-side auth, lookup, audit"
```

---

## Task 7: List-by-entity endpoint

**Files:**
- Create: `…/Kartova.Catalog.Application/RelationshipDirection.cs`
- Create: `…/Kartova.Catalog.Application/ListRelationshipsForEntityQuery.cs`
- Create: `…/Kartova.Catalog.Infrastructure/ListRelationshipsForEntityHandler.cs`
- Create: `…/Kartova.Catalog.Infrastructure/RelationshipSortSpecs.cs`
- Modify: `CatalogEndpointDelegates.cs` (add `ListRelationshipsAsync`), `CatalogModule.cs` (map `GET`), `CatalogPermissionMatrixTests.cs` (GET row)
- Test: `…/Kartova.Catalog.IntegrationTests/ListRelationshipsTests.cs`

**Interfaces:**
- Consumes: Task 6 types; `CursorListBinding.Bind<TSortField>(sortBy, sortOrder, limit, AllowedFieldNames) → (sortBy?, sortOrder?, effectiveLimit)`; `CursorPage<T>`; `ICatalogEntityLookup` (enrichment); the `ListServicesHandler` / `ServiceSortSpecs` cursor-keyset pattern to mirror.
- Produces: `RelationshipDirection {Outgoing, Incoming, All}`; `ListRelationshipsForEntityQuery(EntityRef Entity, RelationshipDirection Direction, RelationshipSortField SortBy, SortOrder SortOrder, string? Cursor, int Limit)`; `ListRelationshipsForEntityHandler.Handle(query, db, lookup, ct) → CursorPage<RelationshipResponse>`; `RelationshipSortSpecs.AllowedFieldNames`.

- [ ] **Step 1: Define direction enum + query record + sort specs**

`RelationshipDirection.cs`:
```csharp
namespace Kartova.Catalog.Application;

public enum RelationshipDirection { Outgoing, Incoming, All }
```
`ListRelationshipsForEntityQuery.cs`:
```csharp
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.SharedKernel; // SortOrder — match ListServicesQuery's using

namespace Kartova.Catalog.Application;

public sealed record ListRelationshipsForEntityQuery(
    EntityRef Entity, RelationshipDirection Direction,
    RelationshipSortField SortBy, SortOrder SortOrder, string? Cursor, int Limit);
```
`RelationshipSortSpecs.cs` — mirror `ServiceSortSpecs` (map `RelationshipSortField` → column + expose `AllowedFieldNames`). Columns: `CreatedAt → created_at`, `Type → type`. The keyset tiebreaker is `id`.

- [ ] **Step 2: Implement the list handler** (mirror `ListServicesHandler`'s keyset/cursor mechanics)

Behavior: start from `db.Relationships`; apply the **direction predicate**:
- `Outgoing`: `Source.Kind == entity.Kind && Source.Id == entity.Id`
- `Incoming`: `Target.Kind == entity.Kind && Target.Id == entity.Id`
- `All`: either of the above (OR)

Then keyset-paginate on `(sort column, id)` exactly as `ListServicesHandler` does, build `CursorPage<RelationshipResponse>`, and enrich each row's **other** endpoint display name via `ICatalogEntityLookup` (batch the distinct refs to avoid N+1). Default sort applied in the delegate (Step 3), not here.

> Reuse the same cursor codec / keyset helper `ListServicesHandler` uses — only the entity type, sort columns, and the direction predicate differ. If translating the complex-property columns in the predicate is awkward, filter on `EF.Property<string>(r, "source_kind")` / `EF.Property<Guid>(r, "source_id")` etc.

- [ ] **Step 3: Add the `ListRelationshipsAsync` delegate** (mirror `ListServicesAsync`)

```csharp
internal static async Task<IResult> ListRelationshipsAsync(
    [FromQuery] string entityKind,
    [FromQuery] Guid entityId,
    [FromQuery] string? direction,
    [FromQuery] string? sortBy,
    [FromQuery] string? sortOrder,
    [FromQuery] string? cursor,
    [FromQuery] string? limit,
    ListRelationshipsForEntityHandler handler,
    ICatalogEntityLookup lookup,
    CatalogDbContext db,
    CancellationToken ct)
{
    if (!Enum.TryParse<EntityKind>(entityKind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) || entityId == Guid.Empty)
        return Results.Problem(type: ProblemTypes.MalformedRequest, title: "Invalid entity reference",
            detail: "entityKind and a non-empty entityId are required.", statusCode: StatusCodes.Status400BadRequest);

    var dir = RelationshipDirection.All;
    if (!string.IsNullOrWhiteSpace(direction)
        && (!Enum.TryParse(direction, ignoreCase: true, out dir) || !Enum.IsDefined(dir)))
        return Results.Problem(type: ProblemTypes.MalformedRequest, title: "Invalid direction",
            detail: "direction must be outgoing, incoming, or all.", statusCode: StatusCodes.Status400BadRequest);

    var (parsedSortBy, parsedSortOrder, effectiveLimit) =
        CursorListBinding.Bind<RelationshipSortField>(sortBy, sortOrder, limit, RelationshipSortSpecs.AllowedFieldNames);

    var query = new ListRelationshipsForEntityQuery(
        new EntityRef(kind, entityId), dir,
        SortBy: parsedSortBy ?? RelationshipSortField.CreatedAt,
        SortOrder: parsedSortOrder ?? SortOrder.Desc,   // default: newest first (spec §7 — relationship rows have no displayName)
        Cursor: cursor, Limit: effectiveLimit);

    var page = await handler.Handle(query, db, lookup, ct);
    return Results.Ok(page);
}
```

- [ ] **Step 4: Map the route + register handler** (`CatalogModule.cs`)

```csharp
group.MapGet("/relationships", CatalogEndpointDelegates.ListRelationshipsAsync)
     /* .RequirePermission(KartovaPermissions.CatalogRead) — match the services GET */;
services.AddScoped<ListRelationshipsForEntityHandler>();
```

- [ ] **Step 5: Write the failing integration tests**

`ListRelationshipsTests.cs` (same base class; reuse the `SeedServiceAsync` helper pattern from Task 6):
```csharp
[TestMethod]
public async Task GET_incoming_returns_consumers_of_an_entity()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team");
    var a = await SeedServiceAsync(client, teamId, "svc-a");   // depends on b
    var b = await SeedServiceAsync(client, teamId, "svc-b");
    await client.PostAsJsonAsync("/api/v1/catalog/relationships",
        Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));

    // b's INCOMING depends-on == its consumers (this is the deferred "consumers" thread).
    var resp = await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={b}&direction=incoming");
    Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
    var page = await resp.Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
    Assert.AreEqual(1, page!.Items.Count);
    Assert.AreEqual(a, page.Items[0].Source.Id);
}

[TestMethod]
public async Task GET_outgoing_lists_only_edges_sourced_at_entity()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team");
    var a = await SeedServiceAsync(client, teamId, "svc-a2");
    var b = await SeedServiceAsync(client, teamId, "svc-b2");
    await client.PostAsJsonAsync("/api/v1/catalog/relationships",
        Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b));

    var outA = await (await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={a}&direction=outgoing"))
        .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
    var outB = await (await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={b}&direction=outgoing"))
        .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
    Assert.AreEqual(1, outA!.Items.Count);
    Assert.AreEqual(0, outB!.Items.Count);
}

[TestMethod]
public async Task GET_is_tenant_isolated()
{
    // Org B's edges never appear for Org A. Build an edge in B, query the same ids as A → empty.
    var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
    var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "B Team");
    var b1 = await SeedServiceAsync(orgB, teamB, "b1");
    var b2 = await SeedServiceAsync(orgB, teamB, "b2");
    await orgB.PostAsJsonAsync("/api/v1/catalog/relationships",
        Rel(EntityKind.Service, b1, RelationshipType.DependsOn, EntityKind.Service, b2));

    var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var page = await (await orgA.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={b1}&direction=all"))
        .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
    Assert.AreEqual(0, page!.Items.Count);
}
```
> Add a pagination/`sortBy=Type` test mirroring `ListServicesPaginationTests` (seed several edges, page forward with `limit`, assert cursor round-trips). Confirm the `CursorPage<T>` member name (`Items`) against `ListServicesPaginationTests.cs`.

- [ ] **Step 6: Add the permission-matrix row** (`GET /catalog/relationships → catalog.read`).

- [ ] **Step 7: Run failing → implement → green**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter ListRelationshipsTests"`
Expected: FAIL first, PASS after implementation. `CatalogPermissionMatrixTests` green.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application src/Modules/Catalog/Kartova.Catalog.Infrastructure src/Modules/Catalog/Kartova.Catalog.IntegrationTests/ListRelationshipsTests.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs
git commit -m "feat(catalog): GET /catalog/relationships by entity with direction + cursor paging"
```

---

## Task 8: Delete endpoint

**Files:**
- Create: `…/Kartova.Catalog.Infrastructure/DeleteRelationshipHandler.cs`
- Modify: `CatalogEndpointDelegates.cs` (add `DeleteRelationshipAsync`), `CatalogModule.cs` (map `DELETE`), `CatalogPermissionMatrixTests.cs` (DELETE row)
- Test: `…/Kartova.Catalog.IntegrationTests/DeleteRelationshipTests.cs`

**Interfaces:**
- Consumes: Tasks 6–7; `ICatalogEntityLookup` (resolve source team for the gate); audit constants.
- Produces: `DeleteRelationshipHandler.Handle(Guid id, CatalogDbContext, IAuditWriter, CancellationToken) → bool` (false = not found); `CatalogEndpointDelegates.DeleteRelationshipAsync`.

- [ ] **Step 1: Implement the handler**

```csharp
using Kartova.Audit;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

public sealed class DeleteRelationshipHandler
{
    public async Task<bool> Handle(Kartova.Catalog.Domain.Relationship rel, CatalogDbContext db, IAuditWriter audit, CancellationToken ct)
    {
        db.Relationships.Remove(rel);
        await db.SaveChangesAsync(ct);
        await audit.AppendAsync(new AuditEntry(
            CatalogAuditActions.RelationshipRemoved,
            CatalogAuditTargetTypes.Relationship,
            rel.Id.Value.ToString(),
            new Dictionary<string, string?>
            {
                ["sourceKind"] = rel.Source.Kind.ToString(), ["sourceId"] = rel.Source.Id.ToString(),
                ["type"] = rel.Type.ToString(),
                ["targetKind"] = rel.Target.Kind.ToString(), ["targetId"] = rel.Target.Id.ToString(),
            }), ct);
        return true;
    }
}
```

- [ ] **Step 2: Add the `DeleteRelationshipAsync` delegate**

Load the relationship by id (RLS scopes to tenant); 404 if absent. Resolve `rel.Source` team via `ICatalogEntityLookup`; apply the **same membership gate** as create (OrgAdmin or member of source team), else 403. Then call the handler; return `Results.NoContent()`.
```csharp
internal static async Task<IResult> DeleteRelationshipAsync(
    Guid id,
    ICatalogEntityLookup lookup,
    DeleteRelationshipHandler handler,
    CatalogDbContext db,
    IAuditWriter audit,
    /* + the same authorization params create uses */
    CancellationToken ct)
{
    var rel = await db.Relationships.FirstOrDefaultAsync(r => EF.Property<Guid>(r, "_id") == id, ct);
    if (rel is null) return Results.Problem(type: ProblemTypes.ResourceNotFound, title: "Not found",
        statusCode: StatusCodes.Status404NotFound);

    var sourceInfo = await lookup.Find(rel.Source.Kind, rel.Source.Id, ct);
    // If the source entity was deleted, fall back to OrgAdmin-only. MEMBERSHIP GATE: OrgAdmin OR member of sourceInfo?.TeamId → else 403.

    await handler.Handle(rel, db, audit, ct);
    return Results.NoContent();
}
```

- [ ] **Step 3: Map the route + register handler** (`CatalogModule.cs`)

```csharp
group.MapDelete("/relationships/{id:guid}", CatalogEndpointDelegates.DeleteRelationshipAsync)
     /* .RequirePermission(KartovaPermissions.CatalogRelationshipsWrite) */;
services.AddScoped<DeleteRelationshipHandler>();
```

- [ ] **Step 4: Write the failing integration tests**

`DeleteRelationshipTests.cs`:
```csharp
[TestMethod]
public async Task DELETE_removes_relationship_returns_204()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Team");
    var a = await SeedServiceAsync(client, teamId, "svc-da");
    var b = await SeedServiceAsync(client, teamId, "svc-db");
    var created = await (await client.PostAsJsonAsync("/api/v1/catalog/relationships",
        Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b)))
        .Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);

    var del = await client.DeleteAsync($"/api/v1/catalog/relationships/{created!.Id}");
    Assert.AreEqual(HttpStatusCode.NoContent, del.StatusCode);

    var page = await (await client.GetAsync($"/api/v1/catalog/relationships?entityKind=Service&entityId={a}&direction=outgoing"))
        .Content.ReadFromJsonAsync<CursorPage<RelationshipResponse>>(KartovaApiFixtureBase.WireJson);
    Assert.AreEqual(0, page!.Items.Count);
}

[TestMethod]
public async Task DELETE_nonexistent_returns_404()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var resp = await client.DeleteAsync($"/api/v1/catalog/relationships/{Guid.NewGuid()}");
    Assert.AreEqual(HttpStatusCode.NotFound, resp.StatusCode);
}

[TestMethod]
public async Task DELETE_by_member_not_in_source_team_returns_403()
{
    var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Restricted Del");
    var a = await SeedServiceAsync(admin, teamId, "svc-rd1");
    var b = await SeedServiceAsync(admin, teamId, "svc-rd2");
    var created = await (await admin.PostAsJsonAsync("/api/v1/catalog/relationships",
        Rel(EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, b)))
        .Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);

    var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
    var resp = await member.DeleteAsync($"/api/v1/catalog/relationships/{created!.Id}");
    Assert.AreEqual(HttpStatusCode.Forbidden, resp.StatusCode);
}
```
> `SeedServiceAsync` / `Rel` helpers: lift into a shared base or test-helper class so all three integration files reuse them (DRY).

- [ ] **Step 5: Permission-matrix row** (`DELETE /catalog/relationships/{id} → catalog.relationships.write`).

- [ ] **Step 6: Run failing → implement → green**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter DeleteRelationshipTests"`
Expected: FAIL first, PASS after.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure src/Modules/Catalog/Kartova.Catalog.IntegrationTests/DeleteRelationshipTests.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CatalogPermissionMatrixTests.cs
git commit -m "feat(catalog): DELETE /catalog/relationships/{id} with source-side auth + audit"
```

---

## Task 9: Full-suite verification + DoD gates

**Files:** none (verification only).

- [ ] **Step 1: Full solution build, warnings-as-errors**

Run: `cmd //c "dotnet build Kartova.slnx -warnaserror"`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite**

Run: `cmd //c "dotnet test Kartova.slnx"`
Expected: all green (architecture + unit + integration). If an integration assembly trips the known Docker named-pipe flake, re-run that assembly in isolation before treating it as red.

- [ ] **Step 3: Architecture tests explicitly** (pagination convention + module rules + contracts coverage)

Run: `cmd //c "dotnet test src/.../Kartova.ArchitectureTests"` (the arch test project)
Expected: green — `PaginationConventionRules` accepts the new `CursorPage` list endpoint; `ContractsCoverageRules` sees `[ExcludeFromCodeCoverage]` on the new DTOs.

- [ ] **Step 4: Mutation gate (blocking — Domain + Application logic changed)**

Run `/misc:mutation-sentinel` on the changed files, then `/misc:test-generator` for survivors. Target ≥80% per `stryker-config.json`; document any survivors. Focus: `Relationship.CreateManual`, `RelationshipTypeRules`, the direction predicate, and the duplicate/422/403 branches in the delegates.

- [ ] **Step 5: `/simplify` the branch diff**

Address should-fix items (e.g. extract the shared `SeedServiceAsync`/`Rel` test helpers; fold the source/target `Find` + `EntityRefDto` build into one helper) or skip with a noted reason.

- [ ] **Step 6: Pre-push CI mirror**

Run: `bash scripts/ci-local.sh backend` (Release build + test + arch). Expected: green.

- [ ] **Step 7: Reviews + final re-verify**

Run `/superpowers:requesting-code-review`, `/pr-review-toolkit:review-pr`, `/deep-review` against the full branch diff (spec + plan as context). Address Blocking + Should-fix. Then re-run Steps 1–2 to confirm still green (gates 5–9 may have changed code).

- [ ] **Step 8: Update the checklist**

In `docs/product/CHECKLIST.md`, annotate E-04.F-01.S-01/S-02 (backend) + the F-02.S-02 dependency as backend-complete (Slice 1a), UI pending Slice 1b. Commit.

---

## Self-Review

**1. Spec coverage** (against `2026-06-24-catalog-relationships-design.md`):
- §3 decisions #1–#15 → Tasks: open discriminator (#2) T5; creatable types (#4) + matrix (#5) T1; origin Manual (#6) T2; source-side auth (#7) T6/T8; lookup (#8) T6; provenance/no-FK (#9) T2/T5; no concurrency token (#10) T5; duplicate 409 (#11) T6; self-ref/cycles (#12) T1/T2; permission (#13) T4; audit (#14) T6/T8; default sort (#15) T7. ✓
- §4 endpoints → T6 (POST), T7 (GET), T8 (DELETE). ✓
- §6 error table → covered by T6/T7 negative tests + ProblemTypes additions. ✓
- §7 list surface → T7 sort allowlist + default; registry row already committed with the spec. ✓
- §8 gate-5 artifacts (domain unit, create/list/delete integration, permission matrix, role map) → each is a named test deliverable (T1/T2, T6, T7, T8, T4). ✓

**2. Placeholder scan:** No "TBD/handle edge cases/write tests for the above". The three "mirror RegisterServiceAsync / ListServicesHandler" notes point at named sibling symbols for project-specific authz/cursor plumbing I could not extract verbatim (compressed), with the relationship-specific logic written out in full — not placeholders. ProblemTypes constants (`InvalidSourceEntity`, etc.) are named and located.

**3. Type consistency:** `CreateManual(source, target, type, createdByUserId, tenantId, clock)` consistent T2↔T6. `EntityLookupResult(TeamId, DisplayName)` consistent T6↔T7↔T8. `ToResponse(this Relationship, EntityRefDto, EntityRefDto)` consistent T6↔T7. `RelationshipDirection {Outgoing,Incoming,All}` and `RelationshipSortField {CreatedAt,Type}` consistent T3/T7. Audit `AuditEntry(action, targetType, idString, dict)` matches the verbatim `RegisterServiceHandler` shape.

**Open items the implementer must confirm against the codebase (named, not guessed):** exact `TenantId`/`SortOrder`/`CursorPage`/`AuditEntry` namespaces; the membership-gate authorization call inside `RegisterServiceAsync`; the `CursorPage<T>` items property name; the integration-test base-class name + user constants; whether `ComplexProperty` columns are filterable/indexable by name (fallback noted).

**No blocking issues found.**
