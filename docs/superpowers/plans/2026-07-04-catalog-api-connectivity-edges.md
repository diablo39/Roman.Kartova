# API Connectivity via Edges — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the `Api` catalog entity into the relationship graph as edges — provider (`provides-api-for`), consumer (`consumes-api-from`), and instance (`instance-of`) — reusing the existing relationship subsystem; remove the overlapping `PartOf` edge.

**Architecture:** All-edge model per the 2026-07-04 revision of ADR-0111. No FK columns, no new endpoint, no new permission, no schema migration (relationship `Kind`/`Type` persist as strings). Teach `EntityKind` the `Api` node, enable the dormant API edge types + a new `InstanceOf` type in `RelationshipTypeRules`, and add an `Api` branch to `ICatalogEntityLookup`. Frontend change is hygiene-only (drop `partOf` from the UI vocabulary).

**Tech Stack:** .NET 10 / C#, EF Core (PostgreSQL + RLS), MSTest v4 (native asserts, `[DataRow]`), Testcontainers real-seam integration (`KartovaApiFixtureBase`), React + TypeScript + Vitest.

**Spec:** `docs/superpowers/specs/2026-07-04-catalog-api-connectivity-edges-design.md`
**ADR:** `docs/architecture/decisions/ADR-0111-*.md` (Revised 2026-07-04)

## Global Constraints

- **No schema migration** — `EntityRef.Kind`, `Relationship.Type`, `Relationship.Origin` map via `HasConversion<string>()` (varchar, no CHECK). Enum add/remove needs no DB change. Adding a migration is a plan violation.
- **No new endpoint, permission, FK column, or derivation.** Reuse `CreateRelationship`/`DeleteRelationship`/`GraphTraversal` and their authz (ADR-0108 either-team). No `KartovaPermissions` 5-sync.
- **Enum wire format = camelCase** (ADR-0109). Persisted string form is the C# name (e.g. `InstanceOf`), asserted via `.ToString()` in audit tests.
- **`TreatWarningsAsErrors=true`** — 0 warnings, 0 errors, whole solution.
- **Wiring changes hit the real seam** (real JWT + real Postgres/RLS via `KartovaApiFixtureBase`); ≥1 happy + ≥1 negative per behavior. No mocked DbContext / fake auth.
- **Line endings LF** (`.gitattributes eol=lf` normalizes on commit).
- **Windows shell:** `cmd //c` or PowerShell wrappers for `dotnet`; multi-line git messages via multiple `-m` flags.
- **Mutation gate (DoD 6) is blocking** — the diff changes `RelationshipTypeRules` logic. Target ≥80%; each `IsAllowedPair` arm needs a true + false case.

---

### Task 1: Domain — `Api` kind, `InstanceOf` type, remove `PartOf`, rule matrix

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/EntityKind.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/RelationshipType.cs`
- Modify: `src/Modules/Catalog/Kartova.Catalog.Domain/RelationshipTypeRules.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.Tests/RelationshipTests.cs`

**Interfaces:**
- Consumes: `EntityKind`, `RelationshipType`, `RelationshipTypeRules.{IsCreatable,IsAllowedPair}`, `EntityRef`, `Relationship.CreateManual` (all existing).
- Produces: `EntityKind.Api`; `RelationshipType.InstanceOf` (and **absence** of `RelationshipType.PartOf`); creatable set `{DependsOn, InstanceOf, ProvidesApiFor, ConsumesApiFrom}`; allowed pairs — `DependsOn`: any→any; `InstanceOf`: `Service→Application`; `ProvidesApiFor`/`ConsumesApiFrom`: `{Application,Service}→Api`.

- [ ] **Step 1: Rewrite the domain test file to the new vocabulary (failing — won't compile: `Api`/`InstanceOf` don't exist yet)**

Replace the whole body of `RelationshipTests.cs` with:

```csharp
using Kartova.Catalog.Domain;
using Kartova.SharedKernel.Multitenancy;

namespace Kartova.Catalog.Tests;

[TestClass]
public class RelationshipTests
{
    private static EntityRef Svc(Guid id) => new(EntityKind.Service, id);
    private static EntityRef App(Guid id) => new(EntityKind.Application, id);
    private static EntityRef Api(Guid id) => new(EntityKind.Api, id);
    private static TenantId T() => new(Guid.NewGuid());

    [TestMethod]
    public void EntityRef_rejects_empty_id()
        => Assert.ThrowsExactly<ArgumentException>(() => new EntityRef(EntityKind.Service, Guid.Empty));

    [TestMethod]
    public void EntityRef_rejects_undefined_kind()
        => Assert.ThrowsExactly<ArgumentException>(() => new EntityRef((EntityKind)99, Guid.NewGuid()));

