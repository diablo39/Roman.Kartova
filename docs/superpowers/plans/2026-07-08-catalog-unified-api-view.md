# Unified API View Per Service (+ Derived Exposure) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a read-only endpoint + panel that shows, for a Service or Application, the APIs it provides (direct + derived-via-`instance-of`) and consumes — sync and async together, with style/version/spec metadata.

**Architecture:** New read-side vertical slice in the existing Catalog module. One bounded (non-cursor) endpoint `GET /api/v1/catalog/api-surface` dispatches to a direct-dispatch handler (ADR-0093) that computes derived exposure **on read** by walking existing relationship edges (`provides-api-for`, `instance-of`, `consumes-api-from`), batch-joins `Api` rows + `catalog_api_specs` for metadata, and shapes the result via a pure mapper. Frontend renders an `ApiSurfaceSection` panel on both detail pages. No migration, no new permission, no domain change.

**Tech Stack:** .NET 10 / ASP.NET Core minimal APIs, EF Core (Npgsql, RLS), MSTest v4 + NSubstitute + Testcontainers, React + TypeScript + react-aria-components (Untitled UI), openapi-fetch generated client.

**Spec:** `docs/superpowers/specs/2026-07-08-catalog-unified-api-view-design.md` (committed `37fda7e`). Branch `feat/catalog-unified-api-view` already created.

## Global Constraints

- Solution file `Kartova.slnx`; build with `TreatWarningsAsErrors=true` (0 warnings). Windows: wrap `dotnet` in `cmd //c` or PowerShell.
- Enums serialize **camelCase** over the wire (ADR-0109): `Rest→rest`, `Grpc→grpc`, `GraphQL→graphQL`, `AsyncApi→asyncApi`, `Direct→direct`, `Derived→derived`.
- All contracts/DTOs (`*Response`, `*Item`, enums used only as DTOs) carry `[ExcludeFromCodeCoverage]`; the bounded flat result carries `[BoundedListResult]` (ContractsCoverageRules arch test enforces both).
- Tenant-scoped DB access only inside `ITenantScope` (already wired by `TenantScopeBeginMiddleware` for this route group). Never `AddDbContext` — reads go through the injected `CatalogDbContext`.
- Reads gated by `KartovaPermissions.CatalogRead` — **no new permission, no 5-sync**.
- Endpoint-URL validation rule and cursor rules do not apply (this is a bounded flat result, `[BoundedListResult]`, ADR-0095 carve-out).
- `.cs` files must stay LF (repo `.gitattributes eol=lf`).
- **Mutation gate (6) is owner-waived for this slice** — record as a waiver (not green) in the DoD ledger; do not run Stryker.
- **Codegen:** the frontend client is generated from the live API. New endpoint/DTOs appear only after the API image is rebuilt and `predev`/`prebuild` regenerate `web/openapi-snapshot.json` + `web/src/generated/*` — see Task 4.

## Impact Analysis (codelens/LSP)

**N/A for signature changes — new read-side code only.** No existing C# symbol's signature or behavior is modified. New symbols added: `ProblemTypes.InvalidEntity` (additive const), `ApiSurfaceOrigin`/`ApiSurfaceItem`/`ApiSurfaceResponse` (contracts), `GetApiSurfaceQuery`/`ApiSurfaceMapper` (application), `GetApiSurfaceHandler` (infra), `GetApiSurfaceAsync` delegate + one `MapGet` + one `AddScoped` (module).

Symbols **read** (unchanged), confirmed at plan time:
- `RelationshipType.{ProvidesApiFor, ConsumesApiFrom, InstanceOf}`, `EntityKind.{Application, Service, Api}` — enum-value reads (codelens under-reports enum refs → grepped; only definitions + `RelationshipTypeRules` reference them; adding a read site is additive).
- `CatalogDbContext.{Relationships, Apis, ApiSpecs}`, `EfApiConfiguration.IdFieldName`, `ApiStyle`, `ApiId`, `ICatalogEntityLookup.Find`, `EntityRef` ctor — consumed exactly as `ListApisHandler`/`ListRelationshipsForEntityHandler`/`CatalogEntityLookup` already use them (verified by reading those files). No caller of any of these changes.

No `find_callers`/`analyze_change_impact` obligation triggers because nothing existing changes shape. If execution reveals a needed signature change, stop and revise the spec first.

---

### Task 1: Contracts + `ProblemTypes.InvalidEntity` + pure surface mapper (with unit tests)

Delivers the wire types, the additive problem-type, and the pure `ApiSurfaceMapper` that does dedupe / direct-wins / origin assembly — fully unit-testable without EF.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/ApiSurfaceOrigin.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/ApiSurfaceItem.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Contracts/ApiSurfaceResponse.cs`
- Modify: `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs` (add one const near `InvalidSourceEntity`, line ~71)
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/GetApiSurfaceQuery.cs`
- Create: `src/Modules/Catalog/Kartova.Catalog.Application/ApiSurfaceMapper.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/ApiSurfaceMapperTests.cs`

**Interfaces:**
- Produces (consumed by Task 2 & 3):
  - `enum ApiSurfaceOrigin { Direct, Derived }`
  - `record ApiSurfaceItem(Guid ApiId, string DisplayName, ApiStyle Style, string Version, bool HasSpec, ApiSurfaceOrigin Origin, Guid? ViaApplicationId, string? ViaApplicationDisplayName)`
  - `record ApiSurfaceResponse(IReadOnlyList<ApiSurfaceItem> Provides, IReadOnlyList<ApiSurfaceItem> Consumes)`
  - `record GetApiSurfaceQuery(EntityKind Kind, Guid EntityId)`
  - `ApiSurfaceMapper.Build(provides, consumesApiIds, apis, appNames) -> ApiSurfaceResponse`, and the input records `ApiSurfaceMapper.ProvidesEdge(Guid ApiId, ApiSurfaceOrigin Origin, Guid? ViaApplicationId)` and `ApiSurfaceMapper.ApiMeta(string DisplayName, ApiStyle Style, string Version, bool HasSpec)`.
  - `ProblemTypes.InvalidEntity` (string, `= Base + "invalid-entity"`).

