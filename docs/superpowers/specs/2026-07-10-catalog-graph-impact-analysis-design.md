# Slice — Catalog: Visual impact analysis on the standalone graph

**Date:** 2026-07-10
**Story / work item:** E-04.F-02.S-06 (last open story in E-04.F-02 Relationship Visualization)
**Phase:** 1 — Core Catalog & Notifications
**Branch:** `feat/catalog-graph-impact-analysis`
**Governing decisions:** [ADR-0040](../../architecture/decisions/ADR-0040-two-view-dependency-graph-navigation.md) (graph visualization), [ADR-0111 revised](../../architecture/decisions/ADR-0111-api-first-class-entity-provider-instance-fields.md) §5 (derived service↔service depends-on), [ADR-0084](../../architecture/decisions/ADR-0084-playwright-mcp-for-frontend-development.md) (browser verification), [ADR-0090](../../architecture/decisions/ADR-0090-tenant-scope-mechanism.md) (tenant scope), [ADR-0095](../../architecture/decisions/ADR-0095-cursor-pagination-contract.md) (list contract — see §7 N/A)

---

## 1. Goal

Deliver the last open story in E-04.F-02: run **impact analysis** ("blast radius") from a node on the standalone `/graph` explorer. From a selected Service or Application, the explorer computes everything that transitively **depends on it**, merges any missing impacted nodes onto the canvas, dims the rest, glows the impacted set by tier (hop distance), and shows a count banner — with a Close button that restores the normal view.

**Acceptance criteria (phase-1-core-catalog.md E-04.F-02.S-06):**
> "Impact Analysis" button on side panel; dims graph except affected downstream path; affected nodes glow by tier; banner: "12 downstream (3× tier-1, 5× tier-2, 4× tier-3)"; "Close Analysis" returns to normal.

## 2. Locked decisions (brainstorming 2026-07-10)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | **Dedicated backend impact endpoint** (not client-side over the loaded graph) | The banner must be trustworthy: a client-side count reflects only loaded/expanded nodes and silently undercounts. The backend returns the true full closure. |
| 2 | Blast radius = transitive **incoming** `depends-on`, **explicit ∪ derived** (B1/B2); tier = hop distance | Derived service↔service depends-on already encodes API provide/consume coupling (ADR-0111 §5), so API consumers are captured with no special-casing; matches the dashed edges already on the graph. |
| 3 | **Overlay-with-merge** | Acceptance wording ("dims graph except affected downstream path… Close returns to normal") is an overlay, not a separate screen. To keep banner count == glowing set, missing impacted nodes are merged onto the canvas (reuses existing expand/merge + dagre relayout). |
| 4 | Full transitive closure, **node-cap 200** + `truncated` flag; depth **not** user-chosen | Impact = everything downstream. Cap protects against explosion; mirrors `/graph` + `/derived-dependencies`. |
| 5 | **Service + Application** subjects only; **Api-as-subject deferred** | Derived depends-on is service↔service (Api nodes collapsed), so incoming depends-on on an Api yields nothing; a provider-service already captures downstream API-consumers transitively. Api-subject needs a different relation (`consumes-api-from`) → follow-up FU-I1. |
| 6 | **No new permission** (no 5-sync) | Read over the catalog graph; reuses existing catalog-read authorization, same as `/graph` and `/derived-dependencies`. |

## 3. Pre-requisites (already on master)