    [TestMethod]
    public void EntityRef_value_equality_holds()
    {
        var id = Guid.NewGuid();
        Assert.AreEqual(new EntityRef(EntityKind.Api, id), new EntityRef(EntityKind.Api, id));
        Assert.AreNotEqual(new EntityRef(EntityKind.Api, id), new EntityRef(EntityKind.Service, id));
    }

    [TestMethod]
    public void IsCreatable_is_dependsOn_instanceOf_provides_consumes()
    {
        foreach (var t in new[] { RelationshipType.DependsOn, RelationshipType.InstanceOf,
                     RelationshipType.ProvidesApiFor, RelationshipType.ConsumesApiFrom })
            Assert.IsTrue(RelationshipTypeRules.IsCreatable(t), $"{t} must be creatable");

        foreach (var t in new[] { RelationshipType.PublishesTo, RelationshipType.SubscribesFrom, RelationshipType.DeployedOn })
            Assert.IsFalse(RelationshipTypeRules.IsCreatable(t), $"{t} must not be creatable yet");
    }

    [TestMethod]
    // depends-on: any → any (incl. Api endpoints)
    [DataRow(RelationshipType.DependsOn, EntityKind.Service, EntityKind.Service, true)]
    [DataRow(RelationshipType.DependsOn, EntityKind.Application, EntityKind.Api, true)]
    // instance-of: Service → Application ONLY
    [DataRow(RelationshipType.InstanceOf, EntityKind.Service, EntityKind.Application, true)]
    [DataRow(RelationshipType.InstanceOf, EntityKind.Application, EntityKind.Service, false)]
    [DataRow(RelationshipType.InstanceOf, EntityKind.Service, EntityKind.Api, false)]
    [DataRow(RelationshipType.InstanceOf, EntityKind.Service, EntityKind.Service, false)]
    // provides-api-for: {App,Service} → Api ONLY
    [DataRow(RelationshipType.ProvidesApiFor, EntityKind.Application, EntityKind.Api, true)]
    [DataRow(RelationshipType.ProvidesApiFor, EntityKind.Service, EntityKind.Api, true)]
    [DataRow(RelationshipType.ProvidesApiFor, EntityKind.Service, EntityKind.Application, false)]
    [DataRow(RelationshipType.ProvidesApiFor, EntityKind.Api, EntityKind.Application, false)]
    // consumes-api-from: {App,Service} → Api ONLY
    [DataRow(RelationshipType.ConsumesApiFrom, EntityKind.Service, EntityKind.Api, true)]
    [DataRow(RelationshipType.ConsumesApiFrom, EntityKind.Application, EntityKind.Api, true)]
    [DataRow(RelationshipType.ConsumesApiFrom, EntityKind.Api, EntityKind.Service, false)]
    [DataRow(RelationshipType.ConsumesApiFrom, EntityKind.Service, EntityKind.Service, false)]
    // non-creatable type hits the default arm → false
    [DataRow(RelationshipType.PublishesTo, EntityKind.Service, EntityKind.Service, false)]
    public void IsAllowedPair_matrix(RelationshipType type, EntityKind source, EntityKind target, bool expected)
        => Assert.AreEqual(expected, RelationshipTypeRules.IsAllowedPair(type, source, target));

    [TestMethod]
    public void CreateManual_dependsOn_sets_fields_and_manual_origin()
    {
        var src = Svc(Guid.NewGuid());
        var tgt = Svc(Guid.NewGuid());
        var creator = Guid.NewGuid();
        var rel = Relationship.CreateManual(src, tgt, RelationshipType.DependsOn, creator, T(), TimeProvider.System);

        Assert.AreEqual(src, rel.Source);
        Assert.AreEqual(tgt, rel.Target);
        Assert.AreEqual(RelationshipType.DependsOn, rel.Type);
        Assert.AreEqual(RelationshipOrigin.Manual, rel.Origin);
        Assert.AreEqual(creator, rel.CreatedByUserId);
        Assert.AreNotEqual(Guid.Empty, rel.Id.Value);
    }

    [TestMethod]
    public void CreateManual_instanceOf_service_to_application_is_valid()
    {
        var rel = Relationship.CreateManual(Svc(Guid.NewGuid()), App(Guid.NewGuid()),
            RelationshipType.InstanceOf, Guid.NewGuid(), T(), TimeProvider.System);
        Assert.AreEqual(RelationshipType.InstanceOf, rel.Type);
    }

    [TestMethod]
    public void CreateManual_providesApiFor_service_to_api_is_valid()
    {
        var rel = Relationship.CreateManual(Svc(Guid.NewGuid()), Api(Guid.NewGuid()),
            RelationshipType.ProvidesApiFor, Guid.NewGuid(), T(), TimeProvider.System);
        Assert.AreEqual(RelationshipType.ProvidesApiFor, rel.Type);
    }

