# Derived service↔service `depends-on` — Sub-slice B1 (graph explorer) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface *derived* service↔service `depends-on` edges (a service consumes an API another service provides) in the `GET /catalog/graph` traversal and render them as dashed, provenance-labeled edges in the standalone graph explorer.

**Architecture:** A pure Application helper (`DerivedDependencies.Compute`) derives the edges from four RLS-scoped edge-sets. `GraphTraversalHandler` precomputes the tenant's full derived-edge set **once** per request; the existing BFS fetch-closure then returns persisted ∪ derived edges touching each frontier, so derived edges drive node discovery with no change to `GraphTraversal.BuildAsync`. Derived edges are returned in a **new, separate** `GraphResponse.DerivedEdges` collection (never mixed into the persisted `Edges`), carrying full provenance (linking API + optional via-app). The frontend folds them into `mergeGraphs`/`layoutGraph` as dashed edges with a legend.

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs · EF Core (owned `EntityRef`) · MSTest v4 + native asserts · Testcontainers (real Postgres/RLS + real JWT via `KartovaApiFixtureBase`) · React + TypeScript · @xyflow/react (React Flow) + dagre · Vitest.

**Spec:** `docs/superpowers/specs/2026-07-09-catalog-derived-service-dependencies-design.md` (this plan implements **B1** only; B2 is a separate later plan).

## Global Constraints

- **Windows shell:** `cmd //c` (double slash) or PowerShell wrappers for `dotnet`; Git Bash lacks `grep -P` (use `-E`/`Select-String`). Multi-line git messages via PowerShell tool + multiple `-m` flags.
- **Solution:** `Kartova.slnx` (build with `dotnet build Kartova.slnx`). `TreatWarningsAsErrors=true` — 0 warnings.
- **Contracts coverage:** every `*Response`/`*Dto` carries `[ExcludeFromCodeCoverage]` (ContractsCoverageRules arch test fails otherwise).
- **Enum wire format:** camelCase (ADR-0109) — `direct`/`derived` etc. serialize camelCase automatically via the configured `JsonStringEnumConverter`; do not hand-map.
- **Derivation scope:** **Service↔Service only** (ADR-0111 §5). `S != T` (no self-edge). One collapsed edge per ordered `(S,T)` pair, provenance = list of paths `{ apiId, apiName, viaAppId?, viaAppName? }` (viaApp null when T provides the API directly). **Explicit wins:** if a persisted `DependsOn` edge `S→T` exists, no derived edge for that pair.
- **Derived edges are read-only** — never persisted, authored, or deletable. `catalog.read` only; no new permission, no migration.
- **RLS:** every DB read runs under the request `ITenantScope`; never bypass it. Cross-tenant edges must never appear (asserted).
- **Tests:** MSTest `[TestClass]`/`[TestMethod]`, native `Assert.*` (no FluentAssertions). Real-seam integration via `KartovaApiFixtureBase` (real JWT + Postgres/RLS).
- **`.cs` files are LF** (`.gitattributes eol=lf`); do not introduce CRLF.

## Impact Analysis (codelens/LSP)

This slice **modifies existing C# symbols**. Blast radius grounded via `Grep` + built-in `LSP` on 2026-07-09 (roslyn-codelens MCP not indexed this session; all targets are records/methods — grep-reliable, not `const`). Every caller below is covered by a task.

| Symbol | Change | References | Covered by |
|--------|--------|-----------|-----------|
| `GraphResponse` (record ctor) | +`DerivedEdges` positional param | **1** production ctor: `GraphTraversalHandler.cs:63`; `.Produces<GraphResponse>` (`CatalogModule.cs:163`) unaffected; **8** test deserialization sites (by-name JSON — extra field harmless) | Task 1 (ctor), Task 3 (assertions) |
| `GraphTraversalEdge` (record ctor) | +`Provenance` nullable param **with default `= null`** | ctors at `GraphTraversalHandler.cs:28`, `GraphTraversalTests.cs:17` — default ⇒ **no forced change** | Task 3 (handler sets it); existing callers unchanged |
| `GraphTraversal.BuildAsync` | **signature unchanged** (derivation lives in the handler closure) | `GraphTraversalHandler.cs:19` + 6 `GraphTraversalTests.cs` sites | untouched (deliberate) |
| `GraphTraversalHandler.Handle` | behavior only (populates `DerivedEdges`); signature stays | **1** caller `CatalogEndpointDelegates.cs:914` | Task 3 |

No shared `const`/enum value is modified — `RelationshipType.{DependsOn,ProvidesApiFor,ConsumesApiFrom,InstanceOf}`, `EntityKind.{Service,Application,Api}` are **read** only. New symbols (`DerivedDependencies`, `DerivedEdgeDto`, `DerivationPathDto`) have no existing consumers.

---

### Task 1: Contracts — `DerivationPathDto`, `DerivedEdgeDto`, extend `GraphResponse`

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/DerivationPathDto.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/DerivedEdgeDto.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Contracts/GraphResponse.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs:63` (update the one `new GraphResponse(...)` call to compile)

**Interfaces:**
- Produces: `DerivationPathDto(Guid ApiId, string ApiName, Guid? ViaApplicationId, string? ViaApplicationDisplayName)`; `DerivedEdgeDto(GraphEndpointDto Source, GraphEndpointDto Target, IReadOnlyList<DerivationPathDto> Paths)`; `GraphResponse` gains 3rd positional param `IReadOnlyList<DerivedEdgeDto> DerivedEdges`.

- [ ] **Step 1: Create `DerivationPathDto`**

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>One provenance path explaining a derived depends-on edge: the API that links consumer→provider,
/// and (when the provider exposes it through an application) the via-application. <see cref="ViaApplicationId"/>
/// is null when the provider service provides the API directly.</summary>
[ExcludeFromCodeCoverage]
public sealed record DerivationPathDto(
    Guid ApiId, string ApiName, Guid? ViaApplicationId, string? ViaApplicationDisplayName);
```