- [ ] **Step 1: Add the `InvalidEntity` problem type**

In `src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs`, directly below the `InvalidSourceEntity` line:

```csharp
    public const string InvalidEntity            = Base + "invalid-entity";           // 422
```

- [ ] **Step 2: Create the contracts**

`ApiSurfaceOrigin.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;

namespace Kartova.Catalog.Contracts;

/// <summary>How an API appears on a component's surface: a direct edge, or derived via instance-of (ADR-0111).</summary>
[ExcludeFromCodeCoverage]
public enum ApiSurfaceOrigin
{
    Direct,
    Derived,
}
```

`ApiSurfaceItem.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Contracts;

/// <summary>One API on a component's surface, with the metadata the panel renders.
/// For Consumes rows <see cref="Origin"/> is always <see cref="ApiSurfaceOrigin.Direct"/> and the
/// Via* fields are null. Via* are populated only when <see cref="Origin"/> is
/// <see cref="ApiSurfaceOrigin.Derived"/> (exposed through the named application).</summary>
[ExcludeFromCodeCoverage]
public sealed record ApiSurfaceItem(
    Guid ApiId,
    string DisplayName,
    ApiStyle Style,
    string Version,
    bool HasSpec,
    ApiSurfaceOrigin Origin,
    Guid? ViaApplicationId,
    string? ViaApplicationDisplayName);
```

`ApiSurfaceResponse.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using Kartova.SharedKernel.Pagination;

namespace Kartova.Catalog.Contracts;

/// <summary>A component's API surface. Bounded (a single component's direct+derived APIs; small N)
/// so it returns flat arrays, not <c>CursorPage&lt;T&gt;</c> — ADR-0095 bounded-list carve-out.</summary>
[BoundedListResult]
[ExcludeFromCodeCoverage]
public sealed record ApiSurfaceResponse(
    IReadOnlyList<ApiSurfaceItem> Provides,
    IReadOnlyList<ApiSurfaceItem> Consumes);
```

> Confirm `BoundedListResultAttribute` namespace by opening `src/Kartova.SharedKernel/Pagination/BoundedListResultAttribute.cs`; adjust the `using` if it differs from `Kartova.SharedKernel.Pagination`.

- [ ] **Step 3: Create the query record**

`GetApiSurfaceQuery.cs`:

```csharp
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Read an entity's API surface (provides direct+derived, consumes direct). Kind is Service or Application.</summary>
public sealed record GetApiSurfaceQuery(EntityKind Kind, Guid EntityId);
```

- [ ] **Step 4: Write the failing mapper tests**

`ApiSurfaceMapperTests.cs`:

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Tests;

[TestClass]
public sealed class ApiSurfaceMapperTests
{
    private static readonly Guid Api1 = Guid.NewGuid();
    private static readonly Guid Api2 = Guid.NewGuid();
    private static readonly Guid App1 = Guid.NewGuid();

    private static Dictionary<Guid, ApiSurfaceMapper.ApiMeta> Meta(params Guid[] ids) =>
        ids.ToDictionary(id => id, id => new ApiSurfaceMapper.ApiMeta($"api-{id:N}", ApiStyle.Rest, "v1", false));

    [TestMethod]
    public void Direct_provides_maps_to_direct_origin()
    {
        var result = ApiSurfaceMapper.Build(
            provides: [new ApiSurfaceMapper.ProvidesEdge(Api1, ApiSurfaceOrigin.Direct, null)],
            consumesApiIds: [],
            apis: Meta(Api1),
            appNames: new Dictionary<Guid, string>());

        Assert.AreEqual(1, result.Provides.Count);
        Assert.AreEqual(ApiSurfaceOrigin.Direct, result.Provides[0].Origin);
        Assert.IsNull(result.Provides[0].ViaApplicationId);
    }

    [TestMethod]
    public void Derived_provides_carries_via_application()
    {
        var result = ApiSurfaceMapper.Build(
            provides: [new ApiSurfaceMapper.ProvidesEdge(Api1, ApiSurfaceOrigin.Derived, App1)],
            consumesApiIds: [],
            apis: Meta(Api1),
            appNames: new Dictionary<Guid, string> { [App1] = "Billing" });

        var item = result.Provides.Single();
        Assert.AreEqual(ApiSurfaceOrigin.Derived, item.Origin);
        Assert.AreEqual(App1, item.ViaApplicationId);
        Assert.AreEqual("Billing", item.ViaApplicationDisplayName);
    }

    [TestMethod]
    public void Direct_wins_when_same_api_is_both_direct_and_derived()
    {
        var result = ApiSurfaceMapper.Build(
            provides:
            [
                new ApiSurfaceMapper.ProvidesEdge(Api1, ApiSurfaceOrigin.Derived, App1),
                new ApiSurfaceMapper.ProvidesEdge(Api1, ApiSurfaceOrigin.Direct, null),
            ],
            consumesApiIds: [],
            apis: Meta(Api1),
            appNames: new Dictionary<Guid, string> { [App1] = "Billing" });

        var item = result.Provides.Single();   // deduped to one row
        Assert.AreEqual(ApiSurfaceOrigin.Direct, item.Origin);
        Assert.IsNull(item.ViaApplicationId);
    }

    [TestMethod]
    public void Consumes_ids_map_to_direct_items()
    {
        var result = ApiSurfaceMapper.Build(
            provides: [],
            consumesApiIds: [Api2],
            apis: Meta(Api2),
            appNames: new Dictionary<Guid, string>());

        var item = result.Consumes.Single();
        Assert.AreEqual(Api2, item.ApiId);
        Assert.AreEqual(ApiSurfaceOrigin.Direct, item.Origin);
        Assert.IsNull(item.ViaApplicationId);
    }

    [TestMethod]
    public void Empty_inputs_produce_empty_lists()
    {
        var result = ApiSurfaceMapper.Build([], [], Meta(), new Dictionary<Guid, string>());
        Assert.AreEqual(0, result.Provides.Count);
        Assert.AreEqual(0, result.Consumes.Count);
    }