    [TestMethod]
    public void CreateManual_consumesApiFrom_application_to_api_is_valid()
    {
        var rel = Relationship.CreateManual(App(Guid.NewGuid()), Api(Guid.NewGuid()),
            RelationshipType.ConsumesApiFrom, Guid.NewGuid(), T(), TimeProvider.System);
        Assert.AreEqual(RelationshipType.ConsumesApiFrom, rel.Type);
    }

    [TestMethod]
    public void CreateManual_rejects_self_reference()
    {
        var same = Svc(Guid.NewGuid());
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            same, same, RelationshipType.DependsOn, Guid.NewGuid(), T(), TimeProvider.System));
    }

    [TestMethod]
    public void CreateManual_rejects_non_creatable_type()
    {
        var ex = Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            Svc(Guid.NewGuid()), Svc(Guid.NewGuid()), RelationshipType.PublishesTo, Guid.NewGuid(), T(), TimeProvider.System));
        StringAssert.Contains(ex.Message, "not yet available");
    }

    [TestMethod]
    public void CreateManual_rejects_disallowed_pair_providesApiFor_api_to_application()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            Api(Guid.NewGuid()), App(Guid.NewGuid()), RelationshipType.ProvidesApiFor, Guid.NewGuid(), T(), TimeProvider.System));
    }

    [TestMethod]
    public void CreateManual_rejects_empty_creator()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Relationship.CreateManual(
            Svc(Guid.NewGuid()), Svc(Guid.NewGuid()), RelationshipType.DependsOn, Guid.Empty, T(), TimeProvider.System));
    }
}
```

- [ ] **Step 2: Run the domain tests — expect a COMPILE failure**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -v q"`
Expected: build error — `EntityKind` has no `Api`, `RelationshipType` has no `InstanceOf`.

- [ ] **Step 3: Add `Api` to `EntityKind`**

`EntityKind.cs` — full file:

```csharp
namespace Kartova.Catalog.Domain;

public enum EntityKind { Application, Service, Api }
```

- [ ] **Step 4: Add `InstanceOf`, remove `PartOf` in `RelationshipType`**

`RelationshipType.cs` — full file:

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
    InstanceOf,
}
```

> `PartOf` removed (reintroduced for System grouping in E-03.F-03). `InstanceOf` appended at the end so existing enum ordinals are undisturbed (values persist as strings anyway).

- [ ] **Step 5: Update the rules**

`RelationshipTypeRules.cs` — full file:

```csharp
namespace Kartova.Catalog.Domain;

public static class RelationshipTypeRules
{
    public static bool IsCreatable(RelationshipType type)
        => type is RelationshipType.DependsOn
            or RelationshipType.InstanceOf
            or RelationshipType.ProvidesApiFor
            or RelationshipType.ConsumesApiFrom;

    public static bool IsAllowedPair(RelationshipType type, EntityKind source, EntityKind target) => type switch
    {
        RelationshipType.DependsOn => true,
        RelationshipType.InstanceOf => source == EntityKind.Service && target == EntityKind.Application,
        RelationshipType.ProvidesApiFor => source is EntityKind.Application or EntityKind.Service && target == EntityKind.Api,
        RelationshipType.ConsumesApiFrom => source is EntityKind.Application or EntityKind.Service && target == EntityKind.Api,
        _ => false,
    };
}
```

> **Precedence note:** `a is X or Y && b == Z` parses as `a is (X or Y)` then `&& (b == Z)` — `is`-pattern binds tighter than `&&`, and `or` is part of the pattern. This yields the intended `(source is App|Service) && (target == Api)`. The `IsAllowedPair_matrix` rows (`ProvidesApiFor, Api→Application, false` and `Service→Api, true`) pin this; if a mutant/typo flips it, they fail.

- [ ] **Step 6: Run the domain tests — expect PASS**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.Tests -v q"`
Expected: PASS (all `RelationshipTests` green).

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Domain/EntityKind.cs \
        src/Modules/Catalog/Kartova.Catalog.Domain/RelationshipType.cs \
        src/Modules/Catalog/Kartova.Catalog.Domain/RelationshipTypeRules.cs \
        src/Modules/Catalog/Kartova.Catalog.Tests/RelationshipTests.cs
git commit -m "feat(catalog): Api entity-kind + API edge types, drop PartOf (E-02.F-03)"
```

---

### Task 2: Infrastructure — `Api` lookup branch + create-relationship real-seam tests

**Files:**
- Modify: `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEntityLookup.cs`
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs`