- `GraphTraversal` primitive + `GraphTraversalHandler` (BFS with `direction`, depth annotation) — PR #64/#65 era.
- **Derived depends-on (B1/B2):** pure `DerivedDependencies.Compute` + `DerivedEdgeLoader.LoadAsync` (tenant-wide RLS-scoped derived service→service edge set, explicit-wins applied) + `DerivedProvenanceNames` + `GetDerivedDependenciesHandler` (bounded `entityId`-only endpoint pattern) — PRs #65/#66.
- `ICatalogEntityLookup.Find(kind, id, ct)` → displayName/teamId, in-tenant enrichment (per-id, bounded).
- FE explorer: `GraphExplorerPage`, `GraphExplorerSidebar` (action group), `EntityGraphNode`, `graphMerge.ts` (merge), `graphLayout.ts` (dagre), `graphFilter.ts` (pure dim pass → `dimmedNodeIds`/`dimmedEdgeIds`), `GraphFilterControls` (`<Panel>` overlay pattern), `useExplorerState`/`useGraphFilters` (sessionStorage-keyed, render-time reconcile).
- FU-A shipped (PR #59): Api nodes render/navigate; `ENTITY_KIND_LABEL.api`, `RelationshipKind` incl. `api`.

## 4. Nature: full-stack (backend-weighted)

Backend adds a pure traversal helper + thin handler + endpoint (mirrors B1/B2). Frontend adds an overlay mode on the existing explorer. Real HTTP/auth/DB seam is touched (new endpoint) → gate-3 real-seam integration + gate-4 container build apply. Mutation gate (6) is **blocking** — the diff adds Application-layer traversal logic.

## 5. Backend design

### 5.1 Pure helper — `ImpactAnalysis.Compute`
`Kartova.Catalog.Application/ImpactAnalysis.cs`:

```
public static class ImpactAnalysis
{
    public sealed record Node(EntityRef Ref, int Tier);
    public sealed record Result(IReadOnlyList<Node> Impacted, bool Truncated);

    // dependsOnEdges: directed (Source depends on Target). Blast radius of `focus` = the
    // transitive set of Sources reachable by following edges BACKWARDS from focus
    // (target ∈ frontier ⇒ source is a dependent at tier+1). Focus itself is tier-0 and
    // excluded from Impacted. First-seen tier wins (cycle-safe). Stops at nodeCap → Truncated.
    public static Result Compute(EntityRef focus, IReadOnlyCollection<(EntityRef Source, EntityRef Target)> dependsOnEdges, int nodeCap);
}
```

- BFS: `seen = {focus: 0}`, frontier = `[focus]`. Each level: sources of edges whose target ∈ frontier and not yet seen → next frontier at `tier+1`. Append to result until `seen.Count - 1 == nodeCap` (focus excluded) → set `Truncated = true`, stop.
- Pure, deterministic, no I/O → unit-tested to high mutation coverage (like `DerivedDependencies.Compute`).
- **Not** reusing `GraphTraversal.BuildAsync`: its final edge inclusion is undirected-among-kept-nodes (`GraphTraversal.cs` §"directed-edge filter … deferred to S-06") and its `Func<frontier,…>` delegate suits incremental explorer loading, not a one-shot fully-loaded closure. A synchronous pure BFS over the full edge set is simpler and directed-correct.

### 5.2 Edge set — explicit ∪ derived depends-on
In the handler, build `IReadOnlyCollection<(EntityRef Source, EntityRef Target)>`:
- **Explicit:** `db.Relationships.Where(r => r.Type == DependsOn)` → `(Source, Target)` refs (any app/service pair; RLS-scoped).
- **Derived:** `DerivedEdgeLoader.LoadAsync(db, ct)` → service→service edges; wrap ids as `EntityRef(Service, id)`.
- Union, dedupe by `(Source, Target)`. (Explicit service→service deps appear in both; dedupe collapses them. Derived-edge set already applied explicit-wins internally — the union is a superset that is harmless for reachability.)

### 5.3 Handler — `GetImpactAnalysisHandler`
`Kartova.Catalog.Infrastructure/GetImpactAnalysisHandler.cs` (mirrors `GetDerivedDependenciesHandler`):
1. Validate the request shape at the endpoint delegate (mirrors `GetApiSurfaceAsync`): `entityKind` must parse to `Service` or `Application` (an `Api` kind or unparseable/empty value is a structural error) and `entityId` must be non-empty → else **400**. Then resolve focus via `lookup.Find` (RLS-scoped) → unknown or cross-tenant service/application → else **422**.
2. Build edge set (§5.2).
3. `ImpactAnalysis.Compute(focus, edges, nodeCap: 200)`.
4. Enrich each impacted `Ref` → displayName/teamId via `lookup.Find` (per-id, bounded by cap).
5. Return the reused `GraphResponse` contract (tier rides in `GraphNodeDto.Depth`).

### 5.4 Query / Contract
- `Kartova.Catalog.Application/GetImpactAnalysisQuery.cs`: `public sealed record GetImpactAnalysisQuery(EntityKind FocusKind, Guid FocusId);`
- No bespoke response contract — the handler returns the existing `GraphResponse` (`Kartova.Catalog.Contracts`), reusing `GraphNodeDto` for impacted nodes. Tier travels in `GraphNodeDto.Depth`; `OutDegree`/`InDegree` are hardcoded to 0 (node-expand affordance is unused in the impact overlay). The FE derives per-tier counts client-side from `Depth`.

### 5.5 Endpoint
`CatalogEndpointDelegates` + `CatalogModule`: `GET /catalog/impact?entityKind={service|application}&entityId={guid}` (param names consistent with `/catalog/relationships` and `/catalog/graph`). Tenant scope + catalog-read auth (existing). Additive — no signature change to existing delegates.

## 6. Frontend design

| File | Change |
|------|--------|
| `api/impact.ts` (new) | `useImpactAnalysis(subject: {kind: RelationshipKind; id: string} \| null)` — React-Query, `enabled: subject != null`; `GET /catalog/impact?entityKind&entityId`; returns `{ nodes, truncated }`. |
| `relationships/impactModel.ts` (new, **pure**) | `computeImpactOverlay(response, graph, focusId)` → `{ nodesToMerge: GraphNodeData[]; dimmedNodeIds: Set<string>; dimmedEdgeIds: Set<string>; tierByNodeId: Map<string, number> }`. Impacted-set ∪ focus stays lit; everything else dims (reuses the `graphFilter` dim shape: edge dims iff either endpoint dims). `nodesToMerge` = impacted nodes absent from `graph`. |
| `pages/GraphExplorerPage.tsx` | `impactSubject` state (`{kind,id} \| null`); on set → `useImpactAnalysis` fetch → merge impacted nodes via existing `graphMerge` + re-run dagre → apply overlay (dim + tier). While impact is active, the overlay **supersedes** kind/team filters for dim/lit (see below). Surfaces `impact.isError` (error strip + "Try again"/"Close") and `impact.isLoading` (pending indicator) in the same `<Panel position="top-right">` slot as the success banner — mutually exclusive. Clearing (`Close Analysis`) → `impactSubject = null`; `merged` is then recomputed from the base graph results alone, so impact-only nodes are removed from the canvas (consistent with "Close returns to normal"). |
| `components/EntityGraphNode.tsx` | Accept `tier?: number`; render a tier-keyed glow ring (token-based ramp, tiers 1–3 distinct, ≥4 shares the deepest ring); dim reuses the existing dimmed styling. |
| `components/ImpactBanner.tsx` (new) | Canvas `<Panel>` (mirrors `GraphFilterControls`): "N downstream (a× tier-1, b× tier-2, …)"; `truncated` → "showing first 200"; **Close Analysis** button. Per-tier counts grouped from `tierByNodeId`. |
| `components/GraphExplorerSidebar.tsx` | "Impact analysis" button in the `mt-auto` action group, rendered only when `selected.kind ∈ {"service","application"}`; `onClick` sets `impactSubject`. |
| `api/impact.ts` types / codegen | Endpoint returns a non-list DTO; if the generated client covers it, use it; otherwise a raw-fetch data layer (as used by the API-spec UI slice). Confirm at plan time after regenerating the client. |

**Overlay/filter composition:** while impact is active, dimmed set = `impactDimmed` **alone** — the impact overlay supersedes kind/team filters for the impacted set. Focus and impacted nodes never dim while impact is active, regardless of active filters; this keeps the invariant "banner count == number of glowing nodes" honest. Filters resume dimming once impact is cleared.

## 7. List surface (ADR-0095 / ADR-0107)

**N/A** — `/catalog/impact` is a graph/traversal query, not a paginated list endpoint; it returns a bounded closure (the reused `GraphResponse` contract), not `CursorPage<T>`. No list screen, no sort/filter surface, no `list-filter-registry.md` change. (Same shape as `/catalog/graph` and `/catalog/derived-dependencies`, which are also non-list.)

## 8. Impact Analysis (codelens)

**Mostly new code.** No existing C# symbol's signature or behavior changes. Consumed existing symbols (unchanged, additive callers only):
- `DerivedEdgeLoader.LoadAsync` — new caller (`GetImpactAnalysisHandler`); signature unchanged.
- `ICatalogEntityLookup.Find` — new caller; signature unchanged.
- `CatalogEndpointDelegates` / `CatalogModule` — additive endpoint registration.

Both consumed symbols are methods (not `const`), so `roslyn-codelens` is reliable. **Plan gate:** confirm with `find_references DerivedEdgeLoader.LoadAsync` and `find_references ICatalogEntityLookup.Find` that this slice only *adds* a caller and does not need to change either; confirm no existing caller of the touched endpoint files breaks. New `ImpactAnalysis.Compute` / `GetImpactAnalysisHandler` / `GetImpactAnalysisQuery` are new symbols (no existing blast radius).

## 9. Testing strategy (per docs/TESTING-STRATEGY.md)

**Backend unit** (`ImpactAnalysisTests`, pure): single-tier; multi-tier chain; diamond (no double-count, min-tier wins); cycle (no infinite loop); explicit-only; derived-only; explicit ∪ derived union; node-cap boundary → `Truncated`; empty (leaf focus) → empty result.

**Real-seam integration** (`GetImpactAnalysisTests`, `KartovaApiFixtureBase`, real Postgres/RLS + real JWT):
- Happy: multi-tier downstream including at least one **derived** service↔service edge; assert node set, per-node tier, count.
- Negative: `entityKind=api` or malformed/empty `entityId` → 400 (structural, mirrors `GetApiSurfaceAsync`); unknown `entityId` → 422; cross-tenant `entityId` → 422 (RLS + validation).

**Mutation gate (6): BLOCKING** — diff adds Application-layer logic. Run Stryker on `ImpactAnalysis.Compute` (+ handler), target ≥80%; document survivors.

**Frontend** (vitest + RTL): `impactModel` (merge set / dim sets / `tierByNodeId`); `GraphExplorerSidebar` button visibility (service/app yes, api no); `ImpactBanner` per-tier counts + truncated copy; `EntityGraphNode` tier glow.

**Playwright (ADR-0084, cold-start first):** focus a service with ≥2 tiers of dependents → click "Impact analysis" → impacted nodes glow by tier, non-impacted dim, banner count == number glowing; **Close Analysis** restores normal view. Verify no blank-page (no react-aria table here, but confirm the overlay renders in a real browser).

**Gate 10 → E2E follow-up (no-folding):** convert the verified flow into a nightly `e2e/` regression spec — expected follow-up, tracked, not part of this slice.

## 10. Definition of Done

The ten always-blocking gates + conditional mutation gate in **CLAUDE.md → Definition of Done** apply verbatim. Gate 6 (mutation) is **blocking** here (Application logic). Gate 4 (container build) applies (new endpoint, no Dockerfile change expected → job runs unaffected). Run `scripts/ci-local.sh` green before push (stop the dev server first — npm ci EPERM-vs-5173 lock). DoD ledger + `gate-findings.yaml` under `docs/superpowers/verification/2026-07-10-catalog-graph-impact-analysis/`.

## 11. Out of scope (explicit deferrals)

| Item | Owning follow-up |
|------|------------------|
| **Api-as-subject** impact (first hop via `consumes-api-from`, then depends-on) | **FU-I1** |
| **Affected-path edge glow** (highlighting the edges along the blast-radius path, beyond node glow) | **FU-I2** — node glow + dim satisfies the acceptance criteria |
| **Upstream / dependency-direction** analysis ("what does X depend on") | none — criteria are downstream-only; dependency direction already visible via expand |
| **Focus-scoped derived-edge loader** (perf) | inherited from B2 §11 — optimize only if latency shows |
| Nightly E2E regression spec for the flow | gate-10 follow-up (§9) |

## 12. Self-review

- No placeholders/TBD; every §5–§6 file maps to a concrete change.
- Consistent: §2 decisions ↔ §5 backend ↔ §6 frontend ↔ §9 tests (semantics = explicit∪derived incoming depends-on; overlay-with-merge; service/app subjects; cap/truncated all covered end-to-end).
- Scope: single slice, ~360 prod LOC (backend ~160 / FE ~200), under the 400 target — no decomposition.
- Ambiguity resolved: banner count is the backend total (== merged glowing set); Api subjects deferred, not silently unsupported; overlay/filter composition is union-of-dimmed.
