# Deep review — FU-A, System nodes in the graph

**Target:** `feat/catalog-system-graph-nodes` vs `master`, range `b9f4eb7f..11527d64` · **Status:** OPEN (pre-merge gate)
**Date:** 2026-08-06 · **Gate:** 8 of the ten always-blocking gates
**Read against:** spec `docs/superpowers/specs/2026-08-05-catalog-system-graph-nodes-design.md` (incl. §3.1 amendments 7a/4a) · plan `docs/superpowers/plans/2026-08-05-catalog-system-graph-nodes.md` · ADR index `docs/architecture/decisions/README.md` · `docs/TESTING-STRATEGY.md` / ADR-0097 · `CLAUDE.md` §Definition of Done · this slice's `dod.md` + `gate-findings.yaml`

**Counts:** 3 blocking · 7 should-fix · 5 nits · 4 missing tests · 5 good
**Controller triage is recorded per finding.** Nothing here is discarded silently.

---

## Overview

The slice makes `system` a first-class frontend graph kind — the graph-facing surfaces move from `RelationshipKind` to the pre-existing `EntityKind` (focus token, kind filter, node data, sidebar, graph actions) — and adds a `SystemDiagram` to the System detail Members tab: `/graph` at depth 1 (2 behind an *Include external dependencies* toggle), members classified off the `partOf` edge direction, a labelled boundary band injected as a non-interactive `systemBoundary` node, and non-members marked per node. No C# production code changes; two integration tests pin the backend traversal and RLS behaviour the diagram rests on. Mid-slice, gate 9 disproved locked decision 7 and forced amendments 7a (per-node marking) and 4a (members-only band extent), both implemented.

---

## Blocking-class issues

### B1 — The new nightly E2E spec asserts a geometric invariant the spec says does not hold

**Evidence:** `e2e/tests/system-diagram-boundary.spec.ts:195-199` asserts `externalsInsideBand` has length 0. Spec §3.1 states the opposite as a *structural* fact: a bounding box cannot carry "inside = member", and 4a "only removed this fixture's failure by luck of the y-assignment". The ledger's own PASS table quantifies the margin at **12 px** (band top 355, `Auth Service` bottom 343). `systemBoundary.ts:6-7` then invites the change that breaks it — `BOUNDARY_PADDING` is documented as "safe to retune for legibility alone" — and raising it past ~`nodesep/2` (`graphLayout.ts`, `nodesep: 40`) swallows the external node into the band with no defect present.

**Impact:** the nightly workflow opens and comments a `nightly-red` GitHub issue on failure. A legibility retune, a dagre bump, or a fixture-ordering change reddens the nightly with nothing broken — the #70 failure mode, and **the same class gate 7 had just removed from this very branch**, reintroduced by the fix for gate 7.

**Fix:** delete the `externalsInsideBand` assertion; keep the two that pin the post-amendment contract (every non-member dashed, no member dashed). If a geometric assertion is wanted, assert what 4a actually guarantees — every *member* intersects the band — which is deterministic. Update the file header, which still frames the spec as guarding band geometry rather than the per-node marking that replaced it.

**Triage: FIX.** The reviewer is right and this is the most valuable finding of the gate.

### B2 — `gate-findings.yaml` is missing every finding from gates 5 and 7

**Evidence:** `gate-findings.yaml` carries entries for `reviews` (gate 2), `requesting-review` (gate 6) and `manual` (gate 9), but no `simplify` or `review-pr` slug, and `head:` still reads `b05cc2a6` while HEAD is `11527d64`. Meanwhile `dod.md` records 16 gate-5 findings and gate 7's 1 Critical + 2 Important + 6 Suggestions.

**Impact:** CLAUDE.md makes this file the machine-readable half of the ledger, updated the moment a gate runs. The cross-slice query the file exists for — rank gates by precision — would report **gate 7 as having found nothing on this slice**, when it found the one Critical six other gates missed. Telemetry degrades invisibly because `dod.md` looks complete.

