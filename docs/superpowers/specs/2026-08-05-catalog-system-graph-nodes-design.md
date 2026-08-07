# Design — System nodes in the graph (FU-A)

**Story:** E-03.F-03.S-01 closeout — the story's third acceptance criterion is "system has description **and diagram**" (`docs/product/phases/phase-1-core-catalog.md:86`). Description shipped 2026-07-22 with the System UI surface; the diagram is the only reason the story is still `[~]`. Registered as **FU-A** in `2026-07-22-catalog-system-ui-surface-design.md:29` and re-deferred by `2026-07-30-catalog-system-membership-assignment-design.md:112`.
**Date:** 2026-08-05 · **Author:** Roman Głogowski (AI-assisted)
**ADRs touched:** none new. Works inside ADR-0111 (`PartOf` visible on the generic relationships/graph read paths), ADR-0040 (canvas-overlay graph filters), ADR-0094 (Untitled UI / react-aria), ADR-0114 (tabbed detail layout), ADR-0084 (browser verification).

---

## 1. Goal

Two things, one slice:

1. **`system` becomes a first-class graph kind on the frontend.** Today a System node reaches the canvas through a cast and its "Set as focus" produces a broken explorer.
2. **The System detail page gets a diagram** of its members and how they relate — the artefact the acceptance criterion names.

## 2. Starting state (verified 2026-08-05, not assumed)

**Backend needs no change.** `GetCatalogGraphAsync` (`CatalogEndpointDelegates.cs:1376-1404`) parses `entityKind` as any `EntityKind` enum member, so `system` already passes; `depth` accepts 1–4. `GraphTraversal.BuildAsync` is kind-agnostic, `CatalogEntityLookup` resolves `EntityKind.System` (`CatalogEntityLookup.cs:23`), and node degrees are counted by id in one batch (`GraphTraversalHandler.cs:81-86`). `/graph` has returned `system` nodes at runtime since S-01 introduced `PartOf` edges.

**The frontend is what is broken.** `RelationshipKind` (`application|service|api`) serves two unrelated purposes: the rules for *creating* an edge, and the type of a node *rendered* in the graph. The second use is wrong for `system`:

| Symptom | Location |
|---|---|
| `system` enters the explorer graph through a cast, with a comment asking for exactly this slice | `graphMerge.ts:44-50` |
| `isRelationshipKind("system")` is false → `parseEntityRef` returns `null` → **`/graph?focus=system:<id>` silently degrades to `{kind:"application", id:""}`** | `relationshipTypeRules.ts:36`, `GraphExplorerPage.tsx:31-37` |
| Kind filter offers Application/Service/API only — a System node cannot be filtered | `GraphFilterControls.tsx:6-10` |
| System detail has `Overview · Members` and no diagram | `SystemDetailPage.tsx:54-86` |

Already fixed by A1 and **not** part of this slice: `ENTITY_KIND_LABEL` and `ENTITY_PATH_SEGMENT` know `system`, so a System node already renders the label "System" and "Open page ↗" already routes to `/catalog/systems/:id`.

## 3. Locked decisions

