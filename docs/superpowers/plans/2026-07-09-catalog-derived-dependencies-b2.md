# Derived service↔service `depends-on` — Sub-slice B2 (endpoint + mini-graph + section) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface *derived* service↔service `depends-on` on the service-detail page — a bounded `GET /catalog/derived-dependencies` endpoint feeding a read-only Dependencies/Dependents section and dashed edges merged into the per-service mini-graph.

**Architecture:** Reuse B1's pure `DerivedDependencies.Compute`. Extract B1's in-handler edge-fetch and provenance-name-join (currently private to `GraphTraversalHandler`) into two shared Infrastructure helpers (`DerivedEdgeLoader`, `DerivedProvenanceNames`) so both the graph handler and the new endpoint compute derivation identically. A new `GetDerivedDependenciesHandler` loads the tenant's derived edges once (RLS-scoped), splits them into Dependencies (focus is source) and Dependents (focus is target) for the focus service, and joins API/via-app + other-service names. The frontend adds a `useDerivedDependencies` hook, a read-only `DerivedDependenciesSection`, and folds derived edges (dashed) into the mini-graph via `toGraphModel`.

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs · EF Core (owned `EntityRef`) · MSTest v4 + native asserts · Testcontainers (real Postgres/RLS + real JWT via `KartovaApiFixtureBase`) · React + TypeScript · @tanstack/react-query · @xyflow/react (React Flow) · Vitest + Testing Library.