**Fix:** add per-finding entries for gate 5 (`simplify`) and gate 7 (`review-pr`), each with a real/delusion verdict, and update `head:`.

**Triage: FIX — controller's own bookkeeping.** (The reviewer also listed gate 9 as missing; that is incorrect — gate 9's entries are present under the `manual` slug, which is the template's own name for it.)

### B3 — Gates 1 and 3 cite evidence commits that predate the branch's last C# change

**Evidence:** `dod.md` gate 3 claims "Commits after `bb930aa2` touch only frontend and docs, so this figure still describes the final commit." False: `920920ec` changed `GetCatalogGraphTests.cs` (+12 lines, the focus-node RLS assertions). Gate 1's `0 Warning(s), 0 Error(s)` is cited at `4190ce4a`, also before that change. The same file's gate-7 section contradicts gate 3 by describing that change as a "test-only C# fix, +12 lines".

**Impact:** two blocking gates' cited evidence does not describe HEAD while the text asserts it does. CLAUDE.md requires completion claims citable by command and output on the **final** commit.

**Fix:** correct the false sentence, name `920920ec`, and let the pending terminal re-verify supply build + full-suite evidence at the final commit, then update both `At:` lines.

**Triage: FIX — controller's own bookkeeping.** The terminal re-verify was already owed and will supply the evidence.

---

## Should-fix issues

### S4 — A third embedded graph view and a new visual channel ship with no governing ADR

**Evidence:** `SystemDiagram.tsx` (new), `SystemBoundaryNode.tsx` (new node type), `GraphNodeData.outsideBoundary`. The spec claims "ADRs touched: none new … works inside ADR-0040". But ADR-0040 decides **two** views — an embedded mini-graph that is a "1-level neighborhood … constrained projection", and the standalone explorer — and lists "Two renderers to maintain" and "Must keep visual consistency between the two views" as its accepted consequences. `SystemDiagram` is a third embedded renderer, depth-configurable rather than 1-level, with its own node type, legend and a containment semantic ADR-0040 does not contemplate.

**Impact:** the "how many embedded graph surfaces, and what visual grammar do they share" decision now lives only in a slice spec, not in the ADR library the guardrails cache reads. The next graph slice has no authority to consult.

**Triage: RAISED WITH THE OWNER, NOT SILENTLY WRITTEN.** CLAUDE.md requires ADR changes to be previewed before saving, so the amendment text goes to the owner for review rather than into the fix wave.

### S5 — `list-filter-registry.md` not updated for the fourth `kind` option

**Evidence:** the registry's `/graph` row records "`kind` (multi-select) + `teamId` (multi-select) … `kind`: built" and is unchanged, while `GraphFilterControls.tsx`'s `KIND_OPTIONS` gained `{ label: "System", value: "system" }`. Neighbouring rows show the convention of recording embedded panels with explicit `none-needed` outcomes; the new diagram panel has no row.

**Impact:** CLAUDE.md makes the registry the canonical per-list filter record; the facet's value set now disagrees with its own entry.

**Triage: FIX.**

### S6 — "Dashed" carries two contradictory meanings on the same canvas

**Evidence:** the band — meaning *inside* this System — is `border border-dashed border-brand` (`SystemBoundaryNode.tsx:13`). Non-member nodes — meaning *outside* — are `border-dashed` (`EntityGraphNode.tsx:28`). The legend states one of the two: `dashed = outside this system` (`SystemDiagram.tsx:133`).

**Impact:** the legend is wrong about the most prominent dashed element on screen. **Gate 9's human pass missed this** — it judged only the node treatment and called the screen legible.

**Fix:** make the band solid (the `bg-brand-primary/10` fill already carries the container read), or move non-members to a different channel. Then assert in `SystemBoundaryNode.test.tsx` that the band's classes do **not** contain `border-dashed`, so the two channels cannot silently re-converge.

