# Visual Impact Analysis on the Graph Explorer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add "Impact analysis" to the standalone `/graph` explorer — from a Service/Application node, compute the transitive blast radius (everything that depends on it), merge those nodes onto the canvas, dim the rest, glow by tier, and show a count banner with a Close button.

**Architecture:** A dedicated backend endpoint `GET /catalog/impact` runs a pure directed BFS (`ImpactAnalysis.Compute`) over the union of explicit + derived `depends-on` edges in the *dependents* direction, tiers each node by hop distance, and emits the **reused `GraphResponse` contract** (tier carried in `GraphNodeDto.Depth`). The frontend feeds that response through the existing `mergeGraphs` (zero new merge code), builds a tier map, dims all non-impacted nodes, glows impacted nodes by tier, and renders a banner.

**Tech Stack:** .NET 10 / ASP.NET Core Minimal APIs · EF Core · Wolverine (not used here — direct handler dispatch per ADR-0090) · React + TypeScript · @xyflow/react + dagre · React Query · Untitled UI · MSTest v4 (native asserts) · Testcontainers · Playwright.

## Global Constraints

- **Build:** full solution builds with `TreatWarningsAsErrors=true` — 0 warnings, 0 errors.
- **Reuse the `GraphResponse` contract verbatim** (`GraphResponse`/`GraphNodeDto`/`GraphEdgeDto`/`GraphEndpointDto`/`DerivedEdgeDto`) — **no new response DTO**. Tier is carried in `GraphNodeDto.Depth`; `OutDegree`/`InDegree` are `0` (affordance unused in impact overlay).
- **Semantics:** blast radius = transitive **incoming** `depends-on` (an edge `Source→Target` = "Source depends on Target"; a dependent of focus F = any Source reaching F). Edge set = **explicit ∪ derived** `depends-on` (ADR-0111 §5). Tier = hop distance; focus is tier 0 and excluded from the downstream count.
- **Bound:** node cap **200**, `Truncated` flag; depth not user-chosen (full closure).
- **Subjects:** `service` or `application` only. Endpoint validation: `entityKind=api` or malformed kind or empty `entityId` → **400**; unknown / cross-tenant service/application → **422** (RLS-scoped `lookup.Find` returns null). *(Aligns with the sibling `GetApiSurfaceAsync`: `api` is a structurally-invalid kind → 400, not a missing entity → 422. This refines the design's "api→422" line — see handoff note.)*
- **Auth:** `.RequireAuthorization(KartovaPermissions.CatalogRead)`. **No new permission, no 5-sync.**
- **`EntityRef`** is `public readonly record struct EntityRef(EntityKind Kind, Guid Id)` — value equality; usable as a dictionary/set key.
- **Windows shell:** run `dotnet` via `cmd //c "..."`. Stop the Vite dev server before `scripts/ci-local.sh frontend` (npm ci EPERM-vs-5173 lock).
- **Line endings:** repo is LF-normalized (`.gitattributes eol=lf`); don't introduce CRLF.
- Commit after each task's tests pass.

---

## File Structure

**Backend (new):**
- `src/Modules/Catalog/Kartova.Catalog.Application/ImpactAnalysis.cs` — pure BFS helper.
- `src/Modules/Catalog/Kartova.Catalog.Application/GetImpactAnalysisQuery.cs` — query record.
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetImpactAnalysisHandler.cs` — loads edges, calls `Compute`, emits `GraphResponse`.
- `src/Modules/Catalog/Kartova.Catalog.Tests/ImpactAnalysisTests.cs` — unit tests.
- `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetImpactAnalysisTests.cs` — real-seam tests.

**Backend (modified):**
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` — add `GetImpactAnalysisAsync`.
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` — route + DI.

**Frontend (new):**
- `web/src/features/catalog/api/impact.ts` — `useImpactAnalysis`.
- `web/src/features/catalog/relationships/impactModel.ts` — pure `buildTierMap`/`impactDim`/`tierCounts`/`impactTotal`.
- `web/src/features/catalog/components/ImpactBanner.tsx` — count banner.
- Test files under `__tests__/` siblings for each.

**Frontend (modified):**
- `web/src/features/catalog/relationships/graphModel.ts` — `GraphNodeData.impactTier`.
- `web/src/features/catalog/relationships/graphLayout.ts` — `tierByNodeId` param → stamp `impactTier`.
- `web/src/features/catalog/components/EntityGraphNode.tsx` — tier glow ring.
- `web/src/features/catalog/components/GraphExplorerSidebar.tsx` — "Impact analysis" button.
- `web/src/features/catalog/pages/GraphExplorerPage.tsx` — state, fetch, merge, dim union, glow, banner.

---

## Task 1: Pure `ImpactAnalysis.Compute` + unit tests

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/ImpactAnalysis.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ImpactAnalysisTests.cs`

**Interfaces:**
- Produces: `ImpactAnalysis.Compute(EntityRef focus, IReadOnlyCollection<(EntityRef Source, EntityRef Target)> dependsOnEdges, int nodeCap) → ImpactAnalysis.Result`; `Result(IReadOnlyList<Node> Impacted, bool Truncated)`; `Node(EntityRef Ref, int Tier)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Tests/ImpactAnalysisTests.cs
using Kartova.Catalog.Application;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public sealed class ImpactAnalysisTests
{
    private static EntityRef Svc(int n) => new(EntityKind.Service, Guid.Parse($"00000000-0000-0000-0000-0000000000{n:D2}"));
    private static EntityRef App(int n) => new(EntityKind.Application, Guid.Parse($"00000000-0000-0000-0000-0000000001{n:D2}"));

    // Edge (Source, Target) = "Source depends on Target".
    private static (EntityRef, EntityRef) Dep(EntityRef source, EntityRef target) => (source, target);

    [TestMethod]
    public void Direct_dependent_is_tier_1()
    {
        var f = Svc(0);
        var a = Svc(1); // a depends on f
        var r = ImpactAnalysis.Compute(f, [Dep(a, f)], nodeCap: 200);

        var node = r.Impacted.Single();
        Assert.AreEqual(a, node.Ref);
        Assert.AreEqual(1, node.Tier);
        Assert.IsFalse(r.Truncated);
    }

    [TestMethod]
    public void Chain_tiers_by_hop_distance()
    {
        var f = Svc(0);
        var b = Svc(1); // b depends on f
        var a = Svc(2); // a depends on b
        var r = ImpactAnalysis.Compute(f, [Dep(b, f), Dep(a, b)], nodeCap: 200);

        Assert.AreEqual(1, r.Impacted.Single(n => n.Ref == b).Tier);
        Assert.AreEqual(2, r.Impacted.Single(n => n.Ref == a).Tier);
        Assert.AreEqual(2, r.Impacted.Count);
    }

    [TestMethod]
    public void Diamond_assigns_min_tier_once()
    {
        var f = Svc(0);
        var b = Svc(1);
        var c = Svc(2);
        var a = Svc(3); // a depends on both b and c; b,c depend on f
        var r = ImpactAnalysis.Compute(f, [Dep(b, f), Dep(c, f), Dep(a, b), Dep(a, c)], nodeCap: 200);

        Assert.AreEqual(3, r.Impacted.Count); // a counted once
        Assert.AreEqual(2, r.Impacted.Single(n => n.Ref == a).Tier);
    }

    [TestMethod]
    public void Cycle_terminates()
    {
        var f = Svc(0);
        var a = Svc(1);
        var b = Svc(2);
        // a↔b cycle, a depends on f
        var r = ImpactAnalysis.Compute(f, [Dep(a, f), Dep(a, b), Dep(b, a)], nodeCap: 200);

        Assert.AreEqual(1, r.Impacted.Single(n => n.Ref == a).Tier);
        Assert.AreEqual(2, r.Impacted.Single(n => n.Ref == b).Tier);
        Assert.AreEqual(2, r.Impacted.Count);
    }

    [TestMethod]
    public void Leaf_focus_has_no_dependents()
    {
        var f = Svc(0);
        var other = Svc(1); // f depends on other (outgoing only) — not a dependent of f
        var r = ImpactAnalysis.Compute(f, [Dep(f, other)], nodeCap: 200);

        Assert.AreEqual(0, r.Impacted.Count);
        Assert.IsFalse(r.Truncated);
    }

    [TestMethod]
    public void Mixed_kinds_traverse_across_app_and_service()
    {
        var f = Svc(0);
        var app = App(1); // app depends on f
        var svc = Svc(2); // svc depends on app
        var r = ImpactAnalysis.Compute(f, [Dep(app, f), Dep(svc, app)], nodeCap: 200);

        Assert.AreEqual(1, r.Impacted.Single(n => n.Ref == app).Tier);
        Assert.AreEqual(2, r.Impacted.Single(n => n.Ref == svc).Tier);
    }

    [TestMethod]
    public void Node_cap_truncates()
    {
        var f = Svc(0);
        var edges = new[] { Dep(Svc(1), f), Dep(Svc(2), f), Dep(Svc(3), f) };
        var r = ImpactAnalysis.Compute(f, edges, nodeCap: 2);

        Assert.AreEqual(2, r.Impacted.Count);
        Assert.IsTrue(r.Truncated);
    }

    [TestMethod]
    public void Focus_never_appears_in_impacted()
    {
        var f = Svc(0);
        var a = Svc(1);
        var r = ImpactAnalysis.Compute(f, [Dep(a, f), Dep(f, a)], nodeCap: 200);

        Assert.IsFalse(r.Impacted.Any(n => n.Ref == f));
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter FullyQualifiedName~ImpactAnalysisTests"`
Expected: FAIL — `ImpactAnalysis` does not exist (compile error).

- [ ] **Step 3: Implement `ImpactAnalysis.Compute`**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/ImpactAnalysis.cs
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Pure blast-radius traversal (E-04.F-02.S-06): the transitive set of entities that depend
/// on <paramref name="focus"/>, following depends-on edges in the DEPENDENTS direction (an edge
/// Source→Target means "Source depends on Target"; a dependent of F is any Source on a path that
/// reaches F). Tier = hop distance from focus; focus is tier 0 and excluded from the result. First-seen
/// tier wins (cycle-safe). Stops once <paramref name="nodeCap"/> impacted nodes are collected →
/// <see cref="Result.Truncated"/>. The edge set is explicit ∪ derived depends-on, unioned by the
/// handler; this helper is agnostic to how an edge arose.</summary>
public static class ImpactAnalysis
{
    public sealed record Node(EntityRef Ref, int Tier);

    public sealed record Result(IReadOnlyList<Node> Impacted, bool Truncated);

    public static Result Compute(
        EntityRef focus,
        IReadOnlyCollection<(EntityRef Source, EntityRef Target)> dependsOnEdges,
        int nodeCap)
    {
        // dependentsOf[X] = every Source that depends on X (edges whose Target == X).
        var dependentsOf = new Dictionary<EntityRef, List<EntityRef>>();
        foreach (var (source, target) in dependsOnEdges)
        {
            if (!dependentsOf.TryGetValue(target, out var list))
                dependentsOf[target] = list = [];
            list.Add(source);
        }

        var tier = new Dictionary<EntityRef, int> { [focus] = 0 };
        var impacted = new List<Node>();
        var frontier = new List<EntityRef> { focus };
        var truncated = false;

        for (var level = 1; frontier.Count > 0 && !truncated; level++)
        {
            var next = new List<EntityRef>();
            foreach (var node in frontier)
            {
                if (!dependentsOf.TryGetValue(node, out var dependents)) continue;
                foreach (var dep in dependents)
                {
                    if (tier.ContainsKey(dep)) continue;         // first-seen tier wins (cycle-safe)
                    if (impacted.Count >= nodeCap) { truncated = true; break; }
                    tier[dep] = level;
                    impacted.Add(new Node(dep, level));
                    next.Add(dep);
                }
                if (truncated) break;
            }
            frontier = next;
        }

        return new Result(impacted, truncated);
    }
}
```

- [ ] **Step 4: Run tests, verify they pass**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter FullyQualifiedName~ImpactAnalysisTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/ImpactAnalysis.cs src/Modules/Catalog/Kartova.Catalog.Tests/ImpactAnalysisTests.cs
git commit -m "feat(catalog): pure ImpactAnalysis.Compute blast-radius traversal (E-04.F-02.S-06)"
```

---

## Task 2: Real-seam integration tests (`GetImpactAnalysisTests`) — red

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetImpactAnalysisTests.cs`

**Interfaces:**
- Consumes: endpoint `GET /api/v1/catalog/impact?entityKind=&entityId=` → `GraphResponse`; seed helpers pattern from `GetDerivedDependenciesTests`.
- Produces: nothing (test-only). These fail until Task 3 lands the endpoint.

- [ ] **Step 1: Write the failing integration tests**

```csharp
// src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetImpactAnalysisTests.cs
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Kartova.Testing.Auth;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public sealed class GetImpactAnalysisTests : CatalogIntegrationTestBase
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
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/services",
            new { displayName = name, description = "x", teamId, endpoints = Array.Empty<object>() });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedService '{name}': {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<ServiceResponse>(KartovaApiFixtureBase.WireJson))!.Id;
    }

    private static async Task<Guid> SeedApplicationAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/applications",
            new { displayName = name, description = "x", teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApp '{name}': {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<ApplicationResponse>(KartovaApiFixtureBase.WireJson))!.Id;
    }

    private static async Task<Guid> SeedApiAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", new
        { displayName = name, description = "x", style = ApiStyle.Rest, version = "v1", specUrl = (string?)null, teamId });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApi '{name}': {resp.StatusCode}");
        return (await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson))!.Id;
    }

    private static Task<HttpResponseMessage> GetImpactAsync(HttpClient client, string kind, Guid id)
        => client.GetAsync($"/api/v1/catalog/impact?entityKind={kind}&entityId={id}");

    [TestMethod]
    public async Task Multi_tier_blast_radius_includes_explicit_and_derived()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Impact " + Guid.NewGuid());

        // Explicit: A depends-on F (tier 1); B depends-on A (tier 2).
        var f = await SeedServiceAsync(client, teamId, "impact-focus");
        var a = await SeedServiceAsync(client, teamId, "impact-a");
        var b = await SeedServiceAsync(client, teamId, "impact-b");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, a, RelationshipType.DependsOn, EntityKind.Service, f)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, b, RelationshipType.DependsOn, EntityKind.Service, a)).StatusCode);

        // Derived: C consumes an API that F provides ⇒ C derives depends-on F (tier 1).
        var c = await SeedServiceAsync(client, teamId, "impact-c");
        var api = await SeedApiAsync(client, teamId, "impact-api");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, f, RelationshipType.ProvidesApiFor, EntityKind.Api, api)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, c, RelationshipType.ConsumesApiFrom, EntityKind.Api, api)).StatusCode);

        var resp = await GetImpactAsync(client, "service", f);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
        Assert.IsNotNull(body);

        // Nodes: focus (depth 0) + A,C (depth 1) + B (depth 2). API node is NOT a dependent (not depends-on).
        int Depth(Guid id) => body!.Nodes.Single(n => n.Id == id).Depth;
        Assert.AreEqual(0, Depth(f));
        Assert.AreEqual(1, Depth(a));
        Assert.AreEqual(1, Depth(c));
        Assert.AreEqual(2, Depth(b));
        Assert.IsFalse(body!.Nodes.Any(n => n.Id == api), "API node is not a depends-on dependent");
        Assert.IsFalse(body.Truncated);
        // A derived edge (C→F) is present in DerivedEdges; explicit A→F, B→A in Edges.
        Assert.IsTrue(body.DerivedEdges.Any(e => e.Source.Id == c && e.Target.Id == f));
        Assert.IsTrue(body.Edges.Any(e => e.Source.Id == a && e.Target.Id == f));
    }

    [TestMethod]
    public async Task Application_focus_is_supported()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Impact App " + Guid.NewGuid());
        var app = await SeedApplicationAsync(client, teamId, "impact-app-focus");
        var svc = await SeedServiceAsync(client, teamId, "impact-app-dependent");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, svc, RelationshipType.DependsOn, EntityKind.Application, app)).StatusCode);

        var resp = await GetImpactAsync(client, "application", app);
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(1, body!.Nodes.Single(n => n.Id == svc).Depth);
    }

    [TestMethod]
    public async Task Api_focus_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Impact Api " + Guid.NewGuid());
        var api = await SeedApiAsync(client, teamId, "impact-api-focus");
        var resp = await GetImpactAsync(client, "api", api);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Unknown_entity_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await GetImpactAsync(client, "service", Guid.NewGuid());
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task Empty_entityId_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var resp = await GetImpactAsync(client, "service", Guid.Empty);
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Other_tenant_entity_is_not_visible_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Impact XT " + Guid.NewGuid());
        var f = await SeedServiceAsync(client, teamId, "impact-xt-focus");

        var otherClient = await Fx.CreateAuthenticatedClientAsync(OrgBUser);
        var resp = await GetImpactAsync(otherClient, "service", f);
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }
}
```

- [ ] **Step 2: Run tests, verify they fail**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~GetImpactAnalysisTests"`
Expected: FAIL — the `/impact` route returns 404 (not registered), so status assertions fail.

