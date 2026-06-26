# Catalog Dependency Graph Explorer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a standalone `/graph` dependency explorer (E-04.F-02.S-03/S-04): a backend BFS traversal endpoint plus a full-content, URL-driven, expandable React Flow graph reached from each entity's mini-graph.

**Architecture:** A new RLS-scoped, cycle-safe, depth-annotated BFS endpoint (`GET /api/v1/catalog/graph`) returns a purpose-built `GraphResponse { nodes, edges, truncated }`. The frontend explorer treats the URL (`?focus` + `?expand`) as the single source of truth, fetches the focus neighbourhood + each expanded node's neighbourhood, merges them, lays them out with dagre, and renders a read-only React Flow canvas where single-click expands/collapses and a per-node link opens the entity page.

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs · EF Core (RLS via `ITenantScope`) · React 19 + TS strict · React Router v7 · TanStack Query · `@xyflow/react` (React Flow) · `@dagrejs/dagre` · Vite/vitest · MSTest v4 + NSubstitute · Testcontainers (real Postgres) + real `JwtBearer`.

**Spec:** `docs/superpowers/specs/2026-06-26-catalog-graph-explorer-design.md`

## Global Constraints

- **Slice ceiling:** ~800 LOC production (this slice ~625). If a task pushes over, stop and flag — the URL-expand path is the split point (spec §4.3).
- **Build:** `TreatWarningsAsErrors=true` — 0 warnings, 0 errors.
- **Contracts coverage:** every `*Response`/`*Dto` carries `[ExcludeFromCodeCoverage]` (enforced by `tests/Kartova.ArchitectureTests/ContractsCoverageRules.cs`).
- **Wire format:** camelCase JSON globally (no per-property attributes — see `RelationshipResponse`). Enums serialize camelCase. Enum wire convention = ADR-0109.
- **Auth:** read endpoints use `RequireAuthorization(KartovaPermissions.CatalogRead)`; no new permission.
- **Tenant scope:** all DB work runs inside `ITenantScope`; query `db.Relationships` (RLS auto-scopes). Never bypass RLS.
- **Endpoint bounds:** `depth` ∈ 1..4 (default 2, out-of-range → 400); node cap = 200 → `truncated:true`. Defaults are user-approved.
- **Windows shell:** wrap `dotnet` in PowerShell or `cmd //c` from Git Bash; `grep -E`/`Select-String` (no `grep -P`). Commit multi-line messages via PowerShell with multiple `-m` flags.
- **.cs CRLF flip (host quirk):** editing existing `.cs` can flip LF→CRLF on this host (repo is LF). After committing a `.cs` change, verify `git show --stat` matches `git show -w --stat`; if not, normalize (`sed -i 's/\r$//'`) and amend. New `.cs` files: write with LF.
- **Codegen:** a new endpoint requires rebuilding the API image, then regenerating `web/src/generated/openapi.ts` + `web/openapi-snapshot.json` (predev/prebuild) and committing.
- **DoD:** the eight always-blocking gates in CLAUDE.md apply verbatim. Gate 6 (mutation) is **blocking** (C# Application logic changes). Maintain `docs/superpowers/verification/2026-06-26-catalog-graph-explorer/dod.md` + `gate-findings.yaml`.

---

## File Structure

**Backend — created:**
- `src/Modules/Catalog/Kartova.Catalog.Contracts/GraphResponse.cs` — `GraphResponse`, `GraphNodeDto`, `GraphEdgeDto`, `GraphEndpointDto` (all `[ExcludeFromCodeCoverage]`).
- `src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversalQuery.cs` — query record.
- `src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversal.cs` — pure BFS core + its node/edge/result types.
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs` — EF adapter (per-level fetch) + enrichment + mapping.

**Backend — modified:**
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` — `GetCatalogGraphAsync` delegate.
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` — route mapping + `AddScoped<GraphTraversalHandler>()`.

**Backend — tests:**
- `src/Modules/Catalog/Kartova.Catalog.Tests/GraphTraversalTests.cs` — pure-core unit tests.
- `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs` — real-seam.

**Frontend — created:**
- `web/src/features/catalog/relationships/graphMerge.ts` — pure merge of `GraphResponse[]` → `ExplorerGraph`.
- `web/src/features/catalog/relationships/graphLayout.ts` — pure dagre layout → React Flow nodes/edges.
- `web/src/features/catalog/api/graph.ts` — `useGraph` (multi-query) hook.
- `web/src/features/catalog/pages/GraphExplorerPage.tsx` — the explorer page.

**Frontend — modified:**
- `web/src/app/router.tsx` — lazy `/graph` route.
- `web/src/features/catalog/relationships/graphModel.ts` — add optional `detailHref` to `GraphNodeData`.
- `web/src/features/catalog/components/EntityGraphNode.tsx` — render the open-detail link when `data.detailHref` is set.
- `web/src/features/catalog/components/DependencyMiniGraph.tsx` — "Open full graph" link.
- `web/package.json` + lockfile — add `@dagrejs/dagre`.

**Frontend — tests:**
- `web/src/features/catalog/relationships/__tests__/graphMerge.test.ts`
- `web/src/features/catalog/relationships/__tests__/graphLayout.test.ts`
- `web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx`

---

## Task 1: Slice verification scaffolding

**Files:**
- Create: `docs/superpowers/verification/2026-06-26-catalog-graph-explorer/dod.md`
- Create: `docs/superpowers/verification/2026-06-26-catalog-graph-explorer/gate-findings.yaml`

- [ ] **Step 1: Copy templates into the slice's verification folder**

```bash
mkdir -p docs/superpowers/verification/2026-06-26-catalog-graph-explorer
cp docs/superpowers/templates/dod-ledger-template.md \
   docs/superpowers/verification/2026-06-26-catalog-graph-explorer/dod.md
cp docs/superpowers/templates/gate-findings-template.yaml \
   docs/superpowers/verification/2026-06-26-catalog-graph-explorer/gate-findings.yaml
```

- [ ] **Step 2: Fill the ledger header**

In `dod.md` set: Slice `2026-06-26-catalog-graph-explorer`, Branch `feat/catalog-graph-explorer`, HEAD (`git rev-parse --short HEAD`), Spec/Plan paths. In `gate-findings.yaml` set `slice: 2026-06-26-catalog-graph-explorer`, `branch:`, `head:`; leave `findings: []`.

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/verification/2026-06-26-catalog-graph-explorer/
git commit -m "chore(catalog): DoD ledger + gate-findings scaffold for graph-explorer slice"
```

---

## Task 2: Backend contracts + query record

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/GraphResponse.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversalQuery.cs`

**Interfaces:**
- Produces: `GraphResponse(IReadOnlyList<GraphNodeDto> Nodes, IReadOnlyList<GraphEdgeDto> Edges, bool Truncated)`; `GraphNodeDto(EntityKind Kind, Guid Id, string DisplayName, int Depth, Guid? TeamId)`; `GraphEdgeDto(Guid Id, GraphEndpointDto Source, GraphEndpointDto Target, RelationshipType Type, RelationshipOrigin Origin)`; `GraphEndpointDto(EntityKind Kind, Guid Id)`; `GraphTraversalQuery(EntityRef Focus, int Depth, RelationshipDirection Direction)`.

- [ ] **Step 1: Create the contracts file**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Contracts/GraphResponse.cs
using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

[ExcludeFromCodeCoverage]
public sealed record GraphResponse(
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphEdgeDto> Edges,
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

- [ ] **Step 2: Create the query record**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversalQuery.cs
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public sealed record GraphTraversalQuery(EntityRef Focus, int Depth, RelationshipDirection Direction);
```

> `EntityRef` is the Domain value object used by `ListRelationshipsForEntityQuery` (`new EntityRef(EntityKind, Guid)`). `RelationshipDirection` is the Application enum `{ Outgoing, Incoming, All }`.

- [ ] **Step 3: Build to verify compile + arch rule**

Run: `cmd //c "dotnet build src/Modules/Catalog/Kartova.Catalog.Contracts" && dotnet test tests/Kartova.ArchitectureTests --filter ContractsCoverageRules`
Expected: build 0 warnings/0 errors; ContractsCoverageRules PASS (every new `*Dto`/`*Response` is excluded).

- [ ] **Step 4: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Contracts/GraphResponse.cs src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversalQuery.cs
git commit -m "feat(catalog): GraphResponse contract + GraphTraversalQuery for graph traversal"
```

---

## Task 3: Pure BFS traverser (TDD, unit)

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversal.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/GraphTraversalTests.cs`

**Interfaces:**
- Consumes: `EntityRef` (Domain), `RelationshipDirection`/`RelationshipType`/`RelationshipOrigin`.
- Produces:
  - `record GraphTraversalEdge(EntityRef Source, EntityRef Target, Guid Id, RelationshipType Type, RelationshipOrigin Origin)`
  - `record GraphTraversalNode(EntityRef Ref, int Depth)`
  - `record GraphTraversalResult(IReadOnlyList<GraphTraversalNode> Nodes, IReadOnlyList<GraphTraversalEdge> Edges, bool Truncated)`
  - `static Task<GraphTraversalResult> GraphTraversal.BuildAsync(EntityRef focus, int depth, RelationshipDirection direction, int maxNodes, Func<IReadOnlyCollection<EntityRef>, CancellationToken, Task<IReadOnlyList<GraphTraversalEdge>>> fetchEdgesTouching, CancellationToken ct)`

> The fetch delegate is given a frontier (set of refs) and returns every edge whose source **or** target is in that frontier (direction filtering happens in the traverser, so the DB fetch stays a simple `IN`). The traverser dedups nodes by `(Kind,Id)`, assigns `Depth` = discovery level (focus = 0), applies `direction` to decide the neighbour, stops adding nodes at `maxNodes` (`Truncated=true`), and includes only edges whose both endpoints are in the final node set.

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Tests/GraphTraversalTests.cs
using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public class GraphTraversalTests
{
    private static readonly Guid F = Guid.NewGuid();
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static EntityRef Svc(Guid id) => new(EntityKind.Service, id);

    private static GraphTraversalEdge Edge(Guid src, Guid tgt) =>
        new(Svc(src), Svc(tgt), Guid.NewGuid(), RelationshipType.DependsOn, RelationshipOrigin.Manual);

    // A fetch delegate over a fixed edge list: returns edges touching the frontier (source or target).
    private static Func<IReadOnlyCollection<EntityRef>, CancellationToken, Task<IReadOnlyList<GraphTraversalEdge>>>
        Fetch(params GraphTraversalEdge[] all) =>
        (frontier, _) =>
        {
            var ids = frontier.Select(f => f.Id).ToHashSet();
            IReadOnlyList<GraphTraversalEdge> hit =
                all.Where(e => ids.Contains(e.Source.Id) || ids.Contains(e.Target.Id)).ToList();
            return Task.FromResult(hit);
        };

    [TestMethod]
    public async Task focus_with_no_edges_returns_only_focus()
    {
        var r = await GraphTraversal.BuildAsync(Svc(F), 2, RelationshipDirection.All, 200, Fetch(), CancellationToken.None);
        Assert.AreEqual(1, r.Nodes.Count);
        Assert.AreEqual(F, r.Nodes[0].Ref.Id);
        Assert.AreEqual(0, r.Nodes[0].Depth);
        Assert.AreEqual(0, r.Edges.Count);
        Assert.IsFalse(r.Truncated);
    }

    [TestMethod]
    public async Task depth_2_discovers_two_hops_with_correct_depths()
    {
        // F -> A -> B
        var r = await GraphTraversal.BuildAsync(Svc(F), 2, RelationshipDirection.All, 200,
            Fetch(Edge(F, A), Edge(A, B)), CancellationToken.None);
        Assert.AreEqual(0, r.Nodes.Single(n => n.Ref.Id == F).Depth);
        Assert.AreEqual(1, r.Nodes.Single(n => n.Ref.Id == A).Depth);
        Assert.AreEqual(2, r.Nodes.Single(n => n.Ref.Id == B).Depth);
        Assert.AreEqual(2, r.Edges.Count);
    }

    [TestMethod]
    public async Task depth_1_excludes_the_second_hop()
    {
        var r = await GraphTraversal.BuildAsync(Svc(F), 1, RelationshipDirection.All, 200,
            Fetch(Edge(F, A), Edge(A, B)), CancellationToken.None);
        Assert.IsFalse(r.Nodes.Any(n => n.Ref.Id == B));
        Assert.AreEqual(1, r.Edges.Count); // only F->A; the A->B edge has an excluded endpoint
    }

    [TestMethod]
    public async Task outgoing_only_follows_source_to_target()
    {
        // C -> F (incoming to F) and F -> A (outgoing from F). Outgoing keeps only A.
        var r = await GraphTraversal.BuildAsync(Svc(F), 1, RelationshipDirection.Outgoing, 200,
            Fetch(Edge(C, F), Edge(F, A)), CancellationToken.None);
        Assert.IsTrue(r.Nodes.Any(n => n.Ref.Id == A));
        Assert.IsFalse(r.Nodes.Any(n => n.Ref.Id == C));
    }

    [TestMethod]
    public async Task incoming_only_follows_target_to_source()
    {
        var r = await GraphTraversal.BuildAsync(Svc(F), 1, RelationshipDirection.Incoming, 200,
            Fetch(Edge(C, F), Edge(F, A)), CancellationToken.None);
        Assert.IsTrue(r.Nodes.Any(n => n.Ref.Id == C));
        Assert.IsFalse(r.Nodes.Any(n => n.Ref.Id == A));
    }

    [TestMethod]
    public async Task cycle_terminates_with_one_node_per_entity()
    {
        // F -> A and A -> F (a 2-cycle)
        var r = await GraphTraversal.BuildAsync(Svc(F), 3, RelationshipDirection.All, 200,
            Fetch(Edge(F, A), Edge(A, F)), CancellationToken.None);
        Assert.AreEqual(2, r.Nodes.Count);          // F, A — each once
        Assert.AreEqual(2, r.Edges.Count);          // both edges present
    }

    [TestMethod]
    public async Task cap_truncates_and_flags()
    {
        // F -> A, F -> B, F -> C ; maxNodes=2 keeps focus + one neighbour
        var r = await GraphTraversal.BuildAsync(Svc(F), 1, RelationshipDirection.All, 2,
            Fetch(Edge(F, A), Edge(F, B), Edge(F, C)), CancellationToken.None);
        Assert.AreEqual(2, r.Nodes.Count);
        Assert.IsTrue(r.Truncated);
        Assert.IsTrue(r.Edges.All(e =>
            r.Nodes.Any(n => n.Ref.Id == e.Source.Id) && r.Nodes.Any(n => n.Ref.Id == e.Target.Id)));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter GraphTraversalTests"`
Expected: FAIL — `GraphTraversal` does not exist.

- [ ] **Step 3: Implement the pure traverser**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversal.cs
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

public sealed record GraphTraversalEdge(
    EntityRef Source, EntityRef Target, Guid Id, RelationshipType Type, RelationshipOrigin Origin);

public sealed record GraphTraversalNode(EntityRef Ref, int Depth);

public sealed record GraphTraversalResult(
    IReadOnlyList<GraphTraversalNode> Nodes, IReadOnlyList<GraphTraversalEdge> Edges, bool Truncated);

public static class GraphTraversal
{
    public static async Task<GraphTraversalResult> BuildAsync(
        EntityRef focus,
        int depth,
        RelationshipDirection direction,
        int maxNodes,
        Func<IReadOnlyCollection<EntityRef>, CancellationToken, Task<IReadOnlyList<GraphTraversalEdge>>> fetchEdgesTouching,
        CancellationToken ct)
    {
        static (EntityKind Kind, Guid Id) Key(EntityRef r) => (r.Kind, r.Id);

        var nodeDepth = new Dictionary<(EntityKind, Guid), int> { [Key(focus)] = 0 };
        var keptEdges = new Dictionary<Guid, GraphTraversalEdge>();
        var truncated = false;
        var frontier = new List<EntityRef> { focus };

        for (var level = 0; level < depth && frontier.Count > 0; level++)
        {
            var edges = await fetchEdgesTouching(frontier, ct);
            var next = new List<EntityRef>();
            var inFrontier = frontier.Select(Key).ToHashSet();

            foreach (var e in edges)
            {
                // Decide the neighbour reachable from a frontier node, per direction.
                var srcInFrontier = inFrontier.Contains(Key(e.Source));
                var tgtInFrontier = inFrontier.Contains(Key(e.Target));
                EntityRef? neighbour = direction switch
                {
                    RelationshipDirection.Outgoing => srcInFrontier ? e.Target : null,
                    RelationshipDirection.Incoming => tgtInFrontier ? e.Source : null,
                    _ => srcInFrontier ? e.Target : (tgtInFrontier ? e.Source : null),
                };
                if (neighbour is null) continue;

                if (!nodeDepth.ContainsKey(Key(neighbour)))
                {
                    if (nodeDepth.Count >= maxNodes) { truncated = true; continue; }
                    nodeDepth[Key(neighbour)] = level + 1;
                    next.Add(neighbour);
                }
            }
            frontier = next;
        }

        // Materialise nodes, then include only edges whose both endpoints survived.
        var nodes = nodeDepth.Select(kv => new GraphTraversalNode(
            new EntityRef(kv.Key.Item1, kv.Key.Item2), kv.Value)).ToList();

        // Re-scan edges once more over the kept set to capture every edge between two kept
        // nodes (including cross-links between neighbours); edges to capped-out nodes are dropped.
        var keptRefs = nodes.Select(n => n.Ref).ToList();
        var allTouching = await fetchEdgesTouching(keptRefs, ct);
        foreach (var e in allTouching)
        {
            if (nodeDepth.ContainsKey(Key(e.Source)) && nodeDepth.ContainsKey(Key(e.Target)))
                keptEdges[e.Id] = e;
        }

        return new GraphTraversalResult(nodes, keptEdges.Values.ToList(), truncated);
    }
}
```

> Note: the final edge-capture re-fetch over the kept set guarantees cross-links between already-discovered nodes (e.g. two neighbours that also depend on each other) appear as edges.

- [ ] **Step 4: Run tests to verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests --filter GraphTraversalTests"`
Expected: PASS (7/7).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversal.cs src/Modules/Catalog/Kartova.Catalog.Tests/GraphTraversalTests.cs
git commit -m "feat(catalog): pure BFS graph traverser (depth, direction, cycle, cap)"
```

---

## Task 4: GraphTraversalHandler + endpoint (TDD via real-seam happy path)

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (add `GetCatalogGraphAsync`)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (map route + register handler)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs`

**Interfaces:**
- Consumes: `GraphTraversal.BuildAsync`, `GraphTraversalQuery`, `ICatalogEntityLookup.Find → EntityLookupResult(Guid TeamId, string DisplayName)`, `CatalogDbContext.Relationships`, `GraphResponse`/DTOs.
- Produces: `GraphTraversalHandler.DefaultNodeCap = 200`; `Task<GraphResponse> GraphTraversalHandler.Handle(GraphTraversalQuery, CatalogDbContext, ICatalogEntityLookup, CancellationToken, int? maxNodes = null)`; endpoint `GET /api/v1/catalog/graph`.

- [ ] **Step 1: Write the failing real-seam happy-path test**

```csharp
// src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public class GetCatalogGraphTests : CatalogIntegrationTestBase
{
    private const string OrgAUser = "admin@orga.kartova.local";
    private const string OrgBUser = "admin@orgb.kartova.local";

    private static async Task<Guid> SeedServiceAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services", new
        {
            displayName = name, description = "x", teamId, endpoints = Array.Empty<object>(),
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}': {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }

    private static Task DependsOnAsync(HttpClient client, Guid src, Guid tgt) =>
        client.PostAsJsonAsync("/api/v1/catalog/relationships", new
        {
            sourceKind = EntityKind.Service, sourceId = src,
            type = RelationshipType.DependsOn,
            targetKind = EntityKind.Service, targetId = tgt,
        });

    [TestMethod]
    public async Task GET_graph_returns_two_hop_neighbourhood_with_depths()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Team");
        var f = await SeedServiceAsync(client, teamId, "graph-focus");
        var a = await SeedServiceAsync(client, teamId, "graph-a");
        var b = await SeedServiceAsync(client, teamId, "graph-b");
        await DependsOnAsync(client, f, a);   // F -> A
        await DependsOnAsync(client, a, b);   // A -> B

        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={f}&depth=2&direction=all");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        Assert.AreEqual(3, graph!.Nodes.Count);
        Assert.AreEqual(0, graph.Nodes.Single(n => n.Id == f).Depth);
        Assert.AreEqual(1, graph.Nodes.Single(n => n.Id == a).Depth);
        Assert.AreEqual(2, graph.Nodes.Single(n => n.Id == b).Depth);
        Assert.AreEqual(teamId, graph.Nodes.Single(n => n.Id == a).TeamId);
        Assert.AreEqual("graph-a", graph.Nodes.Single(n => n.Id == a).DisplayName);
        Assert.AreEqual(2, graph.Edges.Count);
        Assert.IsFalse(graph.Truncated);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter GetCatalogGraphTests"`
Expected: FAIL — 404 (route not mapped) / deserialisation fails.

- [ ] **Step 3: Implement the handler**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs
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
        var result = await GraphTraversal.BuildAsync(
            q.Focus, q.Depth, q.Direction, maxNodes ?? DefaultNodeCap,
            async (frontier, token) =>
            {
                var ids = frontier.Select(f => f.Id).ToList();
                var rows = await db.Relationships
                    .Where(r => ids.Contains(r.Source.Id) || ids.Contains(r.Target.Id))
                    .ToListAsync(token);
                IReadOnlyList<GraphTraversalEdge> edges = rows
                    .Select(r => new GraphTraversalEdge(
                        new EntityRef(r.Source.Kind, r.Source.Id),
                        new EntityRef(r.Target.Kind, r.Target.Id),
                        r.Id.Value, r.Type, r.Origin))
                    .ToList();
                return edges;
            },
            ct);

        // Enrich displayName + teamId for every node (batched over distinct refs).
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

        var edges = result.Edges.Select(e => new GraphEdgeDto(
            e.Id,
            new GraphEndpointDto(e.Source.Kind, e.Source.Id),
            new GraphEndpointDto(e.Target.Kind, e.Target.Id),
            e.Type, e.Origin)).ToList();

        return new GraphResponse(nodes, edges, result.Truncated);
    }
}
```

> `r.Id.Value` reads the `RelationshipId` value object after materialisation (same as `ListRelationshipsForEntityHandler`'s `x.Id.Value`). Filtering by `r.Source.Id`/`r.Target.Id` (Guid, globally unique across kinds) is RLS-scoped and EF-translatable to `IN`.

- [ ] **Step 4: Add the endpoint delegate**

Add to `CatalogEndpointDelegates.cs` (next to `ListRelationshipsAsync`), mirroring its parse/validate style:

```csharp
/// <summary>
/// GET /graph?entityKind=&amp;entityId=&amp;depth=&amp;direction= — BFS dependency neighbourhood
/// around the focus entity. depth 1..4 (default 2); direction outgoing|incoming|all (default all).
/// Bounded aggregate (node cap + truncated flag) — not a cursor list. Claim gate: catalog.read.
/// </summary>
internal static async Task<IResult> GetCatalogGraphAsync(
    [FromQuery] string entityKind,
    [FromQuery] Guid entityId,
    [FromQuery] int? depth,
    [FromQuery] string? direction,
    GraphTraversalHandler handler,
    ICatalogEntityLookup lookup,
    CatalogDbContext db,
    CancellationToken ct)
{
    if (!Enum.TryParse<EntityKind>(entityKind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind) || entityId == Guid.Empty)
        return Results.Problem(type: ProblemTypes.ValidationFailed, title: "Invalid entity reference",
            detail: "entityKind and a non-empty entityId are required.", statusCode: StatusCodes.Status400BadRequest);

    var dir = RelationshipDirection.All;
    if (!string.IsNullOrWhiteSpace(direction)
        && (!Enum.TryParse(direction, ignoreCase: true, out dir) || !Enum.IsDefined(dir)))
        return Results.Problem(type: ProblemTypes.ValidationFailed, title: "Invalid direction",
            detail: "direction must be outgoing, incoming, or all.", statusCode: StatusCodes.Status400BadRequest);

    var effectiveDepth = depth ?? 2;
    if (effectiveDepth < 1 || effectiveDepth > 4)
        return Results.Problem(type: ProblemTypes.ValidationFailed, title: "Invalid depth",
            detail: "depth must be between 1 and 4.", statusCode: StatusCodes.Status400BadRequest);

    var query = new GraphTraversalQuery(new EntityRef(kind, entityId), effectiveDepth, dir);
    var graph = await handler.Handle(query, db, lookup, ct);
    return Results.Ok(graph);
}
```

> If `Kartova.Catalog.Application` is not already imported in this file, add `using Kartova.Catalog.Application;` (it is — `ListRelationshipsForEntityQuery` lives there).

- [ ] **Step 5: Map the route + register the handler**

In `CatalogModule.cs`, beside the other `/relationships` maps:

```csharp
tenant.MapGet("/graph", CatalogEndpointDelegates.GetCatalogGraphAsync)
      .RequireAuthorization(KartovaPermissions.CatalogRead)
      .WithName("GetCatalogGraph")
      .Produces<GraphResponse>(StatusCodes.Status200OK)
      .ProducesProblem(StatusCodes.Status400BadRequest)
      .ProducesProblem(StatusCodes.Status403Forbidden);
```

And in the DI block (next to `services.AddScoped<ListRelationshipsForEntityHandler>();`):

```csharp
services.AddScoped<GraphTraversalHandler>();
```

- [ ] **Step 6: Run the happy-path test to GREEN**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter GetCatalogGraphTests"`
Expected: PASS (1/1).

- [ ] **Step 7: Verify line endings on edited .cs, then commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs \
        src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs
git commit -m "feat(catalog): GET /catalog/graph BFS traversal endpoint (E-04.F-02.S-04)"
git show --stat HEAD | head -20; echo "--- whitespace-only diff should be empty: ---"; git show -w --stat HEAD | head -20
```
Expected: the two `--stat` outputs match (no CRLF-only churn). If they differ, `sed -i 's/\r$//'` the affected files and `git commit --amend --no-edit`.

---

## Task 5: Real-seam edge cases

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs`

- [ ] **Step 1: Add the edge-case tests**

```csharp
[TestMethod]
public async Task GET_graph_depth_1_excludes_second_hop()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph D1");
    var f = await SeedServiceAsync(client, teamId, "d1-focus");
    var a = await SeedServiceAsync(client, teamId, "d1-a");
    var b = await SeedServiceAsync(client, teamId, "d1-b");
    await DependsOnAsync(client, f, a);
    await DependsOnAsync(client, a, b);

    var graph = await (await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={f}&depth=1&direction=all"))
        .Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
    Assert.IsFalse(graph!.Nodes.Any(n => n.Id == b));
}

[TestMethod]
public async Task GET_graph_outgoing_excludes_incoming_neighbour()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Dir");
    var f = await SeedServiceAsync(client, teamId, "dir-focus");
    var a = await SeedServiceAsync(client, teamId, "dir-out");
    var c = await SeedServiceAsync(client, teamId, "dir-in");
    await DependsOnAsync(client, f, a);   // outgoing from F
    await DependsOnAsync(client, c, f);   // incoming to F

    var graph = await (await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={f}&depth=1&direction=outgoing"))
        .Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
    Assert.IsTrue(graph!.Nodes.Any(n => n.Id == a));
    Assert.IsFalse(graph.Nodes.Any(n => n.Id == c));
}

[TestMethod]
public async Task GET_graph_handles_a_cycle()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Cycle");
    var f = await SeedServiceAsync(client, teamId, "cyc-f");
    var a = await SeedServiceAsync(client, teamId, "cyc-a");
    await DependsOnAsync(client, f, a);
    await DependsOnAsync(client, a, f);

    var graph = await (await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={f}&depth=3&direction=all"))
        .Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
    Assert.AreEqual(2, graph!.Nodes.Count);
    Assert.AreEqual(2, graph.Edges.Count);
}

[TestMethod]
public async Task GET_graph_is_tenant_isolated()
{
    var orgB = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
    var teamB = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgBUser), "B Graph Iso");
    var b1 = await SeedServiceAsync(orgB, teamB, "biso-1");
    var b2 = await SeedServiceAsync(orgB, teamB, "biso-2");
    await DependsOnAsync(orgB, b1, b2);

    var orgA = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var graph = await (await orgA.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={b1}&depth=2&direction=all"))
        .Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
    // b1 is invisible to org A → only the (empty) focus node, no neighbours/edges.
    Assert.AreEqual(0, graph!.Edges.Count);
}

[TestMethod]
public async Task GET_graph_without_token_returns_401()
{
    using var client = Fx.CreateAnonymousClient();
    var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={Guid.NewGuid()}&depth=2");
    Assert.AreEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
}

[TestMethod]
public async Task GET_graph_with_invalid_entityKind_returns_400()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Bogus&entityId={Guid.NewGuid()}&depth=2");
    Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
}

[TestMethod]
public async Task GET_graph_with_depth_out_of_range_returns_400()
{
    var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
    var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Service&entityId={Guid.NewGuid()}&depth=9");
    Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
}
```

- [ ] **Step 2: Run the full test class**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter GetCatalogGraphTests"`
Expected: PASS (8/8). (Truncation is covered by the pure unit test in Task 3.)

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs
git commit -m "test(catalog): real-seam edge cases for /catalog/graph (depth, direction, cycle, isolation, 400/401)"
```

---

## Task 6: Regenerate the API client

**Files:**
- Modify: `web/src/generated/openapi.ts`, `web/openapi-snapshot.json` (regenerated)

- [ ] **Step 1: Rebuild the API image so the running API exposes the new endpoint**

Run (per the project's codegen flow — the predev/prebuild scripts regenerate from the live API):
```bash
cmd //c "docker compose build api && docker compose up -d api"
```
Expected: API container healthy.

- [ ] **Step 2: Regenerate the client + snapshot**

```bash
cd web && npm run codegen   # or the predev step that writes src/generated/openapi.ts + openapi-snapshot.json
```
Expected: `operations["GetCatalogGraph"]` and `components["schemas"]["GraphResponse"]` now present in `web/src/generated/openapi.ts`.

- [ ] **Step 3: Verify the new types exist**

Run: `grep -E "GetCatalogGraph|GraphResponse" web/src/generated/openapi.ts | head`
Expected: matches found.

- [ ] **Step 4: Commit**

```bash
git add web/src/generated/openapi.ts web/openapi-snapshot.json
git commit -m "chore(web): regenerate API client for GET /catalog/graph"
```

---

## Task 7: dagre dependency + graphMerge (TDD, unit)

**Files:**
- Modify: `web/package.json` + lockfile
- Create: `web/src/features/catalog/relationships/graphMerge.ts`
- Test: `web/src/features/catalog/relationships/__tests__/graphMerge.test.ts`

**Interfaces:**
- Consumes: `GraphResponse` (generated type), `relationshipTypeLabel`, `RelationshipKind`, `CreatableRelationshipType`.
- Produces:
  - `type ExplorerNode = { id: string; kind: RelationshipKind; entityId: string; displayName: string; depth?: number }`
  - `type ExplorerEdge = { id: string; source: string; target: string; label: string }`
  - `type ExplorerGraph = { nodes: ExplorerNode[]; edges: ExplorerEdge[]; truncated: boolean }`
  - `mergeGraphs(results: GraphResponse[]): ExplorerGraph` (node id = `${kind}:${entityId}`)

- [ ] **Step 1: Add dagre**

```bash
cd web && npm install @dagrejs/dagre
```
Expected: `@dagrejs/dagre` in `package.json` dependencies; lockfile updated. (`@dagrejs/dagre` ships its own types; no `@types/dagre` needed.)

- [ ] **Step 2: Write the failing tests**

```ts
// web/src/features/catalog/relationships/__tests__/graphMerge.test.ts
import { describe, it, expect } from "vitest";
import { mergeGraphs } from "@/features/catalog/relationships/graphMerge";
import type { GraphResponse } from "@/features/catalog/api/graph";

const node = (id: string, displayName: string, depth: number) =>
  ({ kind: "service", id, displayName, depth, teamId: null }) as GraphResponse["nodes"][number];
const edge = (id: string, s: string, t: string) =>
  ({ id, source: { kind: "service", id: s }, target: { kind: "service", id: t }, type: "dependsOn", origin: "manual" }) as GraphResponse["edges"][number];

describe("mergeGraphs", () => {
  it("maps one response to nodes keyed by kind:id with labelled edges", () => {
    const r: GraphResponse = { nodes: [node("f", "Focus", 0), node("a", "A", 1)], edges: [edge("e1", "f", "a")], truncated: false };
    const g = mergeGraphs([r]);
    expect(g.nodes.map((n) => n.id).sort()).toEqual(["service:a", "service:f"]);
    expect(g.edges).toEqual([{ id: "e1", source: "service:f", target: "service:a", label: "Depends on" }]);
    expect(g.truncated).toBe(false);
  });

  it("dedupes a node and an edge that appear in two responses", () => {
    const r1: GraphResponse = { nodes: [node("f", "Focus", 0), node("a", "A", 1)], edges: [edge("e1", "f", "a")], truncated: false };
    const r2: GraphResponse = { nodes: [node("a", "A", 0), node("b", "B", 1)], edges: [edge("e1", "f", "a"), edge("e2", "a", "b")], truncated: false };
    const g = mergeGraphs([r1, r2]);
    expect(g.nodes.map((n) => n.id).sort()).toEqual(["service:a", "service:b", "service:f"]);
    expect(g.edges.map((e) => e.id).sort()).toEqual(["e1", "e2"]);
  });

  it("ORs the truncated flag across responses", () => {
    expect(mergeGraphs([{ nodes: [], edges: [], truncated: false }, { nodes: [], edges: [], truncated: true }]).truncated).toBe(true);
  });
});
```

- [ ] **Step 3: Run to verify failure**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphMerge.test.ts`
Expected: FAIL — `mergeGraphs` not found.

- [ ] **Step 4: Implement `graphMerge.ts`**

```ts
// web/src/features/catalog/relationships/graphMerge.ts
import type { GraphResponse } from "@/features/catalog/api/graph";
import {
  relationshipTypeLabel,
  type RelationshipKind,
  type CreatableRelationshipType,
} from "@/features/catalog/relationships/relationshipTypeRules";

export type ExplorerNode = {
  id: string;
  kind: RelationshipKind;
  entityId: string;
  displayName: string;
  depth?: number;
};
export type ExplorerEdge = { id: string; source: string; target: string; label: string };
export type ExplorerGraph = { nodes: ExplorerNode[]; edges: ExplorerEdge[]; truncated: boolean };

const nodeId = (kind: string, id: string) => `${kind}:${id}`;

export function mergeGraphs(results: GraphResponse[]): ExplorerGraph {
  const nodes = new Map<string, ExplorerNode>();
  const edges = new Map<string, ExplorerEdge>();
  let truncated = false;

  for (const r of results) {
    truncated = truncated || r.truncated;
    for (const n of r.nodes) {
      const id = nodeId(n.kind, n.id);
      if (!nodes.has(id)) {
        nodes.set(id, {
          id,
          kind: n.kind as RelationshipKind,
          entityId: n.id,
          displayName: n.displayName,
          depth: n.depth,
        });
      }
    }
    for (const e of r.edges) {
      if (!edges.has(e.id)) {
        edges.set(e.id, {
          id: e.id,
          source: nodeId(e.source.kind, e.source.id),
          target: nodeId(e.target.kind, e.target.id),
          label: relationshipTypeLabel[e.type as CreatableRelationshipType] ?? e.type,
        });
      }
    }
  }
  return { nodes: [...nodes.values()], edges: [...edges.values()], truncated };
}
```

- [ ] **Step 5: Run to verify pass**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphMerge.test.ts`
Expected: PASS (3/3).

- [ ] **Step 6: Commit**

```bash
git add web/package.json web/package-lock.json web/src/features/catalog/relationships/graphMerge.ts web/src/features/catalog/relationships/__tests__/graphMerge.test.ts
git commit -m "feat(web): @dagrejs/dagre + pure graphMerge for the explorer"
```

---

## Task 8: graphLayout (TDD, unit)

**Files:**
- Modify: `web/src/features/catalog/relationships/graphModel.ts` (add optional `detailHref` to `GraphNodeData`)
- Create: `web/src/features/catalog/relationships/graphLayout.ts`
- Test: `web/src/features/catalog/relationships/__tests__/graphLayout.test.ts`

**Interfaces:**
- Consumes: `ExplorerGraph` (Task 7), `GraphNodeData` (graphModel), `@xyflow/react` `Node`/`Edge`, dagre.
- Produces: `layoutGraph(graph: ExplorerGraph, focusId: string): { nodes: Node<GraphNodeData>[]; edges: Edge[] }` — every node gets a `position`; the focus node's `data.side = "focused"`, others `"dependency"`; each non-focus node's `data.detailHref` is its detail route.

- [ ] **Step 1: Extend `GraphNodeData`**

In `web/src/features/catalog/relationships/graphModel.ts`, add an optional field to the existing `GraphNodeData` type:

```ts
export type GraphNodeData = {
  kind: RelationshipKind;
  entityId: string;
  displayName: string;
  side: GraphSide;
  detailHref?: string; // explorer only: when set, EntityGraphNode renders an "open detail" link
};
```

- [ ] **Step 2: Write the failing test**

```ts
// web/src/features/catalog/relationships/__tests__/graphLayout.test.ts
import { describe, it, expect } from "vitest";
import { layoutGraph } from "@/features/catalog/relationships/graphLayout";
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";

const graph: ExplorerGraph = {
  nodes: [
    { id: "service:f", kind: "service", entityId: "f", displayName: "Focus", depth: 0 },
    { id: "service:a", kind: "service", entityId: "a", displayName: "A", depth: 1 },
  ],
  edges: [{ id: "e1", source: "service:f", target: "service:a", label: "Depends on" }],
  truncated: false,
};

describe("layoutGraph", () => {
  it("assigns a position to every node and marks the focus node", () => {
    const { nodes, edges } = layoutGraph(graph, "service:f");
    expect(nodes).toHaveLength(2);
    expect(nodes.every((n) => Number.isFinite(n.position.x) && Number.isFinite(n.position.y))).toBe(true);
    expect(nodes.find((n) => n.id === "service:f")!.data.side).toBe("focused");
    expect(nodes.find((n) => n.id === "service:a")!.data.side).toBe("dependency");
    expect(edges).toHaveLength(1);
  });

  it("sets a detail href on non-focus nodes only", () => {
    const { nodes } = layoutGraph(graph, "service:f");
    expect(nodes.find((n) => n.id === "service:a")!.data.detailHref).toBe("/catalog/services/a");
    expect(nodes.find((n) => n.id === "service:f")!.data.detailHref).toBeUndefined();
  });
});
```

- [ ] **Step 3: Run to verify failure**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphLayout.test.ts`
Expected: FAIL — `layoutGraph` not found.

- [ ] **Step 4: Implement `graphLayout.ts`**

```ts
// web/src/features/catalog/relationships/graphLayout.ts
import dagre from "@dagrejs/dagre";
import type { Node, Edge } from "@xyflow/react";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const NODE_W = 180;
const NODE_H = 56;

const detailHref = (kind: RelationshipKind, id: string) =>
  `/catalog/${kind === "application" ? "applications" : "services"}/${id}`;

export function layoutGraph(graph: ExplorerGraph, focusId: string): { nodes: Node<GraphNodeData>[]; edges: Edge[] } {
  const g = new dagre.graphlib.Graph();
  g.setGraph({ rankdir: "LR", nodesep: 40, ranksep: 120 });
  g.setDefaultEdgeLabel(() => ({}));
  for (const n of graph.nodes) g.setNode(n.id, { width: NODE_W, height: NODE_H });
  for (const e of graph.edges) g.setEdge(e.source, e.target);
  dagre.layout(g);

  const nodes: Node<GraphNodeData>[] = graph.nodes.map((n) => {
    const pos = g.node(n.id);
    const isFocus = n.id === focusId;
    return {
      id: n.id,
      type: "entity",
      position: { x: pos.x - NODE_W / 2, y: pos.y - NODE_H / 2 },
      data: {
        kind: n.kind,
        entityId: n.entityId,
        displayName: n.displayName,
        side: isFocus ? "focused" : "dependency",
        detailHref: isFocus ? undefined : detailHref(n.kind, n.entityId),
      },
    };
  });

  const edges: Edge[] = graph.edges.map((e) => ({
    id: e.id,
    source: e.source,
    target: e.target,
    label: e.label,
  }));

  return { nodes, edges };
}
```

- [ ] **Step 5: Run to verify pass**

Run: `cd web && npx vitest run src/features/catalog/relationships/__tests__/graphLayout.test.ts`
Expected: PASS (2/2).

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/relationships/graphModel.ts web/src/features/catalog/relationships/graphLayout.ts web/src/features/catalog/relationships/__tests__/graphLayout.test.ts
git commit -m "feat(web): dagre-based graphLayout + detailHref on GraphNodeData"
```

---

## Task 9: useGraph hook

**Files:**
- Create: `web/src/features/catalog/api/graph.ts`

**Interfaces:**
- Consumes: `apiClient`, `unwrapData`, generated `components`/`operations`, `RelationshipKind`.
- Produces:
  - `type GraphResponse = components["schemas"]["GraphResponse"]`
  - `type GraphFocus = { kind: RelationshipKind; id: string }`
  - `useGraph({ focus, expand }: { focus: GraphFocus; expand: GraphFocus[] }) → { results: GraphResponse[]; isLoading: boolean; isError: boolean; refetch: () => void }`
  - focus fetched at `depth=2`, each expand node at `depth=1`, `direction=all`.

- [ ] **Step 1: Implement the hook**

```ts
// web/src/features/catalog/api/graph.ts
import { useQueries } from "@tanstack/react-query";
import { apiClient } from "./client";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

export type GraphResponse = components["schemas"]["GraphResponse"];
export type GraphFocus = { kind: RelationshipKind; id: string };

const FOCUS_DEPTH = 2;
const EXPAND_DEPTH = 1;

export const graphKeys = {
  all: ["catalog", "graph"] as const,
  node: (f: GraphFocus, depth: number) => [...graphKeys.all, f.kind, f.id, depth] as const,
};

async function fetchGraph(f: GraphFocus, depth: number): Promise<GraphResponse> {
  const { data, error } = await apiClient.GET("/api/v1/catalog/graph", {
    params: { query: { entityKind: f.kind, entityId: f.id, depth, direction: "all" } },
  });
  if (error) throw error;
  return unwrapData(data);
}

export function useGraph({ focus, expand }: { focus: GraphFocus; expand: GraphFocus[] }) {
  const queries = useQueries({
    queries: [
      { queryKey: graphKeys.node(focus, FOCUS_DEPTH), queryFn: () => fetchGraph(focus, FOCUS_DEPTH) },
      ...expand.map((n) => ({
        queryKey: graphKeys.node(n, EXPAND_DEPTH),
        queryFn: () => fetchGraph(n, EXPAND_DEPTH),
      })),
    ],
  });
  return {
    results: queries.map((q) => q.data).filter((d): d is GraphResponse => !!d),
    isLoading: queries.some((q) => q.isLoading),
    isError: queries.some((q) => q.isError),
    refetch: () => queries.forEach((q) => q.refetch()),
  };
}
```

> `depth`/`entityId`/`entityKind`/`direction` are the generated query params for `GetCatalogGraph`. If the generated type names `depth` as a number, pass the number directly; if it is string-typed (like the relationship list's `limit`), wrap with `String(depth)` to match the generated signature — let `tsc -b` decide.

- [ ] **Step 2: Type-check**

Run: `cd web && npm run build`
Expected: `tsc -b` passes (binding type gate). Fix the `depth` param typing per the note if it errors.

- [ ] **Step 3: Commit**

```bash
git add web/src/features/catalog/api/graph.ts
git commit -m "feat(web): useGraph multi-query hook (focus depth 2 + per-expand depth 1)"
```

---

## Task 10: GraphExplorerPage + route (TDD component test)

**Files:**
- Create: `web/src/features/catalog/pages/GraphExplorerPage.tsx`
- Modify: `web/src/app/router.tsx` (lazy `/graph` route)
- Test: `web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx`

**Interfaces:**
- Consumes: `useGraph`, `mergeGraphs`, `layoutGraph`, `EntityGraphNode`, `@xyflow/react`, React Router `useSearchParams`/`useNavigate`.
- URL contract: `?focus=<kind>:<id>` (required) + `?expand=<kind>:<id>,<kind>:<id>` (optional). Click a non-focus node → toggle it in `expand`; focus-node click → no-op.

- [ ] **Step 1: Write the failing component test (React Flow + dagre + useGraph mocked)**

```tsx
// web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { MemoryRouter, Routes, Route, useSearchParams } from "react-router-dom";
import { GraphExplorerPage } from "@/features/catalog/pages/GraphExplorerPage";

const mockUseGraph = vi.fn();
vi.mock("@/features/catalog/api/graph", () => ({ useGraph: (a: unknown) => mockUseGraph(a) }));

// Render React Flow as a div exposing nodes + a click hook so we can drive onNodeClick.
vi.mock("@xyflow/react", () => ({
  ReactFlow: ({ nodes, onNodeClick }: any) => (
    <div data-testid="rf">
      {nodes.map((n: any) => (
        <button key={n.id} data-testid={`node-${n.id}`} onClick={() => onNodeClick({}, n)}>
          {n.data.displayName}
        </button>
      ))}
    </div>
  ),
  Background: () => null,
  Controls: () => null,
  MiniMap: () => null,
}));

function ExpandProbe() {
  const [params] = useSearchParams();
  return <div data-testid="expand">{params.get("expand") ?? ""}</div>;
}

const result = {
  nodes: [
    { kind: "service", id: "f", displayName: "Focus", depth: 0, teamId: null },
    { kind: "service", id: "a", displayName: "A", depth: 1, teamId: null },
  ],
  edges: [{ id: "e1", source: { kind: "service", id: "f" }, target: { kind: "service", id: "a" }, type: "dependsOn", origin: "manual" }],
  truncated: false,
};

function renderAt(url: string) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Routes>
        <Route path="/graph" element={<><GraphExplorerPage /><ExpandProbe /></>} />
      </Routes>
    </MemoryRouter>,
  );
}