- [ ] **Step 2: Create `DerivedEdgeDto`**

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>A derived service→service depends-on edge (never persisted). Type is implicitly depends-on
/// (the only derived edge kind), so — unlike <see cref="GraphEdgeDto"/> — it carries no Type/Origin.
/// <see cref="Paths"/> lists every API (and optional via-app) linking source→target.</summary>
[ExcludeFromCodeCoverage]
public sealed record DerivedEdgeDto(
    GraphEndpointDto Source, GraphEndpointDto Target, IReadOnlyList<DerivationPathDto> Paths);
```

- [ ] **Step 3: Extend `GraphResponse`** — add `DerivedEdges` as the 3rd positional param

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record GraphResponse(
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphEdgeDto> Edges,
    IReadOnlyList<DerivedEdgeDto> DerivedEdges,
    bool Truncated);

[ExcludeFromCodeCoverage]
public sealed record GraphNodeDto(
    EntityKind Kind, Guid Id, string DisplayName, int Depth, Guid? TeamId);

[ExcludeFromCodeCoverage]
public sealed record GraphEdgeDto(
    Guid Id, GraphEndpointDto Source, GraphEndpointDto Target,
    RelationshipType Type, RelationshipOrigin Origin);

[ExcludeFromCodeCoverage]
public sealed record GraphEndpointDto(EntityKind Kind, Guid Id);
```

- [ ] **Step 4: Update the one construction site** — `GraphTraversalHandler.cs:63`, pass an empty list for now so the solution compiles (real population comes in Task 3):

```csharp
return new GraphResponse(nodes, edges, Array.Empty<DerivedEdgeDto>(), result.Truncated);
```

- [ ] **Step 5: Build to verify it compiles**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: 0 errors, 0 warnings.

- [ ] **Step 6: Run the existing graph tests to confirm no regression**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter FullyQualifiedName~GraphTraversalTests"`
Expected: PASS (unchanged — `GraphTraversalEdge` not yet modified).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Contracts/ src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs
git commit -m "feat(catalog): add DerivedEdgeDto/DerivationPathDto + GraphResponse.DerivedEdges (empty)"
```

---

### Task 2: `DerivedDependencies.Compute` helper (pure — the derivation core)

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/DerivedDependencies.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/DerivedDependenciesTests.cs`