    [TestMethod]
    public void Metadata_is_projected_onto_items()
    {
        var apis = new Dictionary<Guid, ApiSurfaceMapper.ApiMeta>
        {
            [Api1] = new("Orders API", ApiStyle.AsyncApi, "2.0.0", true),
        };
        var result = ApiSurfaceMapper.Build(
            [new ApiSurfaceMapper.ProvidesEdge(Api1, ApiSurfaceOrigin.Direct, null)], [], apis,
            new Dictionary<Guid, string>());

        var item = result.Provides.Single();
        Assert.AreEqual("Orders API", item.DisplayName);
        Assert.AreEqual(ApiStyle.AsyncApi, item.Style);
        Assert.AreEqual("2.0.0", item.Version);
        Assert.IsTrue(item.HasSpec);
    }
}
```

- [ ] **Step 5: Run the tests — verify they fail**

Run: `cmd //c dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter ApiSurfaceMapperTests`
Expected: FAIL — `ApiSurfaceMapper` does not exist (compile error).

- [ ] **Step 6: Implement `ApiSurfaceMapper`**

`ApiSurfaceMapper.cs`:

```csharp
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;

namespace Kartova.Catalog.Application;

/// <summary>Pure shaping of an entity's API surface (no EF). The handler prepares the four inputs
/// from RLS-scoped queries; this class does dedupe (direct-wins), origin assembly, and metadata join.</summary>
public static class ApiSurfaceMapper
{
    /// <summary>A provides/exposes edge target before metadata join. <paramref name="ViaApplicationId"/>
    /// is set only for derived edges (the application the API is exposed through).</summary>
    public sealed record ProvidesEdge(Guid ApiId, ApiSurfaceOrigin Origin, Guid? ViaApplicationId);

    /// <summary>Metadata for one API, batch-loaded from <c>catalog_apis</c> + <c>catalog_api_specs</c>.</summary>
    public sealed record ApiMeta(string DisplayName, ApiStyle Style, string Version, bool HasSpec);

    public static ApiSurfaceResponse Build(
        IReadOnlyList<ProvidesEdge> provides,
        IReadOnlyList<Guid> consumesApiIds,
        IReadOnlyDictionary<Guid, ApiMeta> apis,
        IReadOnlyDictionary<Guid, string> appNames)
    {
        // Provides: group by API id, DIRECT wins over DERIVED when an API appears both ways.
        var providesItems = provides
            .GroupBy(p => p.ApiId)
            .Select(g =>
            {
                var chosen = g.FirstOrDefault(p => p.Origin == ApiSurfaceOrigin.Direct) ?? g.First();
                return chosen;
            })
            .Where(p => apis.ContainsKey(p.ApiId))   // defensive: skip if metadata missing
            .Select(p =>
            {
                var meta = apis[p.ApiId];
                var derived = p.Origin == ApiSurfaceOrigin.Derived;
                return new ApiSurfaceItem(
                    p.ApiId, meta.DisplayName, meta.Style, meta.Version, meta.HasSpec, p.Origin,
                    derived ? p.ViaApplicationId : null,
                    derived && p.ViaApplicationId is { } via && appNames.TryGetValue(via, out var n) ? n : null);
            })
            .ToList();

        var consumesItems = consumesApiIds
            .Distinct()
            .Where(id => apis.ContainsKey(id))
            .Select(id =>
            {
                var meta = apis[id];
                return new ApiSurfaceItem(
                    id, meta.DisplayName, meta.Style, meta.Version, meta.HasSpec,
                    ApiSurfaceOrigin.Direct, null, null);
            })
            .ToList();

        return new ApiSurfaceResponse(providesItems, consumesItems);
    }
}
```

- [ ] **Step 7: Run the tests — verify they pass**

Run: `cmd //c dotnet test src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj --filter ApiSurfaceMapperTests`
Expected: PASS (6 tests).

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Contracts/ApiSurface*.cs \
        src/Modules/Catalog/Kartova.Catalog.Application/GetApiSurfaceQuery.cs \
        src/Modules/Catalog/Kartova.Catalog.Application/ApiSurfaceMapper.cs \
        src/Modules/Catalog/Kartova.Catalog.Tests/ApiSurfaceMapperTests.cs \
        src/Kartova.SharedKernel.AspNetCore/ProblemTypes.cs
git commit -m "feat(catalog): api-surface contracts + pure mapper (E-02.F-03.S-03)"
```

---

### Task 2: Read handler + endpoint delegate + module wiring + real-seam integration tests

Delivers the working `GET /api/v1/catalog/api-surface` endpoint end-to-end, tested against real Postgres/RLS + real JWT.

**Files:**
- Create: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetApiSurfaceHandler.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` (add `GetApiSurfaceAsync`)
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (map route ~after line 162; register handler ~after line 277)
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetApiSurfaceTests.cs`

**Interfaces:**
- Consumes (from Task 1): `GetApiSurfaceQuery`, `ApiSurfaceMapper` (+ `ProvidesEdge`/`ApiMeta`), `ApiSurfaceResponse`, `ProblemTypes.InvalidEntity`.
- Produces: `GetApiSurfaceHandler.Handle(GetApiSurfaceQuery q, CatalogDbContext db, ICatalogEntityLookup lookup, CancellationToken ct) -> Task<ApiSurfaceResponse>`; delegate `GetApiSurfaceAsync(...)`; route `WithName("GetApiSurface")`.

- [ ] **Step 1: Write the failing integration tests**

Mirror the fixture usage of `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs` (same `KartovaApiFixtureBase`, seed helpers, JWT-issuing HTTP client). `GetApiSurfaceTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Kartova.Catalog.Contracts;

namespace Kartova.Catalog.IntegrationTests;

