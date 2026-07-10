# Review fixes — graph impact analysis (E-04.F-02.S-06)

Branch: `feat/catalog-graph-impact-analysis`
Commit: `57b7d5bae97084852213ec174af2d39940444c5f`

## FIX 1 — Impact overlay must win over filters

**File:** `web/src/features/catalog/pages/GraphExplorerPage.tsx`

**What changed:** The `dimmed` memo no longer unions `applyGraphFilters(...)` with `impactDim(...)` when impact analysis is active. It now branches:

```tsx
const dimmed = useMemo(() => {
  if (impactActive && impactResult) {
    const impactIds = new Set(impactResult.nodes.map((n) => `${n.kind}:${n.id}`));
    return impactDim(merged, impactIds);
  }
  return applyGraphFilters(merged, filters, focusId);
}, [merged, filters, focusId, impactActive, impactResult]);
```

**Why:** The old union meant an impacted node sitting in a filtered-out team/kind still got dimmed, silently breaking the invariant "banner count == number of glowing nodes." Now, while impact is active, `impactDim` is the sole source of dim/lit truth for the impacted∪focus set — filters no longer participate. Filters resume normal dimming the moment impact is cleared.

## FIX 2 — Surface impact fetch error + loading

**File:** `web/src/features/catalog/pages/GraphExplorerPage.tsx` (no change needed to `api/impact.ts` — its `useQuery` wrapper already exposed `isError`/`isLoading`/`refetch`).

**What changed:** Added two new mutually-exclusive `<Panel position="top-right">` blocks alongside the existing success `ImpactBanner`, gated so exactly one renders at a time:

- `impactSubject != null && impact.isError` → error strip: "Couldn't run impact analysis." + "Try again" (`impact.refetch()`) + "Close" (`setImpactSubject(null)`), styled to match the file's existing `/graph` error pattern.
- `impactSubject != null && !impact.isError && impact.isLoading` → pending indicator: "Analysing impact…"
- `impactActive && impactResult && tierByNodeId` → unchanged success banner.

**Why:** `isError`/`isLoading` were previously read nowhere, so a failed or slow impact fetch left the UI silently stuck with no feedback.

## FIX 3 — Tier-3 glow ring must be distinct

**File:** `web/src/features/catalog/components/EntityGraphNode.tsx`

**What changed:** Added a third entry to `IMPACT_RING`:

```tsx
const IMPACT_RING: Record<number, string> = {
  1: "ring-2 ring-[color:var(--color-bg-error-solid)]",
  2: "ring-2 ring-[color:var(--color-bg-warning-solid)]",
  3: "ring-2 ring-[color:var(--color-bg-success-solid)]",
};
```

Tier ≥4 still falls through to the `ring-2 ring-[color:var(--color-bg-brand-solid)]` fallback.

**Theme var chosen for tier 3: `--color-bg-success-solid`** — confirmed present in both the light and dark blocks of `web/src/styles/theme.css`, and distinct from the error (tier 1), warning (tier 2), and brand (≥4 fallback) vars.

**Test added:** `web/src/features/catalog/components/__tests__/EntityGraphNode.test.tsx` — new test `"renders a tier-3 glow ring distinct from tier-2 and the ≥4 brand fallback"` asserts a node with `impactTier: 3` carries the success-ring class and NOT the warning or brand classes.

## FIX 4 — Page-level dimming/honesty regression test

**File:** `web/src/features/catalog/pages/__tests__/GraphExplorerPage.test.tsx`

**What was added:**
- `"impact overlay wins over filters..."` — mocks `useGraph` with a non-impacted node and `useImpactAnalysis` with impacted nodes; asserts the non-impacted node is dimmed while the impacted node is not, including under an active filter (regression for FIX 1).
- `"shows an error strip with Try again / Close..."` — asserts the error strip renders on `impact.isError`, that "Try again" calls `refetch()`, and "Close" clears `impactSubject` (strip disappears).
- `"shows a pending indicator while impact analysis is loading"` — asserts the "Analysing impact…" indicator renders on `impact.isLoading` without an error.