**Interfaces:**
- Produces:
  - `DerivedDependencies.Path(Guid ApiId, Guid? ViaAppId)` — provenance without names (names joined by the handler).
  - `DerivedDependencies.Edge(Guid SourceServiceId, Guid TargetServiceId, IReadOnlyList<Path> Paths)`.
  - `static IReadOnlyList<Edge> Compute(IReadOnlyCollection<(Guid ServiceId, Guid ApiId)> consumes, IReadOnlyCollection<(Guid ServiceId, Guid ApiId)> serviceProvides, IReadOnlyCollection<(Guid ServiceId, Guid AppId)> instanceOf, IReadOnlyCollection<(Guid AppId, Guid ApiId)> appProvides, IReadOnlySet<(Guid Source, Guid Target)> explicitDependsOn)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Kartova.Catalog.Application;

namespace Kartova.Catalog.Tests;

[TestClass]
public class DerivedDependenciesTests
{
    private static readonly Guid S = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid T = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid U = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid App = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid App2 = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid Api1 = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid Api2 = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private static readonly IReadOnlySet<(Guid, Guid)> NoExplicit = new HashSet<(Guid, Guid)>();

    private static IReadOnlyList<DerivedDependencies.Edge> Compute(
        (Guid, Guid)[] consumes = null!, (Guid, Guid)[] serviceProvides = null!,
        (Guid, Guid)[] instanceOf = null!, (Guid, Guid)[] appProvides = null!,
        IReadOnlySet<(Guid, Guid)> explicitDeps = null!) =>
        DerivedDependencies.Compute(
            consumes ?? [], serviceProvides ?? [], instanceOf ?? [], appProvides ?? [], explicitDeps ?? NoExplicit);

    [TestMethod]
    public void direct_provide_path_yields_edge_with_null_via_app()
    {
        // S consumes Api1; T provides Api1 directly → S depends-on T, viaApp null.
        var edges = Compute(consumes: [(S, Api1)], serviceProvides: [(T, Api1)]);
        var e = edges.Single();
        Assert.AreEqual(S, e.SourceServiceId);
        Assert.AreEqual(T, e.TargetServiceId);
        var p = e.Paths.Single();
        Assert.AreEqual(Api1, p.ApiId);
        Assert.IsNull(p.ViaAppId);
    }

    [TestMethod]
    public void via_app_path_yields_edge_with_via_app_populated()
    {
        // S consumes Api1; T instance-of App; App provides Api1 → viaApp = App.
        var edges = Compute(consumes: [(S, Api1)], instanceOf: [(T, App)], appProvides: [(App, Api1)]);
        var p = edges.Single().Paths.Single();
        Assert.AreEqual(Api1, p.ApiId);
        Assert.AreEqual(App, p.ViaAppId);
    }

    [TestMethod]
    public void same_api_direct_and_via_app_dedupes_to_two_distinct_paths()
    {
        // T both provides Api1 directly AND via App. Distinct paths (null via + App via), one edge.
        var edges = Compute(
            consumes: [(S, Api1)], serviceProvides: [(T, Api1)],
            instanceOf: [(T, App)], appProvides: [(App, Api1)]);
        var e = edges.Single();
        Assert.AreEqual(2, e.Paths.Count);
        Assert.IsTrue(e.Paths.Any(p => p.ViaAppId is null));
        Assert.IsTrue(e.Paths.Any(p => p.ViaAppId == App));
    }

    [TestMethod]
    public void multiple_apis_collapse_to_one_edge_with_multiple_paths()
    {
        // S consumes Api1 and Api2; T provides both → one edge, two paths.
        var edges = Compute(consumes: [(S, Api1), (S, Api2)], serviceProvides: [(T, Api1), (T, Api2)]);
        var e = edges.Single();
        Assert.AreEqual(2, e.Paths.Count);
        CollectionAssert.AreEquivalent(new[] { Api1, Api2 }, e.Paths.Select(p => p.ApiId).ToArray());
    }

    [TestMethod]
    public void explicit_depends_on_suppresses_derived_edge_for_that_pair()
    {
        var edges = Compute(
            consumes: [(S, Api1)], serviceProvides: [(T, Api1)],
            explicitDeps: new HashSet<(Guid, Guid)> { (S, T) });
        Assert.AreEqual(0, edges.Count);
    }

    [TestMethod]
    public void self_dependency_is_never_emitted()
    {
        // S consumes Api1 and S also provides Api1 → no S→S edge.
        var edges = Compute(consumes: [(S, Api1)], serviceProvides: [(S, Api1)]);
        Assert.AreEqual(0, edges.Count);
    }

    [TestMethod]
    public void consumer_without_matching_provider_yields_nothing()
    {
        var edges = Compute(consumes: [(S, Api1)], serviceProvides: [(T, Api2)]);
        Assert.AreEqual(0, edges.Count);
    }

    [TestMethod]
    public void two_consumers_of_same_provider_yield_two_edges()
    {
        // S and U both consume Api1 that T provides → S→T and U→T.
        var edges = Compute(consumes: [(S, Api1), (U, Api1)], serviceProvides: [(T, Api1)]);
        Assert.AreEqual(2, edges.Count);
        Assert.IsTrue(edges.Any(e => e.SourceServiceId == S && e.TargetServiceId == T));
        Assert.IsTrue(edges.Any(e => e.SourceServiceId == U && e.TargetServiceId == T));
    }

    [TestMethod]
    public void empty_inputs_yield_empty()
    {
        Assert.AreEqual(0, Compute().Count);
    }

    [TestMethod]
    public void paths_are_deterministically_ordered()
    {
        // Two apps expose the same API for T; order must be stable (by apiId, then viaAppId).
        var edges = Compute(
            consumes: [(S, Api1)], instanceOf: [(T, App2), (T, App)], appProvides: [(App, Api1), (App2, Api1)]);
        var vias = edges.Single().Paths.Select(p => p.ViaAppId).ToList();
        var sorted = vias.OrderBy(v => v).ToList();
        CollectionAssert.AreEqual(sorted, vias);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter FullyQualifiedName~DerivedDependenciesTests"`
Expected: FAIL — `DerivedDependencies` does not exist.

- [ ] **Step 3: Implement the helper**

```csharp
namespace Kartova.Catalog.Application;

/// <summary>Pure derivation of service→service depends-on edges (ADR-0111 §Decision 5):
/// S depends-on T when S consumes an API in T's provided surface (T provides directly, or T is
/// instance-of an application that provides it). Service↔Service only; no self-edge; explicit
/// depends-on pairs are suppressed. Names are joined later by the handler.</summary>
public static class DerivedDependencies
{
    /// <summary>One provenance path: the linking API and (for a via-app exposure) the application.
    /// <paramref name="ViaAppId"/> is null when the provider provides the API directly.</summary>
    public sealed record Path(Guid ApiId, Guid? ViaAppId);

    /// <summary>A derived S→T edge with every provenance path linking them.</summary>
    public sealed record Edge(Guid SourceServiceId, Guid TargetServiceId, IReadOnlyList<Path> Paths);

    public static IReadOnlyList<Edge> Compute(
        IReadOnlyCollection<(Guid ServiceId, Guid ApiId)> consumes,
        IReadOnlyCollection<(Guid ServiceId, Guid ApiId)> serviceProvides,
        IReadOnlyCollection<(Guid ServiceId, Guid AppId)> instanceOf,
        IReadOnlyCollection<(Guid AppId, Guid ApiId)> appProvides,
        IReadOnlySet<(Guid Source, Guid Target)> explicitDependsOn)
    {
        // providersByApi: apiId → set of (providerServiceId, viaAppId?) — T's provided surface.
        var providersByApi = new Dictionary<Guid, HashSet<(Guid Provider, Guid? ViaApp)>>();

        void Add(Guid apiId, Guid provider, Guid? viaApp)
        {
            if (!providersByApi.TryGetValue(apiId, out var set))
                providersByApi[apiId] = set = [];
            set.Add((provider, viaApp));
        }

        foreach (var (svc, api) in serviceProvides) Add(api, svc, null);

        // instance-of ⋈ app-provides: service T instance-of App A, A provides X → T exposes X via A.
        var appProvidesByApp = appProvides
            .GroupBy(x => x.AppId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ApiId).ToList());
        foreach (var (svc, app) in instanceOf)
            if (appProvidesByApp.TryGetValue(app, out var apis))
                foreach (var api in apis)
                    Add(api, svc, app);

        // For each (S consumes X) and each provider (T, viaApp) of X with S != T, collect a path.
        var pathsByPair = new Dictionary<(Guid S, Guid T), List<Path>>();
        foreach (var (consumer, api) in consumes)
        {
            if (!providersByApi.TryGetValue(api, out var providers)) continue;
            foreach (var (provider, viaApp) in providers)
            {
                if (provider == consumer) continue;                          // no self-edge
                if (explicitDependsOn.Contains((consumer, provider))) continue; // explicit wins
                var key = (consumer, provider);
                if (!pathsByPair.TryGetValue(key, out var list))
                    pathsByPair[key] = list = [];
                list.Add(new Path(api, viaApp));
            }
        }

        return pathsByPair
            .Select(kv => new Edge(
                kv.Key.S, kv.Key.T,
                kv.Value
                    .Distinct()
                    .OrderBy(p => p.ApiId).ThenBy(p => p.ViaAppId)
                    .ToList()))
            .OrderBy(e => e.SourceServiceId).ThenBy(e => e.TargetServiceId)
            .ToList();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter FullyQualifiedName~DerivedDependenciesTests"`