[TestClass]
public sealed class GetApiSurfaceTests : CatalogIntegrationTestBase   // match the base class used by CreateRelationshipTests
{
    [TestMethod]
    public async Task Service_surface_includes_direct_derived_and_consumes()
    {
        // Arrange: seed team; App (provides ApiApp1, ApiApp2); Service instance-of App (⇒ derived exposes);
        // Service directly provides ApiSvc; Service consumes ApiCons. (Use the same seed/create helpers as
        // CreateRelationshipTests: register apps/services/apis, then POST /relationships for each edge.)
        var ctx = await SeedServiceSurfaceScenarioAsync();

        // Act
        var resp = await ctx.Client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=service&entityId={ctx.ServiceId}");

        // Assert
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ApiSurfaceResponse>();
        Assert.IsNotNull(body);

        // Provides = 1 direct (ApiSvc) + 2 derived (ApiApp1, ApiApp2)
        Assert.AreEqual(3, body!.Provides.Count);
        var direct = body.Provides.Single(i => i.ApiId == ctx.ApiSvcId);
        Assert.AreEqual(ApiSurfaceOrigin.Direct, direct.Origin);
        var derived = body.Provides.Single(i => i.ApiId == ctx.ApiApp1Id);
        Assert.AreEqual(ApiSurfaceOrigin.Derived, derived.Origin);
        Assert.AreEqual(ctx.AppId, derived.ViaApplicationId);
        Assert.AreEqual(ctx.AppDisplayName, derived.ViaApplicationDisplayName);

        // metadata joined
        Assert.AreEqual("v1", direct.Version);
        Assert.IsFalse(direct.HasSpec);

        // Consumes = 1 direct
        Assert.AreEqual(1, body.Consumes.Count);
        Assert.AreEqual(ctx.ApiConsId, body.Consumes.Single().ApiId);
        Assert.AreEqual(ApiSurfaceOrigin.Direct, body.Consumes.Single().Origin);
    }

