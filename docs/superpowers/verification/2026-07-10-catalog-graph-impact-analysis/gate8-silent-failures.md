# Gate 8 — Silent Failure Audit: Graph Impact Analysis (E-04.F-02.S-06)

Scope: branch diff `review-b4636df..9dc769b`. Files audited: `CatalogEndpointDelegates.cs` (GetImpactAnalysisAsync), `GetImpactAnalysisHandler.cs`, `ImpactAnalysis.cs`, `GetImpactAnalysisQuery.cs`, `web/src/features/catalog/api/impact.ts`, `GraphExplorerPage.tsx`, `GraphExplorerSidebar.tsx`, `ImpactBanner.tsx`, `impactModel.ts`. Comparator files for precedent: `GraphTraversalHandler.cs`, `GetDerivedDependenciesHandler.cs`, `ListRelationshipsForEntityHandler.cs`.

Grep sweep for `catch`/`try` across all impact-analysis files: **zero matches**. No empty-catch or blanket-catch blocks exist anywhere in this slice.

---

## Finding 1 — HIGH: Impact-analysis fetch failure/loading state is invisible to the user

**Location:** `web/src/features/catalog/pages/GraphExplorerPage.tsx:47-49` and `:153-163`

**Issue:** `useImpactAnalysis(impactSubject)` (`web/src/features/catalog/api/impact.ts`) correctly throws on `error` and surfaces it via React Query's `isError`/`error` state — the fetch layer itself is clean. But `GraphExplorerPage.tsx` only destructures `impact.data` (line 48: `const impactResult = impact.data ?? null;`). `impact.isError`, `impact.error`, and `impact.isLoading`/`isFetching` are never read anywhere in the file. The `<ImpactBanner>` is gated on `impactActive && impactResult && tierByNodeId` (line 153) — while loading or on error, this condition is false and nothing renders in its place: no spinner, no error message, no retry, no console/log call.

**Hidden errors:** Any failure of the `/api/v1/catalog/impact` call — 400 (should be unreachable via UI, but still), 422 unknown/cross-tenant entity, 500, network failure, timeout — is swallowed at the UI layer. The button (`onImpactAnalysis` in `GraphExplorerSidebar.tsx:79-87`) has no `disabled`/loading binding either, so nothing on screen changes between a click that fails and a click that hasn't finished.

**User impact:** Clicking "Impact analysis" and hitting a transient 500 or network blip looks identical to a no-op click. The user has no way to know their action registered at all, let alone that it failed, and there's no retry affordance. This is a direct regression against the codebase's own established convention: the same file shows `isError` → error message + retry for the main graph query (`GraphExplorerPage.tsx:106-110`) and a warning banner for partial expand failures (`:116-118`); the sibling `GraphExplorerSidebar.tsx:62-63` does the identical `active.isError` → "Couldn't load details." for its own entity-detail query. Omitting the same pattern for impact analysis is an inconsistency, not an intentional one-off — nothing in the diff/plan documents this omission as a deliberate scope cut.

**Recommendation:** In `GraphExplorerPage.tsx`, destructure `isError`/`error`/`isFetching` from `impact` and render feedback near the impact banner slot: a loading indicator while `impactSubject != null && impact.isFetching`, and an error message + retry/dismiss when `impact.isError` (mirroring the `active.isError` pattern in `GraphExplorerSidebar.tsx:62-63` or the `isError` pattern at lines 106-110 of the same file). Also bind the "Impact analysis" button's disabled/loading state to `impact.isFetching` so a second click doesn't fire a duplicate request while one is in flight.

**Example:**
```tsx
const impact = useImpactAnalysis(impactSubject);
const impactResult = impact.data ?? null;
const impactActive = impactSubject != null && impactResult != null;
...
{impactSubject && impact.isFetching && (
  <Panel position="top-right"><Spinner label="Computing impact…" /></Panel>
)}
{impactSubject && impact.isError && (
  <Panel position="top-right">
    <div className="flex items-center gap-2 rounded-md bg-primary/90 px-3 py-2 text-sm ring-1 ring-secondary">
      <span className="text-error-primary">Couldn&apos;t compute impact analysis.</span>
      <button type="button" className="text-brand-primary underline" onClick={() => impact.refetch()}>Try again</button>
      <button type="button" className="text-tertiary" onClick={() => setImpactSubject(null)}>Dismiss</button>
    </div>
  </Panel>
)}
```

---

## Reviewed, not a defect — backend `info?.DisplayName ?? string.Empty` fallback

**Location:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/GetImpactAnalysisHandler.cs:54-57`

```csharp
var info = await lookup.Find(nodeRef.Kind, nodeRef.Id, ct);
nodes.Add(new GraphNodeDto(
    nodeRef.Kind, nodeRef.Id, info?.DisplayName ?? string.Empty, t, info?.TeamId,
    OutDegree: 0, InDegree: 0));
```

**Judgment: acceptable, not a silent failure.** This is not a case of "any `??` is suspicious" — it's the same pattern used deliberately and explicitly elsewhere in this module:
- `GraphTraversalHandler.cs:64-68` carries an explicit code comment documenting this exact fallback as a "conscious deferral... consistent with `ListRelationshipsForEntityHandler`'s per-ref enrichment pattern," and applies the identical `found?.DisplayName ?? string.Empty` at line 92.
- `GetDerivedDependenciesHandler.cs` documents the companion invariant (derived-edge ids are RLS-scoped by construction, so `Find` resolving is the expected case; the fallback exists for the boundary/orphan case, not as a masking device).

The scenario this fallback covers is a relationship row (explicit or derived) whose endpoint entity was hard-deleted after the relationship was written, and hasn't been cleaned up yet ("boundary node") — not a cross-tenant leak (relationships and the entity lookup are both RLS-scoped, so cross-tenant is structurally excluded) and not an unknown-entity request path (that's already rejected with 422 by the endpoint's `lookup.Find` check on the *focus* entity before this handler runs — `CatalogEndpointDelegates.cs`). Silently defaulting a stale-reference display name to empty string, while still returning `TeamId: null` and rendering the node in the graph rather than dropping it or 500ing the whole request, is the same trade-off already accepted module-wide. Flagging this here without flagging the identical, already-shipped pattern in `GraphTraversalHandler.cs` would be inconsistent; the pattern itself is a reviewed, intentional design decision, not new technical debt introduced by this slice.

No corresponding logging call exists for the "lookup returned null" branch in *any* of the three handlers (`GraphTraversalHandler`, `GetDerivedDependenciesHandler`, `GetImpactAnalysisHandler`) — if this is a real concern, it's a pre-existing module-wide gap, not specific to this slice, and out of scope for this diff-scoped audit.

---

## Backend endpoint validation (`GetImpactAnalysisAsync`) — clean

**Location:** `src/Modules/Catalog/Kartova.Catalog.Infrastructure/CatalogEndpointDelegates.cs:926-966`

400 (bad/missing kind, `Api` kind rejected, empty id) is checked before the 422 existence check; no try/catch; no swallowed exceptions; `lookup.Find` returning null on the focus entity is correctly mapped to 422 rather than falling through to the handler. No issues.

---

## Summary

| Severity | Count |
|---|---|
| CRITICAL | 0 |
| HIGH | 1 |
| MEDIUM | 0 |
| LOW | 0 |

1 real finding (HIGH) + 1 reviewed-and-cleared item (backend `??` fallback, judged acceptable/consistent with existing precedent) + 2 clean surfaces (backend validation, pure BFS/model files — no error-handling logic present).