Expected: PASS (10/10).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/DerivedDependencies.cs src/Modules/Catalog/Kartova.Catalog.Tests/DerivedDependenciesTests.cs
git commit -m "feat(catalog): DerivedDependencies.Compute pure derivation helper + unit tests"
```

---

### Task 3: Wire derived edges into `GraphTraversalHandler` + real-seam tests

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversal.cs` (add `Provenance` to `GraphTraversalEdge`)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs` (precompute derived edges; merge in closure; map to `DerivedEdgeDto`)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs` (add derived-edge cases)

**Interfaces:**
- Consumes: `DerivedDependencies.Compute(...)`, `DerivedEdgeDto`, `DerivationPathDto`, `GraphResponse` (Task 1/2).
- Produces: `GraphResponse.DerivedEdges` populated with deduped derived edges among kept nodes, provenance names resolved.

- [ ] **Step 1: Add `Provenance` to `GraphTraversalEdge`** (default `= null` ⇒ existing callers unaffected). Edit `GraphTraversal.cs` line 6-7:

```csharp
public sealed record GraphTraversalEdge(
    EntityRef Source, EntityRef Target, Guid Id, RelationshipType Type, RelationshipOrigin Origin,
    IReadOnlyList<DerivedDependencies.Path>? Provenance = null);
```

- [ ] **Step 2: Build to confirm no caller breaks**

Run: `cmd //c "dotnet build Kartova.slnx"`
Expected: 0 errors (default param keeps `GraphTraversalTests` + handler ctor valid).

- [ ] **Step 3: Write the failing integration tests** — append to `GetCatalogGraphTests.cs`. Follow the file's existing seeding + fixture pattern (`KartovaApiFixtureBase`, `WireJson`). Insert these `[TestMethod]`s inside the class:

```csharp
    [TestMethod]
    public async Task derived_depends_on_appears_with_provenance_and_drives_discovery()
    {
        // Topology: S --consumes--> Api1 ; T --instance-of--> App --provides--> Api1.
        // Expect: focus=S returns a derived S→T edge with provenance {Api1 via App}, and T is a discovered node
        // even though there is NO persisted edge directly between S and T.
        var (client, _) = await AuthedClientAsync();
        var app = await SeedApplicationAsync(client, "Provider App");
        var svcT = await SeedServiceAsync(client, "Provider Svc");
        var svcS = await SeedServiceAsync(client, "Consumer Svc");
        var api = await SeedApiAsync(client, "Orders API", providerTeamId: null);
        await CreateRelationshipAsync(client, "service", svcT, "instance-of", "application", app);
        await CreateRelationshipAsync(client, "application", app, "provides-api-for", "api", api);
        await CreateRelationshipAsync(client, "service", svcS, "consumes-api-from", "api", api);

        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=service&entityId={svcS}&depth=2&direction=all");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        Assert.IsTrue(graph!.Nodes.Any(n => n.Id == svcT), "provider service T should be discovered via the derived edge");
        var derived = graph.DerivedEdges.Single(e => e.Source.Id == svcS && e.Target.Id == svcT);
        var path = derived.Paths.Single();
        Assert.AreEqual(api, path.ApiId);
        Assert.AreEqual("Orders API", path.ApiName);
        Assert.AreEqual(app, path.ViaApplicationId);
        Assert.AreEqual("Provider App", path.ViaApplicationDisplayName);
    }

    [TestMethod]
    public async Task explicit_depends_on_suppresses_the_derived_duplicate()
    {
        // Same consume/provide topology PLUS an explicit depends-on S→T. Expect: persisted edge in Edges,
        // and NO derived edge for (S,T).
        var (client, _) = await AuthedClientAsync();
        var svcT = await SeedServiceAsync(client, "Prov Svc 2");
        var svcS = await SeedServiceAsync(client, "Cons Svc 2");
        var api = await SeedApiAsync(client, "Billing API", providerTeamId: null);
        await CreateRelationshipAsync(client, "service", svcT, "provides-api-for", "api", api);
        await CreateRelationshipAsync(client, "service", svcS, "consumes-api-from", "api", api);
        await CreateRelationshipAsync(client, "service", svcS, "depends-on", "service", svcT);

        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=service&entityId={svcS}&depth=2&direction=all");
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        Assert.IsFalse(graph!.DerivedEdges.Any(e => e.Source.Id == svcS && e.Target.Id == svcT),
            "explicit depends-on must suppress the derived duplicate");
        Assert.IsTrue(graph.Edges.Any(e =>
            e.Source.Id == svcS && e.Target.Id == svcT && e.Type == RelationshipType.DependsOn));
    }

    [TestMethod]
    public async Task derived_edges_never_cross_tenants()
    {
        // Consumer in tenant A; provider topology in tenant B. Focus in A must see no derived edge.
        var (clientA, _) = await AuthedClientAsync();
        var svcS = await SeedServiceAsync(clientA, "A Consumer");
        var api = await SeedApiAsync(clientA, "A API", providerTeamId: null);
        await CreateRelationshipAsync(clientA, "service", svcS, "consumes-api-from", "api", api);
        // No provider of that API in tenant A → no derived edge.

        var resp = await clientA.GetAsync($"/api/v1/catalog/graph?entityKind=service&entityId={svcS}&depth=2&direction=all");
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(0, graph!.DerivedEdges.Count);
    }
```

