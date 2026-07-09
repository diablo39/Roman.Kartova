# Slice — Catalog: Derived service↔service `depends-on` (sub-slice B)

**Date:** 2026-07-09
**Stories:** E-02.F-03 — **sub-slice B** of the S-03 + FU-B decomposition (FU-B remainder). Closes the derived-dependency half left open when sub-slice A (PR #63) shipped derived *exposure*.
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-derived-dependencies`
**Governing decisions:** [ADR-0111](../../architecture/decisions/ADR-0111-api-first-class-entity-provider-instance-fields.md) §Decision 5 (service↔service `depends-on` **derives** `consumes ∘ exposes⁻¹`, coexists with explicit edges), [ADR-0068](../../architecture/decisions/ADR-0068-fixed-relationship-type-vocabulary.md) (relationship vocabulary), [ADR-0095](../../architecture/decisions/ADR-0095-cursor-pagination-contract.md) (bounded-list carve-out), [ADR-0040](../../architecture/decisions/ADR-0040-two-view-dependency-graph-navigation.md) (graph viz), [ADR-0107](../../architecture/decisions/ADR-0107-list-filtering-consideration-and-filterbar-ui.md) (list-surface), [ADR-0084](../../architecture/decisions/ADR-0084-playwright-mcp-for-frontend-development.md) (`isRowHeader` / browser verification).
**Predecessor spec:** `docs/superpowers/specs/2026-07-08-catalog-unified-api-view-design.md` (sub-slice A — derived exposure, on-read).

---

## 1. Goal

Make the catalog reflect the service dependencies that **already exist implicitly** through the API graph. Today a service `S` that consumes an API provided by service `T` shows **no** `S → T` edge anywhere — the graph explorer, the per-entity mini-graph, and the relationship tables all under-report real coupling, because that dependency lives only as two separate edges (`S consumes X`, `T provides X`) through the API node.

This slice **derives** `S depends-on T` on read (ADR-0111 §Decision 5) and surfaces it on all three views, visually distinct from hand-authored edges and carrying **full provenance** (which API links them, and — when the provider exposes it through an application — which app).

### 1.1 Decomposition context

Brainstorming (2026-07-09) settled the derivation semantics and confirmed **all three surfaces** are in scope. The three surfaces span **two** backend read endpoints and would exceed the ~800-LOC ceiling as one slice, so this design plans as **two sequential, independently shippable sub-slices**:

| | Scope | Endpoint | Surfaces |
|---|---|---|---|
| **B1** (first) | Shared derivation helper + derived edges in graph traversal + explorer rendering (dashed edge, legend, provenance tooltip) | `GET /catalog/graph` (extended) | Standalone graph explorer (`/graph`) |
| **B2** (next) | Bounded derived-dependencies read endpoint + mini-graph merge + new read-only Dependencies/Dependents section | `GET /catalog/derived-dependencies` (new) | Service-detail mini-graph + `DerivedDependenciesSection` |

B2 depends on B1's shared helper. Each is DoD-complete on its own.

---

## 2. Derivation — locked semantics

For services **S** (consumer) and **T** (provider), a **derived** `S depends-on T` edge exists iff there is an API **X** in T's *provided surface* that S consumes:

- **Consume side:** `S --consumes-api-from--> X` (a persisted `ConsumesApiFrom` edge, `S` a Service).
- **Provide side (T's full surface, mirrors sub-slice A):** X is in T's provided surface if **either**
  - `T --provides-api-for--> X` (T provides X **directly**), **or**
  - `T --instance-of--> A` and `A --provides-api-for--> X` (T exposes X **via application A**).

**Scope: Service↔Service only** (ADR-0111 §5). Both endpoints are Services. Application/API consumers and providers are already covered by the sub-slice-A API-surface panel and are **not** part of this derivation.

**Locked rules:**

| # | Rule | Rationale |
|---|------|-----------|
| D1 | **Derive on read** — computed per request by a shared pure helper. No materialized derived-edge table. | Consistent with sub-slice A; no staleness/infra; ADR-0111 leaves strategy open (YAGNI). |
| D2 | **T's provided surface = direct `provides-api-for` ∪ via-app (`instance-of ∘ provides-api-for`).** | Symmetric with sub-slice A's provided-surface definition; a consumer depends on T whether T provides X directly or through its app. |
| D3 | **One collapsed edge per ordered pair `(S,T)`**, carrying **all** provenance paths. Each path `{ apiId, apiName, viaAppId?, viaAppName? }`; `viaApp` null when T provides the API directly. | Multiple linking APIs / multiple exposing apps must not multiply edges; the graph stays one-edge-per-pair, the tooltip/expander lists every path. |
| D4 | **Explicit wins:** if a persisted `depends-on` edge `S → T` exists, **suppress** the derived edge for that ordered pair. | Direct-wins precedent (sub-slice A). The explicit edge already renders (with origin badge + delete) in `RelationshipsSection`; showing a second derived edge for the same pair is noise. |
| D5 | **No self-edge:** `S == T` never yields a derived edge (a service consuming an API it itself provides). | A dependency on oneself is meaningless. |
| D6 | **Derived edges use `catalog.read`.** No new permission, no 5-sync. | Pure read over already-readable edges/entities. |
| D7 | **Derived is never persisted, never authorable/deletable.** Authoring stays on explicit edges via `AddRelationshipDialog`. | Derivation is a computed view; "pinning" a derived edge to manual is a separate future story (E-04.F-01.S-03). |

---

## 3. Pre-requisites (already on master)

- **API entity + all edges live:** `Api` aggregate + `catalog_apis` (RLS); `Relationship` aggregate with `EntityRef{Kind,Id}`; `RelationshipType.{DependsOn, ProvidesApiFor, ConsumesApiFrom, InstanceOf}`; `EntityKind.{Application, Service, Api}`; `RelationshipOrigin.{Manual, Scan, Agent}`.
- **Graph read path:** `GraphTraversal.BuildAsync` (pure BFS over an injected edge-fetch closure), `GraphTraversalHandler` (persisted-edge fetch + node enrichment), `GET /catalog/graph`, `GraphResponse`/`GraphEdgeDto`/`GraphNodeDto`.
- **Relationship + surface reads:** `GET /catalog/relationships` (`useRelationshipsList`), sub-slice-A `GET /catalog/api-surface` + `ApiSurfaceMapper` (on-read derivation precedent), `ICatalogEntityLookup`, `KartovaApiFixtureBase` (real JWT + Postgres/RLS).
- **Frontend:** `GraphExplorerPage` + `EntityGraphNode` (React Flow), `DependencyMiniGraph` (consumes `useRelationshipsList` → `toGraphModel`), `RelationshipsSection` (Outgoing/Incoming), `ApiSurfaceSection` (sub-slice-A sibling-panel precedent), `entityDetailPath`/`graphModel` helpers.
- **Contracts/coverage conventions:** every `*Response`/`*Dto` + `[BoundedListResult]` carry `[ExcludeFromCodeCoverage]` (ContractsCoverageRules arch test); enum wire = camelCase (ADR-0109).

---

## 4. Architecture

### 4.1 Shared derivation helper (Application, pure — both sub-slices use it)

`Kartova.Catalog.Application/DerivedDependencies.cs`

```csharp
public static class DerivedDependencies
{
    public sealed record Path(Guid ApiId, Guid? ViaAppId);          // names joined by the handler
    public sealed record Edge(Guid SourceServiceId, Guid TargetServiceId, IReadOnlyList<Path> Paths);

    // All inputs are RLS-scoped rows the handler has already fetched. Pure; no EF.
    public static IReadOnlyList<Edge> Compute(
        IReadOnlyCollection<(Guid ServiceId, Guid ApiId)> consumes,        // S consumes X
        IReadOnlyCollection<(Guid ServiceId, Guid ApiId)> serviceProvides, // T provides X directly
        IReadOnlyCollection<(Guid ServiceId, Guid AppId)> instanceOf,      // T instance-of A
        IReadOnlyCollection<(Guid AppId,   Guid ApiId)> appProvides,       // A provides X
        IReadOnlySet<(Guid Source, Guid Target)> explicitDependsOn);       // suppress these pairs (D4)
}
```

Builds T's provided-surface map `apiId → set of (serviceT, viaApp?)` from `serviceProvides` (viaApp null) ∪ (`instanceOf` ⋈ `appProvides`) (viaApp = A). Then for each `(S, X) ∈ consumes` and each `(T, viaApp?)` exposing X with `S ≠ T` (D5), emits a path; groups paths by `(S,T)` (D3); drops pairs in `explicitDependsOn` (D4). Deterministic ordering (paths sorted by `(apiId, viaAppId)`).

### 4.2 B1 — graph traversal (`GET /catalog/graph`)

Derived edges **drive node discovery** so a service reachable only through a derived dependency still appears (the whole point). This is done inside the handler's fetch closure — `GraphTraversal.BuildAsync` stays generic and **unchanged**.

```
GET /catalog/graph?focus=&depth=&direction=   (existing; now also returns DerivedEdges)
  → GraphTraversalHandler.Handle
      BuildAsync(focus, depth, direction, cap, fetchEdgesTouching):
        fetchEdgesTouching(frontier):
          persisted = db.Relationships touching frontier            (unchanged)
          derived   = derivedEdgesTouching(frontier):               (NEW)
              // frontier services as CONSUMER (discover providers T) AND as PROVIDER (discover consumers S)
              fetch consumes / service-provides / instance-of / app-provides rows touching frontier svc + their apps
              → DerivedDependencies.Compute(...) filtered to edges incident to a frontier node
              → each Edge → GraphTraversalEdge(src, tgt, synthId, DependsOn, origin=n/a, Provenance=paths)
          return persisted ∪ derived                                // BFS treats uniformly
      // after BFS: map persisted → GraphEdgeDto (unchanged); derived (among kept nodes) → DerivedEdgeDto[]
      // drop a derived (S,T) when a persisted depends-on (S,T) survived among kept edges (D4)
```

- `GraphTraversalEdge` gains `IReadOnlyList<DerivedDependencies.Path>? Provenance = null` (default → existing constructors/tests unaffected). Persisted edges leave it null; derived edges set it. Synthetic deterministic `Id` (GUID from `(src,tgt)`) so BFS dedup works; not surfaced.
- Node enrichment (displayName/teamId) unchanged; provenance app/api names resolved once via a batched lookup over the derived paths' api + app ids.
- **RLS:** every fetch runs under the request `ITenantScope`; cross-tenant edges/APIs never enter the computation (tested).

### 4.3 B2 — bounded derived-dependencies endpoint (`GET /catalog/derived-dependencies`)

```
GET /api/v1/catalog/derived-dependencies?entityId={guid}   (NEW; catalog.read; service-only)
  → GetDerivedDependenciesHandler
      lookup(service, entityId) → 422 invalid-entity if absent (RLS ⇒ cross-tenant absent); 400 if kind ≠ service
      fetch the four edge-sets touching this service (as consumer) and touching it (as provider), + its instance-of apps
      → DerivedDependencies.Compute(...) → split into
          Dependencies = edges where source == focus (this service depends on …)
          Dependents   = edges where target == focus (… depend on this service)
      → join api + via-app display names → DerivedDependenciesResponse (bounded flat, no cursor)
```

Dedicated bounded endpoint (mirrors sub-slice-A `/api-surface`): a single service's derived dependency set is small and bounded → `[BoundedListResult]`, no sort/filter/cursor; avoids mixing synthetic rows into the cursor-paginated `/relationships` list. `direction` is not a query param — the response returns both sides (the section renders both).

### 4.4 New / edited files

**B1**

| File | Change |
|------|--------|
| `Kartova.Catalog.Application/DerivedDependencies.cs` | **new** — pure helper (§4.1). |
| `Kartova.Catalog.Contracts/DerivedEdgeDto.cs` | **new** — `(GraphEndpointDto Source, GraphEndpointDto Target, IReadOnlyList<DerivationPathDto> Paths)`. |
| `Kartova.Catalog.Contracts/DerivationPathDto.cs` | **new** — `(Guid ApiId, string ApiName, Guid? ViaApplicationId, string? ViaApplicationDisplayName)`. |
| `Kartova.Catalog.Contracts/GraphResponse.cs` | **edit** — add `IReadOnlyList<DerivedEdgeDto> DerivedEdges` (positional; update the one construction site). |
| `Kartova.Catalog.Application/GraphTraversal.cs` | **edit** — `GraphTraversalEdge` gains `IReadOnlyList<DerivedDependencies.Path>? Provenance = null` (default; `BuildAsync` signature unchanged). |
| `Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs` | **edit** — closure emits derived edges; map derived → `DerivedEdgeDto`; explicit-wins dedup; batched name resolution. |
| `Kartova.Catalog.Tests/DerivedDependenciesTests.cs` | **new** — helper unit tests. |
| `Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs` | **edit** — derived-edge real-seam cases. |
| `web/src/features/catalog/relationships/graphModel.ts` (+ explorer/`EntityGraphNode`/`GraphExplorerPage`) | render `DerivedEdges` as dashed edges + legend + provenance tooltip. |
| `web/openapi-snapshot.json` + regenerated `web/src/generated/*` | rebuild API image → regenerate → commit (new `DerivedEdges` field + DTOs). |

**B2**

| File | Change |
|------|--------|
| `Kartova.Catalog.Contracts/DerivedDependenciesResponse.cs` + `DerivedDependencyItem.cs` | **new** — `[BoundedListResult]` `{ Dependencies[], Dependents[] }`; item `(Guid ServiceId, string DisplayName, Guid? TeamId, IReadOnlyList<DerivationPathDto> Paths)`. |
| `Kartova.Catalog.Application/GetDerivedDependenciesQuery.cs` | **new** — `(Guid ServiceId)`. |
| `Kartova.Catalog.Infrastructure/GetDerivedDependenciesHandler.cs` | **new** — edge fetch + helper + name join + split. |
| `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` + `CatalogModule.cs` | **edit** — `GetDerivedDependenciesAsync` delegate (validate service kind) + map `GET /derived-dependencies` + `AddScoped` handler. |
| `Kartova.Catalog.IntegrationTests/GetDerivedDependenciesTests.cs` | **new** — real-seam (§8). |
| `web/src/features/catalog/api/derivedDependencies.ts` | **new** — `useDerivedDependencies(entityId)` hook. |
| `web/src/features/catalog/components/DerivedDependenciesSection.tsx` | **new** — read-only Dependencies/Dependents tables + provenance. |
| `web/src/features/catalog/components/DependencyMiniGraph.tsx` | **edit** — merge derived edges (dashed) into the model. |
| `web/src/features/catalog/pages/ServiceDetailPage.tsx` | **edit** — mount `<DerivedDependenciesSection entityId=… />` (service-only). |
| `web/openapi-snapshot.json` + regenerated `web/src/generated/*` | rebuild → regenerate → commit (new endpoint + DTOs). |
| `web/src/features/catalog/**/__tests__/*` | mini-graph dashed-merge + `DerivedDependenciesSection` units. |
| `docs/design/list-filter-registry.md` | record the section as a bounded embedded surface (all facets `none-needed`, justified). |

No migration, no new permission, no domain-aggregate change.

---

## 5. Contracts

```csharp
// Shared
public sealed record DerivationPathDto(
    Guid ApiId, string ApiName, Guid? ViaApplicationId, string? ViaApplicationDisplayName);

// B1 — graph
public sealed record DerivedEdgeDto(
    GraphEndpointDto Source, GraphEndpointDto Target, IReadOnlyList<DerivationPathDto> Paths);

public sealed record GraphResponse(
    IReadOnlyList<GraphNodeDto> Nodes,
    IReadOnlyList<GraphEdgeDto> Edges,
    IReadOnlyList<DerivedEdgeDto> DerivedEdges,   // NEW — empty when none
    bool Truncated);

// B2 — detail-page endpoint
public sealed record DerivedDependencyItem(
    Guid ServiceId, string DisplayName, Guid? TeamId, IReadOnlyList<DerivationPathDto> Paths);

[BoundedListResult] // a single service's derived dependency set is bounded/small — no pagination (ADR-0095 carve-out)
public sealed record DerivedDependenciesResponse(
    IReadOnlyList<DerivedDependencyItem> Dependencies,  // services THIS one depends on (source == focus)
    IReadOnlyList<DerivedDependencyItem> Dependents);   // services depending on THIS one (target == focus)
```

All DTOs `[ExcludeFromCodeCoverage]`. `DerivedEdgeDto`'s type is implicitly `depends-on` (only derived edge kind) → no `Type`/`Origin` field, keeping it distinct from the persisted `GraphEdgeDto`.

---

## 6. Frontend

**Explorer + mini-graph (React Flow):** derived edges render **dashed** with a distinct stroke color, `animated: false`, a legend entry ("Derived dependency"), and a tooltip/label listing provenance paths (`via {ApiName}` / `via {ApiName} · {AppName}`). Persisted edges are unchanged (solid). Derived edges are non-interactive for authoring.

**`DerivedDependenciesSection`** (Service detail only, below the mini-graph):

- Two `<Table>`s — **Dependencies** (outgoing) and **Dependents** (incoming). Each row: service name (→ `/catalog/services/:id`, `isRowHeader` per ADR-0084), a `Derived` badge, and a provenance cell (expander/tooltip listing each `via API · app`).
- **Read-only** — no add/delete (D7). Empty states ("No derived dependencies." / "Nothing derives a dependency on this service.").

**Surface proposal (ADR-0107 — bounded embedded panel, no `FilterBar`):**

| Column | Show | Sort | Filter |
|--------|------|------|--------|
| Service name (→ detail) | ✓ (rowHeader) | client default: name asc | none-needed (bounded) |
| Derived badge | ✓ | — | none-needed |
| Provenance (`via API [· app]`, multi-path) | ✓ | — | none-needed |

Deferral is explicit (bounded panel) — mirrored into `list-filter-registry.md`.

---

## 7. Error semantics

| Case | Status | Type |
|------|--------|------|
| `/graph` — unchanged (derived edges are additive; empty `DerivedEdges` when none) | — | — |
| `/derived-dependencies` `entityId` missing/malformed | 400 | `…/malformed-request` |
| `/derived-dependencies` focus kind ≠ service (query is service-only) | 400 | `…/validation-failed` |
| `/derived-dependencies` focus unknown / cross-tenant (RLS) | 422 | `…/invalid-entity` |
| Valid JWT lacking `catalog.read` | 403 | authz |

(Consistent with sub-slice-A `/api-surface`: single-entity focus must resolve → 422, not empty-200.)

---

## 8. Testing (gate-5 artifacts, per [TESTING-STRATEGY](../../TESTING-STRATEGY.md))

**8.1 Helper unit — `DerivedDependenciesTests.cs`** (mutation-gate target)
Direct-provide path (viaApp null); via-app path (viaApp populated); **both direct & via-app for same (S,T,X)** → one path deduped; multiple APIs / multiple apps → one edge, multiple paths (D3); **explicit-wins suppression** (D4); **no self-edge** (D5); non-service ids excluded (scope); empty inputs → empty; deterministic path ordering.

**8.2 Real-seam integration**
- **B1 `GetCatalogGraphTests`:** derived `S→T` appears in `DerivedEdges` with correct provenance (api + via-app names); **derived edge drives discovery** (T reachable only via derived dep is in `Nodes`); **explicit-wins** (persisted `depends-on S→T` present ⇒ no derived duplicate, persisted edge in `Edges`); **tenant isolation** (cross-tenant consume/provide never derives an edge); no self-edge.
- **B2 `GetDerivedDependenciesTests`:** dependencies happy (direct + via-app provenance); dependents happy (reverse direction); multi-path collapse; **400** non-service `entityKind`; **422** unknown/cross-tenant focus; **400** malformed; tenant isolation. (≥1 happy + ≥1 negative ✓.)

**8.3 Frontend units**
Explorer/mini-graph: derived edges render dashed + legend + provenance; persisted edges stay solid; empty `DerivedEdges` → no dashed edges. `DerivedDependenciesSection`: both tables render, provenance expander, empty states, `getAllByRole("rowheader").length > 0` per populated table, loading/error states.

**8.4 Manual / Playwright (ADR-0084)** — cold-start dev server + live API, authenticate, seed `S consumes X` + `T instance-of A provides X` topology, verify: derived dashed edge + tooltip in `/graph`; mini-graph dashed edge; `DerivedDependenciesSection` Dependencies/Dependents rows + provenance; explicit-wins (add explicit `depends-on`, derived duplicate disappears); dialog opens without blank-page (react-aria guard). Evidence under `verification/2026-07-09-catalog-derived-dependencies/`.

No Dockerfile/`COPY` change → container-build gate (4) runs unchanged.

---

## 9. Impact Analysis (codelens/LSP)

This slice **modifies existing C# symbols** (not new-code-only), so blast radius is enumerated below. Grounded via `Grep` + built-in `LSP` cross-check on 2026-07-09 (roslyn-codelens MCP not indexed for this repo this session; these are records/methods — grep-reliable, not `const`). **`writing-plans` re-confirms via `find_references`/`LSP` and maps every caller to a task.**

| Symbol | Change | References (blast radius) | Coverage |
|--------|--------|---------------------------|----------|
| `GraphResponse` (record ctor) | +`DerivedEdges` positional param | **1** production construction: `GraphTraversalHandler.cs:63`. `.Produces<GraphResponse>` (`CatalogModule.cs:163`) unaffected. **8** test deserialization sites (`GetCatalogGraphTests.cs` ×7, `CreateRelationshipTests.cs:237`) — by-name JSON, extra field harmless; add assertions. | Handler edit is a slice task; tests edited in §8.2. |
| `GraphTraversalEdge` (record ctor) | +`Provenance` nullable param **with default** | Construction: `GraphTraversalHandler.cs:28`, `GraphTraversalTests.cs:17`. Default `= null` ⇒ **no forced change** to existing callers. | No task needed for existing callers; handler sets it for derived edges. |
| `GraphTraversal.BuildAsync` | **signature unchanged** (derivation lives in the handler closure) | Callers: `GraphTraversalHandler.cs:19` + 6 test sites (`GraphTraversalTests.cs`). | Untouched — deliberate, to avoid caller churn. |
| `GraphTraversalHandler.Handle` | behavior only (returns populated `DerivedEdges`) | **1** caller: `CatalogEndpointDelegates.cs:893` (delegate). Signature stays. | Behavior covered by §8.2 B1 tests. |

New symbols (`DerivedDependencies`, `DerivedEdgeDto`, `DerivationPathDto`, B2 endpoint/handler/DTOs) have no existing consumers. No shared `const`/enum value is modified (`RelationshipType.DependsOn` etc. are **read**, not changed — grep confirms no consumer needs updating).

---

## 10. Definition of Done

The eight always-blocking gates + conditional mutation gate in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; this slice does not restate them.

- **Mutation gate (6):** the diff touches **Application logic** (`DerivedDependencies.Compute` — the core derivation) → **blocking** for this slice (not waivable by default): run `/misc:mutation-sentinel` → `/misc:test-generator` on the changed Application/Infrastructure files, target ≥80%, document survivors. (The pure helper is the highest-value mutation target of the whole slice.)
- Run `scripts/ci-local.sh` (Release mirror) green before push.
- Each sub-slice (B1, B2) maintains its own DoD ledger + `gate-findings.yaml`; both live under `docs/superpowers/verification/2026-07-09-catalog-derived-dependencies/` (sub-folders `b1/`, `b2/` or dated siblings — planner decides).

---

## 11. Follow-ups

| ID | Work item | Owning story / ADR |
|----|-----------|--------------------|
| (deferred) | Materialized derived edges (if on-read graph derivation shows latency at the node cap). | ADR-0111 (strategy left open) |
| (deferred) | Promote/pin a derived dependency to an explicit manual edge. | E-04.F-01.S-03 |
| (deferred) | Directed-edge filter / visual impact analysis over derived + persisted edges. | E-04.F-02.S-06 |
| (deferred) | Derived-dependency filter on the graph explorer (kind/team already exist; a "hide derived" toggle). | ADR-0040 / list-filter-registry |

On save: update `docs/product/CHECKLIST.md` E-02.F-03 (sub-slice B registered, B1/B2); add the section row to `list-filter-registry.md` at implementation.

---

## 12. Out of scope (explicit deferrals)

- Derived `depends-on` where either end is an Application or API (service↔service only, ADR-0111 §5).
- Materialization; promote/pin to manual; visual impact analysis; a "hide derived" graph filter (§11).
- Any edit/delete/authoring of derived edges (read-only; authoring stays on explicit edges).
- Changing the mini-graph's existing persisted-edge behavior beyond adding derived edges.

---

## 13. Self-review

**Spec coverage:** §2 rules (D1–D7) trace to §4 (helper + both handlers), §5 (contracts), §6 (FE), §8 (tests). Gate-5 real-seam artifacts named as deliverables in §8 (writing-plans emits one task each). Mutation gate is **blocking** and called out (§10) — the derivation helper is the target.

**Impact analysis (§9):** grounded in grep + LSP cross-check with concrete reference counts (not a grep guess); `GraphResponse` +param → exactly 1 production site + 8 by-name test sites; `GraphTraversalEdge` default-param → no forced churn; `BuildAsync` deliberately unchanged. Plan re-confirms via codelens/LSP.

**Type/contract check:** `DerivationPathDto` reused across graph (`DerivedEdgeDto`) and endpoint (`DerivedDependencyItem`); `GraphResponse.DerivedEdges` empty-when-none; `[BoundedListResult]` + `[ExcludeFromCodeCoverage]` noted (§5). Derivation asymmetry vs sub-slice A (this is the *inverse* of the provided surface) consistent §1/§2.

**Scope check:** two sub-slices, each ~350–500 LOC production business code — under the 800 ceiling; the S-03+FU-B split already happened; B1/B2 split here keeps each shippable.

**Ambiguity check:** derive-on-read vs materialize (D1), full-surface vs via-app-only (D2), one-edge-per-pair + provenance list (D3), explicit-wins (D4), no self-edge (D5), service-only scope (§2), dedicated bounded endpoint vs `/relationships`-mixing (§4.3), read-only derived section vs injecting into Outgoing/Incoming (§6), drive-discovery vs post-hoc (§4.2) — all resolved explicitly.

**No blocking issues found.**
