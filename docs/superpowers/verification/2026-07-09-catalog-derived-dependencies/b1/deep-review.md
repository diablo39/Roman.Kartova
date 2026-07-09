# Deep PR review — E-02.F-03 sub-slice B1 (derived service→service depends-on in `/catalog/graph` + explorer)

**Reviewer:** deep-review (Opus 4.8, cross-referenced against spec + plan + ADR-0111 §5)
**Range:** `1e705d9..1740d52`
**Date:** 2026-07-09

### Overview
B1 surfaces *derived* service→service `depends-on` edges — where service S consumes an API in service T's provided surface (T provides directly, or T is `instance-of` an application that provides it) — through the existing `GET /catalog/graph` traversal and renders them as dashed, provenance-labeled edges in the standalone graph explorer. The implementation faithfully realizes all seven locked semantics (D1–D7) and ADR-0111 §Decision 5: a pure `DerivedDependencies.Compute` helper does the derivation, `GraphTraversalHandler` precomputes the tenant's edge set once and threads derived edges through the frozen `GraphTraversal.BuildAsync` via a nullable `Provenance` marker, and the frontend folds them into `mergeGraphs`/`layoutGraph`. **Merge-ready** — no blocking findings; correctness of the derived×direction and derived×node-cap interactions holds by construction through the shared, already-tested BFS logic.

### Blocking
None.

### Should-fix
- `web/src/features/catalog/relationships/graphMerge.ts:74` — `ExplorerEdge.provenance` (the per-path `{apiName, viaAppName}` list) is populated on every derived edge but is **never consumed in the B1 render path**: `graphLayout.ts` threads only `label` onto the React Flow edge, and `GraphExplorerPage.tsx` passes no provenance to edge/node data. The via-app provenance detail is therefore invisible in the explorer — only the compact `label` (which carries the API name, satisfying "provenance-labeled") is shown. Spec §6 mentions a tooltip/expander, but that reads as a B2 (`DerivedDependenciesSection`) concern. Currently the field exists only to feed a unit test. Either surface it (edge hover/tooltip) in B1 or explicitly defer to B2 and note it; leaving populated-but-unconsumed model data is a mild smell.

### Nits
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GraphTraversalHandler.cs:99` — `SyntheticEdgeId` XOR-mixes the two endpoint GUIDs. A cross-pair collision would silently drop a derived edge in `BuildAsync`'s `keptEdges` dict. Negligible with v4 GUIDs, and this is the pre-accepted single-call-site shortcut; direction-safety (S→T ≠ T→S) is preserved by the rotate-by-7. No action needed beyond the existing in-code note.
- `web/src/features/catalog/pages/GraphExplorerPage.tsx:115` — the legend `Panel` renders unconditionally, even when the graph has zero derived edges. Cosmetic.
- `web/src/features/catalog/relationships/graphMerge.ts:64` — the multi-path `"… +N"` label picks the first API name by server-side `(apiId, viaAppId)` order (i.e. by GUID), not by name or salience. Deterministic but arbitrary-looking. Cosmetic.

### Missing tests
- **Derived edge discovery under directional focus** (e.g. `focus=T` provider, `direction=incoming` → must discover consumer S via the derived edge): not integration-tested. This is the one behavior unique to derived edges not already exercised by a persisted-edge test. Correct-by-construction (same `BuildAsync` neighbour + both-endpoints-kept logic; verified by trace), so **not merge-blocking** — but a single cheap incoming-direction case would close the last derived-specific gap. Agree with the prior test-analyzer note.
- **Derived×node-cap** (derived provider capped out → derived edge dropped by the both-endpoints-kept rule): not integration-tested. Correct-by-construction. **Not blocking.**
- **Derived unit — non-service endpoints ignored**: `DerivedDependencies.Compute` is only fed service/app tuples (the handler pre-filters by kind), and the helper is kind-agnostic, so there is no helper-level test that a non-service consumer/provider is excluded — that filtering lives in `ComputeDerivedEdges` and is only covered indirectly. Minor; **not blocking**.

### What looks good
- `src/Modules/Catalog/Kartova.Catalog.Application/DerivedDependencies.cs:16` — clean pure helper: builds `providersByApi` (direct ∪ `instance-of ⋈ app-provides`, D2), collapses to one edge per ordered pair with a deduped, deterministically-ordered path list (D3), suppresses self-edges (D5) and explicit pairs (D4). Backed by thorough unit tests incl. dedup, ordering, and duplicate-raw-tuple cases (`DerivedDependenciesTests.cs:122,131`).
- `src/Modules/Catalog/Kartova.Catalog.Application/GraphTraversal.cs:8` — `BuildAsync` genuinely stays frozen: derived edges ride through the nullable `Provenance` default-null param (zero churn to the 6 `GraphTraversalTests` sites + persisted-edge ctors), then get re-split by `Provenance is not null` at `GraphTraversalHandler.cs:87,93`. The accepted shortcut is honestly documented in-code (`GraphTraversalHandler.cs:45`).
- `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs:308` — the tenant-isolation test is a *real* cross-tenant oracle: both tenants seed a **complete** derived topology, then asserts tenant A sees exactly its own one derived edge and that no tenant-B id leaks into nodes or derived edges. Proves RLS scoping of the tenant-wide precompute, not a trivial empty result.
- `web/openapi-snapshot.json` — wire contract fully regenerated and plumbed: `GraphResponse.derivedEdges` is `required`, `DerivedEdgeDto`/`DerivationPathDto` present with correct nullable via-app fields (`["null","string"]`), so `r.derivedEdges` typechecks against the generated `components["schemas"]` (`web/src/features/catalog/api/graph.ts:9`).
- `src/Modules/Catalog/Kartova.Catalog.IntegrationTests/GetCatalogGraphTests.cs:281` — explicit-wins (D4) verified end-to-end at the real seam: persisted `depends-on` present in `Edges`, derived duplicate absent from `DerivedEdges`.
