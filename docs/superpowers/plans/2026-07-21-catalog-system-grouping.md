# System Grouping Entity + PartOf Assignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `System` as a first-class, team-stewarded catalog entity (register/get/list) and let `{Application, Service}` components be assigned to it via a reintroduced `PartOf` relationship edge.

**Architecture:** Replicates the `Api` entity template across Domain → Contracts → Application → Infrastructure → Persistence, plus reintroducing `PartOf` into the relationship vocabulary. No new cross-cutting infra — reuses `ITenantScope`, `CursorPage<T>`, the existing `POST /relationships` edge endpoint, and `IAuditWriter`. Backend-first; no UI.

**Tech Stack:** .NET 10 · ASP.NET Core Minimal APIs · Wolverine (direct-dispatch, ADR-0093) · EF Core + PostgreSQL 18 (RLS) · MSTest v4 + native asserts + NSubstitute · Testcontainers.

**Spec:** `docs/superpowers/specs/2026-07-21-catalog-system-grouping-design.md`

## Global Constraints

- **Tenant scope:** register `CatalogDbContext` via `AddModuleDbContext<T>` only (ADR-0090); handlers never touch `ITenantScope`. Never raw `AddDbContext` for the app path.
- **RLS:** every catalog table gets `ENABLE` + `FORCE ROW LEVEL SECURITY` + `CREATE POLICY tenant_isolation USING (tenant_id = current_setting('app.current_tenant_id')::uuid)` in its migration.
- **Handlers:** direct-dispatch (ADR-0093), constructor-injected deps, `AddScoped`.
- **List endpoints:** expose `sortBy` / `sortOrder` / `cursor` / `limit`, return `CursorPage<T>` (ADR-0095); default sort `displayName asc`.
- **Wire enums:** camelCase (ADR-0109).
- **Build:** `TreatWarningsAsErrors=true` — 0 warnings.
- **Coverage:** all new `*Request`/`*Response`/DTO types carry `[ExcludeFromCodeCoverage]` (enforced by `ContractsCoverageRules`).
- **Permission 5-sync:** any new `KartovaPermission` touches all 5 files (C# const+`All`, role map, TS snapshot, TS const, `usePermissions.test.tsx`); `KartovaPermissionsRules` arch test guards C#↔snapshot.
- **Line endings:** LF (repo `.gitattributes`).
- **Steward semantic:** a System's `TeamId` is the **steward** team (curates the grouping); member components keep their own team ownership. Cross-team membership is allowed via ADR-0108 either-team edge authz.

---

## Impact Analysis (codelens)

**Method:** roslyn-codelens attempted, but returned an **incomplete/stale index this session** — `find_callers` on `RelationshipTypeRules.IsAllowedPair`/`IsCreatable`/`ICatalogEntityLookup.Find` returned empty (all are called), and `find_references RelationshipType`/`EntityKind` returned only test files / empty (missing every production consumer). Blast radius below is therefore grounded in **grep + a switch-exhaustiveness read pass** (file:line-anchored). **Re-run `find_callers`/`find_references` after `rebuild_solution` at execution time before editing the enums; add a task for any consumer not in this table.**

| Changed symbol | Change | Tool run | Refs | Notable sites | Covered |
|---|---|---|---|---|---|
| `EntityKind` (enum) | add `System` member (behavior: new value) | grep — codelens stale | ~19 files switch/use | `CatalogEntityLookup.cs:9` (`_ => null` default — safe), `RelationshipTypeRules.cs:11` (`_ => false` default — safe), `CatalogEndpointDelegates` parse sites (`Enum.TryParse`+`IsDefined` — safe) | Task 1, 9 |
| `RelationshipType` (enum) | re-add `PartOf` (behavior: value auto-enters query-filter allow-list) | grep — codelens stale | ~17 files | `EfRelationshipConfiguration.cs:18/56` (dynamic `Enum.GetValues` → **auto-includes PartOf** = option A), `GraphTraversalHandler.cs:109` + `ListRelationshipsForEntityHandler.cs:24` (no type filter → PartOf edges now visible, intended), positive allow-lists in `GetApiSurface`/`GetImpactAnalysis`/`DerivedEdgeLoader` (PartOf can't match — safe) | Task 2, 3 |
| `RelationshipTypeRules.IsCreatable` | add `PartOf` | grep | called from `Relationship.cs:31` | enables PartOf creation | Task 2 |
| `RelationshipTypeRules.IsAllowedPair` | add `PartOf ⇒ {App,Service}→System` arm | grep | called from `Relationship.cs` | gates PartOf endpoints | Task 2 |
| `CatalogEntityLookup.Find` | add `EntityKind.System` arm | grep | `CreateRelationshipAsync`, graph/impact/list enrichment | without it: System unresolvable (empty names / 422), not a crash | Task 9 |

**Blast-radius notes:**
- **Non-obvious #1 — query-filter auto-inclusion (`EfRelationshipConfiguration.cs:18`).** The filter is `Enum.GetValues<RelationshipType>()`, so re-adding `PartOf` makes it visible everywhere with **no code change** (this is exactly option A). Only the stale comment (names PartOf as drift-to-exclude) needs updating. Task 3.
- **Non-obvious #2 — `RelationshipTypeHardeningTests` breaks.** It inserts a `type='PartOf'` drift row and asserts it is **excluded** (`:30`, `:59` `Count==1`). Once `PartOf` is valid, that row materializes and the test fails. Must swap the drift example for a still-unknown string. Task 3.
- Confirmed **safe no-ops** (positive allow-lists / safe defaults, verified by read): `GetApiSurfaceHandler`, `GetImpactAnalysisHandler`, `GetDerivedDependenciesHandler`, `DerivedEdgeLoader`, `RelationshipSortSpecs`, all `CatalogEndpointDelegates` enum-parse sites.

**Coverage check:** every consumer above is either handled by a task (1, 2, 3, 9) or verified a safe no-op. Gap risk: the codelens stale index — mitigated by the re-run instruction above (execution-time gate).

---

## Task 1: Domain — `EntityKind.System` + `SystemId` + `System` aggregate

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/EntityKind.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/SystemId.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Domain/System.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/SystemTests.cs`

**Interfaces:**
- Produces: `System.Create(string displayName, string? description, Guid teamId, string createdByUserId, TimeProvider timeProvider)` → `System`; overload with explicit `DateTimeOffset createdAt`. `SystemId(Guid Value)`. Properties: `Id: SystemId`, `DisplayName`, `Description`, `TeamId: Guid`, `CreatedByUserId`, `CreatedAt`, `TenantId`, `Xmin`.
- Consumes: `ITenantOwned`, `ITeamScopedResource` (SharedKernel); pattern template `Api.cs`.

- [ ] **Step 1: Write failing tests** — `SystemTests.cs`:

```csharp
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public class SystemTests
{
    private static System Create(string name = "Payments Platform", string? desc = "desc") =>
        System.Create(name, desc, Guid.NewGuid(), "user-1", TimeProvider.System);

    [TestMethod]
    public void Create_sets_fields_and_generates_id()
    {
        var s = Create();
        Assert.AreNotEqual(Guid.Empty, s.Id.Value);
        Assert.AreEqual("Payments Platform", s.DisplayName);
        Assert.AreEqual("desc", s.Description);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    public void Create_rejects_empty_display_name(string name) =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(name));

    [TestMethod]
    public void Create_rejects_display_name_over_128() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(new string('x', 129)));

    [TestMethod]
    public void Create_rejects_description_over_4096() =>
        Assert.ThrowsExactly<ArgumentException>(() => Create(desc: new string('x', 4097)));

    [TestMethod]
    public void Create_rejects_empty_team() =>
        Assert.ThrowsExactly<ArgumentException>(() =>
            System.Create("ok", null, Guid.Empty, "user-1", TimeProvider.System));

    [TestMethod]
    public void Create_allows_null_description() => Assert.IsNull(Create(desc: null).Description);
}
```

- [ ] **Step 2: Run — verify fail** — `dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter SystemTests` → FAIL (System not defined).
- [ ] **Step 3: Implement** — `EntityKind.cs` becomes `public enum EntityKind { Application, Service, Api, System }` (append `System` last — smallint stability). `SystemId.cs`: `public readonly record struct SystemId(Guid Value) { public static SystemId New() => new(Guid.NewGuid()); }`. `System.cs`: mirror `Api.cs` structure exactly — `sealed class System : ITenantOwned, ITeamScopedResource`, shadow `_id` Guid field + `SystemId Id => new(_id)`, private EF ctor + private all-args ctor, `Create` factory (TimeProvider overload → explicit-`createdAt` overload), `Xmin` `uint` token, private `Validate*` methods (name non-empty ≤128, description ≤4096, non-empty `teamId`/`createdByUserId`). Drop Api-only fields (Style/Version/SpecUrl); keep `TeamId` (Guid), `Description` (nullable).
- [ ] **Step 4: Run — verify pass** — same filter → PASS.
- [ ] **Step 5: Commit** — `git add` the four files; `git commit -m "feat(catalog): System aggregate + EntityKind.System (E-03.F-03.S-01)"`.

---

## Task 2: Domain — `RelationshipType.PartOf` + `RelationshipTypeRules` arm

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/RelationshipType.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/RelationshipTypeRules.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/RelationshipTests.cs` (add cases)

**Interfaces:**
- Produces: `RelationshipType.PartOf`; `IsCreatable(PartOf) == true`; `IsAllowedPair(PartOf, {Application|Service}, System) == true`, all other `PartOf` pairs `false`.

- [ ] **Step 1: Write failing tests** — add to `RelationshipTests.cs`:

```csharp
[TestMethod]
public void PartOf_is_creatable() =>
    Assert.IsTrue(RelationshipTypeRules.IsCreatable(RelationshipType.PartOf));

[TestMethod]
[DataRow(EntityKind.Application)]
[DataRow(EntityKind.Service)]
public void PartOf_allows_component_to_system(EntityKind source) =>
    Assert.IsTrue(RelationshipTypeRules.IsAllowedPair(RelationshipType.PartOf, source, EntityKind.System));

[TestMethod]
[DataRow(EntityKind.Api, EntityKind.System)]      // Api not a component
[DataRow(EntityKind.System, EntityKind.System)]   // no nested systems
[DataRow(EntityKind.Service, EntityKind.Application)] // wrong target
public void PartOf_rejects_disallowed_pairs(EntityKind source, EntityKind target) =>
    Assert.IsFalse(RelationshipTypeRules.IsAllowedPair(RelationshipType.PartOf, source, target));
```

- [ ] **Step 2: Run — verify fail** — `--filter RelationshipTests` → FAIL (PartOf not defined).
- [ ] **Step 3: Implement** — `RelationshipType.cs`: append `PartOf,` (persisted as string — order irrelevant). `RelationshipTypeRules.cs`: add `or RelationshipType.PartOf` to `IsCreatable`; add arm to `IsAllowedPair`:

```csharp
RelationshipType.PartOf =>
    source is EntityKind.Application or EntityKind.Service && target == EntityKind.System,
```

- [ ] **Step 4: Run — verify pass** — `--filter RelationshipTests` → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(catalog): reintroduce PartOf edge ({App,Service}->System) (E-03.F-03.S-01)"`.

---

## Task 3: Impact remediation — query-filter comment + hardening test

Consequence of Task 2 (see Impact Analysis). `EfRelationshipConfiguration` needs no behavior change (dynamic allow-list already includes PartOf = option A), but the stale comment and the PartOf-as-drift test must be fixed.

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfRelationshipConfiguration.cs:12-18` (comment only)
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/RelationshipTypeHardeningTests.cs:20,30`

- [ ] **Step 1: Update the hardening test first (it will now fail)** — in `RelationshipTypeHardeningTests.cs`, change the drift value from `'PartOf'` (now a valid, visible type) to a still-unknown string so the test keeps asserting drift-exclusion:
  - line 30: `... 'LegacyBogusType', 'Manual', ...`
  - line 20 comment: `simulating drifted/legacy data (a value not in the RelationshipType enum).`
- [ ] **Step 2: Run — verify it still passes** — `dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter RelationshipTypeHardeningTests` → PASS (drift row with unknown type excluded; the change proves PartOf is no longer treated as drift).
- [ ] **Step 3: Update the config comment** — `EfRelationshipConfiguration.cs:12-18`: reword to state the filter guards genuinely-unknown drift strings; `PartOf` is now a valid, visible relationship type (System grouping, E-03.F-03). Keep `KnownRelationshipTypes = Enum.GetValues<RelationshipType>()` unchanged.
- [ ] **Step 4: Commit** — `git commit -m "fix(catalog): PartOf is a valid type again — update drift-hardening comment + test (E-03.F-03.S-01)"`.

---

## Task 4: Persistence — `EfSystemConfiguration` + `DbSet` + `AddSystems` migration

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/EfSystemConfiguration.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogDbContext.cs` (`DbSet<System> Systems` + `ApplyConfiguration`)
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/Migrations/<ts>_AddSystems.cs` (+ regenerated `CatalogDbContextModelSnapshot.cs`)
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/EfSystemConfigurationTests.cs`

**Interfaces:**
- Produces: table `catalog_systems`; `CatalogDbContext.Systems`.

- [ ] **Step 1: Write failing config test** — `EfSystemConfigurationTests.cs`, mirror `EfApiConfigurationTests.cs`: assert `ToTable("catalog_systems")`, `_id` PK mapped to `id`, `Ignore(Id)`, `(TenantId, DisplayName)` index, `TeamId` index, `Xmin` as `xid` rowversion.
- [ ] **Step 2: Run — verify fail** — `--filter EfSystemConfigurationTests` → FAIL.
- [ ] **Step 3: Implement config** — `EfSystemConfiguration.cs` copies `EfApiConfiguration.cs` structure, substituting `System`/`catalog_systems`, dropping Style/Version/SpecUrl mappings, keeping `DisplayName` (128), `Description` (4096, nullable), `TeamId` + `idx_catalog_systems_team`, `TenantId` conversion + `ix_catalog_systems_tenant_id`, composite `(TenantId, DisplayName)`, `Xmin` token. Add `DbSet<System> Systems` + `modelBuilder.ApplyConfiguration(new EfSystemConfiguration())` to `CatalogDbContext.cs`.
- [ ] **Step 4: Run — verify config test pass** — `--filter EfSystemConfigurationTests` → PASS.
- [ ] **Step 5: Generate migration** — `cmd //c "dotnet ef migrations add AddSystems --project src/Modules/Catalog/Kartova.Catalog.Infrastructure --startup-project src/Kartova.Migrator --context CatalogDbContext"`. Then hand-add the RLS block to `Up` (copy verbatim from `20260703161759_AddApis.cs:50-56`, substituting `catalog_systems`) and its reversal to `Down`.
- [ ] **Step 6: Verify migration builds + snapshot updated** — `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure"` → 0 warnings; confirm `CatalogDbContextModelSnapshot.cs` includes `catalog_systems`.
- [ ] **Step 7: Commit** — `git commit -m "feat(catalog): catalog_systems table + RLS migration (E-03.F-03.S-01)"`.

---

## Task 5: Contracts — request/response/sort DTOs

**Files:**
- Create: `RegisterSystemRequest.cs`, `SystemResponse.cs`, `SystemSortField.cs` in `src/Modules/Catalog/Kartova.Catalog.Contracts/`

**Interfaces:**
- Produces: `RegisterSystemRequest(string DisplayName, string? Description, Guid TeamId)`; `SystemResponse(Guid Id, string DisplayName, string? Description, Guid TeamId, string? CreatedByDisplayName, DateTimeOffset CreatedAt)`; `enum SystemSortField { DisplayName, CreatedAt }`.

- [ ] **Step 1: Create the three files** — mirror `RegisterApiRequest`/`ApiResponse`/`ApiSortField`. Every record/DTO carries `[ExcludeFromCodeCoverage]`. `SystemResponse` mirrors `ApiResponse` field-enrichment shape (`CreatedByDisplayName`).
- [ ] **Step 2: Build** — `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Contracts"` → 0 warnings.
- [ ] **Step 3: Commit** — `git commit -m "feat(catalog): System contracts (E-03.F-03.S-01)"`.

---

## Task 6: Application — commands/queries + audit taxonomy

**Files:**
- Create: `RegisterSystemCommand.cs`, `GetSystemByIdQuery.cs`, `ListSystemsQuery.cs`, `SystemResponseExtensions.cs` in `src/Modules/Catalog/Kartova.Catalog.Application/`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/CatalogAuditActions.cs`

**Interfaces:**
- Produces: `RegisterSystemCommand(string DisplayName, string? Description, Guid TeamId)`; `GetSystemByIdQuery(Guid Id)`; `ListSystemsQuery(string? SortBy, string? SortOrder, string? Cursor, int Limit, IReadOnlyList<Guid> TeamId, string? DisplayNameContains)`; `SystemResponseExtensions.ToResponse(this System, string? createdByDisplayName)`; `CatalogAuditActions.SystemRegistered = "system.registered"`; `CatalogAuditTargetTypes.System = "System"`.

- [ ] **Step 1: Create files** — mirror `RegisterApiCommand`/`GetApiByIdQuery`/`ListApisQuery`/`ApiResponseExtensions`, substituting System fields (drop Style filter; keep `TeamId[]` + `DisplayNameContains`).
- [ ] **Step 2: Add audit constants** — `CatalogAuditActions.cs`: `public const string SystemRegistered = "system.registered";` and `CatalogAuditTargetTypes.System = "System"`.
- [ ] **Step 3: Build** — `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Application"` → 0 warnings.
- [ ] **Step 4: Commit** — `git commit -m "feat(catalog): System commands/queries + audit taxonomy (E-03.F-03.S-01)"`.

---

## Task 7: Infrastructure — `SystemSortSpecs`

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/SystemSortSpecs.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/SystemSortSpecsTests.cs`

**Interfaces:**
- Produces: `SystemSortSpecs.AllowedFieldNames` = `["displayName","createdAt"]`; `Resolve(SystemSortField)` → `SortSpec<System>`, throws `InvalidSortFieldException` on miss; `IdSelector`/`IdEquals` keyset helpers.

- [ ] **Step 1: Write failing test** — `SystemSortSpecsTests.cs`: assert `AllowedFieldNames` equals `{displayName, createdAt}`; `Resolve(DisplayName)`/`Resolve(CreatedAt)` return specs; unknown cast value throws `InvalidSortFieldException`.
- [ ] **Step 2: Run — verify fail** → FAIL.
- [ ] **Step 3: Implement** — copy `ApiSortSpecs.cs`, keep only `DisplayName` + `CreatedAt` specs, `IdSelector`/`IdEquals` over `SystemId`.
- [ ] **Step 4: Run — verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(catalog): SystemSortSpecs (E-03.F-03.S-01)"`.

---

## Task 8: Infrastructure — handlers (register / get / list)

**Files:**
- Create: `RegisterSystemHandler.cs`, `GetSystemByIdHandler.cs`, `ListSystemsHandler.cs` in `src/Modules/Catalog/Kartova.Catalog.Infrastructure/`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ListSystemsHandlerFilterTests.cs`

**Interfaces:**
- Consumes: `RegisterSystemCommand`, `ListSystemsQuery`, `GetSystemByIdQuery` (Task 6); `SystemSortSpecs` (Task 7); `CatalogDbContext.Systems` (Task 4).
- Produces: `RegisterSystemHandler.Handle(...)` → `SystemResponse`; `ListSystemsHandler.Handle(...)` → `CursorPage<SystemResponse>`; `GetSystemByIdHandler.Handle(...)` → `SystemResponse?`.

- [ ] **Step 1: Write failing filter/sort tests** — `ListSystemsHandlerFilterTests.cs`, mirror `ListApplicationsHandlerFilterTests.cs`: seed systems in-memory/real per existing pattern; assert `TeamId[]` filter, `DisplayNameContains` filter, default `displayName asc`, sort by `createdAt`, and unknown sort → `InvalidSortFieldException`.
- [ ] **Step 2: Run — verify fail** → FAIL.
- [ ] **Step 3: Implement handlers** — copy `RegisterApiHandler`/`GetApiByIdHandler`/`ListApisHandler`, substituting System. `RegisterSystemHandler`: `System.Create(...)` → `db.Systems.Add` → `SaveChangesAsync` → in-txn `IAuditWriter.AppendAsync(new AuditEntry(CatalogAuditActions.SystemRegistered, CatalogAuditTargetTypes.System, system.Id.Value.ToString(), data))` with `data` = `{displayName, teamId}`, fail-closed; return `system.ToResponse(...)`. `ListSystemsHandler`: filters before pagination, canonical cursor `filters` dict (`teamId`, `displayNameContains`), `ToCursorPagedAsync(...)`, batch-enrich `CreatedBy` via `IUserDirectory.GetManyAsync`.
- [ ] **Step 4: Run — verify pass** → PASS.
- [ ] **Step 5: Commit** — `git commit -m "feat(catalog): System register/get/list handlers (E-03.F-03.S-01)"`.

---

## Task 9: Infrastructure — `CatalogEntityLookup` System arm

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEntityLookup.cs:9-24`

**Interfaces:**
- Produces: `Find(EntityKind.System, id, ct)` resolves a System to `EntityLookupResult(TeamId, DisplayName)`; returns null when not found.

- [ ] **Step 1: Implement the arm** — add to the `switch` (before `_ => null`):

```csharp
EntityKind.System => await db.Systems
    .Where(x => EF.Property<Guid>(x, EfSystemConfiguration.IdFieldName) == id)
    .Select(x => new EntityLookupResult(x.TeamId, x.DisplayName))
    .FirstOrDefaultAsync(ct),
```

(Expose `EfSystemConfiguration.IdFieldName` as `internal const "_id"` to mirror `EfApiConfiguration`.)
- [ ] **Step 2: Verify via edge tests (Task 13)** — no isolated unit test; coverage comes from `CreatePartOfRelationshipTests`. Build now: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Infrastructure"` → 0 warnings.
- [ ] **Step 3: Commit** — `git commit -m "feat(catalog): resolve System nodes in CatalogEntityLookup (E-03.F-03.S-01)"`.

---

## Task 10: Permission — `catalog.systems.register` (5-sync)

**Files:**
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaPermissions.cs` (const + `All`)
- Modify: `src/Kartova.SharedKernel/Multitenancy/KartovaRolePermissions.cs` (Member + OrgAdmin)
- Modify: `web/src/shared/auth/permissions.snapshot.json`
- Modify: `web/src/shared/auth/permissions.ts`
- Modify: `web/src/shared/auth/__tests__/usePermissions.test.tsx`

- [ ] **Step 1: Add the permission across all 5 files** — `KartovaPermissions`: `public const string CatalogSystemsRegister = "catalog.systems.register";` + add to `All`. `KartovaRolePermissions`: grant to `Member` and `OrgAdmin` (mirror `CatalogApisRegister`). `permissions.snapshot.json`: add `"catalog.systems.register"`. `permissions.ts`: `CatalogSystemsRegister: "catalog.systems.register"`. `usePermissions.test.tsx`: add to the OrgAdmin full-set mock.
- [ ] **Step 2: Run arch + frontend guards** — `cmd //c "dotnet test tests/Kartova.ArchitectureTests --filter KartovaPermissionsRules"` → PASS; `cd web && npm run test -- usePermissions` → PASS (TS snapshot import guard green).
- [ ] **Step 3: Commit** — `git commit -m "feat(catalog): catalog.systems.register permission (5-sync) (E-03.F-03.S-01)"`.

---

## Task 11: Endpoints — delegates + `CatalogModule` routes + DI

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (add `RegisterSystemAsync` / `GetSystemByIdAsync` / `ListSystemsAsync`)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (routes + `AddScoped` handlers)

**Interfaces:**
- Consumes: handlers (Task 8), `CatalogSystemsRegister` (Task 10), `SystemSortSpecs` (Task 7).
- Produces: `POST /api/v1/catalog/systems`, `GET /systems/{id:guid}`, `GET /systems`.

- [ ] **Step 1: Add delegates** — mirror `RegisterApiAsync`/`GetApiByIdAsync`/`ListApisAsync` (`CatalogEndpointDelegates.cs:540,575,586`): `RegisterSystemAsync` — team-exists 422 pre-check (`IOrganizationTeamExistenceChecker`), `AuthorizeTargetTeamAsync` 403, dispatch, `Results.Created("/api/v1/catalog/systems/{id}", response)`. `ListSystemsAsync` — `CursorListBinding.Bind<SystemSortField>(sortBy, sortOrder, limit, SystemSortSpecs.AllowedFieldNames)`, parse `teamId[]` + `displayNameContains` (repeated-token, `HashSet` de-dup, unknown → 400).
- [ ] **Step 2: Map routes + DI** — `CatalogModule.cs`: `POST /systems` `.RequireAuthorization(KartovaPermissions.CatalogSystemsRegister)` `Produces<SystemResponse>(201)` + 400/403/422; `GET /systems/{id:guid}` `CatalogRead` `Produces<SystemResponse>(200)`+404; `GET /systems` `CatalogRead` `Produces<CursorPage<SystemResponse>>(200)`+422. `AddScoped` the three handlers in `RegisterServices`.
- [ ] **Step 3: Build** — `cmd //c "dotnet build Kartova.slnx"` → 0 warnings.
- [ ] **Step 4: Commit** — `git commit -m "feat(catalog): System endpoints + routes (E-03.F-03.S-01)"`.

---

## Task 12: Integration (real seam) — System endpoints

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/KartovaApiFixture.cs` (add `SeedSystemAsync` bypass-RLS helper + migrate already covers table)
- Create: `RegisterSystemTests.cs`, `ListSystemsPaginationTests.cs`, `GetSystemSurfaceTests.cs`
- Modify: `CatalogPermissionMatrixTests.cs`, `AuditWiringTests.cs`

- [ ] **Step 1: Write failing tests (real seam, ≥1 happy + ≥1 negative each)** —
  - `RegisterSystemTests`: `201` + `Location` + `SystemResponse` body; audit `system.registered` row present; `400` empty name; `403` non-steward member; `422` missing/cross-tenant team.
  - `ListSystemsPaginationTests`: keyset paging, default `displayName asc`, `teamId[]` + `displayNameContains` filters, bad sort/filter → `400`, RLS tenant isolation (OrgB can't see OrgA systems).
  - `GetSystemSurfaceTests`: `200` for seeded, `404` for random id.
  - `CatalogPermissionMatrixTests`: `catalog.systems.register` → Member `201`/OrgAdmin `201`/Viewer `403`.
  - `AuditWiringTests`: assert `system.registered` `data` shape.
  - `KartovaApiFixture.SeedSystemAsync(tenantId, teamId, name)` via bypass-RLS insert.
- [ ] **Step 2: Run — verify fail** → FAIL (endpoints exist but tests new).
- [ ] **Step 3: Make green** — fix any wiring gaps surfaced (filter-vs-binding order, issuer/audience, `SET LOCAL`).
- [ ] **Step 4: Run — full Catalog integration** — `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests"` → PASS.
- [ ] **Step 5: Commit** — `git commit -m "test(catalog): System endpoint real-seam integration (E-03.F-03.S-01)"`.

---

## Task 13: Integration (real seam) — PartOf assignment + visibility

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CreatePartOfRelationshipTests.cs`

- [ ] **Step 1: Write failing tests** —
  - Happy: `{Application|Service} → System` `PartOf` via `POST /relationships` → `201`.
  - Negatives: `Api → System` `422`; `System → System` `422`; missing endpoint `422`; neither-team `403`; duplicate `409`.
  - **Option A visibility:** after creating a `PartOf` edge, assert it appears in `GET /relationships?entityKind=Application&entityId=...` (relationship list) and in `GET /catalog/graph` edges — proving PartOf is visible (not filtered) and does not `500` those surfaces.
- [ ] **Step 2: Run — verify fail** → FAIL.
- [ ] **Step 3: Make green** — this exercises Task 2 (rules), Task 9 (lookup arm), Task 3 (visibility). Fix gaps.
- [ ] **Step 4: Run** — `--filter CreatePartOfRelationshipTests` → PASS.
- [ ] **Step 5: Commit** — `git commit -m "test(catalog): PartOf assignment + graph/list visibility (E-03.F-03.S-01)"`.

---

## Task 14: Docs — ADR amendment, registry, checklist

**Files:**
- Modify: `docs/architecture/decisions/ADR-0111-*.md` (amendment note — **preview to human before saving**)
- Modify: `docs/design/list-filter-registry.md` (Systems row)
- Modify: `docs/product/CHECKLIST.md` (tick E-03.F-03.S-01)

- [ ] **Step 1: Draft ADR-0111 amendment** — a dated amendment note: `PartOf` reintroduced with allowed pair `{Application, Service} → System`; visible in generic read paths (option A); System is a team-stewarded grouping node. **Present the diff to the human for approval before writing** (repo ADR process).
- [ ] **Step 2: Add registry row** — `list-filter-registry.md`: Systems list — sort `{displayName, createdAt}` default `displayName asc`; filters `displayNameContains`, `teamId[]`; deferred: `createdAt` range, `memberCount`.
- [ ] **Step 3: Update checklist** — mark `E-03.F-03.S-01` done with a one-line summary once gates pass.
- [ ] **Step 4: Commit** — `git commit -m "docs(catalog): ADR-0111 amendment + registry + checklist (E-03.F-03.S-01)"`.

---

## DoD

Governed by CLAUDE.md's eleven gates (ten always-blocking + mutation gate 6, **blocking here** — Domain/Application logic in Tasks 1, 2, 8). Ledger: `docs/superpowers/verification/2026-07-21-catalog-system-grouping/dod.md` (+ `gate-findings.yaml`, extended with `produced_by`/`found_by` tags for the A/B comparison). E2E-impact trigger: N/A (new entity, no UI, no existing `e2e/` spec traverses it). Field-addition trigger: N/A (new entity, no pre-existing list screen). **Execution-time codelens re-verify:** run `rebuild_solution` then `find_references` on `EntityKind`/`RelationshipType` before editing Task 1/2 — confirm no production consumer is missing from the Impact Analysis table.

## Self-Review

- **Spec coverage:** every spec §4 file + §5 surface + §6 flow + §8 test artifact maps to a task (1–14). ✓
- **Placeholders:** none — code shown for novel bits; mirror-bits cite exact template files to copy. ✓
- **Type consistency:** `SystemId`, `System.Create` signature, `SystemSortField {DisplayName,CreatedAt}`, `CatalogSystemsRegister`, `CatalogAuditActions.SystemRegistered` consistent across Tasks 1/5/6/7/8/10. ✓
- **Impact-analysis coverage:** the two non-obvious findings (query-filter auto-include, hardening test) are Task 3; `CatalogEntityLookup` arm is Task 9. ✓