| # | Decision | Why |
|---|---|---|
| 1 | Deliver **both** halves (kind first-classing **and** the diagram) | The diagram invites the user to "open the full graph"; shipping it over a broken focus token would put a known bug on the path the new feature advertises |
| 2 | Diagram shows the **container plus its boundary**: System, members, edges *between* members, and — on demand — the members' external neighbours | A star of `PartOf` edges carries no more information than the Members table already does; the internal structure is the reason to draw a picture |
| 3 | **Default `depth=1`**, external neighbours behind an *Include external dependencies* toggle (`depth=2`) | A 15-member system at `depth=2` is ~60 nodes. `depth=1` is already the complete internal structure, because the traversal re-scans for every edge between kept nodes (`GraphTraversal.cs:66-71`) |
| 4 | Boundary drawn as a **labelled background band** behind the member nodes, computed from their post-layout bounding box | Keeps the existing dagre layout. The alternatives were rejected: xyflow parent/child nodes need a hand-rolled nested layout (dagre cannot nest) and bring relative-coordinate handling; styling-plus-legend alone reads like the existing explorer |
| 5 | Diagram sits at the **top of the existing Members tab**, table underneath. Tabs stay `Overview · Members` | 1:1 with the established pattern on component pages: `ApplicationDetailPage.tsx:148-163` puts the mini-graph and the relationship tables in one tab |
| 6 | `partOf` edges are **not rendered**, but are kept in the dagre input | The band states membership; drawing it again is the noise decision 3 exists to avoid. They stay in the layout graph so members keep their rank next to the System node. One flag to reverse if gate 9 shows it reads as missing |
| ~~7~~ | ~~External neighbours get **no extra styling** — falling outside the band is the signal~~ | ~~One mechanism instead of two~~ — **AMENDED 2026-08-06, see §3.1: the premise was disproved by gate 9** |
| 7a | Non-members carry an explicit **"outside this system"** treatment — dashed border, muted label, legend row — set per node, not inferred from position | Membership must be readable wherever dagre places a node. Deliberately **not** `layoutGraph`'s existing `dimmed` (`opacity-30`): that channel already means "does not match your filter" in the explorer, and 30% opacity would hide the very nodes the toggle was switched on to reveal |
| 4a | The band's extent covers **the members only** — the focus System node is excluded from the bounding box | Amends decision 4's extent. The System occupies its own dagre rank to the right of every member, so including it stretched the band across empty canvas (measured: band 350–1190 for content ending at 887). Accepted trade-off: the System node renders just outside its own band |
| 8 | Authoring stays where it is | `PartOf` is not creatable through the generic Add-Relationship flow (A1 / ADR-0111); the graph is a read surface |

### 3.1 Amendment 2026-08-06 — decision 7 was false as built

Gate 9 drove the diagram on the running stack against a System with three members, two inter-member dependencies and three dependencies out to two non-member services. With the toggle on, measured client rects: band `x 350–1190 / y 347–496`, and `Notifier Service` — a non-member — at `x 1038–1155 / y 365–407`, **entirely inside the band**. `Auth Service` fell outside only because dagre placed it 12 px above the band's top edge.

The cause is structural, not a layout accident. With `rankdir: "LR"` and `partOf` retained in the dagre input, the ranks in that fixture were: members at 0, 1, 2 and non-members at 2, 3 — so a non-member (`Auth`, rank 2) **shares a rank, and therefore an x-column, with a member** (`Fees`, rank 2). Any external dependency of a member sits one rank right of it, and that rank routinely contains members too. A bounding box therefore cannot carry "inside = member", and shrinking it to members only (4a) does not fix that on its own — it only removed this fixture's failure by luck of the y-assignment.

Hence 7a: membership is expressed **per node**. 4a is kept as well, because it is independently worth having (it removes the empty-canvas stretch), not because it fixes the signal.

**Amendment 2026-08-06 (S6) — the band's border is solid, not dashed.** As first built, the band (meaning "inside" this System) and 7a's non-member marking (meaning "outside") both used a dashed border, while the legend stated only the second meaning (`dashed = outside this system`) — a deep-review finding a human gate-9 pass had missed because it judged only the node treatment. The band is now `border` (solid); the translucent brand fill (`bg-brand-primary/10`) already reads as a container without borrowing the channel 7a needs. Pinned by `SystemBoundaryNode.test.tsx` asserting the band's classes never contain `border-dashed`.

**Not adopted:** dropping the focus System node from the diagram entirely. The band is labelled with the System's name, so the node is arguably redundant and removing it would also end the duplicated label — but it changes what the focus node is, and with it selection and the expand affordances. Recorded as a follow-up, not done here.

Evidence: `docs/superpowers/verification/2026-08-05-catalog-system-graph-nodes/dod.md` gate 9, plus `gate9-band-toggle-off.png` / `gate9-band-toggle-on.png`.

### Rejected alternatives