> **NOTE for the implementer:** reuse the seeding helpers already in `GetCatalogGraphTests.cs`/`CreateRelationshipTests.cs`. If a helper (`SeedApiAsync`, `SeedServiceAsync`, `CreateRelationshipAsync`, `AuthedClientAsync`) is named differently or missing, adapt to the existing ones (they exist for the connectivity-edges + api-entity slices) rather than inventing new fixture plumbing. Relationship type wire values are kebab-case (`instance-of`, `provides-api-for`, `consumes-api-from`, `depends-on`).

- [ ] **Step 4: Run the new integration tests to verify they fail**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~GetCatalogGraphTests"`
Expected: the three new tests FAIL (`DerivedEdges` empty); existing graph tests still PASS.

- [ ] **Step 5: Implement the handler wiring.** Replace `GraphTraversalHandler.Handle` with the version below. It (a) precomputes the tenant's full derived-edge set once, (b) merges persisted + derived edges touching each frontier in the fetch closure (so derived edges drive discovery), (c) maps kept derived edges to `DerivedEdgeDto` with resolved names.

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

public sealed class GraphTraversalHandler
{
    public const int DefaultNodeCap = 200;

    public async Task<GraphResponse> Handle(
        GraphTraversalQuery q,
        CatalogDbContext db,
        ICatalogEntityLookup lookup,
        CancellationToken ct,
        int? maxNodes = null)
    {
        // Precompute the tenant's full set of derived service→service depends-on edges ONCE (RLS-scoped).
        // Keyed by (source,target) for fast frontier lookup; provenance carried on the edge.
        var derivedAll = await ComputeDerivedEdges(db, ct);
        var derivedBySource = derivedAll.ToLookup(e => e.SourceServiceId);
        var derivedByTarget = derivedAll.ToLookup(e => e.TargetServiceId);

        var result = await GraphTraversal.BuildAsync(
            q.Focus, q.Depth, q.Direction, maxNodes ?? DefaultNodeCap,
            async (frontier, token) =>
            {
                var ids = frontier.Select(f => f.Id).ToList();
                var rows = await db.Relationships
                    .Where(r => ids.Contains(r.Source.Id) || ids.Contains(r.Target.Id))
                    .ToListAsync(token);

                var edges = rows
                    .Select(r => new GraphTraversalEdge(
                        new EntityRef(r.Source.Kind, r.Source.Id),
                        new EntityRef(r.Target.Kind, r.Target.Id),
                        r.Id.Value, r.Type, r.Origin))
                    .ToList();

                // Derived edges touching the frontier (as source S or target T). Synthetic deterministic
                // Id so BFS keptEdges dedup is stable; Origin is a placeholder (never surfaced for derived).
                var frontierIds = ids.ToHashSet();
                var derivedHits = frontier
                    .Where(f => f.Kind == EntityKind.Service)
                    .SelectMany(f => derivedBySource[f.Id].Concat(derivedByTarget[f.Id]))
                    .Where(e => frontierIds.Contains(e.SourceServiceId) || frontierIds.Contains(e.TargetServiceId))
                    .DistinctBy(e => (e.SourceServiceId, e.TargetServiceId))
                    .Select(e => new GraphTraversalEdge(
                        new EntityRef(EntityKind.Service, e.SourceServiceId),
                        new EntityRef(EntityKind.Service, e.TargetServiceId),
                        SyntheticEdgeId(e.SourceServiceId, e.TargetServiceId),
                        RelationshipType.DependsOn, RelationshipOrigin.Manual, e.Paths));

                edges.AddRange(derivedHits);
                return (IReadOnlyList<GraphTraversalEdge>)edges;
            },
            ct);

        // Node enrichment (displayName + teamId), bounded by the node cap.
        var info = new Dictionary<(EntityKind, Guid), EntityLookupResult?>();
        foreach (var n in result.Nodes)
        {
            var key = (n.Ref.Kind, n.Ref.Id);
            if (!info.ContainsKey(key))
                info[key] = await lookup.Find(n.Ref.Kind, n.Ref.Id, ct);
        }

        var nodes = result.Nodes.Select(n =>
        {
            var found = info[(n.Ref.Kind, n.Ref.Id)];
            return new GraphNodeDto(n.Ref.Kind, n.Ref.Id, found?.DisplayName ?? string.Empty, n.Depth, found?.TeamId);
        }).ToList();

        // Persisted edges (Provenance == null) → GraphEdgeDto; derived edges (Provenance != null) → DerivedEdgeDto.
        var persisted = result.Edges.Where(e => e.Provenance is null)
            .Select(e => new GraphEdgeDto(
                e.Id, new GraphEndpointDto(e.Source.Kind, e.Source.Id),
                new GraphEndpointDto(e.Target.Kind, e.Target.Id), e.Type, e.Origin))
            .ToList();

        var derivedKept = result.Edges.Where(e => e.Provenance is not null).ToList();
        var derivedEdges = await MapDerivedEdges(derivedKept, db, ct);

        return new GraphResponse(nodes, persisted, derivedEdges, result.Truncated);
    }