describe("GraphExplorerPage", () => {
  beforeEach(() => mockUseGraph.mockReset());

  it("renders focus + neighbour nodes", () => {
    mockUseGraph.mockReturnValue({ results: [result], isLoading: false, isError: false, refetch: vi.fn() });
    renderAt("/graph?focus=service:f");
    expect(screen.getByTestId("node-service:f")).toBeInTheDocument();
    expect(screen.getByTestId("node-service:a")).toBeInTheDocument();
  });

  it("clicking a non-focus node adds it to ?expand", () => {
    mockUseGraph.mockReturnValue({ results: [result], isLoading: false, isError: false, refetch: vi.fn() });
    renderAt("/graph?focus=service:f");
    fireEvent.click(screen.getByTestId("node-service:a"));
    expect(screen.getByTestId("expand").textContent).toContain("service:a");
  });

  it("shows an error state", () => {
    mockUseGraph.mockReturnValue({ results: [], isLoading: false, isError: true, refetch: vi.fn() });
    renderAt("/graph?focus=service:f");
    expect(screen.getByText(/couldn.t load/i)).toBeInTheDocument();
  });

  it("prompts when focus is missing", () => {
    mockUseGraph.mockReturnValue({ results: [], isLoading: false, isError: false, refetch: vi.fn() });
    renderAt("/graph");
    expect(screen.getByText(/pick an entity/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run to verify failure**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx`
Expected: FAIL — `GraphExplorerPage` not found.

- [ ] **Step 3: Implement the page**

```tsx
// web/src/features/catalog/pages/GraphExplorerPage.tsx
import { useMemo } from "react";
import { useSearchParams } from "react-router-dom";
import { ReactFlow, Background, Controls, MiniMap, type Node, type Edge } from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useGraph, type GraphFocus } from "@/features/catalog/api/graph";
import { mergeGraphs } from "@/features/catalog/relationships/graphMerge";
import { layoutGraph } from "@/features/catalog/relationships/graphLayout";
import { EntityGraphNode } from "@/features/catalog/components/EntityGraphNode";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

const NODE_TYPES = { entity: EntityGraphNode };

function parseRef(token: string | undefined | null): GraphFocus | null {
  if (!token) return null;
  const [kind, id] = token.split(":");
  if ((kind === "application" || kind === "service") && id) return { kind: kind as RelationshipKind, id };
  return null;
}

export function GraphExplorerPage() {
  const [params, setParams] = useSearchParams();

  const focus = parseRef(params.get("focus"));
  const expandTokens = (params.get("expand") ?? "").split(",").filter(Boolean);
  const expand = expandTokens.map(parseRef).filter((r): r is GraphFocus => r !== null);

  // useGraph must be called unconditionally; pass a harmless placeholder when focus is absent.
  const safeFocus = focus ?? { kind: "application" as RelationshipKind, id: "" };
  const { results, isLoading, isError } = useGraph({ focus: safeFocus, expand });

  const focusId = focus ? `${focus.kind}:${focus.id}` : "";
  const { nodes, edges } = useMemo(() => {
    if (!focus) return { nodes: [] as Node<GraphNodeData>[], edges: [] as Edge[] };
    return layoutGraph(mergeGraphs(results), focusId);
  }, [results, focus, focusId]);

  function toggleExpand(id: string) {
    if (id === focusId) return; // focus node is the root
    const set = new Set(expandTokens);
    if (set.has(id)) set.delete(id);
    else set.add(id);
    const next = new URLSearchParams(params);
    if (set.size) next.set("expand", [...set].join(","));
    else next.delete("expand");
    setParams(next);
  }

  if (!focus) {
    return <div className="p-8 text-sm text-tertiary">Pick an entity to explore its dependency graph.</div>;
  }

  return (
    <div className="flex h-[calc(100vh-8rem)] flex-col gap-2 p-4">
      <h1 className="text-lg font-semibold text-primary">Dependency graph</h1>
      {isLoading ? (
        <Skeleton className="h-full w-full" />
      ) : isError ? (
        <p className="text-sm text-error-primary">Couldn&apos;t load the dependency graph.</p>
      ) : (
        <>
          {results.some((r) => r.truncated) && (
            <p className="text-xs text-tertiary">Showing a partial graph (node limit reached) — refine your focus to see more.</p>
          )}
          <div className="min-h-0 flex-1 overflow-hidden rounded-lg ring-1 ring-secondary">
            <ReactFlow
              nodes={nodes}
              edges={edges}
              nodeTypes={NODE_TYPES}
              fitView
              nodesDraggable={false}
              nodesConnectable={false}
              elementsSelectable={false}
              proOptions={{ hideAttribution: true }}
              onNodeClick={(_, node) => toggleExpand(node.id)}
            >
              <Background />
              <Controls showInteractive={false} />
              <MiniMap pannable zoomable />
            </ReactFlow>
          </div>
        </>
      )}
    </div>
  );
}
```

> Node click = expand/collapse only. Navigation to an entity's detail page is the `<Link>` rendered inside `EntityGraphNode` (Task 11), so this page does not need `useNavigate`.

- [ ] **Step 4: Add the lazy route**

In `web/src/app/router.tsx`: add a lazy import and a route under `<ProtectedShell>`:

```tsx
import { lazy, Suspense } from "react";
// ...
const GraphExplorerPage = lazy(() =>
  import("@/features/catalog/pages/GraphExplorerPage").then((m) => ({ default: m.GraphExplorerPage })),
);
```

Inside the `<Route element={<ProtectedShell />}>` block:

```tsx
<Route
  path="/graph"
  element={
    <Suspense fallback={<div className="p-8 text-sm text-tertiary">Loading graph…</div>}>
      <GraphExplorerPage />
    </Suspense>
  }
/>
```

- [ ] **Step 5: Run the component test to GREEN**

Run: `cd web && npx vitest run src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx`
Expected: PASS (4/4).

- [ ] **Step 6: Type-check + commit**

Run: `cd web && npm run build`
Expected: `tsc -b` clean.

```bash
git add web/src/features/catalog/pages/GraphExplorerPage.tsx web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx web/src/app/router.tsx
git commit -m "feat(web): standalone /graph explorer page with URL-driven expand (E-04.F-02.S-04)"
```

---

## Task 11: EntityGraphNode detail link + "Open full graph" button

**Files:**
- Modify: `web/src/features/catalog/components/EntityGraphNode.tsx`
- Modify: `web/src/features/catalog/components/DependencyMiniGraph.tsx`
- Test: extend `web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx` (exists)

**Interfaces:**
- Consumes: `GraphNodeData.detailHref` (Task 8), React Router `Link`.
- Produces: `EntityGraphNode` renders a `<Link to={data.detailHref}>` (stops propagation) when `detailHref` is set; `DependencyMiniGraph` header shows an "Open full graph" `Link` to `/graph?focus=<kind>:<id>`.

- [ ] **Step 1: Write a failing test for the detail link**

Add to `EntityGraphNode.test.tsx`:

```tsx
it("renders an open-detail link when detailHref is set", () => {
  render(
    <MemoryRouter>
      <EntityGraphNode data={{ kind: "service", entityId: "a", displayName: "A", side: "dependency", detailHref: "/catalog/services/a" }} /* + minimal NodeProps */ />
    </MemoryRouter>,
  );
  const link = screen.getByRole("link", { name: /open/i });
  expect(link).toHaveAttribute("href", "/catalog/services/a");
});
```

> Match the existing test file's render harness for `EntityGraphNode` (it already constructs `NodeProps`); reuse that wrapper and add only the `detailHref` case.

- [ ] **Step 2: Run to verify failure**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/EntityGraphNode.test.tsx`
Expected: FAIL — no link rendered.

- [ ] **Step 3: Add the link to `EntityGraphNode`**

```tsx
import { Handle, Position, type Node, type NodeProps } from "@xyflow/react";
import { Link } from "react-router-dom";
import type { GraphNodeData } from "@/features/catalog/relationships/graphModel";

const KIND_LABEL: Record<string, string> = { application: "Application", service: "Service" };

export function EntityGraphNode({ data }: NodeProps<Node<GraphNodeData>>) {
  const focused = data.side === "focused";
  return (
    <div
      className={
        focused
          ? "rounded-lg border-2 border-secondary bg-primary px-3 py-2 font-semibold shadow-sm"
          : "rounded-lg border border-secondary bg-primary px-3 py-2 shadow-xs"
      }
    >
      <Handle type="target" position={Position.Left} className="!border-0 !bg-transparent" />
      <div className="flex items-center gap-2">
        <div className="text-sm text-primary">{data.displayName}</div>
        {data.detailHref && (
          <Link
            to={data.detailHref}
            onClick={(e) => e.stopPropagation()}
            className="text-xs text-brand-secondary underline"
            aria-label={`Open ${data.displayName} detail page`}
          >
            Open ↗
          </Link>
        )}
      </div>
      <div className="text-xs text-tertiary">{KIND_LABEL[data.kind] ?? data.kind}</div>
      <Handle type="source" position={Position.Right} className="!border-0 !bg-transparent" />
    </div>
  );
}
```

- [ ] **Step 4: Add the "Open full graph" link to `DependencyMiniGraph`**

Replace the header `<h3>` block with a header row containing the link (keep the existing section + states intact):

```tsx
import { Link } from "react-router-dom";
// ...
<div className="flex items-center justify-between">
  <h3 className="text-sm font-semibold text-primary">Dependency graph</h3>
  <Link to={`/graph?focus=${entityKind}:${entityId}`} className="text-xs text-brand-secondary underline">
    Open full graph ↗
  </Link>
</div>
```

- [ ] **Step 5: Run the relevant tests**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/EntityGraphNode.test.tsx src/features/catalog/components/__tests__/DependencyMiniGraph.test.tsx`
Expected: PASS (existing + new). If the mini-graph test asserts exact header markup, update it to expect the link.

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/components/EntityGraphNode.tsx web/src/features/catalog/components/DependencyMiniGraph.tsx web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx
git commit -m "feat(web): open-detail link on graph nodes + Open-full-graph button on the mini-graph (E-04.F-02.S-03)"
```

---

## Task 12: Verification, DoD gates, and checklist

**Files:**
- Modify: `docs/product/CHECKLIST.md`
- Modify: `docs/superpowers/verification/2026-06-26-catalog-graph-explorer/dod.md` + `gate-findings.yaml`

- [ ] **Step 1: Full build (gate 1) + full suite (gate 3)**

Run: `cmd //c "dotnet build Kartova.slnx -warnaserror" && dotnet test Kartova.slnx`
Then: `cd web && npm run lint && npm run build && npx vitest run`
Expected: 0 warnings/errors; all backend + frontend tests green. Record in `dod.md`.

- [ ] **Step 2: Mutation loop (gate 6, blocking)**

Run `/misc:mutation-sentinel` then `/misc:test-generator` scoped to `GraphTraversal.cs` + `GraphTraversalHandler.cs`. Target ≥80%; add unit tests for survivors (most logic is in the pure `GraphTraversal` — extend `GraphTraversalTests`). Record score + survivors in `dod.md`; log any false-positive survivors as `gate: mutation` delusions in `gate-findings.yaml`.

- [ ] **Step 3: /simplify (gate 5)**

Run `/simplify` against the branch diff. Address should-fix items; record findings (real/delusion) in `gate-findings.yaml`.

- [ ] **Step 4: Reviews (gates 2, 7, 8, 9)**

Per-task reviews (gate 2) interleaved; then `/superpowers:requesting-code-review`, `/pr-review-toolkit:review-pr`, `/deep-review` against the full branch diff. Log every finding in `gate-findings.yaml` with its `real`/`delusion` verdict.

- [ ] **Step 5: Container build (gate 4) + Playwright (ADR-0084)**

Run `scripts/ci-local.sh` (Release mirror) — green before push. Then Playwright MCP, cold-start dev server:
- Seed a small graph first (DevSeed has apps but few relationships — create 2–3 `depends-on` edges via the UI or API so multi-hop is visible).
- Application detail → "Open full graph" → `/graph?focus=…` renders, focus emphasised.
- Click a neighbour → expands (URL gains `?expand=…`), new nodes appear; click again → collapses; browser Back undoes it.
- Open-detail link navigates to the entity page. Service focus → same. Empty entity → "Pick an entity"/lone focus. Console clean.
- Save screenshots under `verification/2026-06-26-catalog-graph-explorer/playwright/`. If the dev stack is unavailable, mark Playwright **pending user verification** in `dod.md`.

- [ ] **Step 6: Update the checklist**

In `docs/product/CHECKLIST.md`, mark `E-04.F-02.S-03` and `E-04.F-02.S-04` complete (with the slice slug + date), and note S-05/S-06 remain deferred.

- [ ] **Step 7: Terminal re-verify + push + PR**

Re-run build + full suite after gates 5–9 fixes (terminal re-verify). Push the branch, open the PR, and finish the `dod.md` rows. Completion claim must cite `docs/superpowers/verification/2026-06-26-catalog-graph-explorer/dod.md`.

```bash
git add docs/product/CHECKLIST.md docs/superpowers/verification/2026-06-26-catalog-graph-explorer/
git commit -m "docs(catalog): mark E-04.F-02.S-03/S-04 done + DoD evidence for graph explorer"
git push -u origin feat/catalog-graph-explorer
```

---

## Self-Review

**1. Spec coverage:**
- §3 #1 traversal endpoint → Tasks 2–5. #2 GraphResponse depth+teamId → Task 2 + Task 4 (enrichment) + Task 5 (teamId asserted). #3 BFS/cycle/cap/direction/depth bounds → Task 3 (pure) + Task 4 (delegate validation) + Task 5 (real-seam). #4 `/graph` under ProtectedShell, canvas fills content → Task 10. #5 URL source of truth → Task 10. #6 expansion composes endpoint + merge → Tasks 7, 9, 10. #7 dagre → Tasks 7, 8. #8 read-only + expand/collapse + open-detail → Tasks 10, 11. #9 reuse EntityGraphNode (data-driven link) → Tasks 8, 11. #10 depth returned not visualised → returned (Task 4), not rendered (Task 10). #11 lazy route → Task 10. #12 mocked-in-tests / pure mappers → Tasks 7, 8, 10. #13 CatalogRead → Task 4. #14 bounded aggregate → Task 4 (no cursor). S-03 button → Task 11. Tests §7.1–§7.6 → Tasks 3, 4, 5, 7, 8, 10, 11, 12.
- Gate artifacts (real-seam `GetCatalogGraphTests`, unit `GraphTraversalTests`/`graphMerge`/`graphLayout`, component `GraphExplorerPage`) each have an owning task.

**2. Placeholder scan:** No TBD/TODO. The `depth` param string-vs-number note (Task 9) is resolved by `tsc -b`, with both branches stated.

**3. Type consistency:** `GraphResponse`/`GraphNodeDto`/`GraphEdgeDto`/`GraphEndpointDto` identical across Tasks 2/4/6/7. `GraphTraversalEdge`/`Node`/`Result` + `BuildAsync` signature identical across Tasks 3/4. `ExplorerNode`/`ExplorerEdge`/`ExplorerGraph` + `mergeGraphs` identical across Tasks 7/8/10. `layoutGraph(graph, focusId)` identical across Tasks 8/10. `useGraph({focus, expand})` + `GraphFocus` identical across Tasks 9/10. `GraphNodeData.detailHref` added in Task 8, consumed in Tasks 8/10/11. Node React Flow `type: "entity"` matches `NODE_TYPES = { entity: EntityGraphNode }` (Tasks 8/10).

No gaps found.