**Triage: FIX.** This is the finding that most embarrasses gate 9, and it is right.

### S7 — The feature's only control has never been verified against a mouse click

**Evidence:** the ledger records that a plain `.click()` fails Playwright's actionability check and that a human mouse click "was not verified"; the E2E spec institutionalises `focus()` + `Space`, and the unit test uses jsdom, which does not model hit-testing. `Toggle` wraps a react-aria `Switch`: a `<label>` around a visually-hidden `role="switch"` input, so Playwright resolving to the hidden input and failing hit-testing is expected and a real click on the label almost certainly works — but "almost certainly" is not gate-9 evidence.

**Impact:** if it does not work, the entire depth-2 half of decision 3 is unreachable by mouse and nothing in the suite notices. The ledger flags the same doubt from the A2 multi-select, so this is a repeating unverified pattern.

**Fix:** drive the label (`getByText(/include external dependencies/i).click()`) and keep `expect(toggle).toBeChecked()`; record the result in the ledger, replacing "not verified".

**Triage: FIX.**

### S8 — The amended spec still contains decision-7-era text contradicting 4a

**Evidence:** spec §4.4 still reads "Bounding box over the member nodes **plus the focus node**", contradicted by 4a and by `systemBoundary.ts` (`n.id !== focusId && memberIds.has(n.id)`). §5.1 still frames the boundary test as proving "an external neighbour lies outside the box" — decision-7 reasoning — and names no artefact for 7a's per-node marking, though those tests exist.

**Impact:** the spec is the input to future slices and to gate 2/6/8 reviews; as written §4.4 instructs the opposite of what shipped, so the next reader "fixes" the code back to the disproved design.

**Triage: FIX.**

### S9 — `CHECKLIST.md`'s FU-A sentence cites no verification ledger

**Evidence:** the row was flipped to `[x]` and lists the A1 and A2 ledgers but not FU-A's own — whose header says the slice is *not* complete.

**Triage: FIX.** The `[x]` flip itself is also premature while three gates are pending; the row will be reworded to name the ledger, and the marker revisited at gate 10.

### S10 — The E2E spec seeds unbounded fixture data into the shared dev tenant

**Evidence:** the spec creates one team, one System and four Services per run, keyed by `Date.now()`, with retries set to 2 — so a flaky night triples it. This diverges from the documented convention (`e2e/fixtures/nav.ts` uses deterministic `DevSeed` rows kept in sync with `src/Kartova.Migrator/DevSeed.cs`).

**Impact:** roughly two teams a night against the `limit: 200` team lookups in `SystemDetailPage` and `GraphExplorerPage`; once the shared tenant crosses 200 teams, team names silently stop resolving and the team filter loses options — a slow-fuse failure that surfaces months later as an unrelated bug. List counts other specs read also drift.

**Triage: FIX** — at minimum stop creating a team per run (reuse a fixed one). Moving the whole fixture into `DevSeed` is the fuller fix and is recorded as the follow-up if the minimal one is taken.

---

## Nits

1. **Truncation copy drops the count the spec promised.** `SystemDiagram.tsx:136` renders "Showing only part of a large system."; spec §4.5 specifies a "showing the first N" banner. Either include the count or amend the spec. **Triage: FIX (amend the spec — the node cap is not on the wire).**
2. **`layoutGraph` is now seven positional parameters, called with three `undefined` placeholders.** Convert the tail to an options object. **Triage: DEFER — touches every caller; follow-up.**
3. **Toggling the switch blanks the canvas.** A depth change produces a new query key, so enabling externals flashes the diagram away; `placeholderData: keepPreviousData` would hold the previous layout. **Triage: FIX.**
4. **Spec names a test file that does not exist** — §5.1 says `systemBoundaryBox.test.ts`; the file is `systemBoundary.test.ts`. **Triage: FIX.**
5. **The plan's "deliberately NOT changed" table lists a file the diff touches** (`relationshipTypeRules.ts` — comment-only, a gate-7 finding). **Triage: FIX (one line in the ledger; the plan is gitignored scratch).**

