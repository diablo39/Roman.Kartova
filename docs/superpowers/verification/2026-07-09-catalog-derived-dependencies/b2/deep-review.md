# Deep PR Review — Derived service↔service `depends-on` (sub-slice B2)

**Reviewer:** deep-review (gate 9) · **Date:** 2026-07-09 · **Range:** `f5d2c54..8939a6c` (11 commits)
**Status:** OPEN (pre-merge gate) · **Branch:** `feat/catalog-derived-dependencies-b2`
**Read against:** spec `2026-07-09-catalog-derived-service-dependencies-design.md`, plan `2026-07-09-catalog-derived-dependencies-b2.md`, ADR-0111 §5 / ADR-0095 / ADR-0084 / ADR-0109 / ADR-0090 / ADR-0068, `CLAUDE.md §Definition of Done`, ledger `b2/dod.md`.

### Overview

B2 ships a bounded, read-only `GET /api/v1/catalog/derived-dependencies?entityId=` endpoint (service-only) that computes a focus service's derived `depends-on` relationships on read, split into Dependencies (focus is source) and Dependents (focus is target) with per-API/via-app provenance. It extracts B1's in-handler edge-fetch and name-join into shared `DerivedEdgeLoader` + `DerivedProvenanceNames` (behavior-preserving), adds a read-only `DerivedDependenciesSection` on the service detail page, and merges derived edges as dashed edges into the per-service mini-graph via `toGraphModel`. Real-seam coverage is 7/7; web suite green.

### Blocking-class issues

None.

### Should-fix issues

- **Mini-graph silently swallows a derived-dependencies fetch error.**
  - **Evidence:** `web/src/features/catalog/components/DependencyMiniGraph.tsx:37,60` — only `list.isError` is rendered; `derivedQuery.isError` is never inspected. On a 500 from `/derived-dependencies`, `derivedQuery.data` is `undefined` → `derived` is `undefined` → the graph renders persisted edges only, with no signal that derived data failed.
  - **Impact:** the primary visual under-reports coupling on partial failure and is visually identical to "this service has no derived dependencies." Partially mitigated: the sibling `DerivedDependenciesSection` directly below does surface the error (`DerivedDependenciesSection.tsx:15`), and the mini-graph is service-gated off elsewhere — so the page as a whole is not fully blind, but the graph in isolation is.
  - **Fix:** when `entityKind === "service" && derivedQuery.isError`, append an inline note to the existing legend (e.g. "derived dependencies unavailable") or fall through to the shared error copy; add a `DependencyMiniGraph.test.tsx` case mocking `useDerivedDependencies` → `{ isError: true }` asserting the note renders and persisted edges still show.

### Nits

- **Derived-edge label format diverges between the two surfaces.** `graphMerge.ts:64` labels explorer derived edges `depends on · via {api}`, while the mini-graph labels the same edge class `via {api}` (`DependencyMiniGraph.tsx:24` → `derivedViaLabel`). Same semantic edge, two strings. Pick one prefix for consistency.
- **Empty-state race for derived-only services.** `DependencyMiniGraph.tsx:62` decides "No dependencies yet." on `model.edges.length === 0` without waiting on `derivedQuery.isLoading`. A service whose only dependencies are derived can flash the empty copy before the derived query resolves. Gate the empty branch on `derivedQuery.isLoading` for services.
- **`TeamId` carried but never rendered on this surface.** `DerivedDependencyItem.cs:11` (`Guid? TeamId`) is populated by `GetDerivedDependenciesHandler.cs:46` but the section table shows only name + Derived badge + provenance (`DerivedDependenciesSection.tsx`). Dead field for B2 (kept for graph parity); note it or drop it. (sources: deep-review, gate-7 ledger)
- **Endpoint OpenAPI omits the 403 annotation.** `CatalogModule.cs:172-177` declares 200/400/422 but not 403, though `RequireAuthorization(CatalogRead)` yields 403. Consistent with the `/api-surface` sibling (line 166-171), so not a regression, but the documented surface is incomplete.
- **Undocumented unreachable fallback asymmetry.** `GetDerivedDependenciesHandler.cs:45` (`info?.DisplayName ?? string.Empty`) is an unreachable defensive fallback (the "other" service id always resolves in-tenant), yet — unlike the identical class of fallback in `DerivedProvenanceNames.cs:46-49` which carries a detailed hazard comment — it has no explanatory note. Mirror the comment or leave a one-liner.

### Missing tests

- **Explicit-wins is only asserted from the source side.** `GetDerivedDependenciesTests.cs:137-155` (`Explicit_depends_on_suppresses_the_derived_pair`) asserts only `S.Dependencies.Count == 0`. Add an assertion (same test or a sibling) that focusing the provider `T` after the explicit `depends-on S→T` yields `T.Dependents.Count == 0` — proving suppression removes the ordered pair on both projection sides.
- **No end-to-end multi-path collapse (D3) at the endpoint.** Spec §8.2 lists "multi-path collapse"; the B2 integration suite has no case where two distinct linking APIs between the same `(S,T)` collapse to one `DerivedDependencyItem` with two `Paths`. It is covered at the `DerivedDependencies.Compute` unit tier (B1) and the mini-graph label tier, but not through `/derived-dependencies`. Add a `GetDerivedDependenciesTests` case seeding two APIs both provided by `T` and consumed by `S`, asserting `Dependencies.Single().Paths.Count == 2`.
- **`DerivedDependenciesSection` loading state untested.** `DerivedDependenciesSection.test.tsx` covers render/empty/error but not `query.isLoading` → `DerivedDependenciesSkeleton` (`DerivedDependenciesSection.tsx:14`). Add a case mocking `{ isLoading: true }` and asserting the skeleton renders.

### What looks good

- **Single-source-of-truth extraction.** `DerivedEdgeLoader.cs` + `DerivedProvenanceNames.cs` pull the edge-fetch and batched name-join out of `GraphTraversalHandler` so both the graph handler and the new endpoint derive identically; the refactor is behavior-preserving (B1 graph tests remain green, ledger gate 3).
- **Shared `derivedViaLabel` with a real regression test.** `graphModel.ts:136` dedupes provenance by distinct API name, fixing the latent "via X +1" over-count when a service is reachable through the same API twice; guarded by `DependencyMiniGraph.test.tsx:106` and `graphModel.test.ts:102`.
- **Strong real-seam coverage.** `GetDerivedDependenciesTests.cs` exercises via-app + direct provenance, reverse-direction dependents, explicit-wins, unknown→422, empty-id→400, and cross-tenant→422 against real JWT + Postgres/RLS (ADR-0090).
- **ADR-0084 compliance is real, not assumed.** `DerivedDependenciesSection.tsx:35` marks the `service` column `isRowHeader`, and `DerivedDependenciesSection.test.tsx:55` asserts `getAllByRole("rowheader").length > 0` on a populated table.
- **Service-only scope enforced and tested.** The mini-graph gates the derived fetch `enabled: entityKind === "service"` (`DependencyMiniGraph.tsx:37`) with an explicit assertion (`DependencyMiniGraph.test.tsx:126`), and the endpoint resolves any unknown/non-service/cross-tenant id to 422 — faithful to ADR-0111 §5.