- [ ] **Step 3: Commit the red tests**

```bash
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetImpactAnalysisTests.cs
git commit -m "test(catalog): failing real-seam tests for GET /catalog/impact (E-04.F-02.S-06)"
```

---

## Task 3: Query + Handler + endpoint + route + DI — green Task 2

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/GetImpactAnalysisQuery.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetImpactAnalysisHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (add delegate)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (route + DI)

**Interfaces:**
- Consumes: `ImpactAnalysis.Compute` (Task 1); `DerivedEdgeLoader.LoadAsync(CatalogDbContext, CancellationToken) → Task<IReadOnlyList<DerivedDependencies.Edge>>` (unchanged); `DerivedProvenanceNames.LoadAsync(IEnumerable<DerivedDependencies.Path>, CatalogDbContext, CancellationToken)` + `.Map(DerivedDependencies.Path) → DerivationPathDto` (unchanged); `ICatalogEntityLookup.Find(EntityKind, Guid, CancellationToken) → Task<EntityLookupResult?>` (unchanged; `EntityLookupResult(Guid TeamId, string DisplayName)`).
- Produces: `GetImpactAnalysisQuery(EntityKind FocusKind, Guid FocusId)`; `GetImpactAnalysisHandler.Handle(GetImpactAnalysisQuery, CatalogDbContext, ICatalogEntityLookup, CancellationToken) → Task<GraphResponse>`; endpoint `CatalogEndpointDelegates.GetImpactAnalysisAsync`.

