# Slice — Catalog: Unified sync/async API view per service (+ derived exposure)

**Date:** 2026-07-08
**Stories:** E-02.F-03.S-03 (Unified sync/async API view per service) — **sub-slice A** of the S-03 + FU-B decomposition
**Phase:** 1 — Core Catalog & Notifications
**Branch (proposed):** `feat/catalog-unified-api-view`
**Governing decisions:** [ADR-0111](../../architecture/decisions/ADR-0111-api-first-class-entity-provider-instance-fields.md) (API first-class; provider/instance/consumer are **edges**; exposure **derives** over edges — this slice realizes §Decision 3, the derived-exposure half), [ADR-0068](../../architecture/decisions/ADR-0068-fixed-relationship-type-vocabulary.md) (relationship vocabulary), [ADR-0095](../../architecture/decisions/ADR-0095-cursor-pagination-contract.md) (bounded-list carve-out), [ADR-0107](../../architecture/decisions/ADR-0107-list-filtering-consideration-and-filterbar-ui.md) (list-surface proposal)

---

## 1. Goal

Give a developer looking at a **Service** or **Application** a single consolidated panel answering *"which APIs does this component provide and consume, sync and async?"* — with per-API metadata (style, version, spec presence) that the generic relationships table (FU-A, shipped) does not carry.

The substantive part is **derived exposure**: in the all-edge model the canonical provider is often the *Application* (the contract owner), and a *Service* is `instance-of` that Application — so the service's real "provides" surface is **derived** (`instance-of ∘ provides-api-for`), not a direct edge. This slice computes that derivation **on read** and renders it, closing the checklist story E-02.F-03.S-03.

### 1.1 Decomposition context

Brainstorming (2026-07-08) expanded S-03 to fold in FU-B (derived exposure) and a second detail surface (Application), then split it — over the 800-LOC ceiling — into two sequential, independently shippable sub-slices:

| | Scope | Closes |
|---|---|---|
| **A** (this spec) | On-read derived `exposes` (Service) + read-model endpoint returning an entity's API surface + unified API panel on **Service and Application** detail pages | **E-02.F-03.S-03** |
| **B** (next) | Derived service↔service `depends-on` (`consumes ∘ exposes⁻¹`) surfaced in graph explorer + mini-graph + Dependencies/Dependents tables | FU-B remainder |

B is a dependency-graph concern, not the API panel; it is out of scope here (§8).

---

## 2. Pre-requisites (already on master)