- **Rename the Members tab to "Dependencies"** for symmetry with component pages — considered and dropped: "Dependencies" over a member list reads wrong, and it would move A1's assign/remove surface under a tab whose name does not describe it.
- **A separate `Diagram` tab** — splits one concept (the system's members) across two tabs and breaks the component-page pattern.
- **Diagram on Overview**, literally beside the description — puts a graph fetch on the default tab. That is the shape that reddened the nightly after #70, where a spec's `waitForResponse` assumed a request fired on the default tab.

## 4. Frontend changes

### 4.1 Type split (`relationshipTypeRules.ts` and its consumers)

`RelationshipKind` keeps its **only** legitimate job — the creatable-edge rules. Every graph-facing surface moves to `EntityKind` (`RelationshipKind | "system"`), which already exists:

| Surface | From | To |
|---|---|---|
| `GraphNodeData.kind`, `ExplorerNode.kind` | `RelationshipKind` | `EntityKind` — deletes the cast at `graphMerge.ts:50` |
| `parseEntityRef` guard | `isRelationshipKind` | `isEntityKind` |
| `GraphActions.setFocus` / `openPage`, `graphFocusPath` | `RelationshipKind` | `EntityKind` |
| `GraphFilters.kinds`, `KIND_OPTIONS`, `useGraphFilters` token validation | `RelationshipKind` | `EntityKind` + a "System" option |
| `isAllowedPair`, `allowedOtherKinds`, `offerableTypes`, `AddRelationshipDialog` | — | **unchanged** |

### 4.2 `mergeGraphs` — carry the raw relationship type

`ExplorerEdge` gains `type` alongside the computed `label` (today the wire type is consumed by `relationshipTypeLabel[...]` and lost). Member classification then reads off the edge instead of inferring from depth.

### 4.3 Member classification

`PartOf` runs component → System (ADR-0111), so:

```
member = edge.source  where  edge.type === "partOf" && edge.target === focusId
```

Explicit and directional. A `partOf` edge pointing at a *different* System — reachable at `depth=2` through an external neighbour — must not mark that neighbour as a member.

### 4.4 `systemBoundaryBox` (pure function)

`(layoutNodes, memberIds, focusId) → { x, y, width, height } | null`. Bounding box over the member nodes **only** — the focus node is excluded (amended by 4a in §3.1; this section originally said "plus the focus node", which is decision-7-era text 4a superseded), padded; `null` when the system has no members. Injected as a non-interactive node of a new type `systemBoundary` — below the others by `zIndex`, `selectable`/`draggable` false, `pointer-events: none`, labelled with the System's display name. Computed in the same `useMemo` as the layout, so no second render.

Membership is signalled **per node** (amendment 7a, §3.1), never by band geometry: the band is a *members-only* bounding box, so every member is guaranteed to intersect it, but a non-member is **not** guaranteed to fall outside it (dagre's `rankdir: "LR"` can share a rank — and x-column — between a member and a non-member). A second System reached at depth 2, through a non-member's own `partOf` edge to a *different* System, is itself a non-member and receives the same outside marking as any other non-member — no special-casing by kind. Pinned by `SystemDiagram.test.tsx`.

### 4.5 `SystemDiagram.tsx` (new)

Fetches through the existing explorer hook, which needs one small extension: `useGraph` (`api/graph.ts:37`) hard-codes `FOCUS_DEPTH = 2` and takes `{ focus, expand }`. It gains an **optional `depth`** defaulting to `FOCUS_DEPTH`, so the diagram calls `useGraph({ focus: { kind: "system", id: systemId }, expand: [], depth: includeExternal ? 2 : 1 })` and the explorer is unaffected. `graphKeys.node` already includes depth in the query key, so depth 1 and 2 cannot collide in the cache. Then `mergeGraphs` → `layoutGraph` → boundary injection. Mirrors `DependencyMiniGraph`'s chrome (heading, `Open full graph ↗`, legend, fixed height, non-draggable canvas) but is backed by `/graph` rather than `useRelationshipsList` — **the reason being decision 2**: a relationships list of the System returns only its `PartOf` edges and would never show one member depending on another.

States: skeleton while loading · error card **scoped to the diagram section** so the members table below is unaffected · `No members yet.` when the system is empty · a truncation warning when the response is `truncated` — **no count** (nit 1: `GraphResponse.Truncated` is a bare boolean, `/graph` does not return a total, so the banner cannot name "the first N" without inventing a number the wire doesn't carry) · `Open full graph ↗` → `/graph?focus=system:<id>`, which works only because of §4.1.

`includeExternal` is local component state, default **off**.

### 4.6 `SystemDetailPage.tsx`

`<SystemDiagram>` above `<SystemMembersSection>` inside the existing Members tab. Lazy-loaded, matching how `ApplicationDetailPage` defers `DependencyMiniGraph` (`ApplicationDetailPage.tsx:19-21`).

## 5. Testing strategy (per docs/TESTING-STRATEGY.md)

### 5.1 Frontend unit (vitest) — named artefacts

| Artefact | Asserts |
|---|---|
| `graphMerge.test.ts` (extend) | raw `type` survives; `label` unchanged |
| `systemBoundary.test.ts` (new) — nit 4: this row previously named the non-existent `systemBoundaryBox.test.ts` | padded box over members only, excluding the focus (4a); `null` with no members; an external node does not widen the box |
| member classification (in the same new test file) | direction respected; a `partOf` edge aimed at another System does not mark a member |
| `graphModel.test.ts` (extend) | `parseEntityRef("system:<guid>")` resolves; malformed tokens still `null` |
| `useGraphFilters.test.ts` (extend) | a persisted `system` token is accepted, a junk token rejected |
| `graphFilter.test.ts` (extend) | a `system` node dims under `kinds=["application"]` |
| `SystemDiagram.test.tsx` (new) | band and member nodes render; the toggle changes the `depth` passed to the hook; empty / error / truncated states; `Open full graph` href; **per-node marking (7a)** — a non-member is marked outside while a member and the focus are not, including a second System reached at depth 2 |
| `EntityGraphNode.test.tsx` (extend) | the outside-boundary dashed border composes with the selected/focused border rather than replacing it |
| `SystemBoundaryNode.test.tsx` (extend, S6) | the band's classes never contain `border-dashed` — that channel means "outside this system" on a node, so the band (meaning "inside") must not reuse it |
| `SystemDetailPage.test.tsx` (extend) | the Members tab renders the diagram above the members table |
| `GraphActionsContext.test.ts` (new — missing test) | `createReadOnlyGraphActions`: `setFocus`/`openPage` route through `graphFocusPath`/`entityDetailPath`; `supportsExpand === false` |

### 5.2 Backend integration (real seam) — named artefacts

Two cases added to the existing `GetCatalogGraphTests.cs`, against real Postgres + RLS and real JWT validation via `KartovaApiFixtureBase`. No production C# changes — but the behaviour the whole diagram rests on is currently untested:

1. **Happy:** `GET /graph?entityKind=system&entityId={id}&depth=1` returns the System plus its members, and the edge set includes the members' mutual `dependsOn` — the load-bearing property of `GraphTraversal.cs:66-71` — as well as their `partOf` edges.
2. **Negative:** the same request focused on a System id belonging to **another tenant** leaks no members and no edges.
3. **Missing test 2:** `entityKind=system` lowercase (ADR-0109 camelCase wire enums) — the frontend sends `entityKind: "system"` lowercase (`fetchGraph`, §4.5), but both tests above query `entityKind=System`. Without a lowercase case, a regression to case-sensitive enum parsing leaves both green while every System diagram 400s. The file already exercises lowercase for `service` (e.g. `entityKind=service`); this case follows that pattern.

### 5.3 Gates

Standard ten gates (`CLAUDE.md`). Gate 9 drives the real stack: cold start, in-SPA navigation to a System detail page, screenshot of the Members tab with the toggle off and on, then `/graph?focus=system:<id>` to prove §4.1. No existing `e2e/` spec visits System detail (checked — `system-list-surface.spec.ts` only reads hrefs off the list), so the E2E-impact trigger has nothing to update and the gate-9 conversion is a new spec.

Impact analysis for the plan: **C# is `N/A` — no existing C# symbol changes.** The blast radius is TypeScript, where there is no language server (CLAUDE.md), so the plan grounds it with grep over the `RelationshipKind` consumer set and says so explicitly rather than claiming tool grounding.

## 6. Size estimate

| Part | Prod LOC |
|---|---|
| §4.1 type split across ~8 files | ~80 |
| §4.2 + §4.3 + §4.4 | ~60 |
| §4.5 `SystemDiagram` + boundary node component + the `useGraph` depth param | ~185 |
| §4.6 wiring | ~5 |
| **Total** | **~325** |

Under the ~400 target, well under the 800 ceiling. No decomposition needed.

## 7. Out of scope

System↔System edges (no such relationship type) · creating or removing `PartOf` from the graph · nested systems · impact-analysis mode over a System focus (`GetImpactAnalysisHandler` reports degrees as 0 by design) · batching `ICatalogEntityLookup.Find`, the documented N+1 at `GraphTraversalHandler.cs:64-68`.

## 8. On completion

Update `docs/product/CHECKLIST.md` E-03.F-03.S-01: with the diagram delivered, the row's three acceptance criteria are all met and the story moves from `[~]` to `[x]`.