    private static Guid SyntheticEdgeId(Guid source, Guid target)
    {
        // Deterministic GUID from the ordered pair (XOR bytes) — stable across the two BFS scans.
        var a = source.ToByteArray();
        var b = target.ToByteArray();
        var mixed = new byte[16];
        for (var i = 0; i < 16; i++) mixed[i] = (byte)(a[i] ^ b[(i + 7) % 16]);
        return new Guid(mixed);
    }

    private static async Task<IReadOnlyList<DerivedDependencies.Edge>> ComputeDerivedEdges(
        CatalogDbContext db, CancellationToken ct)
    {
        var rels = await db.Relationships
            .Where(r => r.Type == RelationshipType.ConsumesApiFrom
                     || r.Type == RelationshipType.ProvidesApiFor
                     || r.Type == RelationshipType.InstanceOf
                     || r.Type == RelationshipType.DependsOn)
            .Select(r => new { r.Type, SK = r.Source.Kind, SI = r.Source.Id, TK = r.Target.Kind, TI = r.Target.Id })
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

    private static async Task<IReadOnlyList<DerivedEdgeDto>> MapDerivedEdges(
        IReadOnlyList<GraphTraversalEdge> derivedKept, CatalogDbContext db, CancellationToken ct)
    {
        if (derivedKept.Count == 0) return Array.Empty<DerivedEdgeDto>();

        var apiIds = derivedKept.SelectMany(e => e.Provenance!).Select(p => p.ApiId).Distinct().ToList();
        var appIds = derivedKept.SelectMany(e => e.Provenance!)
            .Where(p => p.ViaAppId is not null).Select(p => p.ViaAppId!.Value).Distinct().ToList();

        var apiNames = await db.Apis
            .Where(a => apiIds.Contains(EF.Property<Guid>(a, EfApiConfiguration.IdFieldName)))
            .Select(a => new { Id = EF.Property<Guid>(a, EfApiConfiguration.IdFieldName), a.DisplayName })
            .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
        var appNames = appIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await db.Applications
                .Where(a => appIds.Contains(EF.Property<Guid>(a, EfApplicationConfiguration.IdFieldName)))
                .Select(a => new { Id = EF.Property<Guid>(a, EfApplicationConfiguration.IdFieldName), a.DisplayName })
                .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);

        return derivedKept.Select(e => new DerivedEdgeDto(
            new GraphEndpointDto(e.Source.Kind, e.Source.Id),
            new GraphEndpointDto(e.Target.Kind, e.Target.Id),
            e.Provenance!.Select(p => new DerivationPathDto(
                p.ApiId,
                apiNames.TryGetValue(p.ApiId, out var an) ? an : string.Empty,
                p.ViaAppId,
                p.ViaAppId is { } via && appNames.TryGetValue(via, out var pn) ? pn : null)).ToList()))
            .ToList();
    }
}
```

- [ ] **Step 6: Run the full Catalog integration graph tests**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~GetCatalogGraphTests"`
Expected: PASS (new 3 + all existing). If a Testcontainers `TimeoutException` appears, re-run the assembly in isolation before treating it as red (documented flake).