- **API entity + edges live:** `Api` aggregate + `catalog_apis` (RLS); `Relationship` aggregate with `EntityRef{Kind,Id}`, `RelationshipType.{ProvidesApiFor, ConsumesApiFrom, InstanceOf}` all **creatable** (connectivity-edges slice #58), `EntityKind.{Application, Service, Api}`; `RelationshipOrigin`; `ICatalogEntityLookup`; graph traversal handler.
- **FU-A shipped (#59):** frontend knows the `api` kind + the three edge types; `RelationshipsSection` renders API edges generically and links to `/catalog/apis/:id`; `AddRelationshipDialog` has an Api entity picker.
- **Catalog module infra:** `CatalogModule : IModule, IModuleEndpoints`, `CatalogDbContext` (`DbSet<Relationship>`, `DbSet<Api>`), `EnlistInTenantScopeInterceptor`, direct-dispatch handler convention (ADR-0093), `ITenantScope` per-request txn (ADR-0090), `KartovaApiFixtureBase` (real JWT + Postgres/RLS), `IUserDirectory` enrichment pattern.
- **Contracts/coverage conventions:** every `*Response`/`*Request`/`*Dto` + `[BoundedListResult]` carry `[ExcludeFromCodeCoverage]` (ContractsCoverageRules arch test).

---

## 3. Design decisions (locked in brainstorming 2026-07-08)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **Derive on read** — compute the traversal per request in the read handler. **No** materialized derived-edge table. | No staleness, no new infra, no perf pressure yet. Materialization deferred until a real need (ADR-0111 leaves derivation strategy open). |
| 2 | **One endpoint serves both pages:** `GET /api/v1/catalog/api-surface?entityKind={service\|application}&entityId={guid}`. | One read model, one handler; the derivation asymmetry (below) is internal. Avoids two near-identical nested routes. |
| 3 | Returns a **`[BoundedListResult]` flat DTO** (`{ Provides[], Consumes[] }`), **not** `CursorPage<T>`. | A single component's API surface is bounded and small; ADR-0095's bounded-list carve-out applies. Inline justification on the type. No sort/filter/cursor. |
| 4 | **Reads reuse `catalog.read`.** No new permission, no 5-sync. | Pure read over already-readable entities. |
| 5 | **Derivation asymmetry:** derived `exposes` applies **only to a Service's Provides list** (via its `instance-of` Applications). Application Provides/Consumes and Service Consumes are always **direct**. | An Application is the derivation *root* — no `instance-of` originates from an app. There is no "derived consumes" concept. |
| 6 | **Provides list = direct `provides-api-for` ∪ derived `exposes`**, deduped by Api id; **direct wins** when an Api is both. Each derived item carries `via` = the Application it was exposed through. | An Api directly provided by the service is "more owned" than one merely exposed via its app; show one row, labeled `direct`. |
| 7 | **Consumes list = direct `consumes-api-from`** edges where the focus entity is the source. | Consumption is a direct declaration; no derivation. |
| 8 | Per-Api metadata (`style`, `version`, `hasSpec`, `displayName`) resolved by **batch-loading `Api` rows** for the collected ids — not carried on the relationship edge. | `RelationshipResponse` only exposes `{kind,id,displayName}`; the panel needs style/version/spec. One batched `WHERE id IN (...)` over `db.Apis`. |
| 9 | **"Unified sync/async" = one panel, style badge conveys sync vs async** (`Rest`/`Grpc`/`GraphQL` = sync; `AsyncApi` = async). No separate sync-only / async-only sections. | YAGNI — a `Style` column already distinguishes them; the value is *one* consolidated view, not two. |
| 10 | Panel placed **above** `RelationshipsSection` on both detail pages. | APIs are the higher-value lens for this feature; generic edges stay below. |
| 11 | 422 `invalid-entity` when the focus entity is unknown/cross-tenant; 400 on undefined `entityKind`; `Api` as `entityKind` is **rejected** (400) — an API has no API surface. | Consistent with existing 422-on-unknown-entity; the surface question is only meaningful for Service/Application. |

---

## 4. Architecture

### 4.1 Endpoint

```
GET /api/v1/catalog/api-surface?entityKind={service|application}&entityId={guid}
    (tenant-scoped, NEW; catalog.read; ApiSurfaceResponse — bounded flat)
```

### 4.2 Read flow (on-read derivation)

```
Client → JWT auth → tenant-claims transform
  → TenantScopeBeginMiddleware (BEGIN TX, SET LOCAL app.current_tenant_id)
  → endpoint binding (GET /api-surface)
  → GetApiSurfaceDelegate
      ├ claim catalog.read → 403
      ├ validate entityKind ∈ {service, application} → 400 if not / if api
      → GetApiSurfaceHandler.Handle(entityKind, entityId)
          ├ ICatalogEntityLookup.Find(kind,id) → 422 invalid-entity if absent (RLS ⇒ cross-tenant absent)
          ├ directProvides = Relationships[type=ProvidesApiFor, source=(kind,id)] → target api ids (origin=direct)
          ├ IF service:  instanceApps = Relationships[type=InstanceOf, source=(service,id)] → app ids
          │              derivedExposes = Relationships[type=ProvidesApiFor, source ∈ (application, instanceApps)]
          │                               → target api ids (origin=derived, via=that app)
          ├ consumes = Relationships[type=ConsumesApiFrom, source=(kind,id)] → target api ids (origin=direct)
          ├ apiIds = distinct(directProvides ∪ derivedExposes ∪ consumes)
          ├ apis = db.Apis WHERE id IN apiIds   (batch; style/version/hasSpec/displayName)
          ├ provides = merge(directProvides, derivedExposes) dedupe-by-apiId, DIRECT WINS
          ├ appNames = displayName for the `via` app ids (from lookup / apps batch)
          └ project → ApiSurfaceResponse { Provides[], Consumes[] } (each item joined to api metadata)
      → Results.Ok(ApiSurfaceResponse)
  → TenantScopeCommitEndpointFilter (COMMIT TX)
```

All relationship/api reads run under the request `ITenantScope` — RLS guarantees cross-tenant edges/APIs never appear (tested).

### 4.3 New files

| File | Purpose |
|------|---------|
| `Kartova.Catalog.Application/GetApiSurfaceQuery.cs` | Query record `(EntityKind Kind, Guid EntityId)`. |
| `Kartova.Catalog.Application/ApiSurfaceResponseExtensions.cs` | Pure mapper: (edges + api rows + app names) → `ApiSurfaceResponse`; dedupe + direct-wins + origin/`via` assembly. Unit-testable. |
| `Kartova.Catalog.Contracts/ApiSurfaceResponse.cs` | `[BoundedListResult]` `[ExcludeFromCodeCoverage]` DTO. |
| `Kartova.Catalog.Contracts/ApiSurfaceItem.cs` | `[ExcludeFromCodeCoverage]` DTO. |
| `Kartova.Catalog.Contracts/ApiSurfaceOrigin.cs` | Enum `{ Direct, Derived }` (camelCase over wire, ADR-0109). |
| `Kartova.Catalog.Infrastructure/GetApiSurfaceHandler.cs` | Direct-dispatch handler; the edge queries + batch Api load; delegates shaping to the mapper. |
| `Kartova.Catalog.Tests/ApiSurfaceMapperTests.cs` | Unit: dedupe/direct-wins/origin/`via`/empty. |
| `Kartova.Catalog.IntegrationTests/GetApiSurfaceTests.cs` | Real-seam (§7). |

### 4.4 Edited files

| File | Change |
|------|--------|
| `Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs` | add `GetApiSurfaceAsync` delegate (validate `entityKind`, dispatch query). |
| `Kartova.Catalog.Infrastructure/CatalogModule.cs` | map `GET /api-surface`; register `GetApiSurfaceHandler` (`AddScoped`). |
| `web/src/features/catalog/api/apis.ts` (or new `apiSurface.ts`) | `useApiSurface(entityKind, id)` hook (openapi-fetch, flat array). |
| `web/src/features/catalog/components/ApiSurfaceSection.tsx` | **new** panel component (two tables). |
| `web/src/features/catalog/pages/ServiceDetailPage.tsx` | mount `<ApiSurfaceSection entityKind="service" .../>` above `RelationshipsSection`. |
| `web/src/features/catalog/pages/ApplicationDetailPage.tsx` | mount `<ApiSurfaceSection entityKind="application" .../>` above `RelationshipsSection`. |
| `web/openapi-snapshot.json` + regenerated `web/src/generated/*` | rebuild API image → predev/prebuild regenerates → commit (new endpoint + DTOs). |
| `web/src/features/catalog/components/__tests__/ApiSurfaceSection.test.tsx` | **new** FE unit. |
| `docs/design/list-filter-registry.md` | record the panel as a bounded embedded surface (all sort/filter facets `none-needed`, with justification). |

No new permission, no migration, no domain aggregate change.

---

## 5. Contracts

```csharp
public enum ApiSurfaceOrigin { Direct, Derived }   // camelCase over wire (ADR-0109)

public sealed record ApiSurfaceItem(
    Guid ApiId,
    string DisplayName,
    ApiStyle Style,
    string Version,
    bool HasSpec,
    ApiSurfaceOrigin Origin,
    Guid? ViaApplicationId,           // set iff Origin == Derived
    string? ViaApplicationDisplayName // set iff Origin == Derived
);

[BoundedListResult] // per-entity API surface is bounded (a component's direct+derived APIs); small N, no pagination — ADR-0095 carve-out
public sealed record ApiSurfaceResponse(
    IReadOnlyList<ApiSurfaceItem> Provides,
    IReadOnlyList<ApiSurfaceItem> Consumes
);
```

Both DTOs `[ExcludeFromCodeCoverage]`. `ApiSurfaceItem` for the Consumes list always has `Origin = Direct` and null `Via*`.

---

## 6. Frontend — unified API panel

`ApiSurfaceSection({ entityKind, entityId })`:

- Fetches `useApiSurface(entityKind, entityId)` (flat `{ provides, consumes }`).
- Renders **two `<Table>`s**: **Provides** and **Consumes**. Each row uses the identifying Name column as `isRowHeader` (react-aria requirement — assert `getAllByRole("rowheader")` in tests).
- Sort: **client-side**, sync styles before async, then `displayName` asc (default, non-configurable).
- Empty states per table ("No APIs provided." / "No APIs consumed.").

**Surface proposal (ADR-0107 — bounded embedded panel, no `FilterBar`):**

| Column | Show | Sort | Filter |
|--------|------|------|--------|
| Name (→ `/catalog/apis/:id`) | ✓ (rowHeader) | client default (sync-then-async, name asc) | none-needed (bounded panel) |
| Style badge (Rest / gRPC / GraphQL / AsyncAPI) | ✓ | — | none-needed |
| Version | ✓ | — | none-needed |
| Spec (has-spec badge) | ✓ | — | none-needed |
| Origin (`Direct`, or `Derived · via {App}` link → `/catalog/applications/:id`) — **Provides only** | ✓ | — | none-needed |

Filter/sort deferral is explicit (bounded panel, small N) — mirrored into `list-filter-registry.md`.

---

## 7. Error semantics (reuse existing handlers)

| Case | Status | Type |
|------|--------|------|
| `entityKind` missing/undefined, or `= api` | 400 | `…/validation-failed` (or `…/malformed-request` for missing) |
| Focus entity unknown / cross-tenant (RLS) | 422 | `…/invalid-entity` |
| Missing `entityId` / malformed query | 400 | `…/malformed-request` |
| Valid JWT lacking `catalog.read` | 403 | authz |

---

## 8. Testing (gate-5 artifacts, per [TESTING-STRATEGY](../../TESTING-STRATEGY.md))

**8.1 Mapper unit — `ApiSurfaceMapperTests.cs`**
- Direct-only provides; derived-only provides (origin + `via` populated); **both direct & derived for same Api → one row, `Direct`**; consumes mapping; empty surface → empty lists; multiple `instance-of` apps exposing overlapping APIs → deduped.

**8.2 Real-seam integration — `GetApiSurfaceTests.cs`** (HTTP + real JWT + Postgres/RLS via `KartovaApiFixtureBase`)
- **Service happy path:** service with 1 direct `provides-api-for` + `instance-of` App whose 2 `provides-api-for` APIs appear as **derived** (origin=`derived`, correct `via` App id/name); 1 `consumes-api-from`. Response `Provides` deduped, `Consumes` correct; each item's `style`/`version`/`hasSpec` match the `Api` rows.
- **Direct-wins:** service directly provides an Api its app also provides → single `Direct` row.
- **Application happy path:** direct provides + consumes; **no derivation** (assert no `derived` origin ever appears for an application focus).
- **Tenant isolation (negative):** edges/APIs in another tenant never appear.
- **422** unknown/cross-tenant focus entity; **400** `entityKind=api`; **400** malformed query. (≥1 happy + ≥1 negative ✓.)

**8.3 Frontend — `ApiSurfaceSection.test.tsx`**
- Both tables render; derived row shows `Derived · via {App}` link; empty states; `getAllByRole("rowheader").length > 0` for each populated table; loading skeleton; error state.

No Dockerfile/`COPY` change → container-build gate (4) runs unchanged (no new regression target expected).

---

## 9. Impact Analysis (codelens/LSP)

**N/A for signature changes — new read-side code only.** This slice adds a read endpoint, query, handler, mapper, and DTOs; it does **not** modify any existing C# symbol's signature or behavior. It **reads** (unchanged) shared symbols:

- `RelationshipType.{ProvidesApiFor, ConsumesApiFrom, InstanceOf}`, `EntityKind.{Application, Service}`, `RelationshipOrigin` — enum-value reads (codelens under-reports const/enum refs → **grep** at plan time to confirm no consumer needs updating).
- `CatalogDbContext.{Relationships, Apis}`, `EfApiConfiguration.IdFieldName`, `ApiStyle`, `ICatalogEntityLookup.Find` — consumed as-is; `find_references` (methods/types) at plan time to confirm the query shape matches existing handler usage.

No caller/consumer of an existing symbol changes. `writing-plans` keeps this heading with the `N/A — new read-side code; no existing symbol modified` line plus the grep/codelens confirmations above.

---

## 10. Definition of Done

The eight always-blocking gates + conditional mutation gate in **CLAUDE.md → Working agreements → Definition of Done** apply verbatim; this slice does not restate them. **Mutation gate (6):** the diff touches Application/Infrastructure derivation logic (normally blocking), but is **owner-waived for this slice** (recorded 2026-07-08) → logged as a **waiver, not green**, in the ledger. Run `scripts/ci-local.sh` (Release mirror) green before push. DoD ledger + `gate-findings.yaml` under `docs/superpowers/verification/2026-07-08-catalog-unified-api-view/`.

---

## 11. Follow-ups

| ID | Work item | Owning story / ADR |
|----|-----------|--------------------|
| **Sub-slice B** | Derived service↔service `depends-on` (`consumes ∘ exposes⁻¹`) surfaced in graph explorer + mini-graph + Dependencies/Dependents tables. | E-02.F-03 / ADR-0111 §Decision 5 |
| (deferred) | Materialized derived edges (if on-read becomes a perf issue). | ADR-0111 (strategy left open) |
| (deferred) | Per-service exposure opt-out (worker/partial deployments). | ADR-0111 §Decision 3 / FU-10 |

On save: update `docs/product/CHECKLIST.md` E-02.F-03 (S-03 sub-slice A in progress; sub-slice B registered); add the panel row to `list-filter-registry.md`.

---

## 12. Out of scope (explicit deferrals)

- Derived service↔service `depends-on` in graph → sub-slice B.
- Materialized derivation, per-service exposure opt-out → deferred.
- Spec rendering (OpenAPI/proto/AsyncAPI) → E-11; version history → E-21; search indexing → E-05.
- Any edit/delete of edges from this panel (read-only; authoring stays in `RelationshipsSection`/`AddRelationshipDialog`).

---

## 13. Self-review

**Spec coverage:** §3 decisions trace to §4 (files/flow), §5 (contracts), §6 (panel), §8 (tests). Gate-5 real-seam artifacts named as deliverables in §8 (writing-plans emits one task each). Mutation-gate waiver called out (§10). Impact analysis is grep/codelens-grounded and correctly `N/A` for signature changes (§9).

**Type/contract check:** `ApiSurfaceOrigin {Direct, Derived}`, `ApiSurfaceItem`, `ApiSurfaceResponse` consistent across §4.3/§5/§6. Derivation asymmetry (service-provides only) consistent §1/§3 #5/§8. `[BoundedListResult]` + `[ExcludeFromCodeCoverage]` noted (§5).

**Scope check:** ~8 new + ~9 edited files; ~350–500 LOC production business code — under the 800 ceiling; no further decomposition (the S-03+FU-B split already happened).

**Ambiguity check:** derive-on-read vs materialize (§3 #1), bounded flat vs cursor (§3 #3), one endpoint vs two (§3 #2), direct-wins dedupe (§3 #6), derivation asymmetry (§3 #5), `entityKind=api` rejected (§3 #11), unified = style-badge-not-sections (§3 #9) — all resolved explicitly.

**No blocking issues found.**