**Interfaces:**
- Consumes: `ICatalogEntityLookup.Find(EntityKind, Guid, ct) → EntityLookupResult?`; `db.Apis`; `EfApiConfiguration.IdFieldName`; `EntityKind.Api` (Task 1). `CreateRelationshipAsync` endpoint (existing) uses the lookup for 422-on-missing + either-team authz.
- Produces: `CatalogEntityLookup.Find` resolves `EntityKind.Api` → `EntityLookupResult(TeamId, DisplayName)` from `catalog_apis`.

- [ ] **Step 1: Add the failing/updated integration tests**

In `CreateRelationshipTests.cs`: add a `SeedApiAsync` helper, add the API-edge test cases, and **remove** the two `PartOf` tests (`POST_partOf_service_to_application_returns_201`, `POST_partOf_application_to_service_returns_400`).

Add this helper next to `SeedApplicationAsync` (mirrors the `RegisterApiTests` body shape — `PostAsJsonAsync` default serializer handles the `ApiStyle` enum + camelCase per the app's configured options):

```csharp
    private static async Task<Guid> SeedApiAsync(HttpClient client, Guid teamId, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/catalog/apis", new
        {
            displayName = name,
            description = "x",
            style = ApiStyle.Rest,
            version = "v1",
            specUrl = (string?)null,
            teamId,
        });
        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode, $"SeedApi '{name}' failed: {resp.StatusCode}");
        var body = await resp.Content.ReadFromJsonAsync<ApiResponse>(KartovaApiFixtureBase.WireJson);
        return body!.Id;
    }
```

Add these test methods:

```csharp
    [TestMethod]
    public async Task POST_application_providesApiFor_api_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Provider App");
        var appId = await SeedApplicationAsync(client, teamId, "app-provider-201");
        var apiId = await SeedApiAsync(client, teamId, "orders-api-201");

        var resp = await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(RelationshipType.ProvidesApiFor, body!.Type);
        Assert.AreEqual("orders-api-201", body.Target.DisplayName);
    }

    [TestMethod]
    public async Task POST_two_services_can_provide_the_same_api()
    {
        // The driving scenario: one API contract implemented by N connector services.
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel N Providers");
        var apiId = await SeedApiAsync(client, teamId, "shared-contract");
        var connectorA = await SeedServiceAsync(client, teamId, "connector-a");
        var connectorB = await SeedServiceAsync(client, teamId, "connector-b");

        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, connectorA, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Service, connectorB, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId)).StatusCode);
    }

    [TestMethod]
    public async Task POST_service_consumesApiFrom_api_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Consumer");
        var svcId = await SeedServiceAsync(client, teamId, "svc-consumer-201");
        var apiId = await SeedApiAsync(client, teamId, "consumed-api-201");

        var resp = await PostRelAsync(client, EntityKind.Service, svcId, RelationshipType.ConsumesApiFrom, EntityKind.Api, apiId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_service_instanceOf_application_returns_201()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel InstanceOf 201");
        var svcId = await SeedServiceAsync(client, teamId, "svc-instance-201");
        var appId = await SeedApplicationAsync(client, teamId, "app-instance-201");

        var resp = await PostRelAsync(client, EntityKind.Service, svcId, RelationshipType.InstanceOf, EntityKind.Application, appId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<RelationshipResponse>(KartovaApiFixtureBase.WireJson);
        Assert.AreEqual(RelationshipType.InstanceOf, body!.Type);
    }

    [TestMethod]
    public async Task POST_providesApiFor_unknown_api_returns_422()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Unknown Api");
        var appId = await SeedApplicationAsync(client, teamId, "app-unknown-api-422");

        var resp = await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, Guid.NewGuid());

        Assert.AreEqual(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_disallowed_pair_providesApiFor_api_to_application_returns_400()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Bad Pair");
        var apiId = await SeedApiAsync(client, teamId, "api-badpair");
        var appId = await SeedApplicationAsync(client, teamId, "app-badpair");

        // Api → Application is not a valid ProvidesApiFor pair (provider points AT the Api).
        var resp = await PostRelAsync(client, EntityKind.Api, apiId, RelationshipType.ProvidesApiFor, EntityKind.Application, appId);

        Assert.AreEqual(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [TestMethod]
    public async Task POST_duplicate_providesApiFor_returns_409()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Rel Dup Provider");
        var appId = await SeedApplicationAsync(client, teamId, "app-dup-provider");
        var apiId = await SeedApiAsync(client, teamId, "api-dup-provider");
        Assert.AreEqual(HttpStatusCode.Created,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId)).StatusCode);
        Assert.AreEqual(HttpStatusCode.Conflict,
            (await PostRelAsync(client, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId)).StatusCode);
    }

    [TestMethod]
    public async Task POST_providesApiFor_by_member_of_api_team_returns_201()
    {
        // ADR-0108 either-team authority resolves the Api's team via the new lookup branch:
        // a member of the API's owning team (but not the provider app's) may declare the edge.
        var admin = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var tenant = Fx.TenantIdForEmail(OrgAUser);
        var appTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Provider App Team");
        var apiTeam = await Fx.SeedTeamInOrganizationAsync(tenant, "Api Owner Team");
        var appId = await SeedApplicationAsync(admin, appTeam, "app-authz-provider");
        var apiId = await SeedApiAsync(admin, apiTeam, "api-authz-target");

        var member = await Fx.CreateAuthenticatedClientAsync("member@orga.kartova.local", new[] { KartovaRoles.Member });
        var memberId = await Fx.GetSubClaimAsync("member@orga.kartova.local");
        await Fx.SeedTeamMembershipAsync(apiTeam, memberId, roleByte: 1 /* Member */);

        var resp = await PostRelAsync(member, EntityKind.Application, appId, RelationshipType.ProvidesApiFor, EntityKind.Api, apiId);

        Assert.AreEqual(HttpStatusCode.Created, resp.StatusCode);
    }
```

Then delete `POST_partOf_service_to_application_returns_201` and `POST_partOf_application_to_service_returns_400`.

- [ ] **Step 2: Run the create-relationship tests — expect FAIL on the API cases**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~CreateRelationshipTests -v q"`
Expected: the new `...providesApiFor...`/`...consumesApiFrom...` **201** cases FAIL with **422** — `CatalogEntityLookup.Find` returns `null` for `EntityKind.Api` (default arm), so a valid Api reads as "not found". (The 422/400/instanceOf cases may already pass.)

- [ ] **Step 3: Add the `Api` branch to the lookup**

`CatalogEntityLookup.cs` — add the `EntityKind.Api` arm before `_ => null`:

```csharp
        EntityKind.Api => await db.Apis
            .Where(a => EF.Property<Guid>(a, EfApiConfiguration.IdFieldName) == id)
            .Select(a => new EntityLookupResult(a.TeamId, a.DisplayName))
            .SingleOrDefaultAsync(ct),
        _ => null,
```

- [ ] **Step 4: Run the create-relationship tests — expect PASS**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~CreateRelationshipTests -v q"`
Expected: PASS (all cases including provider/consumer 201, N-providers, 422 unknown Api, 400 bad pair, 409 dup, api-team authz).

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEntityLookup.cs \
        src/Modules/Catalog/Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs
git commit -m "feat(catalog): resolve Api nodes in entity lookup; API-edge create tests (E-02.F-03)"
```

---

### Task 3: Graph traversal surfaces `Api` nodes

**Files:**
- Test: `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs`

**Interfaces:**
- Consumes: `GET /api/v1/catalog/graph?entityKind=Api&entityId=...` (the graph endpoint accepts `entityKind=Api` for free once `EntityKind` has `Api`); `GraphTraversalHandler` enriches every node via `ICatalogEntityLookup` (Task 2). No production change in this task.
- Produces: regression coverage that an `Api` node carries `DisplayName`/`TeamId` and API edges appear in the graph.

- [ ] **Step 1: Add the graph test**

Add a `SeedApiAsync` helper (same body as Task 2's) to `GetCatalogGraphTests.cs`, plus:

```csharp
    [TestMethod]
    public async Task GET_graph_focused_on_api_returns_provider_edge_and_enriched_api_node()
    {
        var client = await Fx.CreateAuthenticatedClientAsync(OrgAUser);
        var teamId = await Fx.SeedTeamInOrganizationAsync(Fx.TenantIdForEmail(OrgAUser), "Graph Api Team");
        var apiId = await SeedApiAsync(client, teamId, "graph-api");
        var providerSvc = await SeedServiceAsync(client, teamId, "graph-provider-svc");

        var rel = await client.PostAsJsonAsync("/api/v1/catalog/relationships", new
        {
            sourceKind = EntityKind.Service, sourceId = providerSvc,
            type = RelationshipType.ProvidesApiFor,
            targetKind = EntityKind.Api, targetId = apiId,
        });
        Assert.AreEqual(HttpStatusCode.Created, rel.StatusCode, $"seed provider edge: {rel.StatusCode}");

        var resp = await client.GetAsync($"/api/v1/catalog/graph?entityKind=Api&entityId={apiId}&depth=1&direction=all");
        Assert.AreEqual(HttpStatusCode.OK, resp.StatusCode);
        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>(KartovaApiFixtureBase.WireJson);

        var apiNode = graph!.Nodes.Single(n => n.Id == apiId);
        Assert.AreEqual("graph-api", apiNode.DisplayName);   // proves the CatalogEntityLookup Api branch enriched it
        Assert.AreEqual(teamId, apiNode.TeamId);
        Assert.IsTrue(graph.Nodes.Any(n => n.Id == providerSvc), "provider service present at depth 1");
        Assert.AreEqual(1, graph.Edges.Count);
    }
```

- [ ] **Step 2: Run the graph tests — expect PASS**

Run: `cmd //c "dotnet test src/Modules/Catalog/Kartova.Catalog.IntegrationTests --filter FullyQualifiedName~GetCatalogGraphTests -v q"`
Expected: PASS. (If the Api node's `DisplayName` is empty, Task 2's lookup branch is missing/incorrect — fix there, not here.)

- [ ] **Step 3: Commit**

```bash
git add src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs
git commit -m "test(catalog): graph surfaces enriched Api nodes + provider edges (E-02.F-03)"
```

---

### Task 4: Frontend hygiene — drop `partOf`; regenerate OpenAPI snapshot

**Files:**
- Modify: `web/src/features/catalog/relationships/relationshipTypeRules.ts`
- Modify: `web/src/features/catalog/relationships/__tests__/relationshipTypeRules.test.ts`
- Modify: `web/src/features/catalog/relationships/__tests__/graphModel.test.ts`
- Modify: `web/src/features/catalog/components/__tests__/AddRelationshipDialog.test.tsx`
- Modify (generated): `web/openapi-snapshot.json`, `web/src/generated/*`

**Interfaces:**
- Consumes: backend enum surface (Task 1) reflected in the regenerated OpenAPI schema.
- Produces: `CreatableRelationshipType = "dependsOn"` (only); UI no longer offers `partOf`. No new UI types (`api`/`instanceOf`/`providesApiFor`/`consumesApiFrom` land in FU-A).

> Scope note: this task does **not** teach the graph/dialog the `Api` kind or the new edge types. Backend-created API edges are not reachable from the current UI (dialog offers only `dependsOn`; dev seed has none), so no FE render path hits them this slice. Full API graph UI = FU-A.

- [ ] **Step 1: Remove `partOf` from the rules module**

`relationshipTypeRules.ts` — replace lines 1–25 (types, label, creatable list, `isAllowedPair`) with:

```typescript
export type RelationshipKind = "application" | "service";
export type CreatableRelationshipType = "dependsOn";
export type FixedRole = "source" | "target";

export const relationshipTypeLabel: Record<CreatableRelationshipType, string> = {
  dependsOn: "Depends on",
};

const CREATABLE_TYPES: CreatableRelationshipType[] = ["dependsOn"];
const KINDS: RelationshipKind[] = ["application", "service"];

// Mirror of backend RelationshipTypeRules.IsAllowedPair (ADR-0068, creatable UI subset).
// Only `dependsOn` is creatable from the UI this slice; API edge types (providesApiFor,
// consumesApiFrom, instanceOf) and the `api` kind land with the API graph UI (FU-A).
export function isAllowedPair(
  _type: CreatableRelationshipType,
  _source: RelationshipKind,
  _target: RelationshipKind,
): boolean {
  return true; // dependsOn: any → any
}
```

Leave `allowedOtherKinds` and `offerableTypes` unchanged (they call `isAllowedPair`).

- [ ] **Step 2: Update the rules unit test**

`relationshipTypeRules.test.ts` — remove the `partOf`-specific cases and fix the `offerableTypes` expectations (a `service` source no longer offers `partOf`):

```typescript
  it("offerableTypes is dependsOn for every fixed role/kind", () => {
    expect(offerableTypes("source", "application")).toEqual(["dependsOn"]);
    expect(offerableTypes("source", "service")).toEqual(["dependsOn"]);
    expect(offerableTypes("target", "application")).toEqual(["dependsOn"]);
    expect(offerableTypes("target", "service")).toEqual(["dependsOn"]);
  });
```

Delete the `it("partOf allows only service -> application", ...)`, the `allowedOtherKinds("partOf", ...)` assertions, and the `relationshipTypeLabel.partOf` assertion. Keep the `dependsOn` assertions.

- [ ] **Step 3: Update the graph-model test**

`graphModel.test.ts` — delete the `it("labels a partOf edge 'Part of'", ...)` block (lines ~46–51). `partOf` is no longer a creatable type; label fallback for unknown types is separately covered by the `?? r.type` path.

- [ ] **Step 4: Update the dialog test**

`AddRelationshipDialog.test.tsx` — delete the whole `it("forces the other kind to Application (disabled) for PartOf, source side, Service fixed", ...)` block (lines ~174–198). The type dropdown now offers only `dependsOn`, so a `partOf` selection is unreachable. Keep the `expect(...).toEqual(["dependsOn"])` option-list test.

- [ ] **Step 5: Verify no `partOf` / `Part of` references remain in the web tree**

Run: `cmd //c "cd web && npx grep -rn partOf src || rg -n \"partOf|Part of\" src"`
(Or `git grep -n "partOf\|Part of" -- web/src`.) Expected: **no matches**.

- [ ] **Step 6: Frontend typecheck + tests**

Run: `cmd //c "cd web && npm run test -- --run && npm run build"`
Expected: Vitest green; `tsc -b` (the binding type gate) succeeds — `CreatableRelationshipType = "dependsOn"` typechecks everywhere it's used (`graphModel.ts`, `graphMerge.ts`, `RelationshipsSection.tsx`, dialog).

- [ ] **Step 7: Regenerate the OpenAPI snapshot (new enum members in the schema)**

The backend enum surface changed (`EntityKind` gained `Api`; `RelationshipType` gained `InstanceOf`, lost `PartOf`). Per the codegen workflow, rebuild the API image so the live spec is current, then regenerate the committed snapshot:

Run: `cmd //c "cd web && npm run predev"` (or the project's snapshot-regen script — `predev`/`prebuild` pulls the live spec into `web/openapi-snapshot.json` + `web/src/generated/*`).
Expected: `web/openapi-snapshot.json` diff shows `Api` added to the entity-kind enum and `InstanceOf`/`-PartOf` in the relationship-type enum, nothing else semantic. Commit the regenerated files. (If the API image can't be rebuilt locally, flag this step **pending user verification** and do not hand-edit the snapshot.)

- [ ] **Step 8: Commit**

```bash
git add web/src/features/catalog/relationships/relationshipTypeRules.ts \
        web/src/features/catalog/relationships/__tests__/relationshipTypeRules.test.ts \
        web/src/features/catalog/relationships/__tests__/graphModel.test.ts \
        web/src/features/catalog/components/__tests__/AddRelationshipDialog.test.tsx \
        web/openapi-snapshot.json web/src/generated
git commit -m "chore(web): drop partOf from relationship UI vocabulary; regen OpenAPI snapshot (E-02.F-03)"
```

---

### Task 5: Slice verification (DoD gates) + checklist

**Files:**
- Create: `docs/superpowers/verification/2026-07-04-catalog-api-connectivity-edges/dod.md` (copy `docs/superpowers/templates/dod-ledger-template.md`)
- Create: `docs/superpowers/verification/2026-07-04-catalog-api-connectivity-edges/gate-findings.yaml` (copy `docs/superpowers/templates/gate-findings-template.yaml`)
- Modify: `docs/product/CHECKLIST.md` (E-02.F-03 note)

- [ ] **Step 1: Create the DoD ledger + gate-findings from templates**, filling the slice header (date `2026-07-04`, branch `feat/catalog-api-connectivity-edges`, spec/plan links).

- [ ] **Step 2: Gate 1 — full solution build, warnings-as-errors.**
Run: `cmd //c "dotnet build Kartova.slnx -c Debug"` — expect 0 warnings, 0 errors. Record in ledger.

- [ ] **Step 3: Gate 3 — full test suite (unit + architecture + integration real-seam).**
Run: `cmd //c "dotnet test Kartova.slnx"` — expect green. (Per the Docker-flake note, re-run any single integration assembly that trips a named-pipe timeout before calling it red.) Record.

- [ ] **Step 4: Gate 6 — mutation (blocking; rules logic changed).**
Run `/misc:mutation-sentinel` scoped to `RelationshipTypeRules.cs` (+ `CatalogEntityLookup.cs`); then `/misc:test-generator` for survivors. Target ≥80%. Record survivors + score.

- [ ] **Step 5: Gate 5 — `/simplify` on the branch diff.** Address or note-skip should-fix items. Record.

- [ ] **Step 6: Gates 2/7/8/9 — reviews.** `/superpowers:requesting-code-review`, `/pr-review-toolkit:review-pr`, `/deep-review` against the full branch diff (spec + plan + ADR revision as context). Each runs for real (no folding); log findings to `gate-findings.yaml` with real/delusion verdicts. Address blocking + should-fix.

- [ ] **Step 7: Gate 4 + Release mirror — `scripts/ci-local.sh`.**
Run: `cmd //c "bash scripts/ci-local.sh"` (or subsets `backend`/`frontend`). Expect green (Release build+test, web image, helm/stryker). This is the container-build gate too (no Dockerfile change, but it still runs).

- [ ] **Step 8: Terminal re-verify.** After any gate-5–9 fixes, re-run gate 1 (`dotnet build Kartova.slnx -c Debug`) + gate 3 (`dotnet test Kartova.slnx`) on the final commit; confirm green. Mark ledger summary.

- [ ] **Step 9: Update `docs/product/CHECKLIST.md`** — under E-02.F-03, note: FU-1/FU-2/FU-11 superseded by the all-edge slice (ADR-0111 revised 2026-07-04); API provider/consumer/instance edges shipped; FU-A (API graph UI), FU-B (derived exposure), FU-C (async), FU-D (System + PartOf return), FU-E (unified view) registered. Commit.

```bash
git add docs/superpowers/verification/2026-07-04-catalog-api-connectivity-edges docs/product/CHECKLIST.md
git commit -m "docs(catalog): DoD ledger + checklist for API connectivity edges (E-02.F-03)"
```

---

## Impact Analysis (codelens/LSP)

Changes to **shared enums** (`EntityKind`, `RelationshipType`) and one static rule class. Enum literals are **under-reported by codelens** (const/enum carve-out in CLAUDE.md) → blast radius established by **grep**, cross-checked with codelens/LSP for method/switch sites. Findings (fresh grep at execution time must reconcile against these):

**`RelationshipType.PartOf` removal — every production/test reference is covered by a task:**
- `RelationshipType.cs` (def), `RelationshipTypeRules.cs` (`IsCreatable`, `IsAllowedPair` arm) → **Task 1**.
- `Kartova.Catalog.Tests/RelationshipTests.cs` (matrix + `CreateManual_partOf...`) → **Task 1** (rewritten).
- `Kartova.Catalog.IntegrationTests/CreateRelationshipTests.cs` (`POST_partOf_*` ×2) → **Task 2** (removed).
- `web/.../relationshipTypeRules.ts`, `relationshipTypeRules.test.ts`, `graphModel.test.ts`, `AddRelationshipDialog.test.tsx` → **Task 4**.
- Docs (`specs/plans/verification/*`) — historical; superseded by this plan + ADR revision; not edited.
- **No other production reference.** (Confirm: `git grep -n "PartOf\|partOf\|Part of"` — every non-doc hit must map to Task 1/2/4.)

**`EntityKind` add `Api` — switch/consumer sites (codelens `find_references` on the enum type + grep for `EntityKind.`):**
- `CatalogEntityLookup.Find` (switch, `_ => null` default swallows `Api`) → **Task 2** (branch added). This is the one site that MUST change or a valid Api reads as 422.
- `EntityRef` ctor `Enum.IsDefined` — additive, no change.
- `GraphTraversalHandler` / `GraphNodeDto` / graph endpoint `entityKind` bind — pass `Kind` through; `entityKind=Api` parses for free → **Task 3** exercises it, no prod change.
- FE `graphModel.ts` `parseEntityRef`/`ENTITY_KIND_LABEL` (only `application|service`) — **intentionally not extended** (Api not rendered until FU-A); no FE path forces an Api node this slice (scope note, Task 4).

**`ProvidesApiFor`/`ConsumesApiFrom` enable — already-defined enum values**; grep confirms only `RelationshipType.cs` + rules reference them. Making them creatable is additive; no other caller assumes they are non-creatable except `RelationshipTests.IsCreatable_*` → **Task 1** (rewritten).

No existing C# **method/interface signature** changes — `IsCreatable`/`IsAllowedPair`/`Find` keep their signatures; only their internal logic/branches grow. So no caller-site churn beyond the enum literal sites above.

---

## Self-Review

**Spec coverage:** §5.1 backend (EntityKind/RelationshipType/rules → Task 1; lookup → Task 2); §5.2 frontend hygiene + snapshot → Task 4; §5.3 tests (domain → Task 1; create real-seam → Task 2; graph → Task 3); §6 error semantics exercised (422/400/409/403 in Task 2); §7 impact analysis → this plan's Impact section (grep-grounded); §8 DoD incl. blocking mutation → Task 5. No spec section unmapped.

**Placeholder scan:** every code step shows full content; commands have expected output; snapshot-regen step names the fallback (flag pending if image can't build) rather than a TODO. No "similar to Task N".

**Type consistency:** `EntityKind.Api`, `RelationshipType.InstanceOf` (no `PartOf`), `RelationshipTypeRules.{IsCreatable,IsAllowedPair}` signatures unchanged; `ICatalogEntityLookup.Find`/`EntityLookupResult(TeamId, DisplayName)` used consistently in Task 2/3; `ApiResponse`/`RelationshipResponse`/`GraphResponse` DTOs match existing usages; `CreatableRelationshipType = "dependsOn"` consistent across Task 4 edits. Helper `SeedApiAsync` identical in Task 2 and Task 3.

**No blocking issues found.**