- [ ] **Step 7: Run the Catalog unit suite to confirm `GraphTraversalTests` still green**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversal.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs
git commit -m "feat(catalog): derive service depends-on edges in graph traversal (drives discovery) + real-seam tests"
```

---

### Task 4: Regenerate the OpenAPI client + snapshot

**Files:**
- Modify: `web/openapi-snapshot.json` (regenerated)
- Modify: `web/src/generated/*` (regenerated)

**Interfaces:**
- Produces: `components["schemas"]["GraphResponse"]` gains `derivedEdges: DerivedEdgeDto[]`; new `DerivedEdgeDto`, `DerivationPathDto` schemas — consumed by Task 5.

- [ ] **Step 1: Rebuild the API image so the running API exposes the new schema**

Run: `cmd //c "docker compose build api"`
Expected: build succeeds.

- [ ] **Step 2: Regenerate the client + snapshot** (predev/prebuild regenerates from the live API; or run the repo's codegen script directly)

Run: `cmd //c "cd web && npm run codegen"` (use the actual codegen script name if different — check `web/package.json` `scripts`)
Expected: `web/src/generated/openapi.d.ts` + `web/openapi-snapshot.json` now include `derivedEdges` on `GraphResponse` and the two new DTO schemas.

- [ ] **Step 3: Sanity-check the diff**

Run: `git --no-pager diff --stat web/openapi-snapshot.json web/src/generated`
Expected: additive changes only (new `derivedEdges` field + DTOs). Param-order-only churn is cosmetic — keep it.

- [ ] **Step 4: Commit**

```bash
git add web/openapi-snapshot.json web/src/generated
git commit -m "chore(web): regenerate OpenAPI client for GraphResponse.derivedEdges"
```

---

### Task 5: Frontend — dashed derived edges + legend in the explorer

**Files:**
- Modify: `web/src/features/catalog/relationships/graphMerge.ts` (fold `derivedEdges` into `ExplorerEdge[]`)
- Modify: `web/src/features/catalog/relationships/graphLayout.ts` (dashed style for derived edges)
- Modify: `web/src/features/catalog/pages/GraphExplorerPage.tsx` (legend Panel)
- Test: `web/src/features/catalog/relationships/__tests__/graphMerge.test.ts` (create or extend)
- Test: `web/src/features/catalog/relationships/__tests__/graphLayout.test.ts` (create or extend)

**Interfaces:**
- Consumes: regenerated `GraphResponse` with `derivedEdges` (Task 4).
- Produces: `ExplorerEdge` gains `derived?: boolean` and `provenance?: { apiName: string; viaAppName?: string | null }[]`; derived edges carry `style.strokeDasharray` + a compact provenance `label`.

- [ ] **Step 1: Write the failing `mergeGraphs` test** — extend `graphMerge.test.ts` (create if absent). Assert a `derivedEdges` entry becomes a dashed `ExplorerEdge` with a synthetic id, `derived: true`, provenance, and a compact label:

```ts
import { describe, it, expect } from "vitest";
import { mergeGraphs } from "@/features/catalog/relationships/graphMerge";
import type { GraphResponse } from "@/features/catalog/api/graph";

const S = "11111111-1111-1111-1111-111111111111";
const T = "22222222-2222-2222-2222-222222222222";
const API = "66666666-6666-6666-6666-666666666666";
const APP = "44444444-4444-4444-4444-444444444444";

const graph = (over: Partial<GraphResponse>): GraphResponse => ({
  nodes: [
    { kind: "service", id: S, displayName: "Consumer", depth: 0, teamId: null },
    { kind: "service", id: T, displayName: "Provider", depth: 1, teamId: null },
  ],
  edges: [],
  derivedEdges: [],
  truncated: false,
  ...over,
}) as GraphResponse;

describe("mergeGraphs — derived edges", () => {
  it("folds a derived edge into a dashed ExplorerEdge with provenance", () => {
    const g = mergeGraphs([
      graph({
        derivedEdges: [
          {
            source: { kind: "service", id: S },
            target: { kind: "service", id: T },
            paths: [{ apiId: API, apiName: "Orders API", viaApplicationId: APP, viaApplicationDisplayName: "Provider App" }],
          },
        ],
      }),
    ]);
    const edge = g.edges.find((e) => e.derived);
    expect(edge).toBeDefined();
    expect(edge!.source).toBe(`service:${S}`);
    expect(edge!.target).toBe(`service:${T}`);
    expect(edge!.label).toContain("Orders API");
    expect(edge!.id).toBe(`service:${S}->service:${T}:derived`);
  });

  it("emits no derived edge when derivedEdges is empty", () => {
    const g = mergeGraphs([graph({})]);
    expect(g.edges.some((e) => e.derived)).toBe(false);
  });
});
```

- [ ] **Step 2: Run to verify it fails**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/relationships/__tests__/graphMerge.test.ts"`
Expected: FAIL (no `derived` field / not folded).

- [ ] **Step 3: Implement the merge.** Edit `graphMerge.ts`: extend `ExplorerEdge`, and after the persisted-edge loop, fold `r.derivedEdges`:

```ts
export type ExplorerEdge = {
  id: string;
  source: string;
  target: string;
  label: string;
  derived?: boolean;
  provenance?: { apiName: string; viaAppName?: string | null }[];
};
```

Inside `mergeGraphs`, after the `for (const e of r.edges)` block, add:

```ts
    for (const d of r.derivedEdges ?? []) {
      const source = nodeId(d.source.kind, d.source.id);
      const target = nodeId(d.target.kind, d.target.id);
      const id = `${source}->${target}:derived`;
      if (edges.has(id)) continue;
      const apiNames = d.paths.map((p) => p.apiName);
      const label =
        apiNames.length === 1
          ? `depends on · via ${apiNames[0]}`
          : `depends on · via ${apiNames[0]} +${apiNames.length - 1}`;
      edges.set(id, {
        id,
        source,
        target,
        label,
        derived: true,
        provenance: d.paths.map((p) => ({ apiName: p.apiName, viaAppName: p.viaApplicationDisplayName })),
      });
    }
```

- [ ] **Step 4: Run the merge test to verify it passes**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/relationships/__tests__/graphMerge.test.ts"`
Expected: PASS.

- [ ] **Step 5: Write the failing `layoutGraph` test** — a derived edge must get a dashed `strokeDasharray` style:

```ts
import { describe, it, expect } from "vitest";
import { layoutGraph } from "@/features/catalog/relationships/graphLayout";
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";

const S = "service:aaaa", T = "service:bbbb";
const g: ExplorerGraph = {
  nodes: [
    { id: S, kind: "service", entityId: "aaaa", displayName: "S" },
    { id: T, kind: "service", entityId: "bbbb", displayName: "T" },
  ],
  edges: [{ id: `${S}->${T}:derived`, source: S, target: T, label: "depends on · via X", derived: true }],
  truncated: false,
};

describe("layoutGraph — derived edge styling", () => {
  it("renders derived edges dashed", () => {
    const { edges } = layoutGraph(g, S, null);
    const e = edges.find((x) => x.id === `${S}->${T}:derived`);
    expect(e).toBeDefined();
    expect(e!.style?.strokeDasharray).toBeDefined();
  });
});
```

- [ ] **Step 6: Run to verify it fails**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/relationships/__tests__/graphLayout.test.ts"`
Expected: FAIL (no dashed style).

- [ ] **Step 7: Implement the dashed style.** Edit the `edges` map in `graphLayout.ts` to merge a dashed style for derived edges (preserve the dim behavior):

```ts
  const edges: Edge[] = graph.edges.map((e) => {
    const dimmedStyle = dimmed.edgeIds.has(e.id) ? { opacity: 0.2 } : {};
    const derivedStyle = e.derived ? { strokeDasharray: "6 4", stroke: "var(--color-fg-quaternary, #98A2B3)" } : {};
    return {
      id: e.id,
      source: e.source,
      target: e.target,
      label: e.label,
      style: { ...derivedStyle, ...dimmedStyle },
    };
  });
```

- [ ] **Step 8: Run the layout test to verify it passes**

Run: `cmd //c "cd web && npx vitest run src/features/catalog/relationships/__tests__/graphLayout.test.ts"`
Expected: PASS.

- [ ] **Step 9: Add a legend to the explorer.** In `GraphExplorerPage.tsx`, add a second `<Panel position="bottom-left">` inside `<ReactFlow>` (after the existing `top-left` filter panel):

```tsx
                <Panel position="bottom-left">
                  <div className="rounded-md bg-primary/90 px-2 py-1 text-xs text-tertiary ring-1 ring-secondary">
                    <span className="mr-3">— explicit</span>
                    <span className="font-mono">- - derived</span>
                  </div>
                </Panel>
```

- [ ] **Step 10: Run the full web test suite + typecheck + build**

Run: `cmd //c "cd web && npm run build && npx vitest run"`
Expected: `tsc -b` 0 errors; all tests pass.

- [ ] **Step 11: Commit**

```bash
git add web/src/features/catalog/relationships/graphMerge.ts web/src/features/catalog/relationships/graphLayout.ts web/src/features/catalog/pages/GraphExplorerPage.tsx web/src/features/catalog/relationships/__tests__
git commit -m "feat(web): render derived depends-on as dashed edges + legend in graph explorer"
```

---

## Definition of Done (B1)

The ten always-blocking gates + conditional mutation gate in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim (not restated). B1-specific notes:

- **Gate 6 (mutation) is BLOCKING for B1** — the diff touches Application derivation logic (`DerivedDependencies.Compute`). Run `/misc:mutation-sentinel` → `/misc:test-generator` on `DerivedDependencies.cs` (+ the handler's derivation methods), target ≥80%, document survivors. This helper is the highest-value mutation target.
- **Gate 3 real-seam:** the graph endpoint is HTTP/auth/DB → the Task-3 integration tests hit the real seam (real JWT + Postgres/RLS via `KartovaApiFixtureBase`). ≥1 happy (`derived_depends_on_appears…`) + ≥1 negative (`derived_edges_never_cross_tenants`) ✓.
- **Gate 4 container build:** no Dockerfile/`COPY` change → runs unchanged on the PR.
- **Gate 10 visual/API:** cold-start dev server + live API, seed `S consumes X` + `T instance-of App provides X`, open `/graph?focus=service:S`, confirm the dashed derived edge + legend + provenance label; add an explicit `depends-on S→T` and confirm the derived duplicate disappears. Evidence under `docs/superpowers/verification/2026-07-09-catalog-derived-dependencies/b1/`.
- **DoD ledger:** copy `docs/superpowers/templates/dod-ledger-template.md` → `docs/superpowers/verification/2026-07-09-catalog-derived-dependencies/b1/dod.md`; copy `gate-findings-template.yaml` alongside. Update rows as gates run.
- **Pre-push:** `scripts/ci-local.sh` (Release mirror) green before PR.
- On merge: tick E-02.F-03 sub-slice-B1 progress in `docs/product/CHECKLIST.md`; note B2 (endpoint + mini-graph + `DerivedDependenciesSection`) as the next plan.

## Self-Review

**Spec coverage:** D1 on-read (Task 3 precompute) · D2 full surface (helper: `serviceProvides` ∪ instance-of⋈app-provides) · D3 one-edge-per-pair + path list (helper grouping + Task-5 merge) · D4 explicit-wins (helper `explicitDependsOn`, Task-2 + Task-3 tests) · D5 no self-edge (helper + test) · D6 `catalog.read` (endpoint unchanged) · D7 read-only (no authoring path). §4.2 drive-discovery = Task-3 closure returns derived edges. §6 dashed + legend + provenance label = Task 5. §8 helper unit + real-seam + FE units all present. §9 impact analysis = plan header, every caller mapped to a task.

**Placeholder scan:** none — every code step shows complete code; the one adaptation note (Task 3 Step 3 fixture helper names) is explicit and bounded, not a TODO.

**Type consistency:** `DerivedDependencies.Path(ApiId, ViaAppId)` / `.Edge(SourceServiceId, TargetServiceId, Paths)` used identically in Task 2 (def), Task 3 (`GraphTraversalEdge.Provenance`, `MapDerivedEdges`). `DerivationPathDto(ApiId, ApiName, ViaApplicationId, ViaApplicationDisplayName)` consistent Task 1 ↔ Task 3 ↔ Task 5 (`viaApplicationDisplayName`). `GraphResponse(Nodes, Edges, DerivedEdges, Truncated)` order consistent Task 1 ↔ Task 3 construction. FE `ExplorerEdge.derived`/`provenance` consistent Task 5 merge ↔ layout.

**Scope check:** B1 only (graph explorer). ~200–300 LOC production (helper + handler methods + FE merge/layout) — well under the 800 ceiling. B2 is a separate plan.
