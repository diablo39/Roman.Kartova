# Gate 8 code review — E-04.F-02.S-06 graph impact analysis

Reviewed: branch diff `review-b4636df..9dc769b.diff` (12 commits, full diff read end-to-end), plus direct reads of the final on-disk state of `GetImpactAnalysisHandler.cs`, `EntityGraphNode.tsx`, `graphMerge.ts`, and the design doc `docs/superpowers/specs/2026-07-10-catalog-graph-impact-analysis-design.md`.

## Summary

- Critical (90-100): 0
- Important (80-89): 1
- Guideline checks (a)-(e): all respected, no violations

## Important (80-89)

### 1. Tier-glow ring only distinguishes 2 tiers; design doc and acceptance criteria commit to 3

**Confidence: 83**

**File:** `web/src/features/catalog/components/EntityGraphNode.tsx:27-33`

```tsx
const IMPACT_RING: Record<number, string> = {
  1: "ring-2 ring-[color:var(--color-bg-error-solid)]",
  2: "ring-2 ring-[color:var(--color-bg-warning-solid)]",
};
const impact = data.impactTier
  ? IMPACT_RING[data.impactTier] ?? "ring-2 ring-[color:var(--color-bg-brand-solid)]"
  : "";
```

The design doc for this exact slice (`docs/superpowers/specs/2026-07-10-catalog-graph-impact-analysis-design.md`, §6) specifies:

> "render a tier-keyed glow ring (token-based ramp, **tiers 1–3 distinct**, ≥4 shares the deepest ring)"

and the acceptance criteria's own worked example treats tier-3 as an ordinary, expected case: `"12 downstream (3× tier-1, 5× tier-2, 4× tier-3)"`.

As shipped, `IMPACT_RING` only maps tiers 1 and 2 to distinct colors. Tier 3 falls through to the same fallback (`brand-solid`) that is supposed to be reserved for tier ≥4 — so tiers 3 and 4+ are visually indistinguishable, contradicting the locked design decision. The new `EntityGraphNode.test.tsx` tests only assert tier-1 and tier-2 ring classes; there is no test covering tier-3, so the gap isn't caught by the test suite either — the tests validate the as-shipped (2-tier) behavior rather than the as-designed (3-tier) behavior.

**Fix:** add a third distinct ring color for tier 3 (e.g. a token-based value distinct from both `error-solid`/`warning-solid` and the `brand-solid` fallback — check `theme.css` for an available third semantic ring token, or extend the arbitrary-value pattern already used here for a third color), and add a test asserting tier-3 renders a ring class distinct from both tier-2 and the ≥4 fallback. Alternatively, if 2 distinct tiers is now an accepted simplification, update the design doc §6 and the acceptance-criteria example to match — but that requires explicit confirmation, not a silent implementation-side descoping.

## Guideline checks (no violations found)

- **(a) `/catalog/impact` treated as graph/aggregate query, not a list:** Confirmed. `CatalogModule.cs` registers it as a bounded traversal endpoint returning `GraphResponse` (no `CursorPage<T>`, no `sortBy`/`sortOrder`/`cursor`/`limit`); `openapi-snapshot.json` shows no pagination params on the new path; frontend calls it via a one-shot `useImpactAnalysis` React Query hook, not `useCursorList`. Design doc §7 explicitly states N/A with the same rationale (same shape as `/graph` and `/derived-dependencies`).
- **(b) No new DTO lacking `[ExcludeFromCodeCoverage]`:** Confirmed. The slice reuses the existing `GraphResponse`/`GraphNodeDto`/`GraphEdgeDto`/`DerivedEdgeDto` contracts verbatim (tier smuggled into `GraphNodeDto.Depth`); no new Contracts-layer type is added. `ImpactAnalysis.Node`/`Result` in `Kartova.Catalog.Application` are plain internal records, not `*Dto`/`*Request`/`*Response` types in the Contracts project, so the arch-test-enforced coverage-exclusion rule doesn't apply to them — and they're directly covered by `ImpactAnalysisTests.cs`.
- **(c) No new/missed `KartovaPermission`:** Confirmed. `CatalogModule.cs` wires `.RequireAuthorization(KartovaPermissions.CatalogRead)`, matching the sibling `/graph`/`/derived-dependencies` endpoints. None of the 5-sync touchpoints (`KartovaPermissions.cs`, `KartovaRolePermissions.cs`, `permissions.snapshot.json`, `permissions.ts`, `usePermissions.test.tsx`) appear in the diff — correctly, since no new permission was needed.
- **(d) react-aria `<Table>` `isRowHeader`:** N/A — no new `<Table>` component in this diff.
- **(e) Windows/LF line endings:** Not independently verifiable from diff text (no stray `\r`/`^M` artifacts visible in the read); `.gitattributes eol=lf` normalizes on commit regardless.

## Not flagged (excluded per task instructions)

- `GetImpactAnalysisHandler.cs:27-30` — the `dependsOnOnly.Contains(r.Type)` EF workaround (deliberate, documented, mirrors `DerivedEdgeLoader.RelevantTypes`/`GetApiSurfaceHandler`).
- `EntityGraphNode.tsx:24-32` — `ring-[color:var(--color-bg-*-solid)]` arbitrary Tailwind values (token utilities don't exist for these colors, per in-code comment).

## Investigated, not reportable (sub-80 confidence)

- Possible duplicate `lookup.Find` call: the endpoint delegate calls `lookup.Find` once for the 422 existence check, and `GetImpactAnalysisHandler.Handle` calls it again per node in its enrichment loop (including the focus node at tier 0). This mirrors the established `GraphTraversalHandler` pattern (endpoint validates, handler independently enriches all closure nodes including focus) — not a novel defect introduced by this slice.
- Hardcoded `nodeCap={200}` literal in `GraphExplorerPage.tsx`'s `<ImpactBanner>` JSX, duplicating `GetImpactAnalysisHandler.DefaultNodeCap` rather than deriving it from the response. Minor DRY nit, no CLAUDE.md rule elevates it, and it mirrors how the `/graph` page already handles its own soft cap — left as a nit, not reported.
- Impact-overlay merge correctness: verified via direct read of `graphMerge.ts` that `mergeGraphs` is first-wins on node id, and the page always spreads `results` (primary graph) before `impactResult` — so real graph node data (outDegree/inDegree/depth) is never overwritten by the impact endpoint's enrichment data. Not a bug.