- [ ] **Step 1: Add the query record**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Application/GetImpactAnalysisQuery.cs
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Read a Service's or Application's blast radius (transitive dependents over explicit ∪
/// derived depends-on). Subject kind is Service or Application only (validated at the endpoint).</summary>
public sealed record GetImpactAnalysisQuery(EntityKind FocusKind, Guid FocusId);
```

- [ ] **Step 2: Add the handler**

```csharp
// src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetImpactAnalysisHandler.cs
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Computes a focus entity's blast radius (E-04.F-02.S-06): the transitive set of entities
/// that depend on it over explicit ∪ derived depends-on edges (ADR-0111 §5), tiered by hop distance.
/// Emits the reused <see cref="GraphResponse"/> contract so the explorer merges the impacted nodes/edges
/// via mergeGraphs — tier rides in <see cref="GraphNodeDto.Depth"/>; OutDegree/InDegree are 0 (affordance
/// unused in the impact overlay). Node cap + Truncated mirror the /graph endpoint.</summary>
public sealed class GetImpactAnalysisHandler
{
    public const int DefaultNodeCap = 200;

    public async Task<GraphResponse> Handle(
        GetImpactAnalysisQuery q, CatalogDbContext db, ICatalogEntityLookup lookup, CancellationToken ct)
    {
        // Explicit depends-on relationships (any app/service pair), RLS-scoped. Materialize then read the
        // Id value object + complex Source/Target in memory (EF can't translate r.Id.Value in a projection —
        // mirrors GraphTraversalHandler, which also materializes before reading r.Id.Value).
        var explicitRels = await db.Relationships
            .Where(r => r.Type == RelationshipType.DependsOn)
            .ToListAsync(ct);

        // Derived service→service depends-on edges (shared loader; explicit-wins already applied).
        var derivedAll = await DerivedEdgeLoader.LoadAsync(db, ct);

        // Unified directed edge set for the pure traversal (Source depends on Target).
        var edges = new List<(EntityRef Source, EntityRef Target)>(explicitRels.Count + derivedAll.Count);
        edges.AddRange(explicitRels.Select(r =>
            (new EntityRef(r.Source.Kind, r.Source.Id), new EntityRef(r.Target.Kind, r.Target.Id))));
        edges.AddRange(derivedAll.Select(e =>
            (new EntityRef(EntityKind.Service, e.SourceServiceId), new EntityRef(EntityKind.Service, e.TargetServiceId))));

        var focus = new EntityRef(q.FocusKind, q.FocusId);
        var result = ImpactAnalysis.Compute(focus, edges, DefaultNodeCap);

        // Closure = focus (tier 0) + impacted. tierByRef drives node projection; closure gates edge inclusion.
        var tierByRef = new Dictionary<EntityRef, int> { [focus] = 0 };
        foreach (var n in result.Impacted) tierByRef[n.Ref] = n.Tier;
        var closure = tierByRef.Keys.ToHashSet();

        // Enrich displayName + teamId per node (bounded by cap; per-id, mirrors GraphTraversalHandler).
        var nodes = new List<GraphNodeDto>(tierByRef.Count);
        foreach (var (nodeRef, t) in tierByRef)
        {
            var info = await lookup.Find(nodeRef.Kind, nodeRef.Id, ct);
            nodes.Add(new GraphNodeDto(
                nodeRef.Kind, nodeRef.Id, info?.DisplayName ?? string.Empty, t, info?.TeamId,
                OutDegree: 0, InDegree: 0));
        }

        // Explicit depends-on edges within the closure → GraphEdgeDto (real ids dedupe with /graph edges FE-side).
        var persisted = explicitRels
            .Where(r => closure.Contains(new EntityRef(r.Source.Kind, r.Source.Id))
                     && closure.Contains(new EntityRef(r.Target.Kind, r.Target.Id)))
            .Select(r => new GraphEdgeDto(
                r.Id.Value,
                new GraphEndpointDto(r.Source.Kind, r.Source.Id),
                new GraphEndpointDto(r.Target.Kind, r.Target.Id),
                RelationshipType.DependsOn, r.Origin))
            .ToList();

        // Derived edges within the closure → DerivedEdgeDto (provenance names via the shared loader).
        var derivedKept = derivedAll
            .Where(e => closure.Contains(new EntityRef(EntityKind.Service, e.SourceServiceId))
                     && closure.Contains(new EntityRef(EntityKind.Service, e.TargetServiceId)))
            .ToList();
        var names = await DerivedProvenanceNames.LoadAsync(derivedKept.SelectMany(e => e.Paths), db, ct);
        var derivedEdges = derivedKept
            .Select(e => new DerivedEdgeDto(
                new GraphEndpointDto(EntityKind.Service, e.SourceServiceId),
                new GraphEndpointDto(EntityKind.Service, e.TargetServiceId),
                e.Paths.Select(names.Map).ToList()))
            .ToList();

        return new GraphResponse(nodes, persisted, derivedEdges, result.Truncated);
    }
}
```

> If `r.Id.Value` / `r.Source.Kind` don't compile as written, check `GraphTraversalHandler.cs` (lines 32-37) for the exact member access on a materialized `Relationship` — this handler mirrors it.

- [ ] **Step 3: Add the endpoint delegate**

Insert after `GetDerivedDependenciesAsync` in `CatalogEndpointDelegates.cs`:

```csharp
    /// <summary>
    /// GET /impact?entityKind=&amp;entityId= — a Service's or Application's blast radius: the transitive set
    /// of entities that depend on it over explicit ∪ derived depends-on (E-04.F-02.S-06), tiered by hop
    /// distance. Reuses the <see cref="GraphResponse"/> contract (tier in Depth). Claim gate: catalog.read.
    /// `entityKind=api`/malformed/empty id → 400 (structural, per GetApiSurfaceAsync); unknown or cross-tenant
    /// service/application → 422 (RLS-scoped lookup returns null).
    /// </summary>
    internal static async Task<IResult> GetImpactAnalysisAsync(
        [FromQuery] string entityKind,
        [FromQuery] Guid entityId,
        GetImpactAnalysisHandler handler,
        ICatalogEntityLookup lookup,
        CatalogDbContext db,
        CancellationToken ct)
    {
        if (!Enum.TryParse<EntityKind>(entityKind, ignoreCase: true, out var kind)
            || !Enum.IsDefined(kind)
            || kind == EntityKind.Api
            || entityId == Guid.Empty)
        {
            return Results.Problem(
                type: ProblemTypes.ValidationFailed,
                title: "Invalid entity reference",
                detail: "entityKind must be 'service' or 'application' and entityId must be non-empty.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        if (await lookup.Find(kind, entityId, ct) is null)
        {
            return Results.Problem(
                type: ProblemTypes.InvalidEntity,
                title: "Invalid entity",
                detail: "The entity does not exist in this tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var result = await handler.Handle(new GetImpactAnalysisQuery(kind, entityId), db, lookup, ct);
        return Results.Ok(result);
    }
```

- [ ] **Step 4: Register route + DI in `CatalogModule.cs`**

Add next to the `/derived-dependencies` route (after line ~173):

```csharp
        tenant.MapGet("/impact", CatalogEndpointDelegates.GetImpactAnalysisAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead);
```

Add next to the other handler registrations (after the derived-dependencies / graph handler registration):

```csharp
        services.AddScoped<GetImpactAnalysisHandler>();
```

> Verify `GraphTraversalHandler` / `GetDerivedDependenciesHandler` are registered the same way and place `GetImpactAnalysisHandler` beside them; match the existing `AddScoped` style in the file.

- [ ] **Step 5: Build + run the integration tests, verify green**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter FullyQualifiedName~GetImpactAnalysisTests"`
Expected: PASS (6 tests). If Docker saturation flakes one assembly (known env issue), re-run that assembly in isolation before treating as red.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Application/GetImpactAnalysisQuery.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetImpactAnalysisHandler.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs
git commit -m "feat(catalog): GET /catalog/impact blast-radius endpoint (E-04.F-02.S-06)"
```

---

## Task 4: Regenerate the OpenAPI client

**Files:**
- Modify (generated): `web/src/generated/openapi.ts` (+ `web/openapi-snapshot.json` if the snapshot is committed).

**Interfaces:**
- Produces: `apiClient.GET("/api/v1/catalog/impact", …)` typed path; `components["schemas"]["GraphResponse"]` already exists (reused).

- [ ] **Step 1: Build the API image so the new endpoint is exposed**

The codegen reads the live API's OpenAPI document. Rebuild + restart the API container so `/impact` appears (per the codegen memory: a new endpoint needs the API image rebuilt).

Run: `cmd //c "docker compose build api && docker compose up -d api"`
(Adjust service name if the compose service differs; confirm the API is serving on its usual port.)

- [ ] **Step 2: Regenerate the client**

Run: `cd web && npm run codegen`
Expected: `web/src/generated/openapi.ts` now contains a `"/api/v1/catalog/impact"` path. `web/openapi-snapshot.json` may show param-order churn (cosmetic, per memory — keep the regenerated version, don't revert).

- [ ] **Step 3: Verify the path type exists**

Run: `cd web && npx tsc -b --noEmit` (or `grep -n "catalog/impact" src/generated/openapi.ts`)
Expected: the path is present; typecheck clean.

- [ ] **Step 4: Commit**

```bash
git add web/src/generated/openapi.ts web/openapi-snapshot.json
git commit -m "chore(web): regenerate OpenAPI client for /catalog/impact"
```

---

## Task 5: FE pure `impactModel.ts` + unit tests

**Files:**
- Create: `web/src/features/catalog/relationships/impactModel.ts`
- Test: `web/src/features/catalog/relationships/__tests__/impactModel.test.ts`

**Interfaces:**
- Consumes: `ExplorerGraph` (from `graphMerge.ts`); `GraphResponse` (from `api/graph.ts`).
- Produces: `buildTierMap(impact: GraphResponse) → Map<string, number>`; `impactDim(graph: ExplorerGraph, impactNodeIds: Set<string>) → { dimmedNodeIds: Set<string>; dimmedEdgeIds: Set<string> }`; `tierCounts(tierByNodeId: Map<string, number>) → { tier: number; count: number }[]`; `impactTotal(tierByNodeId: Map<string, number>) → number`.

- [ ] **Step 1: Write the failing tests**

```ts
// web/src/features/catalog/relationships/__tests__/impactModel.test.ts
import { describe, it, expect } from "vitest";
import { buildTierMap, impactDim, tierCounts, impactTotal } from "@/features/catalog/relationships/impactModel";
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";
import type { GraphResponse } from "@/features/catalog/api/graph";

const impact: GraphResponse = {
  nodes: [
    { kind: "service", id: "f", displayName: "F", depth: 0, teamId: null, outDegree: 0, inDegree: 0 },
    { kind: "service", id: "a", displayName: "A", depth: 1, teamId: null, outDegree: 0, inDegree: 0 },
    { kind: "service", id: "b", displayName: "B", depth: 2, teamId: null, outDegree: 0, inDegree: 0 },
  ],
  edges: [],
  derivedEdges: [],
  truncated: false,
} as unknown as GraphResponse;

const graph: ExplorerGraph = {
  nodes: [
    { id: "service:f", kind: "service", entityId: "f", displayName: "F", outDegree: 0, inDegree: 0 },
    { id: "service:a", kind: "service", entityId: "a", displayName: "A", outDegree: 0, inDegree: 0 },
    { id: "service:x", kind: "service", entityId: "x", displayName: "X", outDegree: 0, inDegree: 0 },
  ],
  edges: [
    { id: "e1", source: "service:a", target: "service:f", label: "depends on" },
    { id: "e2", source: "service:x", target: "service:f", label: "depends on" },
  ],
  truncated: false,
};

describe("impactModel", () => {
  it("buildTierMap maps nodeId → depth", () => {
    const m = buildTierMap(impact);
    expect(m.get("service:f")).toBe(0);
    expect(m.get("service:a")).toBe(1);
    expect(m.get("service:b")).toBe(2);
  });

  it("impactDim dims everything not in the impacted set; edge dims iff an endpoint dims", () => {
    const impacted = new Set(["service:f", "service:a"]);
    const { dimmedNodeIds, dimmedEdgeIds } = impactDim(graph, impacted);
    expect(dimmedNodeIds.has("service:x")).toBe(true);
    expect(dimmedNodeIds.has("service:f")).toBe(false);
    expect(dimmedNodeIds.has("service:a")).toBe(false);
    expect(dimmedEdgeIds.has("e2")).toBe(true);  // x dims → e2 dims
    expect(dimmedEdgeIds.has("e1")).toBe(false); // a,f lit → e1 lit
  });

  it("tierCounts groups by tier, excludes focus (tier 0), ascending", () => {
    const m = new Map([["service:f", 0], ["service:a", 1], ["service:c", 1], ["service:b", 2]]);
    expect(tierCounts(m)).toEqual([{ tier: 1, count: 2 }, { tier: 2, count: 1 }]);
  });

  it("impactTotal excludes focus", () => {
    const m = new Map([["service:f", 0], ["service:a", 1], ["service:b", 2]]);
    expect(impactTotal(m)).toBe(2);
  });
});
```

- [ ] **Step 2: Run, verify fail**

Run: `cd web && npm run test -- src/features/catalog/relationships/__tests__/impactModel.test.ts`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `impactModel.ts`**

```ts
// web/src/features/catalog/relationships/impactModel.ts
import type { ExplorerGraph } from "@/features/catalog/relationships/graphMerge";
import type { GraphResponse } from "@/features/catalog/api/graph";

const nodeId = (kind: string, id: string) => `${kind}:${id}`;

/** nodeId → tier (hop distance) from the impact response. Focus is depth 0. */
export function buildTierMap(impact: GraphResponse): Map<string, number> {
  const m = new Map<string, number>();
  for (const n of impact.nodes) m.set(nodeId(n.kind, n.id), Number(n.depth));
  return m;
}

/** Dim every merged node NOT in the impacted set; an edge dims iff either endpoint dims.
 *  Mirrors applyGraphFilters' return shape so the page can union the two dim sets. */
export function impactDim(
  graph: ExplorerGraph,
  impactNodeIds: Set<string>,
): { dimmedNodeIds: Set<string>; dimmedEdgeIds: Set<string> } {
  const dimmedNodeIds = new Set<string>();
  for (const n of graph.nodes) if (!impactNodeIds.has(n.id)) dimmedNodeIds.add(n.id);
  const dimmedEdgeIds = new Set<string>();
  for (const e of graph.edges)
    if (dimmedNodeIds.has(e.source) || dimmedNodeIds.has(e.target)) dimmedEdgeIds.add(e.id);
  return { dimmedNodeIds, dimmedEdgeIds };
}

/** Per-tier counts for the banner, ascending, excluding tier 0 (focus). */
export function tierCounts(tierByNodeId: Map<string, number>): { tier: number; count: number }[] {
  const counts = new Map<number, number>();
  for (const t of tierByNodeId.values()) {
    if (t === 0) continue;
    counts.set(t, (counts.get(t) ?? 0) + 1);
  }
  return [...counts.entries()].sort((a, b) => a[0] - b[0]).map(([tier, count]) => ({ tier, count }));
}

/** Total downstream count (excludes focus). */
export function impactTotal(tierByNodeId: Map<string, number>): number {
  let n = 0;
  for (const t of tierByNodeId.values()) if (t !== 0) n++;
  return n;
}
```

- [ ] **Step 4: Run, verify pass**

Run: `cd web && npm run test -- src/features/catalog/relationships/__tests__/impactModel.test.ts`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/relationships/impactModel.ts web/src/features/catalog/relationships/__tests__/impactModel.test.ts
git commit -m "feat(web): pure impactModel (tier map / dim / counts) for graph impact analysis"
```

---

## Task 6: Tier glow — `GraphNodeData.impactTier` + `layoutGraph` param + `EntityGraphNode` ring

**Files:**
- Modify: `web/src/features/catalog/relationships/graphModel.ts` (add `impactTier`)
- Modify: `web/src/features/catalog/relationships/graphLayout.ts` (new param, stamp)
- Modify: `web/src/features/catalog/components/EntityGraphNode.tsx` (glow ring)
- Test: `web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx` (extend or create)

**Interfaces:**
- Consumes: `GraphNodeData` (extended), `layoutGraph` (extended signature).
- Produces: `layoutGraph(graph, focusId, selectedId, dimmed?, decorate?, tierByNodeId?: Map<string, number>)` stamps `data.impactTier`; `EntityGraphNode` renders a ring keyed on `data.impactTier > 0`.

- [ ] **Step 1: Add `impactTier` to `GraphNodeData`**

In `graphModel.ts`, add to the `GraphNodeData` type (after `dimmed`):

```ts
  // explorer: impact-analysis tier (hop distance from the analyzed node); undefined outside impact mode,
  // 0 for the analyzed node itself (no glow). Drives the tier glow ring in EntityGraphNode.
  impactTier?: number;
```

- [ ] **Step 2: Thread `tierByNodeId` through `layoutGraph`**

In `graphLayout.ts`, add the param and stamp it:

```ts
export function layoutGraph(
  graph: ExplorerGraph,
  focusId: string,
  selectedId: string | null,
  dimmed: { nodeIds: Set<string>; edgeIds: Set<string> } = { nodeIds: new Set(), edgeIds: new Set() },
  decorate?: Map<string, ExpandAffordance>,
  tierByNodeId?: Map<string, number>,   // NEW
): { nodes: Node<GraphNodeData>[]; edges: Edge[] } {
```

In the `data: { … }` object of the node map, add:

```ts
        impactTier: tierByNodeId?.get(n.id),
```

- [ ] **Step 3: Render the glow ring in `EntityGraphNode.tsx`**

Add above the `return` (after the `dim` const):

```tsx
  // Impact-analysis glow: tier-1 strongest → deeper tiers cooler. Focus (tier 0) gets no ring.
  const IMPACT_RING: Record<number, string> = {
    1: "ring-2 ring-error-solid",
    2: "ring-2 ring-warning-solid",
  };
  const impact =
    data.impactTier && data.impactTier > 0 ? (IMPACT_RING[data.impactTier] ?? "ring-2 ring-brand-solid") : "";
```

Add `${impact}` to the wrapper `div` className:

```tsx
    <div className={`${base} ${variant} ${dim} ${impact} relative`}>
```

> Verify `ring-error-solid` / `ring-warning-solid` / `ring-brand-solid` resolve in this Untitled UI / Tailwind v4 setup (the derived-edge stroke already uses `var(--color-fg-quaternary)`, so `--color-*-solid` vars exist). If a `ring-*-solid` utility isn't generated, fall back to arbitrary values, e.g. `ring-2 ring-[color:var(--color-error-solid)]`.

- [ ] **Step 4: Write/extend the node test**

Add to `EntityGraphNode.test.tsx` (wrap in the existing `GraphActionsProvider` test harness used by sibling tests):

```tsx
it("renders a tier glow ring when impactTier > 0 and none for the focus (tier 0)", () => {
  const { container, rerender } = renderNode({ impactTier: 1 });      // helper builds NodeProps with data
  expect(container.querySelector(".ring-error-solid")).not.toBeNull();

  rerender(nodeWith({ impactTier: 0 }));
  expect(container.querySelector(".ring-error-solid")).toBeNull();
});
```

> Match the existing test's render harness (it must provide `GraphActionsContext`). If the file doesn't exist yet, model it on `__tests__/RelationshipsSection.test.tsx`'s setup and assert on `data.impactTier` via the rendered class.

- [ ] **Step 5: Run tests + typecheck**

Run: `cd web && npm run test -- src/features/catalog/components/__tests__/EntityGraphNode.test.tsx`
Then: `cd web && npx tsc -b --noEmit`
Expected: PASS + clean typecheck.

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/relationships/graphModel.ts web/src/features/catalog/relationships/graphLayout.ts web/src/features/catalog/components/EntityGraphNode.tsx web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx
git commit -m "feat(web): tier glow ring on impacted graph nodes"
```

---

## Task 7: `ImpactBanner` component

**Files:**
- Create: `web/src/features/catalog/components/ImpactBanner.tsx`
- Test: `web/src/features/catalog/components/__tests__/ImpactBanner.test.tsx`

**Interfaces:**
- Produces: `ImpactBanner(props: { total: number; tiers: { tier: number; count: number }[]; truncated: boolean; nodeCap: number; onClose: () => void })`.

- [ ] **Step 1: Write the failing test**

```tsx
// web/src/features/catalog/components/__tests__/ImpactBanner.test.tsx
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ImpactBanner } from "@/features/catalog/components/ImpactBanner";

describe("ImpactBanner", () => {
  it("summarizes total + per-tier counts and fires onClose", async () => {
    const onClose = vi.fn();
    render(<ImpactBanner total={12} tiers={[{ tier: 1, count: 3 }, { tier: 2, count: 5 }, { tier: 3, count: 4 }]} truncated={false} nodeCap={200} onClose={onClose} />);
    expect(screen.getByText(/12 downstream/)).toBeInTheDocument();
    expect(screen.getByText(/3× tier-1/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole("button", { name: /close analysis/i }));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it("shows the cap note when truncated", () => {
    render(<ImpactBanner total={200} tiers={[{ tier: 1, count: 200 }]} truncated nodeCap={200} onClose={() => {}} />);
    expect(screen.getByText(/showing first 200/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run, verify fail**

Run: `cd web && npm run test -- src/features/catalog/components/__tests__/ImpactBanner.test.tsx`
Expected: FAIL — module not found.

- [ ] **Step 3: Implement `ImpactBanner.tsx`**

```tsx
// web/src/features/catalog/components/ImpactBanner.tsx
export function ImpactBanner(props: {
  total: number;
  tiers: { tier: number; count: number }[];
  truncated: boolean;
  nodeCap: number;
  onClose: () => void;
}) {
  const { total, tiers, truncated, nodeCap, onClose } = props;
  const summary = tiers.map((t) => `${t.count}× tier-${t.tier}`).join(", ");
  return (
    <div className="flex items-center gap-3 rounded-md bg-primary/90 px-3 py-2 text-sm ring-1 ring-secondary">
      <span className="font-medium text-primary">
        {total} downstream{summary ? ` (${summary})` : ""}
      </span>
      {truncated && <span className="text-xs text-warning-primary">showing first {nodeCap}</span>}
      <button
        type="button"
        onClick={onClose}
        className="ml-2 rounded-md border border-secondary px-2 py-1 text-xs text-primary"
      >
        Close Analysis
      </button>
    </div>
  );
}
```

- [ ] **Step 4: Run, verify pass**

Run: `cd web && npm run test -- src/features/catalog/components/__tests__/ImpactBanner.test.tsx`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/components/ImpactBanner.tsx web/src/features/catalog/components/__tests__/ImpactBanner.test.tsx
git commit -m "feat(web): ImpactBanner count/tier summary component"
```

---

## Task 8: "Impact analysis" button on `GraphExplorerSidebar`

**Files:**
- Modify: `web/src/features/catalog/components/GraphExplorerSidebar.tsx`
- Test: `web/src/features/catalog/components/__tests__/GraphExplorerSidebar.test.tsx` (extend or create)

**Interfaces:**
- Produces: `GraphExplorerSidebar` gains prop `onImpactAnalysis?: () => void`; renders an "Impact analysis" button only when `selected.kind` is `"service"` or `"application"` and the prop is provided.

- [ ] **Step 1: Add the prop + button**

In the props type, add `onImpactAnalysis?: () => void;` and destructure it. In the `mt-auto` action group, above the `dirRow("out", …)` calls, add:

```tsx
        {(selected.kind === "service" || selected.kind === "application") && onImpactAnalysis && (
          <button
            type="button"
            onClick={onImpactAnalysis}
            className="w-full rounded-md border border-secondary px-3 py-1.5 text-sm text-primary"
          >
            Impact analysis
          </button>
        )}
```

- [ ] **Step 2: Write/extend the test**

```tsx
it("shows Impact analysis for service/application and hides it for api", () => {
  const onImpactAnalysis = vi.fn();
  const { rerender } = renderSidebar({ selected: { kind: "service", id: "s1" }, onImpactAnalysis });
  expect(screen.getByRole("button", { name: /impact analysis/i })).toBeInTheDocument();

  rerender(sidebarWith({ selected: { kind: "api", id: "a1" }, onImpactAnalysis }));
  expect(screen.queryByRole("button", { name: /impact analysis/i })).toBeNull();
});
```

> Use the existing sidebar test harness (it mocks `useApplication`/`useService`/`useApi`). If no test file exists, model the query mocks on how `GraphExplorerSidebar` calls those hooks (all three are always called; inactive ones disabled via `id=""`).

- [ ] **Step 3: Run tests + typecheck**

Run: `cd web && npm run test -- src/features/catalog/components/__tests__/GraphExplorerSidebar.test.tsx`
Then: `cd web && npx tsc -b --noEmit`
Expected: PASS + clean.

- [ ] **Step 4: Commit**

```bash
git add web/src/features/catalog/components/GraphExplorerSidebar.tsx web/src/features/catalog/components/__tests__/GraphExplorerSidebar.test.tsx
git commit -m "feat(web): Impact analysis button on graph explorer sidebar (service/app only)"
```

---

## Task 9: Wire `GraphExplorerPage` — state, fetch, merge, dim union, glow, banner

**Files:**
- Create: `web/src/features/catalog/api/impact.ts`
- Modify: `web/src/features/catalog/pages/GraphExplorerPage.tsx`
- Test: `web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx` (extend or create)

**Interfaces:**
- Consumes: `useImpactAnalysis` (new); `mergeGraphs`, `applyGraphFilters`, `layoutGraph` (existing); `buildTierMap`/`impactDim`/`tierCounts`/`impactTotal` (Task 5); `ImpactBanner` (Task 7); sidebar `onImpactAnalysis` (Task 8).
- Produces: end-to-end impact overlay behavior on `/graph`.

- [ ] **Step 1: Add the data hook `api/impact.ts`**

```ts
// web/src/features/catalog/api/impact.ts
import { useQuery } from "@tanstack/react-query";
import { apiClient } from "./client";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { GraphResponse } from "@/features/catalog/api/graph";
import type { RelationshipKind } from "@/features/catalog/relationships/relationshipTypeRules";

export type ImpactSubject = { kind: RelationshipKind; id: string };

async function fetchImpact(s: ImpactSubject): Promise<GraphResponse> {
  const { data, error } = await apiClient.GET("/api/v1/catalog/impact", {
    params: { query: { entityKind: s.kind, entityId: s.id } },
  });
  if (error) throw error;
  return unwrapData(data);
}

export function useImpactAnalysis(subject: ImpactSubject | null) {
  return useQuery({
    queryKey: ["catalog", "impact", subject?.kind, subject?.id],
    queryFn: () => fetchImpact(subject!),
    enabled: subject != null,
  });
}
```

- [ ] **Step 2: Wire the page**

Add imports:

```tsx
import { useState } from "react";
import { useImpactAnalysis, type ImpactSubject } from "@/features/catalog/api/impact";
import { buildTierMap, impactDim, tierCounts, impactTotal } from "@/features/catalog/relationships/impactModel";
import { ImpactBanner } from "@/features/catalog/components/ImpactBanner";
```

Add state + fetch (after the `useGraphFilters` line):

```tsx
  const [impactSubject, setImpactSubject] = useState<ImpactSubject | null>(null);
  // Clear a stale impact overlay when the focus changes (prev-key guard, matching useExplorerState).
  const [prevFocusForImpact, setPrevFocusForImpact] = useState(focusId);
  if (prevFocusForImpact !== focusId) {
    setPrevFocusForImpact(focusId);
    setImpactSubject(null);
  }
  const impact = useImpactAnalysis(impactSubject);
  const impactResult = impact.data ?? null;
  const impactActive = impactSubject != null && impactResult != null;
```

Change `merged` to include the impact graph when present:

```tsx
  const merged = useMemo(
    () => (focusId ? mergeGraphs(impactResult ? [...results, impactResult] : results) : { nodes: [], edges: [], truncated: false }),
    [results, impactResult, focusId],
  );

  const tierByNodeId = useMemo(() => (impactResult ? buildTierMap(impactResult) : null), [impactResult]);
```

Replace the `dimmed` memo with the union of filter-dim + impact-dim:

```tsx
  const dimmed = useMemo(() => {
    const f = applyGraphFilters(merged, filters, focusId);
    if (!impactActive || !impactResult) return f;
    const impactIds = new Set(impactResult.nodes.map((n) => `${n.kind}:${n.id}`));
    const im = impactDim(merged, impactIds);
    return {
      dimmedNodeIds: new Set([...f.dimmedNodeIds, ...im.dimmedNodeIds]),
      dimmedEdgeIds: new Set([...f.dimmedEdgeIds, ...im.dimmedEdgeIds]),
    };
  }, [merged, filters, focusId, impactActive, impactResult]);
```

Pass `tierByNodeId` into `layoutGraph`:

```tsx
  const { nodes, edges } = useMemo(
    () =>
      focusId
        ? layoutGraph(merged, focusId, selected, { nodeIds: dimmed.dimmedNodeIds, edgeIds: dimmed.dimmedEdgeIds }, decorate, tierByNodeId ?? undefined)
        : { nodes: [] as Node<GraphNodeData>[], edges: [] as Edge[] },
    [merged, focusId, selected, dimmed, decorate, tierByNodeId],
  );
```

Render the banner (add a `<Panel position="top-right">` inside `<ReactFlow>`, beside the existing panels):

```tsx
                  {impactActive && impactResult && tierByNodeId && (
                    <Panel position="top-right">
                      <ImpactBanner
                        total={impactTotal(tierByNodeId)}
                        tiers={tierCounts(tierByNodeId)}
                        truncated={impactResult.truncated}
                        nodeCap={200}
                        onClose={() => setImpactSubject(null)}
                      />
                    </Panel>
                  )}
```

Wire the sidebar button:

```tsx
              <GraphExplorerSidebar
                selected={selectedRef}
                depthFromFocus={depthFromFocus}
                isExpanded={isExpanded}
                atCap={atCap}
                onToggleExpand={toggleExpand}
                onSetFocus={() => navigate(graphFocusPath(selectedRef.kind, selectedRef.id))}
                onClose={() => select(null)}
                onImpactAnalysis={() => setImpactSubject(selectedRef)}
              />
```

- [ ] **Step 3: Extend the page test**

Add a test that mocks `useImpactAnalysis` to return a two-tier `GraphResponse`, clicks the sidebar "Impact analysis" button, and asserts the banner text appears and non-impacted nodes carry the dimmed class. Mirror the existing `GraphExplorerPage.test.tsx` harness (mocked `useGraph`, `MemoryRouter` with `?focus=service:...`, QueryClientProvider). Example core:

```tsx
it("runs impact analysis: banner shows counts and Close clears it", async () => {
  // mock useGraph → focus + a neighbour; mock useImpactAnalysis → { nodes:[focus d0, n d1], ... }
  renderExplorer(); // helper
  await userEvent.click(screen.getByRole("button", { name: /impact analysis/i })); // via sidebar (select a node first)
  expect(await screen.findByText(/1 downstream/)).toBeInTheDocument();
  await userEvent.click(screen.getByRole("button", { name: /close analysis/i }));
  expect(screen.queryByText(/downstream/)).toBeNull();
});
```

> If the existing page test doesn't select a node, first drive `onNodeClick`/select to open the sidebar, or set `?focus` and click the rendered node. Keep to the harness already in the file.

- [ ] **Step 4: Run tests + typecheck + full FE build**

Run: `cd web && npm run test -- src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx`
Then: `cd web && npm run build`  *(tsc -b binding gate + vite build)*
Expected: PASS + clean build.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/api/impact.ts web/src/features/catalog/pages/GraphExplorerPage.tsx web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx
git commit -m "feat(web): wire impact analysis overlay into the graph explorer (E-04.F-02.S-06)"
```

---

## Task 10: Mutation gate (Stryker) on the changed Domain/Application logic

**Files:** none (verification). Target: `ImpactAnalysis.cs` (pure logic).

- [ ] **Step 1: Run Stryker scoped to the new pure helper**

Per repo `stryker-config.json`; use an absolute mutate path (memory: absolute paths in filters). Run detached if it exceeds the 10-min tool cap (Start-Process/nohup + poll).

Run (illustrative — match the repo's Stryker invocation):
`cmd //c "dotnet stryker --project Kartova.Catalog.Application --mutate \"**/ImpactAnalysis.cs\" --test-project src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj"`
Expected: mutation score ≥ 80% on `ImpactAnalysis`.

- [ ] **Step 2: Kill survivors**

For each surviving mutant, add a targeted unit test to `ImpactAnalysisTests` (e.g. tier off-by-one, cap boundary `>=` vs `>`, cycle guard). Re-run until ≥80% or survivors are documented as equivalent.

- [ ] **Step 3: Commit any added tests**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Tests/ImpactAnalysisTests.cs
git commit -m "test(catalog): kill ImpactAnalysis mutation survivors"
```

> The handler (`GetImpactAnalysisHandler`) is DB-bound and covered by the Task 2 real-seam tests; mutation focus is the pure `Compute`.

---

## Task 11: Gate-10 visual verification + docs

**Files:**
- Modify: `docs/product/CHECKLIST.md` (mark E-04.F-02.S-06 done)
- Create: `docs/superpowers/verification/2026-07-10-catalog-graph-impact-analysis/dod.md` (copy the ledger template) + `gate-findings.yaml` (copy the template) + evidence.

- [ ] **Step 1: Cold-start the stack and verify in-browser (ADR-0084, Playwright MCP)**

Cold-start the dev server (not HMR), authenticate (`admin@orga` / `dev_password_12`), navigate in-SPA to `/graph?focus=service:<id>` for a service with ≥2 tiers of dependents (seed via the UI or use a dev-seed service). Select a node → click **Impact analysis**. Verify: impacted nodes glow by tier, non-impacted dim, banner count == number of glowing nodes, **Close Analysis** restores the normal view. Screenshot to the verification folder. Check the browser console for errors.

- [ ] **Step 2: Fill the DoD ledger**

Copy `docs/superpowers/templates/dod-ledger-template.md` → `dod.md`; record each gate's status with command + output. Copy `docs/superpowers/templates/gate-findings-template.yaml` → `gate-findings.yaml`; log each gate's findings + real/delusion verdict. Note the mutation gate ran (Task 10) — blocking here (Application logic).

- [ ] **Step 3: Update the checklist**

Tick `E-04.F-02.S-06` in `docs/product/CHECKLIST.md` with a one-line summary (endpoint + overlay + tier glow + banner; explicit ∪ derived depends-on; service/app subjects; api-subject deferred FU-I1).

- [ ] **Step 4: Run the pre-push CI mirror**

Stop the dev server first (npm ci EPERM-vs-5173). Run: `cmd //c "bash scripts/ci-local.sh"` (or the relevant subsets `backend` / `frontend`). Confirm green (Release build + full suite + web image).

- [ ] **Step 5: Commit + open PR**

```bash
git add docs/product/CHECKLIST.md docs/superpowers/verification/2026-07-10-catalog-graph-impact-analysis/
git commit -m "docs(catalog): DoD ledger + checklist for graph impact analysis (E-04.F-02.S-06)"
```

Open the PR (base `master`); confirm CI green (gate 11, terminal).

---

## Definition of Done

All ten always-blocking gates + the conditional mutation gate (blocking here — Application logic) in **CLAUDE.md → Definition of Done** apply verbatim; the ledger (`docs/superpowers/verification/2026-07-10-catalog-graph-impact-analysis/dod.md`) is the queryable status and completion claims must cite it. Terminal re-verify: after gates 5–9 apply any fixes, re-run build + full suite on the final commit, then gate 10 (visual) and gate 11 (CI green on PR).

## Impact Analysis (codelens)

**New code + additive callers only — no existing C# symbol signature or behavior changes.** New symbols (`ImpactAnalysis`, `GetImpactAnalysisQuery`, `GetImpactAnalysisHandler`, `GetImpactAnalysisAsync`) have no existing blast radius. Consumed existing symbols, all **unchanged** (this slice only adds one new caller each):
- `DerivedEdgeLoader.LoadAsync(CatalogDbContext, CancellationToken)` — signature read directly (`DerivedEdgeLoader.cs:21`); existing callers `GetDerivedDependenciesHandler.cs:21`, `GraphTraversalHandler.cs:21` (read directly).
- `ICatalogEntityLookup.Find(EntityKind, Guid, CancellationToken) → Task<EntityLookupResult?>` — signature confirmed via codelens `analyze_method`; callers across `CatalogEndpointDelegates` + handlers (read directly).
- `DerivedProvenanceNames.LoadAsync` + `.Map` — signatures read directly (`DerivedProvenanceNames.cs:23,50`).
- Additive edits to `CatalogEndpointDelegates.cs` / `CatalogModule.cs` (new delegate + route + DI registration) — no change to existing delegates.

> **Codelens caveat (documented, grounded):** `find_references` returned `[]` for both `DerivedEdgeLoader.LoadAsync` (internal static) and `ICatalogEntityLookup.Find` (interface-dispatched) even after `rebuild_solution` — a codelens under-report for these symbol classes (same family as the `const` carve-out in CLAUDE.md). Caller enumeration above is therefore grep/read-grounded. Because the slice changes no signature, the blast radius is additive and no existing caller is affected. `find_implementations` not needed (no interface/base change). TypeScript changes have no codelens in this repo — FE blast radius (extended `GraphNodeData`, `layoutGraph` signature, `GraphExplorerSidebar` props) is enumerated in the File Structure and each maps to a task.

## Self-Review

- **Spec coverage:** endpoint (T3) ✓; explicit ∪ derived depends-on semantics (T1 Compute + T3 handler union) ✓; tier=hop (T1) ✓; node-cap 200 + truncated (T1/T3, banner T7) ✓; overlay-with-merge (T9 reuses mergeGraphs) ✓; dim non-impacted (T5 impactDim + T9 union) ✓; glow by tier (T6) ✓; banner + Close (T7/T9) ✓; service/app subjects, api→400, unknown→422 (T2/T3) ✓; no new permission (T3 route `CatalogRead`) ✓; real-seam happy+negative (T2) ✓; mutation blocking (T10) ✓; Playwright + ledger + checklist (T11) ✓; list-surface N/A (spec §7) — no list endpoint, nothing to add ✓.
- **Placeholder scan:** no TBD/TODO; every code step has complete code; test steps have real assertions.
- **Type consistency:** `ImpactAnalysis.Compute`/`Result`/`Node` names match across T1↔T3↔T10; `GetImpactAnalysisQuery(FocusKind, FocusId)` matches T3 handler + endpoint; `buildTierMap`/`impactDim`/`tierCounts`/`impactTotal` names match T5↔T9; `layoutGraph`'s new `tierByNodeId` param matches T6↔T9; `GraphResponse` reused verbatim (tier in `Depth`); `ImpactBanner` prop shape matches T7↔T9; sidebar `onImpactAnalysis` matches T8↔T9.
- **Ambiguity resolved:** api-focus status reconciled to **400** (aligns with sibling `GetApiSurfaceAsync`; refines the spec's 422 line — flagged in handoff); banner total == merged glowing set (both from the backend closure); focus excluded from downstream count; impact cleared on focus change (prev-key guard).