---

## Missing tests

1. **Criterion — spec §4.1:** `GraphActions.setFocus`/`openPage` widen to `EntityKind`; the plan calls this "a live bug fix, not future-proofing" because the mini-graph on component pages already renders System neighbours. **`createReadOnlyGraphActions` has no test at all.** Add `web/src/features/catalog/relationships/__tests__/GraphActionsContext.test.ts`: `setFocus("system","s1")` → `/graph?focus=system:s1`, `openPage("system","s1")` → `/catalog/systems/s1`, `supportsExpand === false`. Without it the branch's headline bug fix is asserted nowhere. **Triage: FIX.**
2. **Criterion — ADR-0109 camelCase wire enums:** `fetchGraph` sends `entityKind: "system"` (lowercase) but both new integration tests query `entityKind=System`. Add a case in `GetCatalogGraphTests.cs` issuing lowercase for a System focus and asserting the same shape — the file already exercises lowercase for `service`. Today a regression to case-sensitive enum parsing leaves both new tests green while the SPA 400s on every System diagram. **Triage: FIX — this is the sharpest of the four.**
3. **Criterion — spec §4.5 "skeleton while loading":** `SystemDiagram.test.tsx` covers error, empty, truncated and the happy canvas but never `isLoading: true` — the branch that renders on every first paint of the tab. Add it. **Triage: FIX.**
4. **Criterion — amendment 7a:** at depth 2 the non-member set can include *another System*, reached through a non-member's own `partOf` edge, which then renders dashed under the legend "outside this system". Neither decided in the spec nor pinned by a test. **Triage: FIX — decide it (a second System is a non-member, so it is marked) and pin it.**

---

## What looks good

1. **Membership read off the wire type, narrowed to the generated union.** `systemBoundary.ts:11,22` (`PART_OF_TYPE`) plus `ExplorerEdge.type?: WireRelationshipType` derived from the OpenAPI schema means a typo in the comparison is a compile error rather than a populated System silently rendering "No members yet." The directional check honours ADR-0111's component → System direction, and the test file pins all four wrong directions plus the second-System case.
2. **The type split is enforced, not just intended.** `RelationshipKind` keeps only the creatable-edge matrix; `EntityKind` covers render, URL and filter surfaces; `ENTITY_KIND_LABEL: Record<EntityKind, string>` turns a future fifth kind into a build error rather than a silent fallback. The impact-analysis narrowing routes through the named `isRelationshipKind` guard instead of a hand-rolled inequality.
3. **7a implemented through the existing extension point, not beside it.** `outsideBoundary` is threaded as its own set through `layoutGraph`'s per-node data builder rather than a post-layout `.map`, and deliberately not folded into `dimmed` — with the reason recorded where a maintainer will find it. `EntityGraphNode.test.tsx` pins that the dashed border *composes* with the selected border rather than replacing it, which is the failure mode class-string concatenation invites.
4. **The RLS integration test distinguishes what is disclosed from what is guarded.** It explains in prose that the focus ref is always present as a bare node (the caller supplied the id, so no disclosure) and asserts the thing that would actually leak first — `DisplayName == string.Empty` and `TeamId is null` from the RLS-scoped lookup. A weaker "the other tenant's member is absent" test would pass through a real enrichment regression.
5. **The E2E spec's cardinality guards and the honesty behind them.** It documents why the matcher is a prefix match and then asserts both non-members and both members were actually found, so a matcher that matches nothing fails loudly instead of reporting clean — with the header naming the vacuously-passing probe that motivated it. Self-seeding through the product's own API with no environment dependency makes it materially better than the probe it replaced. (B1 concerns one assertion inside it, not the design.)