**Test-failure fix during verification:** the error-strip test's `screen.getByRole("button", { name: /^close$/i })` matched two buttons — the error strip's "Close" and the mocked `GraphExplorerSidebar` stub's "close" button (case-insensitive regex ambiguity) — causing a "multiple elements found" failure. Fixed by scoping the query with React Testing Library's `within()`:

```tsx
const errorStrip = screen.getByText(/couldn't run impact analysis/i).closest("div") as HTMLElement;
fireEvent.click(within(errorStrip).getByRole("button", { name: /try again/i }));
expect(refetch).toHaveBeenCalledOnce();
fireEvent.click(within(errorStrip).getByRole("button", { name: /^close$/i }));
expect(screen.queryByText(/couldn't run impact analysis/i)).toBeNull();
```

(`within` added to the `@testing-library/react` import.)

## FIX 5 — Reconcile stale design doc

**File:** `docs/superpowers/specs/2026-07-10-catalog-graph-impact-analysis-design.md`

**What changed:**
- **§5.3** (validation steps): rewritten to describe the actual two-stage validation mirroring `GetApiSurfaceAsync` — structural failure (`entityKind` not `Service`/`Application`, or empty `entityId`) → **400**; then RLS-scoped `lookup.Find` failure (unknown/cross-tenant) → **422**. Final step now says "Return the reused `GraphResponse` contract (tier rides in `GraphNodeDto.Depth`)" instead of the old bespoke-DTO language.
- **§5.4** (contracts): removed the bespoke `ImpactAnalysisResponse`/`ImpactNode` DTO description; replaced with a note that the handler reuses `GraphResponse`/`GraphNodeDto` from `Kartova.Catalog.Contracts`, tier travels in `Depth`, `OutDegree`/`InDegree` are hardcoded to 0 (node-expand affordance unused in the overlay), and the FE derives tier counts client-side from `Depth`.
- **§6** (GraphExplorerPage row): replaced the stale "union of dimmed sets" / "Close keeps merged nodes on canvas" text with a description matching FIX 1 + FIX 2 + the corrected Close behavior — overlay supersedes filters; error/loading/success are mutually exclusive in the same Panel slot; Close nulls `impactSubject`, and `merged` is then recomputed from the base graph alone, so impact-only nodes are removed from the canvas.
- **§6** ("Overlay/filter composition" note): replaced "dimmed set = impactDimmed ∪ filterDimmed... a filter may still dim an impacted node — acceptable" (now wrong post-FIX-1) with: dimmed set = `impactDimmed` alone while impact is active; focus/impacted nodes never dim regardless of filters; filters resume once impact is cleared.
- **§7** (list surface) and **§9** (negative tests): two additional stale references found during the verification re-read (not explicitly named in the original fix list, fixed for consistency) — §7's "bounded closure (`ImpactAnalysisResponse`)" → "bounded closure (the reused `GraphResponse` contract)"; §9's "unknown entityId → 422; entityKind=api → 422" → "entityKind=api or malformed/empty entityId → 400 (structural, mirrors `GetApiSurfaceAsync`); unknown entityId → 422; cross-tenant entityId → 422."

Confirmed via `CatalogEndpointDelegates.cs` (read-only, lines ~900–970): the endpoint's actual validation and docstring match this rewrite exactly — structural failure (`Api` kind, unparseable/empty `entityKind`, or empty `entityId`) → 400; RLS-scoped `lookup.Find` returning null → 422.

## Exclusions honored

No backend `.cs` file was touched. The handler's `.Contains(r.Type)` EF workaround and the arbitrary-value ring approach were left as-is.

## Verification

- `cd web && cmd //c "npm run test -- src/features/catalog"` → **397/397 tests passed across 56 test files** (after the `within()` fix described under FIX 4).
- `cd web && cmd //c "npm run build"` → **clean** (tsc -b + vite); only a pre-existing bundle chunk-size warning, unrelated to this change.
- All edited files verified LF-terminated (repo `.gitattributes eol=lf` normalizes at commit; `git diff --stat` line-change counts are consistent with content-only edits).

## Fixes that could not be completed

None — all five fixes were completed and verified.
