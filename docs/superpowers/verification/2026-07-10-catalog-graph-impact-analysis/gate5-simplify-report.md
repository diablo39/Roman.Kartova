# Gate 5 simplify — graph impact analysis slice (E-04.F-02.S-06)

Scope: production files only, per task list. Preserve all behavior; no public-contract changes.

## Reviewed, no changes

- `src/Modules/Catalog/Kartova.Catalog.Application/ImpactAnalysis.cs` — pure BFS over a
  `dependentsOf` adjacency map, first-seen-tier cycle guard, cap/truncation check inline. Already
  minimal and well-documented; no redundant branches or unnecessary allocations found.
- `src/Modules/Catalog/Kartova.Catalog.Application/GetImpactAnalysisQuery.cs` — one-line record,
  nothing to simplify.
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetImpactAnalysisHandler.cs` — the
  `.Contains(RelationshipType.DependsOn)` predicate is a deliberate EF-Core global-query-filter
  workaround (documented in the comment) — left untouched per instructions. The rest (edge
  unification, closure computation, node/edge projection) is already linear and non-duplicative;
  no further reuse or reduction opportunities within scope.
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs`
  (`GetImpactAnalysisAsync`) — validation → lookup → handler call, matches the shape of sibling
  delegates in the file (e.g. `GetApiSurfaceAsync`). No simplification found.
- `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogModule.cs` (new route + DI lines) —
  consistent with neighboring route registrations; nothing to change.
- `web/src/features/catalog/api/impact.ts` — small `useQuery` wrapper matching the project's
  other `use*` API hooks. No changes.
- `web/src/features/catalog/components/ImpactBanner.tsx` — straightforward presentational
  component, props destructured once, no duplication. No changes.
- `web/src/features/catalog/components/GraphExplorerSidebar.tsx` (impact-button lines) — matches
  the existing button styling/pattern used by sibling buttons in the same component. No changes.
- `web/src/features/catalog/pages/GraphExplorerPage.tsx` (impact wiring) — hooks, `useMemo`
  dependencies, and dim-set union logic are already tight; the hardcoded `nodeCap={200}` passed
  to `ImpactBanner` duplicates `GetImpactAnalysisHandler.DefaultNodeCap` conceptually, but
  `GraphResponse` carries no node-cap field to source it from — plumbing that through would be a
  contract change, out of scope for a quality-only pass. Left as-is.
- `web/src/features/catalog/relationships/impactModel.ts` — four small pure functions
  (`buildTierMap`, `impactDim`, `tierCounts`, `impactTotal`), each single-purpose, no duplicated
  logic across them. No changes.
- `web/src/features/catalog/relationships/graphModel.ts` / `graphLayout.ts` (new `impactTier`
  lines) — a type field and a single pass-through assignment respectively. No changes.

## Changed

- `web/src/features/catalog/components/EntityGraphNode.tsx` — removed the redundant `> 0` in the
  impact-ring guard (prior review nit).

  Before:
  ```ts
  const impact =
    data.impactTier && data.impactTier > 0
      ? (IMPACT_RING[data.impactTier] ?? "ring-2 ring-[color:var(--color-bg-brand-solid)]")
      : "";
  ```

  After:
  ```ts
  const impact = data.impactTier
    ? IMPACT_RING[data.impactTier] ?? "ring-2 ring-[color:var(--color-bg-brand-solid)]"
    : "";
  ```

  Rationale: `data.impactTier` is `number | undefined` and tiers are non-negative (0 = focus,
  excluded from the ring; 1+ = impacted). `data.impactTier &&` already treats `0` and `undefined`
  as falsy, so any value that passes the `&&` is a truthy number, which for a non-negative tier is
  always `> 0`. The `> 0` re-check added nothing — confirmed safe to drop since it can't change
  which branch is taken for any value `impactTier` can hold (`undefined`, `0`, or a positive
  integer).

## Backend workaround preserved as instructed

`GetImpactAnalysisHandler.Handle` keeps the array `.Contains(r.Type)` predicate (not `==`) per the
comment explaining the global-query-filter `WHERE FALSE` trap — not touched.

## Test / build results

- No `.cs` files changed → backend re-test skipped per instructions (no `dotnet test` /
  build-warning re-check needed).
- Frontend:
  - `cd web && npm run test -- src/features/catalog` → **56 test files / 393 tests passed**.
  - `cd web && npx tsc -b --noEmit` → clean, no output/errors.

## Commit

`refactor(catalog): simplify graph impact analysis slice (gate 5)` — see git log for SHA.