    [TestMethod]
    public async Task Direct_provides_wins_over_derived_for_same_api()
    {
        // Service directly provides ApiX AND is instance-of an app that also provides ApiX.
        var ctx = await SeedDirectWinsScenarioAsync();

        var body = await (await ctx.Client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=service&entityId={ctx.ServiceId}"))
            .Content.ReadFromJsonAsync<ApiSurfaceResponse>();

        var rows = body!.Provides.Where(i => i.ApiId == ctx.ApiXId).ToList();
        Assert.AreEqual(1, rows.Count);                       // deduped
        Assert.AreEqual(ApiSurfaceOrigin.Direct, rows[0].Origin);
    }

    [TestMethod]
    public async Task Application_surface_has_no_derived_rows()
    {
        var ctx = await SeedApplicationSurfaceScenarioAsync();  // app provides + consumes directly

        var body = await (await ctx.Client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=application&entityId={ctx.AppId}"))
            .Content.ReadFromJsonAsync<ApiSurfaceResponse>();

        Assert.IsTrue(body!.Provides.All(i => i.Origin == ApiSurfaceOrigin.Direct));
        Assert.IsFalse(body.Provides.Any(i => i.ViaApplicationId is not null));
    }

    [TestMethod]
    public async Task Other_tenant_edges_and_apis_do_not_appear()
    {
        // Seed the scenario in tenant A; issue the request as a user in tenant B against A's service id.
        var ctx = await SeedServiceSurfaceScenarioAsync();
        var otherClient = await ClientForOtherTenantAsync();

        var resp = await otherClient.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=service&entityId={ctx.ServiceId}");

        // The service isn't visible under B's RLS → focus-entity lookup misses → 422.
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task Unknown_entity_returns_422()
    {
        var client = await ClientForSeededTenantAsync();
        var resp = await client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=service&entityId={Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task EntityKind_api_returns_400()
    {
        var client = await ClientForSeededTenantAsync();
        var resp = await client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=api&entityId={Guid.NewGuid()}");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task Missing_entityId_returns_400()
    {
        var client = await ClientForSeededTenantAsync();
        var resp = await client.GetAsync(
            $"/api/v1/catalog/api-surface?entityKind=service&entityId={Guid.Empty}");
        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
```

> The `Seed*ScenarioAsync` / `ClientФor*` helpers are stubs to write against the fixture that `CreateRelationshipTests` already uses. Open that file first and reuse its exact base class name, HTTP-client factory (per-tenant JWT), and entity/relationship seed helpers — do **not** invent a new fixture. Each scenario = register apps/services/apis via their POST endpoints, then POST `/api/v1/catalog/relationships` for `instanceOf` / `providesApiFor` / `consumesApiFrom` edges.

- [ ] **Step 2: Run the tests — verify they fail**

Run: `cmd //c dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter GetApiSurfaceTests`
Expected: FAIL — 404 (route not mapped) / compile error (`GetApiSurfaceHandler` missing).

- [ ] **Step 3: Implement `GetApiSurfaceHandler`**

`GetApiSurfaceHandler.cs`:

```csharp
using Kartova.Catalog.Application;
using Kartova.Catalog.Contracts;
using Kartova.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Kartova.Catalog.Infrastructure;

/// <summary>Computes a component's API surface ON READ (ADR-0111 §Decision 3): direct provides +
/// (Service only) derived exposes via instance-of, plus direct consumes. RLS scopes every query, so
/// cross-tenant edges/APIs never appear. Shaping (dedupe/direct-wins/metadata join) is delegated to
/// the pure <see cref="ApiSurfaceMapper"/>.</summary>
public sealed class GetApiSurfaceHandler
{
    public async Task<ApiSurfaceResponse> Handle(
        GetApiSurfaceQuery q, CatalogDbContext db, ICatalogEntityLookup lookup, CancellationToken ct)
    {
        // 1. Direct provides (component -> Api).
        var directProvides = await db.Relationships
            .Where(r => r.Source.Kind == q.Kind && r.Source.Id == q.EntityId
                        && r.Type == RelationshipType.ProvidesApiFor
                        && r.Target.Kind == EntityKind.Api)
            .Select(r => r.Target.Id)
            .ToListAsync(ct);

        var provides = directProvides
            .Select(id => new ApiSurfaceMapper.ProvidesEdge(id, ApiSurfaceOrigin.Direct, null))
            .ToList();

        // 2. Derived exposes — Service only: instance-of App(s), then those apps' provided APIs.
        if (q.Kind == EntityKind.Service)
        {
            var instanceAppIds = await db.Relationships
                .Where(r => r.Source.Kind == EntityKind.Service && r.Source.Id == q.EntityId
                            && r.Type == RelationshipType.InstanceOf
                            && r.Target.Kind == EntityKind.Application)
                .Select(r => r.Target.Id)
                .ToListAsync(ct);

            if (instanceAppIds.Count > 0)
            {
                var derived = await db.Relationships
                    .Where(r => r.Type == RelationshipType.ProvidesApiFor
                                && r.Source.Kind == EntityKind.Application
                                && instanceAppIds.Contains(r.Source.Id)
                                && r.Target.Kind == EntityKind.Api)
                    .Select(r => new { ApiId = r.Target.Id, ViaAppId = r.Source.Id })
                    .ToListAsync(ct);

                provides.AddRange(derived.Select(d =>
                    new ApiSurfaceMapper.ProvidesEdge(d.ApiId, ApiSurfaceOrigin.Derived, d.ViaAppId)));
            }
        }

        // 3. Direct consumes.
        var consumesApiIds = await db.Relationships
            .Where(r => r.Source.Kind == q.Kind && r.Source.Id == q.EntityId
                        && r.Type == RelationshipType.ConsumesApiFrom
                        && r.Target.Kind == EntityKind.Api)
            .Select(r => r.Target.Id)
            .ToListAsync(ct);

        // 4. Batch-load API metadata for every referenced id.
        var apiGuids = provides.Select(p => p.ApiId).Concat(consumesApiIds).Distinct().ToList();

        var apiRows = await db.Apis
            .Where(a => apiGuids.Contains(EF.Property<Guid>(a, EfApiConfiguration.IdFieldName)))
            .Select(a => new
            {
                Id = EF.Property<Guid>(a, EfApiConfiguration.IdFieldName),
                a.DisplayName, a.Style, a.Version,
            })
            .ToListAsync(ct);

        // has-spec: presence of a row in catalog_api_specs (1:1). Mirrors ListApisHandler.
        var apiIdKeys = apiGuids.Select(g => new ApiId(g)).ToList();
        var idsWithSpec = (await db.ApiSpecs
                .Where(s => apiIdKeys.Contains(s.ApiId))
                .Select(s => s.ApiId)
                .ToListAsync(ct))
            .Select(id => id.Value)
            .ToHashSet();

        var apis = apiRows.ToDictionary(
            a => a.Id,
            a => new ApiSurfaceMapper.ApiMeta(a.DisplayName, a.Style, a.Version, idsWithSpec.Contains(a.Id)));

        // 5. `via` application display names (derived rows only).
        var viaAppIds = provides
            .Where(p => p.Origin == ApiSurfaceOrigin.Derived && p.ViaApplicationId is not null)
            .Select(p => p.ViaApplicationId!.Value)
            .Distinct()
            .ToList();

        var appNames = new Dictionary<Guid, string>();
        foreach (var appId in viaAppIds)
        {
            var found = await lookup.Find(EntityKind.Application, appId, ct);
            if (found is not null) appNames[appId] = found.DisplayName;
        }

        return ApiSurfaceMapper.Build(provides, consumesApiIds, apis, appNames);
    }
}
```

> `new ApiId(g)` mirrors the `RelationshipId`/`ServiceId` value-object ctor; confirm `ApiId`'s public ctor takes a single `Guid` (open `Kartova.Catalog.Domain/ApiId.cs`). `ApiSpec.ApiId` is an `ApiId` (verified in `ListApisHandler`), so the `Contains(s.ApiId)` value-equality pattern is identical to that handler.

- [ ] **Step 4: Add the endpoint delegate**

In `CatalogEndpointDelegates.cs`, add after `ListRelationshipsAsync` (the `using` aliases for `EntityKind`, `RelationshipType`, etc. are already present at the top of the file):

```csharp
    /// <summary>
    /// GET /api-surface?entityKind=&amp;entityId= — a Service's or Application's unified API surface
    /// (provides direct+derived, consumes direct). Bounded flat result (ADR-0095 carve-out), not a
    /// cursor list. Claim gate: catalog.read. `entityKind=api` is rejected 400 (an API has no surface);
    /// an unknown/cross-tenant focus entity is 422 invalid-entity.
    /// </summary>
    internal static async Task<IResult> GetApiSurfaceAsync(
        [FromQuery] string entityKind,
        [FromQuery] Guid entityId,
        GetApiSurfaceHandler handler,
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

        // Focus entity must exist in this tenant (RLS ⇒ cross-tenant id resolves as absent → 422).
        if (await lookup.Find(kind, entityId, ct) is null)
        {
            return Results.Problem(
                type: ProblemTypes.InvalidEntity,
                title: "Invalid entity",
                detail: "The entity does not exist in this tenant.",
                statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        var surface = await handler.Handle(new GetApiSurfaceQuery(kind, entityId), db, lookup, ct);
        return Results.Ok(surface);
    }
```

- [ ] **Step 5: Map the route + register the handler**

In `CatalogModule.cs`, after the `/graph` mapping (~line 162 block), add:

```csharp
        tenant.MapGet("/api-surface", CatalogEndpointDelegates.GetApiSurfaceAsync)
              .RequireAuthorization(KartovaPermissions.CatalogRead)
              .WithName("GetApiSurface")
              .ProducesProblem(StatusCodes.Status400BadRequest)
              .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
```

And in the handler-registration block (after line 277, near the other `AddScoped` handlers):

```csharp
        services.AddScoped<GetApiSurfaceHandler>();
```

- [ ] **Step 6: Run the integration tests — verify they pass**

Run: `cmd //c dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests/Kartova.Catalog.IntegrationTests.csproj --filter GetApiSurfaceTests`
Expected: PASS (7 tests). (Requires Docker for Testcontainers Postgres.)

- [ ] **Step 7: Full build (warnings-as-errors)**

Run: `cmd //c dotnet build Kartova.slnx -c Debug`
Expected: 0 warnings, 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetApiSurfaceHandler.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs \
        src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs \
        src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetApiSurfaceTests.cs
git commit -m "feat(catalog): api-surface read endpoint + on-read derived exposure (E-02.F-03.S-03)"
```

---

### Task 3: Regenerate the frontend client (expose the new endpoint/DTOs)

The generated client only learns `GetApiSurface` + `ApiSurfaceResponse`/`ApiSurfaceItem`/`ApiSurfaceOrigin` after the API image is rebuilt and the OpenAPI snapshot regenerated.

**Files:**
- Modify (generated): `web/openapi-snapshot.json`, `web/src/generated/*`

- [ ] **Step 1: Rebuild the API image so the live spec includes the new endpoint**

Run: `cmd //c docker compose build api` (or the compose service name the repo uses for the API — confirm in `docker-compose.yml`).
Expected: image builds green.

- [ ] **Step 2: Regenerate the OpenAPI snapshot + client**

Start the API (or the dev stack) so `web` `predev`/`prebuild` can hit it, then:

Run: `cd web && npm run build` (predev/prebuild regenerate `openapi-snapshot.json` + `src/generated/*` from the live API — see the "OpenAPI snapshot codegen" convention).
Expected: `web/src/generated/openapi.d.ts` now contains `ApiSurfaceResponse`, `ApiSurfaceItem`, `ApiSurfaceOrigin`, and a `GetApiSurface` operation; `tsc -b` passes.

- [ ] **Step 3: Verify the generated types exist**

Run: `cd web && npx tsc -b`
Expected: PASS. Confirm `components["schemas"]["ApiSurfaceResponse"]` resolves (grep the generated file).

- [ ] **Step 4: Commit the regenerated artifacts**

```bash
git add web/openapi-snapshot.json web/src/generated
git commit -m "chore(web): regenerate client for api-surface endpoint (E-02.F-03.S-03)"
```

---

### Task 4: Frontend data hook + `ApiSurfaceSection` component (with tests)

**Files:**
- Create: `web/src/features/catalog/api/apiSurface.ts`
- Create: `web/src/features/catalog/components/ApiSurfaceSection.tsx`
- Test: `web/src/features/catalog/components/__tests__/ApiSurfaceSection.test.tsx`

**Interfaces:**
- Consumes (from Task 3): `components["schemas"]["ApiSurfaceResponse"]`, `["ApiSurfaceItem"]`, operation `GetApiSurface`.
- Produces: `useApiSurface(entityKind: "service" | "application", entityId: string)` → `{ data, isLoading, isError }`; `<ApiSurfaceSection entityKind entityId />`.

- [ ] **Step 1: Write the data hook**

`apiSurface.ts` (mirror the openapi-fetch pattern in `web/src/features/catalog/api/apis.ts`):

```ts
import { useQuery } from "@tanstack/react-query";
import { apiClient } from "./client";
import { unwrapData } from "@/shared/api/openapi-fetch-helpers";
import type { components } from "@/generated/openapi";

export type ApiSurfaceResponse = components["schemas"]["ApiSurfaceResponse"];
export type ApiSurfaceItem = components["schemas"]["ApiSurfaceItem"];

export function useApiSurface(entityKind: "service" | "application", entityId: string) {
  return useQuery({
    queryKey: ["catalog", "api-surface", entityKind, entityId],
    enabled: entityId !== "",
    queryFn: async (): Promise<ApiSurfaceResponse> => {
      const { data, error } = await apiClient.GET("/api/v1/catalog/api-surface", {
        params: { query: { entityKind, entityId } },
      });
      if (error) throw error;
      return unwrapData(data);
    },
  });
}
```

> If `unwrapData` is envelope-specific (used for cursor lists), and this endpoint returns the bare object, drop `unwrapData` and `return data!;`. Confirm by how the generated response is typed (bare `ApiSurfaceResponse` vs wrapped).

- [ ] **Step 2: Write the failing component test**

`__tests__/ApiSurfaceSection.test.tsx` (mirror the render/query-client setup in an existing catalog component test, e.g. `RelationshipsSection` tests or `ServiceDetailPage` tests):

```tsx
import { render, screen } from "@testing-library/react";
import { describe, it, expect, vi } from "vitest";
import { ApiSurfaceSection } from "@/features/catalog/components/ApiSurfaceSection";
import * as api from "@/features/catalog/api/apiSurface";
// ...import the shared test render wrapper (QueryClientProvider + router) used by sibling tests

function mockSurface(data: api.ApiSurfaceResponse) {
  vi.spyOn(api, "useApiSurface").mockReturnValue({
    data, isLoading: false, isError: false,
  } as unknown as ReturnType<typeof api.useApiSurface>);
}

describe("ApiSurfaceSection", () => {
  it("renders provides and consumes tables with a derived via-link", () => {
    mockSurface({
      provides: [
        { apiId: "a1", displayName: "Orders API", style: "rest", version: "v1", hasSpec: true,
          origin: "derived", viaApplicationId: "app1", viaApplicationDisplayName: "Billing" },
      ],
      consumes: [
        { apiId: "a2", displayName: "Events API", style: "asyncApi", version: "2.0", hasSpec: false,
          origin: "direct", viaApplicationId: null, viaApplicationDisplayName: null },
      ],
    });

    renderWithProviders(<ApiSurfaceSection entityKind="service" entityId="svc1" />);

    expect(screen.getByText("Orders API")).toBeInTheDocument();
    expect(screen.getByText("Events API")).toBeInTheDocument();
    expect(screen.getByText(/via Billing/i)).toBeInTheDocument();
    // react-aria Table requires a rowheader column
    expect(screen.getAllByRole("rowheader").length).toBeGreaterThan(0);
  });

  it("shows empty copy when a list is empty", () => {
    mockSurface({ provides: [], consumes: [] });
    renderWithProviders(<ApiSurfaceSection entityKind="application" entityId="app1" />);
    expect(screen.getByText(/no apis provided/i)).toBeInTheDocument();
    expect(screen.getByText(/no apis consumed/i)).toBeInTheDocument();
  });
});
```

- [ ] **Step 3: Run the test — verify it fails**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/ApiSurfaceSection.test.tsx`
Expected: FAIL — `ApiSurfaceSection` not found.

- [ ] **Step 4: Implement `ApiSurfaceSection`**

`ApiSurfaceSection.tsx` (mirror `<Table>` usage in `ServiceDetailPage`/`RelationshipsSection`; one `isRowHeader` column per table):

```tsx
import { Link } from "react-router-dom";
import { Badge } from "@/components/base/badges/badges";
import { Table } from "@/components/application/table/table";
import { Skeleton } from "@/components/base/skeleton/skeleton";
import { useApiSurface, type ApiSurfaceItem } from "@/features/catalog/api/apiSurface";

// Wire values are camelCase enum names (ADR-0109). Sync styles sort before async.
const STYLE_LABEL: Record<string, string> = {
  rest: "REST",
  grpc: "gRPC",
  graphQL: "GraphQL",
  asyncApi: "AsyncAPI",
};
const STYLE_ORDER: Record<string, number> = { rest: 0, grpc: 1, graphQL: 2, asyncApi: 3 };

function sortItems(items: ApiSurfaceItem[]): ApiSurfaceItem[] {
  return [...items].sort(
    (a, b) =>
      (STYLE_ORDER[a.style] ?? 99) - (STYLE_ORDER[b.style] ?? 99) ||
      a.displayName.localeCompare(b.displayName),
  );
}

interface Props {
  entityKind: "service" | "application";
  entityId: string;
}

export function ApiSurfaceSection({ entityKind, entityId }: Props) {
  const query = useApiSurface(entityKind, entityId);

  if (query.isLoading) return <Skeleton className="h-40 w-full" />;
  if (query.isError || !query.data) {
    return <p className="text-sm text-error-primary">Couldn&apos;t load APIs.</p>;
  }

  const { provides, consumes } = query.data;

  return (
    <section className="space-y-6" aria-label="APIs">
      <ApiTable
        title="Provides"
        emptyCopy="No APIs provided."
        items={sortItems(provides)}
        showOrigin
      />
      <ApiTable
        title="Consumes"
        emptyCopy="No APIs consumed."
        items={sortItems(consumes)}
        showOrigin={false}
      />
    </section>
  );
}

function ApiTable({
  title,
  emptyCopy,
  items,
  showOrigin,
}: {
  title: string;
  emptyCopy: string;
  items: ApiSurfaceItem[];
  showOrigin: boolean;
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
              <Table.Head id="name" isRowHeader>
                Name
              </Table.Head>
              <Table.Head id="style">Style</Table.Head>
              <Table.Head id="version">Version</Table.Head>
              <Table.Head id="spec">Spec</Table.Head>
              {showOrigin && <Table.Head id="origin">Origin</Table.Head>}
            </Table.Header>
            <Table.Body>
              {items.map((i) => (
                <Table.Row key={i.apiId} id={i.apiId}>
                  <Table.Cell>
                    <Link to={`/catalog/apis/${i.apiId}`} className="text-primary hover:underline">
                      {i.displayName}
                    </Link>
                  </Table.Cell>
                  <Table.Cell>
                    <Badge type="pill-color" size="sm" color="brand">
                      {STYLE_LABEL[i.style] ?? i.style}
                    </Badge>
                  </Table.Cell>
                  <Table.Cell className="font-mono text-sm">{i.version}</Table.Cell>
                  <Table.Cell>
                    {i.hasSpec ? (
                      <Badge type="pill-color" size="sm" color="success">
                        Spec
                      </Badge>
                    ) : (
                      <span className="text-sm text-tertiary">—</span>
                    )}
                  </Table.Cell>
                  {showOrigin && (
                    <Table.Cell className="text-sm">
                      {i.origin === "derived" && i.viaApplicationId ? (
                        <span className="text-tertiary">
                          Derived · via{" "}
                          <Link
                            to={`/catalog/applications/${i.viaApplicationId}`}
                            className="text-primary hover:underline"
                          >
                            {i.viaApplicationDisplayName ?? "application"}
                          </Link>
                        </span>
                      ) : (
                        <span className="text-tertiary">Direct</span>
                      )}
                    </Table.Cell>
                  )}
                </Table.Row>
              ))}
            </Table.Body>
          </Table>
        </div>
      )}
    </div>
  );
}
```

> Confirm `Badge` color tokens (`brand`/`success`) and `Table`/`Table.Head`/`isRowHeader` import paths match the versions used in `RelationshipsSection.tsx` — copy them verbatim from there if they differ.

- [ ] **Step 5: Run the test — verify it passes**

Run: `cd web && npx vitest run src/features/catalog/components/__tests__/ApiSurfaceSection.test.tsx`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add web/src/features/catalog/api/apiSurface.ts \
        web/src/features/catalog/components/ApiSurfaceSection.tsx \
        web/src/features/catalog/components/__tests__/ApiSurfaceSection.test.tsx
git commit -m "feat(web): ApiSurfaceSection panel + useApiSurface hook (E-02.F-03.S-03)"
```

---

### Task 5: Mount the panel on both detail pages

**Files:**
- Modify: `web/src/features/catalog/pages/ServiceDetailPage.tsx` (add import; insert `<ApiSurfaceSection>` + `<hr>` above `<RelationshipsSection>`, ~line 123)
- Modify: `web/src/features/catalog/pages/ApplicationDetailPage.tsx` (add import; insert above `<RelationshipsSection>`, ~line 144)

- [ ] **Step 1: Wire into `ServiceDetailPage`**

Add the import alongside the existing catalog-component imports:

```tsx
import { ApiSurfaceSection } from "@/features/catalog/components/ApiSurfaceSection";
```

Insert directly **above** the `<RelationshipsSection>` block (after the mini-graph `<hr>`):

```tsx
          <hr className="border-secondary" />
          <ApiSurfaceSection entityKind="service" entityId={svc.id} />
```

- [ ] **Step 2: Wire into `ApplicationDetailPage`**

Add the same import, then insert above `<RelationshipsSection>` (after the mini-graph `<hr>` at ~line 144):

```tsx
          <hr className="border-secondary" />
          <ApiSurfaceSection entityKind="application" entityId={app.id} />
```

- [ ] **Step 3: Typecheck + run the catalog page tests**

Run: `cd web && npx tsc -b && npx vitest run src/features/catalog`
Expected: PASS (existing page tests still green; no `isRowHeader` regression).

- [ ] **Step 4: Cold-start browser verification (ADR-0084)**

Cold-start the dev server (stop any running instance first — see the ci-local/dev-server lock note), log in (`admin@orga` / `dev_password_12`), navigate **in-SPA** to a Service detail page that has API edges, and confirm: Provides/Consumes tables render, a derived row shows "Derived · via {App}", opening no dialog blank-pages (rowheader present). Repeat on an Application detail page. Capture a screenshot into the verification folder.

> DevSeed has 120 apps but no services/relationships (see the local-UI note) — you must create a Service, an `instance-of` edge, and `provides-api-for`/`consumes-api-from` edges via the UI (AddRelationship dialog) or seed them to exercise the panel. Note this in the verification evidence.

- [ ] **Step 5: Commit**

```bash
git add web/src/features/catalog/pages/ServiceDetailPage.tsx \
        web/src/features/catalog/pages/ApplicationDetailPage.tsx
git commit -m "feat(web): mount ApiSurfaceSection on Service + Application detail pages (E-02.F-03.S-03)"
```

---

### Task 6: Registry + checklist + DoD ledger scaffold

**Files:**
- Modify: `docs/design/list-filter-registry.md` (add the api-surface panel row)
- Modify: `docs/product/CHECKLIST.md` (E-02.F-03 note: S-03 sub-slice A done; sub-slice B registered)
- Create: `docs/superpowers/verification/2026-07-08-catalog-unified-api-view/dod.md` (copy `docs/superpowers/templates/dod-ledger-template.md`)
- Create: `docs/superpowers/verification/2026-07-08-catalog-unified-api-view/gate-findings.yaml` (copy `docs/superpowers/templates/gate-findings-template.yaml`)

- [ ] **Step 1: Add the registry row**

In `docs/design/list-filter-registry.md`, add a row for the api-surface panel: bounded embedded panel, default sort = sync-then-async then displayName asc, **all filter facets `none-needed`** (bounded, small N — justification inline). Follow the file's existing row format.

- [ ] **Step 2: Update the checklist**

In `docs/product/CHECKLIST.md` under **E-02.F-03**, mark S-03 sub-slice A (unified API view + derived exposure) as implemented (date 2026-07-08, branch `feat/catalog-unified-api-view`) and register sub-slice B (derived service↔service depends-on in graph) as the follow-up. Do not tick the `- [ ]` box until all blocking DoD gates are green.

- [ ] **Step 3: Scaffold the DoD ledger + findings log**

Copy the two templates into the verification folder and fill the slice header (date, topic, spec/plan paths, mutation-gate = **owner waiver**).

- [ ] **Step 4: Commit**

```bash
git add docs/design/list-filter-registry.md docs/product/CHECKLIST.md \
        docs/superpowers/verification/2026-07-08-catalog-unified-api-view
git commit -m "docs(catalog): registry + checklist + DoD ledger for unified API view (E-02.F-03.S-03)"
```

---

## Definition of Done (executed via the DoD workflow, not as plan tasks)

The eight always-blocking gates in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim. Run them against the final branch diff, updating `dod.md` per gate as it runs:

1. Full solution build, `TreatWarningsAsErrors=true`.
2. Per-task subagent reviews (interleaved during dev).
3. Full suite green (unit + architecture + integration real-seam — the `GetApiSurfaceTests` hit real Postgres/RLS + real JWT).
4. Container build (`docker compose build`).
5. `/simplify` on the branch diff.
6. **Mutation gate — OWNER-WAIVED this slice** (record as waiver, not green; do not run Stryker).
7. `/superpowers:requesting-code-review` on the full branch diff.
8. `/pr-review-toolkit:review-pr` (run for real — do not fold into 7/9).
9. `/deep-review` on the branch diff.

Terminal re-verify after gates 5/7/8/9 apply any fixes: re-run build + full suite on the final commit. Pre-push: `scripts/ci-local.sh` (Release mirror) green. Until the blocking gates pass, status is "implementation staged, verification pending".

---

## Self-Review

**Spec coverage:** §3 decisions → tasks: #1 on-read/bounded/direct-wins types (D1,D3,D6); #2 endpoint/derivation/asymmetry/422/400 (D2,D4,D5,D7,D11); #4–5 panel/placement/unified-badge (D9,D10); #6 registry (ADR-0107). §8 test matrix → #1 mapper unit + #2 integration (derived, direct-wins, application-no-derivation, tenant isolation, 422, 400×2) + #4 FE unit. §9 Impact Analysis reproduced above (N/A signature changes). §10 mutation waiver carried into Task 6 + DoD section.

**Placeholder scan:** No TBD/TODO. The only "confirm X" notes are explicit verification asks against named real files (fixture base class, `ApiId` ctor, `Badge`/`Table` import paths, `unwrapData` applicability, compose service name) — each names the file to check and the fallback, not deferred design.

**Type consistency:** `ApiSurfaceOrigin {Direct, Derived}`, `ApiSurfaceItem`, `ApiSurfaceResponse`, `GetApiSurfaceQuery(EntityKind, Guid)`, `ApiSurfaceMapper.Build/ProvidesEdge/ApiMeta`, `GetApiSurfaceHandler.Handle(query, db, lookup, ct)`, hook `useApiSurface(entityKind, entityId)` — names identical across tasks 1→2→4. Wire enum casing (`rest/grpc/graphQL/asyncApi/direct/derived`) consistent between backend (ADR-0109) and FE label/order maps.