**Spec:** `docs/superpowers/specs/2026-07-09-catalog-derived-service-dependencies-design.md` (this plan implements **B2** only; B1 shipped in PR #65 / commit `4075e54`).

## Global Constraints

- **Windows shell:** `cmd //c` (double slash) or PowerShell wrappers for `dotnet`; Git Bash lacks `grep -P` (use `-E`/`Select-String`). Multi-line git messages via PowerShell tool + multiple `-m` flags.
- **Solution:** `Kartova.slnx` (build with `dotnet build Kartova.slnx`). `TreatWarningsAsErrors=true` — 0 warnings.
- **Contracts coverage:** every `*Response`/`*Dto`/`*Item` carries `[ExcludeFromCodeCoverage]`; bounded flat results carry `[BoundedListResult(<justification>)]` (ContractsCoverageRules + the bounded-list arch test fail otherwise).
- **Enum wire format:** camelCase (ADR-0109) — serialized automatically via the configured `JsonStringEnumConverter`.
- **Derivation scope:** **Service↔Service only** (ADR-0111 §5); `S != T`; one collapsed edge per ordered `(S,T)` pair; provenance = list of paths `{ apiId, apiName, viaApplicationId?, viaApplicationDisplayName? }` (viaApp null when T provides directly); **explicit `depends-on` suppresses the derived pair** (handled inside `DerivedDependencies.Compute` — reused, unchanged).
- **Read-only:** derived dependencies are never persisted, authored, or deletable. `catalog.read` only; no new permission, no 5-sync, no migration.
- **RLS:** every DB read runs under the request `ITenantScope`; never bypass it. Cross-tenant edges/APIs/services must never appear (asserted).
- **Tests:** MSTest `[TestClass]`/`[TestMethod]`, native `Assert.*` (no FluentAssertions). Real-seam integration via `KartovaApiFixtureBase` (real JWT + Postgres/RLS). Frontend: Vitest + Testing Library; a populated react-aria `<Table>` MUST have an `isRowHeader` column and be asserted with `getAllByRole("rowheader").length > 0` (ADR-0084).
- **`.cs` files are LF** (`.gitattributes eol=lf`); do not introduce CRLF.

## Open decision for review (endpoint shape)

The design's §4.3 header shows `GET /catalog/derived-dependencies?entityId={guid}` (service-only), while its §7 error table lists a "focus kind ≠ service → 400" row (which only exists if `entityKind` is a param, as on `/api-surface`). **This plan implements the `entityId`-only shape** (faithful to the §4.3 header + the session breadcrumb + simpler FE): the endpoint assumes Service and calls `lookup.Find(Service, entityId)`, so an unknown, non-service, or cross-tenant id all resolve as absent → **422 invalid-entity** (the §7 "kind ≠ service → 400" row becomes N/A — no `entityKind` param). If you prefer the explicit 400-on-non-service, switch to the `/api-surface` shape (`?entityKind=&entityId=`, reject any kind but Service → 400). **Flagged for confirmation at plan review.**

## Impact Analysis (codelens/LSP)

This slice **adds new endpoint/handler/DTOs/FE** (no existing consumers) and performs **one behavior-preserving refactor** of a B1 symbol. `roslyn-codelens` MCP and the roslyn LSP were both unavailable this session; blast radius grounded via `Grep` + direct `Read` of the changed file (all targets are methods/types — grep-reliable per the CLAUDE.md const-carve-out; no `const`/enum value is modified — `RelationshipType.*`/`EntityKind.*` are read only). Every impacted caller is covered by a task.

| Symbol | Change | References (blast radius) | Covered by |
|--------|--------|---------------------------|-----------|
| `GraphTraversalHandler` (private `ComputeDerivedEdges`, `MapDerivedEdges`, `DerivationRelevantTypes`) | **Extract** edge-fetch → `DerivedEdgeLoader.LoadAsync`; extract name-join → `DerivedProvenanceNames`. **Public `Handle` signature + observable behavior unchanged.** | `Handle` callers: `CatalogEndpointDelegates.cs:893` (delegate) + DI reg `CatalogModule.cs:283` — **unaffected** (signature identical). The three private members are **file-local** (grep: only `GraphTraversalHandler.cs`). | Task 2 (extraction + re-run `GetCatalogGraphTests` + `GraphTraversalTests` to prove behavior identical) |
| `CatalogModule.MapEndpoints` / `AddCatalogModule` | Additive: one `MapGet("/derived-dependencies")` + one `AddScoped` | n/a (additive) | Task 4 |
| `CatalogEndpointDelegates` | Additive: new `GetDerivedDependenciesAsync` | n/a (additive) | Task 4 |
| `toGraphModel` (TS) | +optional 3rd param `derived?` (default `undefined`) | callers: `graphModel.ts` (def), `graphModel.test.ts`, `DependencyMiniGraph.tsx` (grep) — default keeps all unchanged; existing `toEqual` edge assertions ignore the absent optional `derived` key | Task 7 |
| `GraphEdge` (TS type) | +optional `derived?: boolean` | same callers; persisted edges never set the key → existing `toEqual` unaffected | Task 7 |
| `DependencyMiniGraph.tsx` (TS) | Add service-gated derived fetch + dashed merge | mounted by `ServiceDetailPage.tsx` + `ApplicationDetailPage.tsx` (grep) → fetch guarded `enabled: entityKind === "service"`; existing test must also mock the new hook | Task 7 |
| `ServiceDetailPage.tsx` (TS) | Additive: mount `<DerivedDependenciesSection>` | n/a (additive) | Task 6 |

**Reused unchanged:** `DerivedDependencies.Compute` + `.Path`/`.Edge` (B1), `DerivationPathDto` (B1), `ICatalogEntityLookup.Find` / `EntityLookupResult(Guid TeamId, string DisplayName)`, `EfApiConfiguration.IdFieldName` / `EfApplicationConfiguration.IdFieldName`. New C# symbols with no existing consumers: `DerivedEdgeLoader`, `DerivedProvenanceNames`, `GetDerivedDependenciesQuery`, `GetDerivedDependenciesHandler`, `DerivedDependencyItem`, `DerivedDependenciesResponse`.

---

### Task 1: Contracts — `DerivedDependencyItem`, `DerivedDependenciesResponse`

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/DerivedDependencyItem.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/DerivedDependenciesResponse.cs`

**Interfaces:**
- Consumes: existing `DerivationPathDto` (B1, `Kartova.Catalog.Contracts`).
- Produces: `DerivedDependencyItem(Guid ServiceId, string DisplayName, Guid? TeamId, IReadOnlyList<DerivationPathDto> Paths)`; `DerivedDependenciesResponse(IReadOnlyList<DerivedDependencyItem> Dependencies, IReadOnlyList<DerivedDependencyItem> Dependents)` — `[BoundedListResult]`.

- [ ] **Step 1: Create `DerivedDependencyItem`**

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>One derived depends-on relationship for a focus service: the other service (the provider for a
/// Dependencies row, the consumer for a Dependents row) plus every provenance path that links them. Read-only,
/// never persisted (ADR-0111 §Decision 5).</summary>
[ExcludeFromCodeCoverage]
public sealed record DerivedDependencyItem(
    Guid ServiceId,
    string DisplayName,
    Guid? TeamId,
    IReadOnlyList<DerivationPathDto> Paths);
```

- [ ] **Step 2: Create `DerivedDependenciesResponse`**

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Contracts;

/// <summary>A single service's derived depends-on relationships, computed on read (ADR-0111 §Decision 5).
/// Bounded (one service's derived neighbours; small N) so it returns flat arrays, not <c>CursorPage&lt;T&gt;</c>
/// — ADR-0095 bounded-list carve-out.</summary>
[BoundedListResult(
    "A single service's derived depends-on set (dependencies + dependents) is bounded and small; no pagination — ADR-0095 carve-out.")]
[ExcludeFromCodeCoverage]
public sealed record DerivedDependenciesResponse(
    IReadOnlyList<DerivedDependencyItem> Dependencies,  // services THIS one derives a depends-on TO (source == focus)
    IReadOnlyList<DerivedDependencyItem> Dependents);   // services that derive a depends-on on THIS one (target == focus)
```

- [ ] **Step 3: Build**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Confirm the contracts arch tests pass** (proves the `[BoundedListResult]` + `[ExcludeFromCodeCoverage]` attributes satisfy the guards)

Run: `cmd //c "dotnet test tests/Kartova.ArchitectureTests/Kartova.ArchitectureTests.csproj"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Contracts/DerivedDependencyItem.cs src/Modules/Catalog/Kartova.Catalog.Contracts/DerivedDependenciesResponse.cs
git commit -m "feat(catalog): DerivedDependenciesResponse + DerivedDependencyItem contracts"
```

---

### Task 2: Extract shared derived-edge helpers from `GraphTraversalHandler` (DRY groundwork)

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/DerivedEdgeLoader.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/DerivedProvenanceNames.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs` (use the two helpers; delete the now-extracted private members)

**Interfaces:**
- Consumes: `DerivedDependencies.Compute` (B1), `DerivationPathDto` (B1), `EfApiConfiguration.IdFieldName`, `EfApplicationConfiguration.IdFieldName`.
- Produces:
  - `internal static class DerivedEdgeLoader` → `static Task<IReadOnlyList<DerivedDependencies.Edge>> LoadAsync(CatalogDbContext db, CancellationToken ct)`.
  - `internal sealed class DerivedProvenanceNames` → `static Task<DerivedProvenanceNames> LoadAsync(IEnumerable<DerivedDependencies.Path> paths, CatalogDbContext db, CancellationToken ct)` + `DerivationPathDto Map(DerivedDependencies.Path p)`.

- [ ] **Step 1: Create `DerivedEdgeLoader`** — verbatim body of the current `GraphTraversalHandler.ComputeDerivedEdges` (fetch four contributing edge kinds + explicit depends-on in one round-trip, delegate to `DerivedDependencies.Compute`).

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Loads the tenant's full set of derived service→service depends-on edges (RLS-scoped): fetches the
/// four contributing edge kinds + explicit depends-on in one round-trip, then delegates shaping to the pure
/// <see cref="DerivedDependencies.Compute"/>. Shared by <see cref="GraphTraversalHandler"/> and
/// <see cref="GetDerivedDependenciesHandler"/> so the edge-fetch lives in exactly one place.</summary>
internal static class DerivedEdgeLoader
{
    private static readonly RelationshipType[] RelevantTypes =
    [
        RelationshipType.ConsumesApiFrom,
        RelationshipType.ProvidesApiFor,
        RelationshipType.InstanceOf,
        RelationshipType.DependsOn,
    ];

    public static async Task<IReadOnlyList<DerivedDependencies.Edge>> LoadAsync(
        CatalogDbContext db, CancellationToken ct)
    {
        var rels = await db.Relationships
            .Where(r => RelevantTypes.Contains(r.Type))
            .Select(r => new { SK = r.Source.Kind, SI = r.Source.Id, TK = r.Target.Kind, TI = r.Target.Id, r.Type })
            .ToListAsync(ct);

        var consumes = rels.Where(r => r.Type == RelationshipType.ConsumesApiFrom
                && r.SK == EntityKind.Service && r.TK == EntityKind.Api)
            .Select(r => (r.SI, r.TI)).ToList();
        var serviceProvides = rels.Where(r => r.Type == RelationshipType.ProvidesApiFor
                && r.SK == EntityKind.Service && r.TK == EntityKind.Api)
            .Select(r => (r.SI, r.TI)).ToList();
        var instanceOf = rels.Where(r => r.Type == RelationshipType.InstanceOf
                && r.SK == EntityKind.Service && r.TK == EntityKind.Application)
            .Select(r => (r.SI, r.TI)).ToList();
        var appProvides = rels.Where(r => r.Type == RelationshipType.ProvidesApiFor
                && r.SK == EntityKind.Application && r.TK == EntityKind.Api)
            .Select(r => (r.SI, r.TI)).ToList();
        var explicitDeps = rels.Where(r => r.Type == RelationshipType.DependsOn
                && r.SK == EntityKind.Service && r.TK == EntityKind.Service)
            .Select(r => (r.SI, r.TI)).ToHashSet();

        return DerivedDependencies.Compute(consumes, serviceProvides, instanceOf, appProvides, explicitDeps);
    }
}
```

- [ ] **Step 2: Create `DerivedProvenanceNames`** — the batch API/via-app name-join extracted from `GraphTraversalHandler.MapDerivedEdges` (preserve the unreachable-fallback comment — it documents a real future-delete hazard).

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Batch-resolves API + via-application display names for a set of derivation paths, then maps each
/// <see cref="DerivedDependencies.Path"/> to a <see cref="DerivationPathDto"/>. Shared by
/// <see cref="GraphTraversalHandler"/> and <see cref="GetDerivedDependenciesHandler"/> so the name-join lives
/// in exactly one place.</summary>
internal sealed class DerivedProvenanceNames
{
    private readonly IReadOnlyDictionary<Guid, string> _apiNames;
    private readonly IReadOnlyDictionary<Guid, string> _appNames;

    private DerivedProvenanceNames(
        IReadOnlyDictionary<Guid, string> apiNames, IReadOnlyDictionary<Guid, string> appNames)
    {
        _apiNames = apiNames;
        _appNames = appNames;
    }

    public static async Task<DerivedProvenanceNames> LoadAsync(
        IEnumerable<DerivedDependencies.Path> paths, CatalogDbContext db, CancellationToken ct)
    {
        var pathList = paths as ICollection<DerivedDependencies.Path> ?? paths.ToList();
        var apiIds = pathList.Select(p => p.ApiId).Distinct().ToList();
        var appIds = pathList.Where(p => p.ViaAppId is not null).Select(p => p.ViaAppId!.Value).Distinct().ToList();

        var apiNames = apiIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Apis
                .Where(a => apiIds.Contains(EF.Property<Guid>(a, EfApiConfiguration.IdFieldName)))
                .Select(a => new { Id = EF.Property<Guid>(a, EfApiConfiguration.IdFieldName), a.DisplayName })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var appNames = appIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Applications
                .Where(a => appIds.Contains(EF.Property<Guid>(a, EfApplicationConfiguration.IdFieldName)))
                .Select(a => new { Id = EF.Property<Guid>(a, EfApplicationConfiguration.IdFieldName), a.DisplayName })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        return new DerivedProvenanceNames(apiNames, appNames);
    }

    // The empty/null fallbacks below are currently UNREACHABLE: api/app ids are re-derived from a fresh
    // RLS-scoped query on every request, and there is no Api/Application delete path today, so a provenance
    // id can never fail to resolve. A future entity-delete slice MUST revisit this — deleting a referenced
    // Api/Application would otherwise silently render blank provenance instead of surfacing the dangling ref.
    public DerivationPathDto Map(DerivedDependencies.Path p) => new(
        p.ApiId,
        _apiNames.TryGetValue(p.ApiId, out var apiName) ? apiName : string.Empty,
        p.ViaAppId,
        p.ViaAppId is { } via && _appNames.TryGetValue(via, out var appName) ? appName : null);
}
```

- [ ] **Step 3: Refactor `GraphTraversalHandler`** to use the helpers. Three edits:

(a) line 21 — replace the private call with the loader:

```csharp
        var derivedAll = await DerivedEdgeLoader.LoadAsync(db, ct);
```

(b) replace the whole `MapDerivedEdges` method body (lines 144-178) with the helper-based version:

```csharp
    private static async Task<IReadOnlyList<DerivedEdgeDto>> MapDerivedEdges(
        IReadOnlyList<GraphTraversalEdge> derivedKept, CatalogDbContext db, CancellationToken ct)
    {
        if (derivedKept.Count == 0) return Array.Empty<DerivedEdgeDto>();

        var names = await DerivedProvenanceNames.LoadAsync(derivedKept.SelectMany(e => e.Provenance!), db, ct);

        return derivedKept.Select(e => new DerivedEdgeDto(
            new GraphEndpointDto(e.Source.Kind, e.Source.Id),
            new GraphEndpointDto(e.Target.Kind, e.Target.Id),
            e.Provenance!.Select(names.Map).ToList()))
            .ToList();
    }
```

(c) delete the now-unused private members: `ComputeDerivedEdges` (lines 117-142) and `DerivationRelevantTypes` (lines 109-115). Leave `SyntheticEdgeId` and everything else intact. Trim the `using Microsoft.EntityFrameworkCore;` only if no other usage remains in the file (the fetch moved out — verify by building; keep it if `Handle`'s closure still uses `ToListAsync`, which it does, so **keep the using**).

- [ ] **Step 4: Build**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: 0 errors, 0 warnings.

- [ ] **Step 5: Prove behavior is identical** — run B1's graph tests (unit + real-seam):

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter FullyQualifiedName~GraphTraversalTests"`
Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~GetCatalogGraphTests"`
Expected: PASS (unchanged from B1). A Testcontainers `TimeoutException` is the documented flake — re-run the assembly in isolation before treating it as red.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/DerivedEdgeLoader.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/DerivedProvenanceNames.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs
git commit -m "refactor(catalog): extract DerivedEdgeLoader + DerivedProvenanceNames from GraphTraversalHandler"
```

---

### Task 3: `GetDerivedDependenciesQuery` + `GetDerivedDependenciesHandler`

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/GetDerivedDependenciesQuery.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetDerivedDependenciesHandler.cs`

**Interfaces:**
- Consumes: `DerivedEdgeLoader.LoadAsync`, `DerivedProvenanceNames` (Task 2), `ICatalogEntityLookup.Find` → `EntityLookupResult(Guid TeamId, string DisplayName)`, `DerivedDependenciesResponse`/`DerivedDependencyItem` (Task 1).
- Produces: `GetDerivedDependenciesQuery(Guid ServiceId)`; `GetDerivedDependenciesHandler.Handle(GetDerivedDependenciesQuery q, CatalogDbContext db, ICatalogEntityLookup lookup, CancellationToken ct) → Task<DerivedDependenciesResponse>`.

- [ ] **Step 1: Create the query**

```csharp
namespace Kartova.Catalog.Application;

/// <summary>Read a service's derived depends-on relationships (dependencies + dependents). Service-only (ADR-0111 §5).</summary>
public sealed record GetDerivedDependenciesQuery(Guid ServiceId);
```

- [ ] **Step 2: Create the handler**

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Computes a focus service's derived depends-on relationships ON READ (ADR-0111 §Decision 5), split
/// into Dependencies (services the focus derives a depends-on TO — focus is the edge source) and Dependents
/// (services that derive a depends-on ON the focus — focus is the edge target). Reuses
/// <see cref="DerivedEdgeLoader"/> (tenant-wide RLS-scoped derivation, explicit-wins already applied) and
/// <see cref="DerivedProvenanceNames"/>; the other-service display name + team come from
/// <see cref="ICatalogEntityLookup"/>. Every id involved is in-tenant by construction (RLS-scoped fetch).</summary>
public sealed class GetDerivedDependenciesHandler
{
    public async Task<DerivedDependenciesResponse> Handle(
        GetDerivedDependenciesQuery q, CatalogDbContext db, ICatalogEntityLookup lookup, CancellationToken ct)
    {
        var all = await DerivedEdgeLoader.LoadAsync(db, ct);

        var dependencyEdges = all.Where(e => e.SourceServiceId == q.ServiceId).ToList(); // focus depends on TargetServiceId
        var dependentEdges = all.Where(e => e.TargetServiceId == q.ServiceId).ToList();  // SourceServiceId depends on focus

        var names = await DerivedProvenanceNames.LoadAsync(
            dependencyEdges.Concat(dependentEdges).SelectMany(e => e.Paths), db, ct);

        // Resolve the "other" service's display name + team. Bounded set (one service's derived neighbours) →
        // per-id lookup, mirroring GraphTraversalHandler's node enrichment.
        var otherIds = dependencyEdges.Select(e => e.TargetServiceId)
            .Concat(dependentEdges.Select(e => e.SourceServiceId))
            .Distinct()
            .ToList();

        var svc = new Dictionary<Guid, EntityLookupResult?>();
        foreach (var id in otherIds)
            svc[id] = await lookup.Find(EntityKind.Service, id, ct);

        DerivedDependencyItem ToItem(Guid otherServiceId, IReadOnlyList<DerivedDependencies.Path> paths)
        {
            var info = svc.GetValueOrDefault(otherServiceId);
            return new DerivedDependencyItem(
                otherServiceId,
                info?.DisplayName ?? string.Empty,
                info?.TeamId,
                paths.Select(names.Map).ToList());
        }

        var dependencies = dependencyEdges.Select(e => ToItem(e.TargetServiceId, e.Paths)).ToList();
        var dependents = dependentEdges.Select(e => ToItem(e.SourceServiceId, e.Paths)).ToList();
        return new DerivedDependenciesResponse(dependencies, dependents);
    }
}
```

- [ ] **Step 3: Build**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: 0 errors, 0 warnings. (No standalone unit test here — the derivation core `DerivedDependencies.Compute` is already mutation-covered by B1; this handler's DB-bound filter/join is exercised by the Task-4 real-seam tests.)

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/GetDerivedDependenciesQuery.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetDerivedDependenciesHandler.cs
git commit -m "feat(catalog): GetDerivedDependencies query + handler (dependencies/dependents split)"
```

---

### Task 4: Endpoint delegate + module wiring + real-seam integration tests

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (add `GetDerivedDependenciesAsync` after `GetApiSurfaceAsync`, ~line 881)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (route after the `/api-surface` block ~line 171; `AddScoped` after `GetApiSurfaceHandler` ~line 284)
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetDerivedDependenciesTests.cs`

**Interfaces:**
- Consumes: `GetDerivedDependenciesHandler` (Task 3), `ICatalogEntityLookup`, `CatalogDbContext`, `DerivedDependenciesResponse` (Task 1).
- Produces: `GET /api/v1/catalog/derived-dependencies?entityId={guid}` → `200 DerivedDependenciesResponse` · `400` (empty id) · `422` (unknown/non-service/cross-tenant) · `403` (missing `catalog.read`).

- [ ] **Step 1: Write the failing integration tests** — create `GetDerivedDependenciesTests.cs`, mirroring `GetApiSurfaceTests` seeding/fixture conventions (`CatalogIntegrationTestBase`, `Fx.CreateAuthenticatedClientAsync`, `Fx.SeedTeamInOrganizationAsync`, `KartovaApiFixtureBase.WireJson`, kebab-case relationship POST helper).

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public sealed class GetDerivedDependenciesTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private static Task<HttpResponseMessage> PostRelAsync(
        HttpClient client, EntityKind sk, Guid sid, RelationshipType t, EntityKind tk, Guid tid)
        => client.PostAsJsonAsync(
            "/api/v1/catalog/relationships",
            new { sourceKind = sk, sourceId = sid, type = t, targetKind = tk, targetId = tid },
            KartovaApiFixtureBase.WireJson);

    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        { displayName = name, description = "x", teamId, endpoints = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static async Task<Guid> SeedApplicationAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/applications",
            new { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApplication '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static async Task<Guid> SeedApiAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", new
        {
            displayName = name, description = "x", style = ApiStyle.Rest, version = "v1",
            specUrl = (string?)null, teamId,
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApi '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static Task<DerivedDependenciesResponse?> GetAsync(HttpClient client, Guid entityId)
        => client.GetAsync($"/api/v1/catalog/derived-dependencies?entityId={entityId}")
            .ContinueWith(t => t.Result.Content.ReadFromJsonAsync<DerivedDependenciesResponse>(KartovaApiFixtureBase.WireJson))
            .Unwrap();

    // Topology: consumer S --consumes--> Api1 ; provider T --instance-of--> App --provides--> Api1.
    // => derived S depends-on T, provenance {Api1 via App}.
    private sealed record ViaAppContext(HttpClient Client, Guid S, Guid T, Guid App, string AppName, Guid Api);

    private static async Task<ViaAppContext> SeedViaAppScenarioAsync()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Derived Team " + Guid.NewGuid());
        const string appName = "derived-provider-app";
        var app = await SeedApplicationAsync(client, teamId, appName);
        var t = await SeedServiceAsync(client, teamId, "derived-provider-svc");
        var s = await SeedServiceAsync(client, teamId, "derived-consumer-svc");
        var api = await SeedApiAsync(client, teamId, "derived-orders-api");

        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, t, RelationshipType.InstanceOf, EntityKind.Application, app)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, app, RelationshipType.ProvidesApiFor, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, s, RelationshipType.ConsumesApiFrom, EntityKind.Api, api)).StatusCode);

        return new ViaAppContext(client, s, t, app, appName, api);
    }

    [TestMethod]
    public async Task Dependencies_include_provider_with_via_app_provenance()
    {
        var ctx = await SeedViaAppScenarioAsync();

        var body = await GetAsync(ctx.Client, ctx.S);

        Assert.IsNotNull(body);
        Assert.AreEqual(0, body!.Dependents.Count);
        var dep = body.Dependencies.Single();
        Assert.AreEqual(ctx.T, dep.ServiceId);
        Assert.AreEqual("derived-provider-svc", dep.DisplayName);
        var path = dep.Paths.Single();
        Assert.AreEqual(ctx.Api, path.ApiId);
        Assert.AreEqual("derived-orders-api", path.ApiName);
        Assert.AreEqual(ctx.App, path.ViaApplicationId);
        Assert.AreEqual(ctx.AppName, path.ViaApplicationDisplayName);
    }

    [TestMethod]
    public async Task Dependents_are_the_reverse_direction()
    {
        var ctx = await SeedViaAppScenarioAsync();

        // Focus the PROVIDER T: S derives a depends-on ON T → S appears in Dependents.
        var body = await GetAsync(ctx.Client, ctx.T);

        Assert.IsNotNull(body);
        Assert.AreEqual(0, body!.Dependencies.Count);
        var dependent = body.Dependents.Single();
        Assert.AreEqual(ctx.S, dependent.ServiceId);
        Assert.AreEqual("derived-consumer-svc", dependent.DisplayName);
        Assert.AreEqual(ctx.Api, dependent.Paths.Single().ApiId);
    }

    [TestMethod]
    public async Task Direct_provide_yields_null_via_app()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Derived Direct " + Guid.NewGuid());
        var t = await SeedServiceAsync(client, teamId, "direct-provider");
        var s = await SeedServiceAsync(client, teamId, "direct-consumer");
        var api = await SeedApiAsync(client, teamId, "direct-api");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, t, RelationshipType.ProvidesApiFor, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, s, RelationshipType.ConsumesApiFrom, EntityKind.Api, api)).StatusCode);

        var body = await GetAsync(client, s);

        var path = body!.Dependencies.Single().Paths.Single();
        Assert.AreEqual(api, path.ApiId);
        Assert.IsNull(path.ViaApplicationId);
        Assert.IsNull(path.ViaApplicationDisplayName);
    }

    [TestMethod]
    public async Task Explicit_depends_on_suppresses_the_derived_pair()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Derived Explicit " + Guid.NewGuid());
        var t = await SeedServiceAsync(client, teamId, "explicit-provider");
        var s = await SeedServiceAsync(client, teamId, "explicit-consumer");
        var api = await SeedApiAsync(client, teamId, "explicit-api");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, t, RelationshipType.ProvidesApiFor, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, s, RelationshipType.ConsumesApiFrom, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, s, RelationshipType.DependsOn, EntityKind.Service, t)).StatusCode);

        var body = await GetAsync(client, s);

        Assert.AreEqual(0, body!.Dependencies.Count, "explicit depends-on must suppress the derived pair");
    }

    [TestMethod]
    public async Task Unknown_entity_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/derived-dependencies?entityId={Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task Missing_entityId_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await client.GetAsync($"/api/v1/catalog/derived-dependencies?entityId={Guid.Empty}");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Other_tenant_service_is_not_visible_422()
    {
        // Seed the scenario in tenant A; request A's consumer id as a user in tenant B.
        var ctx = await SeedViaAppScenarioAsync();
        var otherClient = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await otherClient.GetAsync($"/api/v1/catalog/derived-dependencies?entityId={ctx.S}");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }
}
```

- [ ] **Step 2: Run the new tests to verify they fail** (endpoint not mapped yet → 404)

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~GetDerivedDependenciesTests"`
Expected: FAIL (404 / deserialization) — endpoint not wired.

- [ ] **Step 3: Add the delegate** — insert `GetDerivedDependenciesAsync` immediately after `GetApiSurfaceAsync` (after line 881) in `CatalogEndpointDelegates.cs`:

```csharp
    /// <summary>
    /// GET /derived-dependencies?entityId= — a Service's derived depends-on relationships (Dependencies +
    /// Dependents), computed on read (ADR-0111 §Decision 5). Bounded flat result (ADR-0095 carve-out). Claim
    /// gate: catalog.read. Service-only: an unknown, non-service, or cross-tenant focus id resolves as absent
    /// under RLS → 422 invalid-entity.
    /// </summary>
    internal static async Task<IResult> GetDerivedDependenciesAsync(
        [FromQuery] Guid entityId,
        GetDerivedDependenciesHandler handler,
        ICatalogEntityLookup lookup,
        CatalogDbContext db,
        CancellationToken ct)
    {
        if (entityId == Guid.Empty)
        {
            return Results.Problem(
                type: ProblemTypes.ValidationFailed,
                title: "Invalid entity reference",
                detail: "entityId must be non-empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        // Focus must be a service in this tenant (RLS ⇒ cross-tenant / non-service / unknown id → absent → 422).
        if (await lookup.Find(EntityKind.Service, entityId, ct) is null)
        {
            return Results.Problem(
                type: ProblemTypes.InvalidEntity,
                title: "Invalid entity",
                detail: "The service does not exist in this tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var result = await handler.Handle(new GetDerivedDependenciesQuery(entityId), db, lookup, ct);
        return Results.Ok(result);
    }
```

> If the `EntityKind` / `GetDerivedDependenciesQuery` types aren't already imported in this file, add `using Kartova.Catalog.Domain;` / `using Kartova.Catalog.Application;` (they are already used elsewhere in this file for `GetApiSurfaceAsync` — verify by building).

- [ ] **Step 4: Map the route + register the handler** in `CatalogModule.cs`. After the `/api-surface` `MapGet` block (ends ~line 171) add:

```csharp
        tenant.MapGet("/derived-dependencies", CatalogEndpointDelegates.GetDerivedDependenciesAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("GetDerivedDependencies")
              .Produces<DerivedDependenciesResponse>(StatusCodes.Status200OK)
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
```

After `services.AddScoped<GetApiSurfaceHandler>();` (line 284) add:

```csharp
        services.AddScoped<GetDerivedDependenciesHandler>();
```

- [ ] **Step 5: Build**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: 0 errors, 0 warnings.

- [ ] **Step 6: Run the integration tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~GetDerivedDependenciesTests"`
Expected: PASS (7/7). Testcontainers `TimeoutException` → re-run the assembly in isolation before treating as red.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetDerivedDependenciesTests.cs
git commit -m "feat(catalog): GET /derived-dependencies endpoint (service-only, bounded) + real-seam tests"
```

---

### Task 5: Regenerate the OpenAPI client + snapshot

**Files:**
- Modify: `web/openapi-snapshot.json` (regenerated)
- Modify: `web/src/generated/*` (regenerated)

**Interfaces:**
- Produces: `components["schemas"]["DerivedDependenciesResponse"]` + `["DerivedDependencyItem"]`; path `/api/v1/catalog/derived-dependencies` — consumed by Tasks 6-7.

- [ ] **Step 1: Rebuild the API image so the running API exposes the new schema + path**

Run: `cmd //c "docker compose build api"`
Expected: build succeeds.

- [ ] **Step 2: Regenerate the client + snapshot** (predev/prebuild regenerates from the live API; confirm the script name in `web/package.json` `scripts`)

Run: `cmd //c "cd web && npm run codegen"`
Expected: `web/src/generated/openapi.d.ts` + `web/openapi-snapshot.json` now include the `/derived-dependencies` path and the two new DTO schemas. (`DerivationPathDto` already exists from B1 and is reused.)

- [ ] **Step 3: Sanity-check the diff**

Run: `git --no-pager diff --stat web/openapi-snapshot.json web/src/generated`
Expected: additive changes only (new path + 2 DTOs). Param-order-only churn is cosmetic — keep it.

- [ ] **Step 4: Commit**

```bash
git add web/openapi-snapshot.json web/src/generated
git commit -m "chore(web): regenerate OpenAPI client for /derived-dependencies"
```

---

### Task 6: Frontend — hook + `DerivedDependenciesSection` + mount

**Files:**
- Create: `web/src/features/catalog/api/derivedDependencies.ts`
- Create: `web/src/features/catalog/components/DerivedDependenciesSection.tsx`
- Create: `web/src/features/catalog/components/__tests__/DerivedDependenciesSection.test.tsx`
- Modify: `web/src/features/catalog/pages/ServiceDetailPage.tsx` (mount the section)

**Interfaces:**
- Consumes: regenerated `components["schemas"]["DerivedDependenciesResponse"]` / `["DerivedDependencyItem"]` (Task 5).
- Produces: `useDerivedDependencies(entityId: string, options?: { enabled?: boolean })` (consumed by Task 7); `DerivedDependenciesSection({ entityId })`.

- [ ] **Step 1: Create the hook** (mirror `apiSurface.ts`; add an `enabled` override so the mini-graph can service-gate it)

```ts
import { useQuery } from "@tanstack/react-query";
import { apiClient } from "./client";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";

export type DerivedDependenciesResponse = components["schemas"]["DerivedDependenciesResponse"];
export type DerivedDependencyItem = components["schemas"]["DerivedDependencyItem"];

export function useDerivedDependencies(entityId: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["catalog", "derived-dependencies", entityId],
    enabled: entityId !== "" && (options?.enabled ?? true),
    queryFn: async (): Promise<DerivedDependenciesResponse> => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/derived-dependencies", {
        params: { query: { entityId } },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}
```

- [ ] **Step 2: Write the failing section test** — create `DerivedDependenciesSection.test.tsx` (mirror `ApiSurfaceSection.test.tsx` mocking):

```tsx
import { it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { DerivedDependenciesSection } from "@/features/catalog/components/DerivedDependenciesSection";
import * as api from "@/features/catalog/api/derivedDependencies";

function mock(data: api.DerivedDependenciesResponse) {
  vi.spyOn(api, "useDerivedDependencies").mockReturnValue({
    data,
    isLoading: false,
    isError: false,
  } as never);
}

function renderSection(entityId: string) {
  return render(
    <MemoryRouter>
      <DerivedDependenciesSection entityId={entityId} />
    </MemoryRouter>,
  );
}

beforeEach(() => vi.restoreAllMocks());

it("renders dependencies and dependents with provenance", () => {
  mock({
    dependencies: [
      {
        serviceId: "t1",
        displayName: "AuthService",
        teamId: null,
        paths: [
          { apiId: "a1", apiName: "Orders API", viaApplicationId: "app1", viaApplicationDisplayName: "Billing" },
        ],
      },
    ],
    dependents: [
      {
        serviceId: "s2",
        displayName: "Checkout",
        teamId: null,
        paths: [{ apiId: "a2", apiName: "Events API", viaApplicationId: null, viaApplicationDisplayName: null }],
      },
    ],
  });

  renderSection("svc1");

  expect(screen.getByText("AuthService")).toBeInTheDocument();
  expect(screen.getByText("Checkout")).toBeInTheDocument();
  expect(
    screen.getAllByText((_, el) => /via orders api/i.test(el?.textContent ?? "")).length,
  ).toBeGreaterThan(0);
  // ADR-0084: a populated react-aria Table must expose an isRowHeader column.
  expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
});

it("shows empty copy for both tables when there are no derived edges", () => {
  mock({ dependencies: [], dependents: [] });
  renderSection("svc1");
  expect(screen.getByText(/no derived dependencies/i)).toBeInTheDocument();
  expect(screen.getByText(/nothing derives a dependency on this service/i)).toBeInTheDocument();
});

it("shows an error message when the query fails", () => {
  vi.spyOn(api, "useDerivedDependencies").mockReturnValue({
    data: undefined,
    isLoading: false,
    isError: true,
  } as never);
  renderSection("svc1");
  expect(screen.getByText(/couldn.t load derived dependencies/i)).toBeInTheDocument();
});
```

- [ ] **Step 3: Run to verify it fails**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/components/__tests__/DerivedDependenciesSection.test.tsx"`
Expected: FAIL — component does not exist.

- [ ] **Step 4: Implement `DerivedDependenciesSection`** (read-only, two tables + provenance; mirror `ApiSurfaceSection` structure)

```tsx
import { Link } from "react-router-dom";
import { Badge } from "@/components/base/badges/badges";
import { Table } from "@/components/application/table/table";
import { TableSkeleton } from "@/components/application/data-table/data-table";
import {
  useDerivedDependencies,
  type DerivedDependencyItem,
} from "@/features/catalog/api/derivedDependencies";

interface Props {
  entityId: string;
}

export function DerivedDependenciesSection({ entityId }: Props) {
  const query = useDerivedDependencies(entityId);

  if (query.isLoading) return <DerivedDependenciesSkeleton />;
  if (query.isError || !query.data)
    return <p className="text-sm text-error-primary">Couldn&apos;t load derived dependencies.</p>;

  const { dependencies, dependents } = query.data;

  return (
    <section className="space-y-6" aria-label="Derived dependencies">
      <DerivedTable title="Dependencies" emptyCopy="No derived dependencies." items={dependencies} />
      <DerivedTable
        title="Dependents"
        emptyCopy="Nothing derives a dependency on this service."
        items={dependents}
      />
    </section>
  );
}

function DerivedTable({
  title,
  emptyCopy,
  items,
}: {
  title: string;
  emptyCopy: string;
  items: DerivedDependencyItem[];
}) {
  return (
    <div className="space-y-2">
      <h3 className="text-sm font-semibold text-primary">{title}</h3>
      {items.length === 0 ? (
        <p className="text-sm italic text-tertiary">{emptyCopy}</p>
      ) : (
        <div className="overflow-hidden rounded-lg ring-1 ring-secondary">
          <Table aria-label={title}>
            <Table.Header>
              <Table.Head id="service" isRowHeader>
                Service
              </Table.Head>
              <Table.Head id="kind">Kind</Table.Head>
              <Table.Head id="provenance">Via</Table.Head>
            </Table.Header>
            <Table.Body>
              {items.map((i) => (
                <Table.Row key={i.serviceId} id={i.serviceId}>
                  <Table.Cell>
                    <Link to={`/catalog/services/${i.serviceId}`} className="text-primary hover:underline">
                      {i.displayName}
                    </Link>
                  </Table.Cell>
                  <Table.Cell>
                    <Badge type="pill-color" size="sm" color="gray">
                      Derived
                    </Badge>
                  </Table.Cell>
                  <Table.Cell className="text-sm text-tertiary">
                    <ul className="space-y-0.5">
                      {i.paths.map((p) => (
                        <li key={`${p.apiId}:${p.viaApplicationId ?? "direct"}`}>
                          via{" "}
                          <Link to={`/catalog/apis/${p.apiId}`} className="text-primary hover:underline">
                            {p.apiName}
                          </Link>
                          {p.viaApplicationId ? (
                            <>
                              {" · "}
                              <Link
                                to={`/catalog/applications/${p.viaApplicationId}`}
                                className="text-primary hover:underline"
                              >
                                {p.viaApplicationDisplayName ?? "application"}
                              </Link>
                            </>
                          ) : null}
                        </li>
                      ))}
                    </ul>
                  </Table.Cell>
                </Table.Row>
              ))}
            </Table.Body>
          </Table>
        </div>
      )}
    </div>
  );
}

function DerivedDependenciesSkeleton() {
  return (
    <section className="space-y-6" aria-label="Derived dependencies">
      {["Dependencies", "Dependents"].map((title) => (
        <div className="space-y-2" key={title}>
          <h3 className="text-sm font-semibold text-primary">{title}</h3>
          <Table aria-label={title}>
            <Table.Header>
              <Table.Head id="service" isRowHeader>
                Service
              </Table.Head>
              <Table.Head id="kind">Kind</Table.Head>
              <Table.Head id="provenance">Via</Table.Head>
            </Table.Header>
            <TableSkeleton rows={2} cells={3} />
          </Table>
        </div>
      ))}
    </section>
  );
}
```

> If `color="gray"` is not a valid `Badge` color in this repo, use the neutral color the codebase already uses for non-status pills (check `@/components/base/badges/badges`); the ApiSurface panel uses `color="brand"`/`"success"` — pick the closest neutral. This is a display-only choice, not behavior.

- [ ] **Step 5: Run the section test to verify it passes**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/components/__tests__/DerivedDependenciesSection.test.tsx"`
Expected: PASS.

- [ ] **Step 6: Mount in `ServiceDetailPage.tsx`** — add the import next to `ApiSurfaceSection`:

```tsx
import { DerivedDependenciesSection } from "@/features/catalog/components/DerivedDependenciesSection";
```

Insert the section (with a divider) right after the `<ApiSurfaceSection … />` line (currently line 125), before the `<RelationshipsSection … />` divider:

```tsx
          <ApiSurfaceSection entityKind="service" entityId={svc.id} />
          <hr className="border-secondary" />
          <DerivedDependenciesSection entityId={svc.id} />
          <hr className="border-secondary" />
```

- [ ] **Step 7: Build + full web tests**

Run: `cmd //c "cd web && npm run build && npx vitest run"`
Expected: `tsc -b` 0 errors; all tests pass.

- [ ] **Step 8: Commit**

```bash
git add web/src/features/catalog/api/derivedDependencies.ts web/src/features/catalog/components/DerivedDependenciesSection.tsx web/src/features/catalog/components/__tests__/DerivedDependenciesSection.test.tsx web/src/features/catalog/pages/ServiceDetailPage.tsx
git commit -m "feat(web): read-only DerivedDependenciesSection on service detail"
```

---

### Task 7: Frontend — merge derived edges (dashed) into the mini-graph

**Files:**
- Modify: `web/src/features/catalog/relationships/graphModel.ts` (`GraphEdge` +`derived?`; `toGraphModel` +optional `derived` param; export merge input types)
- Modify: `web/src/features/catalog/relationships/__tests__/graphModel.test.ts` (add derived-merge cases)
- Modify: `web/src/features/catalog/components/DependencyMiniGraph.tsx` (service-gated fetch + dashed edge styling + legend)
- Modify: `web/src/features/catalog/components/__tests__/DependencyMiniGraph.test.tsx` (mock the new hook; add derived cases)

**Interfaces:**
- Consumes: `useDerivedDependencies` (Task 6), `DerivedDependencyItem` (Task 6).
- Produces: `GraphEdge` gains `derived?: boolean`; `toGraphModel(focused, relationships, derived?: DerivedDependencySets)`; exported `DerivedNeighbour` / `DerivedDependencySets` types.

- [ ] **Step 1: Write the failing `toGraphModel` derived-merge tests** — append to `graphModel.test.ts`:

```ts
describe("toGraphModel — derived edges", () => {
  it("adds a dashed edge + node for a derived dependency (focused → other)", () => {
    const m = toGraphModel(focused, [], {
      dependencies: [{ serviceId: "t9", displayName: "Provider", label: "via Orders API" }],
      dependents: [],
    });
    const other = m.nodes.find((n) => n.id === "service:t9")!;
    expect(other.data.side).toBe("dependency");
    const edge = m.edges.find((e) => e.derived)!;
    expect(edge.id).toBe("service:s1->service:t9:derived");
    expect(edge.source).toBe("service:s1");
    expect(edge.target).toBe("service:t9");
    expect(edge.label).toBe("via Orders API");
  });

  it("adds a dashed edge for a derived dependent (other → focused)", () => {
    const m = toGraphModel(focused, [], {
      dependencies: [],
      dependents: [{ serviceId: "s9", displayName: "Consumer", label: "via Events API" }],
    });
    const edge = m.edges.find((e) => e.derived)!;
    expect(edge.id).toBe("service:s9->service:s1:derived");
    expect(edge.source).toBe("service:s9");
    expect(edge.target).toBe("service:s1");
  });

  it("does not add a derived edge when derived sets are empty", () => {
    const m = toGraphModel(focused, [rel({ id: "r1" })], { dependencies: [], dependents: [] });
    expect(m.edges.some((e) => e.derived)).toBe(false);
    expect(m.edges).toHaveLength(1);
  });

  it("reuses an existing persisted neighbour node rather than duplicating it", () => {
    // s2 is already a persisted neighbour; a derived dependency on s2 must not add a second node.
    const m = toGraphModel(focused, [rel({ id: "r1" })], {
      dependencies: [{ serviceId: "s2", displayName: "AuthService", label: "via X" }],
      dependents: [],
    });
    expect(m.nodes.filter((n) => n.id === "service:s2")).toHaveLength(1);
    expect(m.edges.filter((e) => e.derived)).toHaveLength(1);
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/relationships/__tests__/graphModel.test.ts"`
Expected: FAIL (derived param/field not supported).

- [ ] **Step 3: Implement in `graphModel.ts`.** (a) extend the edge type; (b) add the merge input types; (c) add an optional `derived` param to `toGraphModel` and fold it in **before** layout.

Extend `GraphEdge` (line 27):

```ts
export type GraphEdge = { id: string; source: string; target: string; label: string; derived?: boolean };
```

Add after `FocusedEntity` (line 31):

```ts
export type DerivedNeighbour = { serviceId: string; displayName: string; label: string };
export type DerivedDependencySets = { dependencies: DerivedNeighbour[]; dependents: DerivedNeighbour[] };
```

Change the signature (line 39) to accept the optional param:

```ts
export function toGraphModel(
  focused: FocusedEntity,
  relationships: RelationshipResponse[],
  derived?: DerivedDependencySets,
): GraphModel {
```

Immediately **after** the `for (const r of relationships) { … }` loop and **before** the `const nodes: GraphNode[] = [ … ]` layout block, insert the merge:

```ts
  if (derived) {
    const addDerived = (n: DerivedNeighbour, side: GraphSide, source: string, target: string) => {
      const otherId = nodeId("service", n.serviceId);
      if (otherId === focusedId) return; // no self-edge (Compute already excludes S==T, but guard anyway)
      if (!neighbours.has(otherId)) {
        neighbours.set(otherId, { kind: "service", entityId: n.serviceId, displayName: n.displayName, side });
      }
      const id = `${source}->${target}:derived`;
      if (!edges.some((e) => e.id === id)) {
        edges.push({ id, source, target, label: n.label, derived: true });
      }
    };
    for (const d of derived.dependencies) addDerived(d, "dependency", focusedId, nodeId("service", d.serviceId));
    for (const d of derived.dependents) addDerived(d, "dependent", nodeId("service", d.serviceId), focusedId);
  }
```

- [ ] **Step 4: Run the model tests to verify they pass**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/relationships/__tests__/graphModel.test.ts"`
Expected: PASS (existing + 4 new). The existing `toEqual` edge assertions still pass — persisted edges never set `derived`, and `toEqual` ignores the absent optional key.

- [ ] **Step 5: Update the mini-graph test first** (it will break once the component calls the new hook). Add the hook mock + derived cases to `DependencyMiniGraph.test.tsx`:

Add the import + a default mock so existing cases still pass (no derived edges), and one derived case:

```ts
import * as derivedApi from "@/features/catalog/api/derivedDependencies";
```

In `beforeEach`, after `navigate.mockReset();`, add a default (no derived data):

```ts
  vi.spyOn(derivedApi, "useDerivedDependencies").mockReturnValue({
    data: undefined, isLoading: false, isError: false,
  } as never);
```

Add a new test:

```ts
it("merges a derived dependency as an extra dashed edge", () => {
  vi.spyOn(api, "useRelationshipsList").mockReturnValue(listResult(outgoing)); // focused + 1 persisted neighbour
  vi.spyOn(derivedApi, "useDerivedDependencies").mockReturnValue({
    data: {
      dependencies: [{ serviceId: "s3", displayName: "PaymentsService", teamId: null,
        paths: [{ apiId: "a1", apiName: "Orders API", viaApplicationId: null, viaApplicationDisplayName: null }] }],
      dependents: [],
    },
    isLoading: false, isError: false,
  } as never);
  renderGraph();
  expect(screen.getByTestId("node-count")).toHaveTextContent("3"); // focused + persisted + derived neighbour
  expect(screen.getByTestId("edge-count")).toHaveTextContent("2"); // 1 persisted + 1 derived
});
```

- [ ] **Step 6: Run the mini-graph test to verify the new case fails** (component not merging yet)

Run: `cmd //c "cd web && npx vitest run src/features/catalog/components/__tests__/DependencyMiniGraph.test.tsx"`
Expected: the new "merges a derived dependency" case FAILS (edge-count 1, node-count 2); the others PASS (default mock → no derived).

- [ ] **Step 7: Implement the mini-graph merge.** Edit `DependencyMiniGraph.tsx`:

Add the import:

```ts
import { useDerivedDependencies } from "@/features/catalog/api/derivedDependencies";
```

Add a label helper above the component:

```ts
function derivedLabel(paths: { apiName: string }[]): string {
  const first = paths[0]?.apiName ?? "API";
  return paths.length <= 1 ? `via ${first}` : `via ${first} +${paths.length - 1}`;
}
```

Inside the component, after the `useRelationshipsList` line, fetch derived deps (service-gated):

```ts
  const derivedQuery = useDerivedDependencies(entityId, { enabled: entityKind === "service" });
```

Replace the `model` `useMemo` with one that folds in derived:

```ts
  const model = useMemo(() => {
    const focused: FocusedEntity = { kind: entityKind, id: entityId, displayName };
    const derived = derivedQuery.data
      ? {
          dependencies: derivedQuery.data.dependencies.map((d) => ({
            serviceId: d.serviceId, displayName: d.displayName, label: derivedLabel(d.paths),
          })),
          dependents: derivedQuery.data.dependents.map((d) => ({
            serviceId: d.serviceId, displayName: d.displayName, label: derivedLabel(d.paths),
          })),
        }
      : undefined;
    return toGraphModel(focused, list.items ?? [], derived);
  }, [list.items, entityKind, entityId, displayName, derivedQuery.data]);
```

Apply dashed styling when passing edges to React Flow (replace `edges={model.edges as Edge[]}`):

```tsx
              edges={model.edges.map((e) => ({
                ...e,
                ...(e.derived
                  ? { style: { strokeDasharray: "6 4", stroke: "var(--color-fg-quaternary, #98A2B3)" } }
                  : {}),
              })) as Edge[]}
```

Add a compact legend under the graph — insert right after the closing `</div>` of the React-Flow container (before the `{list.hasNext && …}` block):

```tsx
          <p className="text-xs text-tertiary">
            <span className="mr-3">— explicit</span>
            <span className="font-mono">- - derived</span>
          </p>
```

- [ ] **Step 8: Run the mini-graph test to verify it passes**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/components/__tests__/DependencyMiniGraph.test.tsx"`
Expected: PASS (all, including the derived-merge case).

- [ ] **Step 9: Full web build + tests**

Run: `cmd //c "cd web && npm run build && npx vitest run"`
Expected: `tsc -b` 0 errors; all tests pass.

- [ ] **Step 10: Commit**

```bash
git add web/src/features/catalog/relationships/graphModel.ts web/src/features/catalog/relationships/__tests__/graphModel.test.ts web/src/features/catalog/components/DependencyMiniGraph.tsx web/src/features/catalog/components/__tests__/DependencyMiniGraph.test.tsx
git commit -m "feat(web): merge derived depends-on as dashed edges into the service mini-graph"
```

---

### Task 8: Registry + checklist

**Files:**
- Modify: `docs/design/list-filter-registry.md` (add the derived-dependencies section row)
- Modify: `docs/product/CHECKLIST.md` (tick E-02.F-03 sub-slice-B2)

**Interfaces:** none (docs).

- [ ] **Step 1: Add a registry row** to `docs/design/list-filter-registry.md`, in the `## Registry` table (after the API-surface row):

```markdown
| Derived dependencies panel | Service detail → Derived dependencies section (`GET /catalog/derived-dependencies?entityId=`) | none (no `<FilterBar>` facets) | **none-needed** | E-02.F-03.S-03 (FU-B, B2) | Bounded read-only embedded panel (`DerivedDependenciesSection`), service-only, not a top-level list — no `<FilterBar>`. Default sort: **client-side `displayName asc`**, non-configurable (no `sortBy`/`sortOrder`). All facets (provenance API/app, team) — **none-needed**: bounded, small N (a service's derived neighbours). Also surfaced as dashed edges in the per-service mini-graph. Slice 2026-07-09 (`feat/catalog-derived-dependencies`, B2). |
```

- [ ] **Step 2: Tick the checklist** — in `docs/product/CHECKLIST.md`, find the E-02.F-03 sub-slice-B entry and mark B2 (endpoint + mini-graph + section) complete. (Locate with `grep -n "F-03" docs/product/CHECKLIST.md` and edit the matching B-series line; if B1/B2 aren't itemized, add the B2 completion note next to the F-03 row.)

- [ ] **Step 3: Commit**

```bash
git add docs/design/list-filter-registry.md docs/product/CHECKLIST.md
git commit -m "docs(catalog): register derived-dependencies panel + tick E-02.F-03 B2"
```

---

## Definition of Done (B2)

The ten always-blocking gates + conditional mutation gate in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim (not restated). B2-specific notes:

- **Gate 3 real-seam:** the endpoint is HTTP/auth/DB → the Task-4 integration tests hit the real seam (real JWT + Postgres/RLS via `KartovaApiFixtureBase`). ≥1 happy (`Dependencies_include_provider_with_via_app_provenance`) + ≥1 negative (`Other_tenant_service_is_not_visible_422`) ✓.
- **Gate 4 container build:** no Dockerfile/`COPY` change → the `images` job runs unchanged on the PR. (Task 5 rebuilds the API image only to regenerate the client, not a Dockerfile change.)
- **Gate 6 mutation — SHOULD-DO (conditional), not blocking:** the derivation core `DerivedDependencies.Compute` is unchanged and already ≥80% mutation-covered by B1. B2's new logic is (a) a logicless `GetDerivedDependenciesQuery` record and (b) the Infrastructure handler's source/target split + name-join — Infrastructure, not Domain/Application logic. Run `/misc:mutation-sentinel` on `GetDerivedDependenciesHandler.cs` + the two extracted helpers if practical (target ≥80%, document survivors); otherwise skip with this reason noted. The Task-2 extraction is behavior-preserving and re-verified against B1's existing tests.
- **Gate 10 visual/API:** cold-start dev server + live API (per [local UI verification setup]), authenticate, seed `S consumes X` + `T instance-of App provides X`, open the service `S` detail page → confirm the `DerivedDependenciesSection` Dependencies row (via API · app provenance) + the dashed derived edge + legend in the mini-graph; open service `T` → confirm the Dependents row; add an explicit `depends-on S→T` → confirm the derived row/edge disappears (explicit-wins). Also hit the live endpoint (real auth + DB) and capture the JSON. Evidence under `docs/superpowers/verification/2026-07-09-catalog-derived-dependencies/b2/`.
- **DoD ledger:** copy `docs/superpowers/templates/dod-ledger-template.md` → `docs/superpowers/verification/2026-07-09-catalog-derived-dependencies/b2/dod.md`; copy `gate-findings-template.yaml` alongside. Update each row the moment its gate runs.
- **Pre-push:** `scripts/ci-local.sh` (Release mirror) green before PR. (Watch the known `ci-local.sh frontend` npm-ci-vs-dev-server EPERM — stop the vite dev server first.)
- **`.cs` LF:** verify no LF→CRLF flip crept into edited `.cs` files before commit (repo is `eol=lf`).

## Self-Review

**Spec coverage (design §4.3, §4.4 B2, §5, §6, §7, §8.2):**
- §4.3 bounded endpoint (fetch edge-sets → Compute → split Dependencies/Dependents → name join) → Tasks 3-4.
- §4.4 B2 file table: `DerivedDependenciesResponse`+`DerivedDependencyItem` (Task 1) · `GetDerivedDependenciesQuery` (Task 3) · `GetDerivedDependenciesHandler` (Task 3) · delegate + module (Task 4) · `GetDerivedDependenciesTests` (Task 4) · `derivedDependencies.ts` (Task 6) · `DerivedDependenciesSection.tsx` (Task 6) · `DependencyMiniGraph.tsx` merge (Task 7) · `ServiceDetailPage.tsx` mount (Task 6) · OpenAPI regen (Task 5) · FE units (Tasks 6-7) · `list-filter-registry.md` (Task 8). All present.
- §5 contracts: `DerivedDependenciesResponse` `[BoundedListResult]` + `[ExcludeFromCodeCoverage]`, item `(ServiceId, DisplayName, TeamId, Paths)`, `DerivationPathDto` reused → Task 1.
- §6 FE: read-only two tables + provenance + `isRowHeader` + empty states (Task 6); dashed mini-graph edges + legend (Task 7).
- §7 error semantics: 400 empty id, 422 unknown/cross-tenant → Task-4 delegate + tests. **Deviation flagged** (Open decision): entityId-only ⇒ non-service also 422 (no `entityKind` param, so §7's "kind ≠ service → 400" row is N/A) — confirm at review.
- §8.2 tests: via-app + direct provenance (happy), dependents reverse, explicit-wins, 422 unknown, 400 missing, tenant isolation → Task 4 (7 cases; ≥1 happy + ≥1 negative ✓).

**Placeholder scan:** none. Every code step shows complete code. Two bounded adaptation notes (delegate `using`s already present; `Badge` neutral color) are explicit display-only choices, not TODOs.

**Type consistency:** `DerivedDependencies.Edge(SourceServiceId, TargetServiceId, Paths)` / `.Path(ApiId, ViaAppId)` (B1) used identically in `DerivedEdgeLoader` (Task 2), `GetDerivedDependenciesHandler` (Task 3), `DerivedProvenanceNames.Map` (Task 2). `DerivedDependencyItem(ServiceId, DisplayName, TeamId, Paths)` consistent Task 1 ↔ Task 3. `DerivationPathDto(ApiId, ApiName, ViaApplicationId, ViaApplicationDisplayName)` (B1) consistent handler ↔ FE (`viaApplicationId`/`viaApplicationDisplayName` camelCase). `DerivedDependenciesResponse(Dependencies, Dependents)` consistent Task 1 ↔ 3 ↔ 6. FE `toGraphModel(focused, relationships, derived?)` + `GraphEdge.derived` consistent Task 7 def ↔ mini-graph consumer ↔ tests. `useDerivedDependencies(entityId, {enabled})` consistent Task 6 def ↔ Task 7 mini-graph.

**Impact analysis:** the one behavior-preserving refactor (`GraphTraversalHandler`) is proven by re-running B1's `GraphTraversalTests` + `GetCatalogGraphTests` (Task 2 Step 5); its only `Handle` caller (`CatalogEndpointDelegates.cs:893`) is unaffected (signature identical); the three extracted private members are file-local (grep-confirmed). All other C# changes are additive; TS optional-param/optional-field changes keep existing `toEqual` assertions green.

**Scope check:** production business code ≈ contracts (~25) + `DerivedEdgeLoader`/`DerivedProvenanceNames` (net ~0, moved) + query+handler (~55) + delegate+module (~30) + FE hook (~20) + section (~110) + mini-graph/graphModel merge (~40) ≈ **~280 LOC** (excl. tests/generated) — well under the 800 ceiling.

**No blocking issues found** (one design tension flagged for review: endpoint shape).
