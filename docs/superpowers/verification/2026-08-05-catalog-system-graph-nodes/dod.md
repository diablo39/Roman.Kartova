# DoD Ledger — System nodes in the graph (FU-A, E-03.F-03.S-01 closeout)

**Slice:** `2026-08-05-catalog-system-graph-nodes` · **Branch:** `feat/catalog-system-graph-nodes` · **HEAD:** `e3ab579d`
**Merge base (master):** `b9f4eb7f` · **PR:** [#85](https://github.com/diablo39/Roman.Kartova/pull/85) — **merged 2026-08-07 as `95dcc519`** (squash) · **Last updated:** 2026-08-07
**Spec:** `docs/superpowers/specs/2026-08-05-catalog-system-graph-nodes-design.md`
**Plan:** `docs/superpowers/plans/2026-08-05-catalog-system-graph-nodes.md` (gitignored scratch)
**Execution:** `superpowers:subagent-driven-development` — 11 tasks, fresh implementer + task review each, then one whole-branch review and four fix waves (gates 6, 7, 8 and the gate-9 amendment). The SDD scratch workspace held 45 artefacts: the per-task briefs and reports, the per-gate findings lists and fix reports, and 12 review-package diffs. It was retained while gates 4–10 ran, since those gates cite it, and **deleted at merge** — everything load-bearing was lifted into this file and `gate-findings.yaml`, and git history is the record now.
**Findings telemetry:** `./gate-findings.yaml`

> ✅ **All ten gates and the terminal re-verify are green, each citable by command and output below.** The last open finding (S4) closed on 2026-08-07 with the ADR-0040 amendment at `e3ab579d`. The slice is ready to merge; it is **not merged yet**.
>
> Three gates each caught something the ones before them missed, which is the argument against folding any of them: **gate 9** disproved locked decision 7 by measurement and forced the §3.1 amendments; **gate 7** found a Critical — a spec committed with an unset-env-var dependency that would have reddened the nightly every night — that six earlier gates had looked at and not seen; **gate 8** then found the same nightly-landmine class *inside the spec that replaced gate 7's*, plus a legend that contradicted the most prominent element on a screen a human gate-9 pass had already approved.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-08-06 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-08-05 |
| 3 Full suite (+ real-seam) | ✅ PASS | 2026-08-05 |
| 4 Container build (images CI) | ✅ PASS | 2026-08-06 |
| 5 `/simplify` | ✅ PASS | 2026-08-06 |
| 6 `requesting-code-review` | ✅ PASS | 2026-08-05 |
| 7 `review-pr` | ✅ PASS | 2026-08-06 |
| 8 `deep-review` | ✅ PASS | 2026-08-07 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-08-07 |
| 9 Visual / API verification (ADR-0084) | ✅ PASS (after fix) | 2026-08-06 |
| 10 CI green on PR | ✅ PASS | 2026-08-07 |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
`cmd //c "dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true --nologo"` → **0 Warning(s), 0 Error(s)**, 1m35s. Frontend type gate green alongside it: `npx tsc -b --noEmit` → exit 0.
**At:** `4190ce4a`

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
All 11 tasks reviewed individually (spec compliance + code quality) by a fresh reviewer against the task brief, the implementer's report and the task's own diff. **11/11 approved.** Zero Critical, zero Important across the whole run; nine Minor findings recorded and triaged (see `gate-findings.yaml`).
Notable per-task catches: Task 2's three out-of-brief edits were each reviewed on their merits (one — the `GraphExplorerSidebar` record guard — is what stopped a System-node selection throwing once Task 1 widened `parseEntityRef`); Task 8's wholesale `@xyflow/react` mock was verified to match the established sibling pattern and to leave the real `mergeGraphs`/`layoutGraph`/`systemMemberIds` executing; Task 10's brief had guessed two names wrong (`CatalogSystemResponse` → `SystemResponse`, `204` → `200 + SystemMembershipResponse`) and the implementer corrected them against the existing test file rather than adjusting assertions.
**One partial:** Task 5's reviewer report was truncated by output compression after its Strengths section, so its severity sections were never legible. That module (`systemBoundary.ts`) was therefore given a full review inside gate 6 instead, which found the assertion gaps closed by the fix wave.
**At:** `92b5872c` (per task, incrementally)

### 3 — Full test suite (unit + arch + integration; real seam)
**Status:** ✅ PASS
**Frontend:** `npx vitest run` from `web/` → **134 files / 988 tests passed** on the final commit `b05cc2a6` (983 before the fix wave; +5 from the new sidebar and boundary cases). `npx tsc -b --noEmit` → exit 0. `npx eslint --no-ignore` → clean on every touched file (frontend lint is not in CI, so this is the only place it runs).
**Backend real seam:** `Kartova.Catalog.IntegrationTests` → **406/406** against real Postgres + RLS via Testcontainers and real JWT validation through `KartovaApiFixtureBase`, measured at `bb930aa2`. The slice adds **no C# production code**, so the two new tests exist to pin behaviour the diagram depends on and had no coverage: (a) a depth-1 traversal focused on a System returns the `dependsOn` edge **between its members** — matched on edge type *and* both endpoints, so a wrong edge mix cannot satisfy it; (b) a cross-tenant System focus leaks no member and no edge.
**Correction (gate 8, finding B3):** this row previously claimed "commits after `bb930aa2` touch only frontend and docs, so this figure still describes the final commit". That was **false** — `920920ec` (gate 7) and `a7252906` (gate 8) both changed `GetCatalogGraphTests.cs`, and the frontend figure predated three later waves. Superseded by the terminal re-verify below, which is the row to cite for the final commit.
**At:** `b05cc2a6` (frontend) / `bb930aa2` (backend) — **stale; see the terminal re-verify**

### 4 — Container build (images CI job)
**Status:** ✅ PASS
`docker compose build` → exit 0. Images built: `kartova/migrator:dev`, `kartova/web:dev`, `kartova/api:dev`.
**At:** `4190ce4a`

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS
Four cleanup agents in parallel (reuse · simplification · efficiency · altitude) over the code-only branch diff. **16 findings: 8 applied, 8 skipped with reasons.** Two agents converged independently on the same site (`GraphExplorerPage`'s redundant kind enumeration), which is the signal that mattered most.

Applied (`055716e9`, `c0d970cc`): the impact-analysis kind rule stopped being enumerated twice in two files and is now derived (`kind !== "system"`) — the duplicate also silently excluded `api`, so loosening the sidebar's gate later would have produced a silent no-op; `outsideBoundary` now flows through `layoutGraph`'s existing per-node extension point instead of a bespoke post-layout `.map` beside it, so there is one mechanism for "mark a computed set of node ids" rather than two; `SystemDiagram`'s data pipeline recovered the two-memo split its sibling `GraphExplorerPage` deliberately has, so clicking a node no longer re-runs `mergeGraphs` + `systemMemberIds` + `systemBoundaryBox` when only the layout needed redoing; `.some(k => k === x)` went back to `.includes(x)` now that the widening made it type-check; the optional chaining added when the sidebar's kind record had three keys is gone now that it has four; the read-only `GraphActions` object, duplicated verbatim between the two preview surfaces, moved to a factory in the module that already owns the type; `DependencyMiniGraph` stopped hand-rolling the `/graph?focus=` URL three lines from the helper that builds it; and the new filter-kind test was aligned with the sibling it was copy-pasted from.

Skipped with reasons (recorded so they are decisions, not omissions): the legend JSX has deliberately diverged (the diagram gained a third row); the `@xyflow/react` test mock is a fourth copy of an already-tolerated three; the integration-test seed helpers follow an established per-file pattern; the sidebar's four per-kind queries are the wrong altitude but fixing it touches four API modules and every caller — recorded as a follow-up; `useGraph`'s unstable `results` identity and dagre re-laying out on selection are inherited from the sibling, not introduced here; `systemBoundary.ts`'s second `null` guard is defensive, not dead (without it `Math.min(...[])` yields `Infinity` and a garbage box); and `GraphNodeData`'s five per-node flags are genuinely orthogonal, with the branch's own test asserting they compose.

**Behaviour-unchanged check, run because two of the fixes restructured the diagram's pipeline:** the gate-9 probe was re-run and returned **byte-identical geometry and marking** (band 351–979; every node position and dashed flag the same). The unit suite alone would not have proven that.
**At:** `c0d970cc`

### 6 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
Whole-branch review (13 commits, `b9f4eb7f..92b5872c`) on the most capable model, against the spec's eight locked decisions, with the per-task deferred minors handed over for triage. **1 Blocking, 6 Should-fix, 8 Nits, 5 missing-test gaps.**
The Blocking finding was **this file's absence** — `CHECKLIST.md` had been flipped to `[x]` while the only record of the slice's verification sat in gitignored scratch, so no completion claim was citable. Fixed by writing this ledger and `gate-findings.yaml`.
One consolidated fix wave (`6cd82963`, `b05cc2a6`) closed **F1–F8 and all four nits**; a scoped re-review verdicted every one ADDRESSED with file:line evidence and found no new breakage. Highlights: the explorer sidebar now resolves a System through the existing `useSystem` hook instead of rendering a bare UUID under a comment that falsely claimed no such query existed; `ExplorerEdge.type` is narrowed to the generated wire union and both `"partOf"` comparison sites route through one exported constant (a typo previously compiled and would have rendered "No members yet." for a populated System); and `systemBoundaryBox`'s size assertions are now exact rather than an inequality that let arithmetic mutations survive.
**Two findings were deliberately excluded from the fix wave** and are recorded as open decisions, not silent discards — see `gate-findings.yaml` and the note below.
**At:** `b05cc2a6`

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS — **and it earned its keep.** Five specialist agents in parallel (code · tests · comments · silent failures · type design). **1 Critical, 2 Important, 6 Suggestions**, all applied in `920920ec` + `11527d64`. The Critical was invisible to every earlier gate, and two agents found it independently.

**The Critical — a nightly landmine, self-inflicted.** The gate-9 probe had been committed as `e2e/tests/gate9-band.spec.ts`, reading its System id from `process.env.GATE9_SYSTEM_ID!`. TypeScript's `!` is compile-time only, `playwright.config.ts` discovers every spec under `tests/` with no filter, `run.sh` runs them unfiltered, and the nightly workflow sets no such variable and seeds no such data — so the nightly would have navigated to `/catalog/systems/undefined`, timed out, and (thanks to the nightly-red tracking issue added earlier the same day) opened a GitHub issue every night. Precisely the #70 failure mode CLAUDE.md's retro warns about, reintroduced by the person who wrote that retro's guard.
Fixed by conversion rather than deletion, because the defect it guards is the one no unit test can reach: it is now `e2e/tests/system-diagram-boundary.spec.ts`, seeding its own System, two members, two non-members and the edges between them through the product's API, and asserting that **no non-member intersects the band** and that **every non-member carries the outside marking while no member does** — with the cardinality guards that stop the matcher matching nothing. The implementer validated it mutation-style: reintroduced each defect, watched the spec fail, reverted. Controller re-ran it independently **with the variable unset**, as the nightly will: **1 passed, 4.2s.**

**The two Important:** the cross-tenant RLS test asserted the other tenant's *member* was absent but never the focus System node itself — so a regression that resolved the focus for the wrong tenant while correctly hiding its members would have passed unchanged (test-only C# fix, +12 lines, `GetCatalogGraphTests` 18/18). And one stale comment survived the branch's own five-comment sweep: `isRelationshipKind`'s doc still named "URL graph focus and persisted filter kinds" as its callers, both of which this branch moved to `isEntityKind`.

**The six Suggestions**, all applied: `ENTITY_KIND_LABEL` typed `Record<EntityKind, string>` to match its exhaustive sibling four lines away (a fifth kind was a silent fallback, now a build error); `KIND_OPTIONS` tied to `EntityKind`; the impact-analysis narrowing routed through the named `isRelationshipKind` guard instead of a hand-rolled `!== "system"`; `useGraph`'s `depth` narrowed to `1 | 2 | 3 | 4`, matching the server's own domain; `BOUNDARY_PADDING` given the rationale its neighbours had; and two weak assertions strengthened — a test whose name claimed it proved the band rendered while asserting only text the System's own node also carries, and an edge test using text-absence where the sibling test one line below already used an exact edge count.

**Recorded as follow-ups, not fixed:** no `ErrorBoundary` anywhere in `web/src` (systemic, the sibling mini-graph has the identical exposure); `GraphNodeData`'s five orthogonal per-node flags (revisit at a fourth surface or sixth flag); `ExplorerEdge`'s `type?`/`derived?` pairing permitting states its single producer never emits.
**Not folded into 6 or 8.**
**At:** `11527d64`

### 8 — `deep-review`
**Status:** 🟡 PARTIAL. `/deep-review` ran against `b9f4eb7f..11527d64` (spec incl. §3.1 amendments, plan, ADR index, `docs/TESTING-STRATEGY.md`/ADR-0097, this ledger + `gate-findings.yaml`). **3 blocking · 7 should-fix · 5 nits · 4 missing tests · 5 good.** Full record: `./deep-review.md`.

A fix wave closed the twelve findings routed to it — **B1, S5, S6, S7, S8, S9, S10, nits 1/3/4, and all four missing tests (MT1–MT4)**:
- **B1 (blocking):** the E2E spec's `externalsInsideBand`/`toHaveLength(0)` assertion pinned a geometric invariant the spec itself says does not hold (held by 12px of luck; `BOUNDARY_PADDING` is documented as safe to retune, which would have reddened the nightly with no defect present — the same failure class gate 7 had just removed, reintroduced by gate 7's own fix). Replaced with the one property 4a actually guarantees deterministically: every member intersects the band. The two per-node marking assertions are unchanged. File header rewritten to frame the real contract as per-node marking (7a), not band geometry.
- **S5:** `docs/design/list-filter-registry.md`'s `/graph` row now documents the `kind` facet's fourth option (System).
- **S6:** the boundary band's border is solid (`border border-brand`, no `border-dashed`) — the legend's "dashed = outside this system" was wrong about the band, the canvas's most prominent dashed element. `SystemBoundaryNode.test.tsx` now asserts the class list carries no `border-dashed`. **Verified on screen** at `localhost:5173` (screenshot below): the band renders solid, both external (non-member) cards render dashed, matching the legend.
- **S7:** the E2E spec now clicks the visible label (`getByText(/include external dependencies/i).click()`) instead of `focus()`+`Space`, exercising react-aria `Switch`'s native label→input delegation — a real mouse click. **Re-run against `localhost:5173`: 1 passed.**
- **S8:** spec §4.4/§5.1 decision-7-era text (the band as "members plus the focus node"; the boundary test framed as proving containment) rewritten to match what shipped (4a/7a).
- **S9:** `CHECKLIST.md`'s FU-A line now names this slice's own ledger and states gates 8/terminal-reverify/10 are pending; the premature `[x]` marker was left in place per the finding's own triage note ("revisit the marker at gate 10").
- **S10 (minimal fix, per triage):** the E2E spec now find-or-creates one Team against a fixed name instead of minting one per run. System/Service creation stays run-scoped (no delete endpoint exists) — moving the whole fixture into `DevSeed` is recorded as the fuller follow-up.
- **Nit 1:** the spec's truncation-banner text amended to match the wire shape — `GraphResponse.Truncated` is a bare boolean, so the banner cannot name "the first N".
- **Nit 3:** `graph.ts`'s focus and expand queries now pass `placeholderData: keepPreviousData`, so toggling *Include external dependencies* no longer blanks the canvas mid-query.
- **Nit 4:** spec §5.1 now names the real file, `systemBoundary.test.ts`.
- **MT1:** new `GraphActionsContext.test.ts` — `setFocus`/`openPage` routing and `supportsExpand === false` were previously asserted nowhere.
- **MT2 (sharpest of the four, test-only — no C# production change):** `GetCatalogGraphTests.cs` gained a lowercase-`entityKind` System-focus case (ADR-0109). `CatalogEndpointDelegates.cs` already parses with `Enum.TryParse<EntityKind>(..., ignoreCase: true, ...)`, so this pins existing behaviour rather than fixing a bug.
- **MT3:** `SystemDiagram.test.tsx` gained an `isLoading: true` case (skeleton visible, canvas absent) — previously uncovered among error/empty/truncated/happy-path.
- **MT4:** pinned exactly as the finding specified — a second System reached at depth 2 through a non-member's own `partOf` edge to a *different* System is itself a non-member and gets the same outside marking, no special-casing by kind.

**Deliberately left open, not silently fixed:**
- **B2** (`gate-findings.yaml` missing gate-5/gate-7 entries, stale `head:`) and **B3** (gates 1/3's cited evidence commits predate the branch's last C# change) are the controller's own bookkeeping findings, out of scope for this fix wave.
- **S4** (a third embedded graph surface + a new visual channel with no governing ADR) is a proposed ADR-0040 amendment — raised with the owner for preview rather than silently written, per CLAUDE.md.
- **Nit 2** (`layoutGraph`'s seven positional params → options object) is DEFER — touches every caller, recorded as a follow-up.

**S6 solid-band verification (screenshot, `localhost:5173`, fixture system "Boundary Band E2E System"):** the band around the two member cards renders with a plain solid blue border and light fill; the two external (non-member) cards outside it render with a visibly dashed border, matching "dashed = outside this system." Legibility reads fine — no channel collision, no washed-out non-member treatment.

**At:** `a725290` (fix wave: `8a733fa` e2e, `6952617` frontend, `a725290` backend test)

### Terminal re-verify (build + full suite after gates 5–8) — **✅ PASS, 2026-08-07**
**This is the row to cite for the final commit.** Run after every gate that could apply fixes had applied them (5, 6, 7, 8 and gate 9's amendment wave):
- `cmd //c "dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true --nologo"` → **0 Warning(s), 0 Error(s)** (1m28s)
- `npx vitest run` from `web/` → **135 files / 1001 tests passed** (983 → 988 → 994 → 1001 across the four waves)
- `cmd //c "dotnet test …Kartova.Catalog.IntegrationTests…"` → **407/407**, real Postgres + RLS via Testcontainers, real JWT (406 before gate 8's lowercase-`entityKind` test)
- `e2e/tests/system-diagram-boundary.spec.ts` → **1 passed**, run with **no environment variables** against a **freshly rebuilt container image** on 4173 — the nightly's actual configuration, not the dev server. This was checked specifically because the fix wave's report flagged a failure here; it turned out to be a stale local image, and rebuilding as `run.sh` does resolves it.
**At:** `197e4457` + the ledger commit that follows it.

### Superseded — original terminal re-verify note
**Status:** owed after gates 5, 7 and 8 (deep-review fix wave) apply their fixes. Re-run on the final commit of this fix wave: **frontend** `npx vitest run` → 135 files / 1001 tests passed (up from 134/988 pre-wave); `npx tsc -b --noEmit` → exit 0; `npx eslint --no-ignore` clean on every touched file. **Backend** `dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true --nologo` → 0 Warning(s), 0 Error(s); `Kartova.Catalog.IntegrationTests` filtered to `GetCatalogGraphTests` → 19/19 against real Postgres/RLS (18 pre-wave + the new MT2 case).
**At:** `a725290`

### 9 — Visual / API verification (observe the running system)
**Status:** ✅ **PASS at `cc508e89`, after failing at `58fa2e61` and forcing a spec amendment.** The gate's first run disproved spec §3 decision 7 — an external non-member rendered inside the boundary band. The owner chose remedy **C**; decisions **7a** and **4a** were written into the spec (§3.1) and implemented, and the same probe now passes against the fixed build. Both states are recorded below, because the failure is the more useful half of the record.

#### Re-run after the fix (`cc508e89`) — measured, same probe

| Element | x | y | marked outside? |
|---|---|---|---|
| band (members only) | **351–979** | 355–441 | — |
| `Notifier Service` (non-member) | **1052–1170** | 373–415 | **yes** |
| `Auth Service` (non-member) | 824–929 | 300–343 | **yes** |
| `Ledger` / `Fees` / `Checkout Service` (members) | inside | inside | no |

`EXTERNALS_INSIDE_BAND` is now empty **and the matcher is proven live** — the same run reports both non-members found and marked, so the empty result is a real one rather than the vacuous one this probe produced on its first attempt. Console clean. The band tightened from 350–1190 to 351–979, so the empty-canvas stretch is gone as well.

The per-node signal reads correctly and, crucially, **legibly**: the two non-members carry a dashed border and a muted label with a `dashed = outside this system` legend row, rather than the 30% opacity that would have hidden the very nodes the toggle exists to reveal. That was the reason spec 7a rejected reusing `layoutGraph`'s `dimmed` channel.

**One thing to look at with human eyes before merge:** the focus System node now sits outside its own band, detached at the lower right, and reads a little orphaned — it is the trade-off spec 4a records, but seeing it makes the "drop the System node from the diagram entirely" follow-up (spec §3.1) look more attractive than it did on paper. Not blocking; the band already carries the System's name.

**Original failure, retained — this is what the gate caught:**

**What was driven.** Real stack (`docker compose`: postgres + keycloak + migrator + api) with the **vite dev server on 5173** for the web tier — deliberately not the 4173 container, whose image predates this branch and would have verified nothing. Seeded through the product's own API: System *Payments Platform*, three member services (*Ledger*, *Fees*, *Checkout Service*) assigned via `PUT /catalog/services/{id}/system`, two inter-member `dependsOn` edges, and three external `dependsOn` edges to two non-member services (*Auth Service*, *Notifier Service*) — the "≥2 members with external dependencies" recipe this ledger asked for. Driven in-SPA (ADR-0084) via Playwright with the repo's own `e2e/fixtures/auth.ts` login. Probe: `e2e/tests/gate9-band.spec.ts` (a probe, not yet a regression spec). Evidence: `gate9-band-toggle-off.png`, `gate9-band-toggle-on.png`.

**The finding, measured rather than eyeballed** (client rects, toggle ON):

| Element | x | y |
|---|---|---|
| band | 350–1190 | 347–496 |
| `Notifier Service` (non-member) | **1038–1155** | **365–407** |
| `Auth Service` (non-member) | 815–918 | 294–335 |

`Notifier Service` lies **entirely inside the band**. `Auth Service` escapes only because dagre happened to place it 12 px above the band's top edge — luck, not a mechanism. Cause is exactly as predicted: with `rankdir: LR` and `partOf` retained in the layout input, the System sits one rank right of its members and a member's external out-neighbour lands in that same rank, i.e. inside the box the band spans. **Decision 7 ("external neighbours get no extra styling — falling outside the band is the signal") is therefore false as built.**

**Honesty note on the probe.** Its first run reported `EXTERNALS_INSIDE_BAND []` — a clean result — because it matched node labels by exact equality against the display name while the DOM text is display name + kind concatenated (`"Notifier ServiceService"`). The check was vacuous and the "clean" result meaningless; the verdict above initially came from reading the raw coordinates by hand. The matcher was then fixed to a prefix match **plus** two length guards asserting all five nodes are actually found, and the re-run reproduced the finding through the assertion. Recorded because a vacuously-passing check is precisely what this slice's own reviews were told to hunt for.

**Remedies on the table** (owner's call — the fix wave deliberately left this alone because every option reverses or amends an approved decision): (A) pass the non-member ids as `layoutGraph`'s existing `dimmed` argument so externals render at `opacity-30` — robust regardless of geometry, ~5 lines, reverses decision 7; (B) drop the focus System node from `systemBoundaryBox`'s extent so the band hugs only its members — fixes this case (the box would end at x 887) and removes the large empty area, but is not robust in general and leaves the System node outside its own band; (C) both.

**What gate 9 confirmed as working** (worth as much as the failure): `partOf` edges being hidden reads as intentional, not as missing edges — decision 6 holds. Both inter-member `dependsOn` edges render with "Depends on" labels, which is the payoff decision 2 exists for and the thing the Members table cannot show. The legend is present, the band is labelled, and the console is clean (zero errors, zero pageerrors).

**Further observations, not blocking:** the layout is sparse — the System node occupies its own rank far right, so even with the toggle OFF the band spans 350→1190 for content ending at 887, making over half the band empty; the band label duplicates the System node's own name; and the toggle **cannot be clicked** under Playwright's actionability check (its inner thumb and the tab panel both "intercept pointer events"), so the probe activates it with Space. Whether a human mouse click lands was not verified — this may be the same class as the A2 multi-select popover issue.

**Original brief for this gate, retained for the record:**
This is a first-time visual surface, so gate 9 is where the boundary band is actually judged. The gate-6 review raised a geometry concern the automated tests structurally cannot answer: `systemBoundaryBox` computes an **axis-aligned bounding box** over the focus plus its members, and nothing prevents a *non-member* from being laid out inside that rectangle. With dagre's `rankdir: "LR"` and the `partOf` edges retained in the layout input, the System sits one rank right of its members, and a member's external out-neighbour can land in that same rank — i.e. the same x-column the box spans. If that reproduces, spec §3 **decision 7** ("external neighbours get no extra styling — falling outside the band is the signal") rests on a false premise.
**Drive it deliberately:** a System with **≥2 members that each have external dependencies**, with the *Include external dependencies* toggle **on**. If an external node renders inside the band, the cheapest fix that preserves decision 4 (keep the band, keep dagre) is to pass the non-member ids as `layoutGraph`'s existing `dimmed` argument so externals render at `opacity-30` — that reverses only decision 7. **That is the owner's call, which is why the fix wave left it alone.**
Also worth human eyes: whether hiding `partOf` edges (decision 6) reads as intentional or as missing, and the band label's position.
Convert to a new `e2e/` spec afterwards. **E2E-impact trigger: N/A, verified** — no spec under `e2e/tests/` references the graph explorer, `?focus=`, or a System detail tab.

### 10 — CI green on the PR (terminal)
**Status:** ✅ PASS
**PR [#85](https://github.com/diablo39/Roman.Kartova/pull/85)**, run [31158955954](https://github.com/diablo39/Roman.Kartova/actions/runs/31158955954) on `e3ab579d` — **all five jobs green**: Backend (arch + unit + integration), Container images, Frontend (test + typecheck + build), Helm, Stryker config drift. The runner is the source of truth.

**Pre-push mirror (`scripts/ci-local.sh`):** 5/5 — but the first aggregate run reported `frontend FAIL`, and the diagnosis is worth keeping. It was the known `lightningcss` EPERM: even with the vite dev server stopped, an orphaned `esbuild.exe` running out of `web/node_modules` **and** one stray `node` process from an earlier run in this session still held the native module, and the failed `npm ci` had half-wiped `node_modules` on its way out. Resolved by identifying the single process that actually had the module loaded (rather than killing node processes indiscriminately, which risks the owner's own), reinstalling, and re-running: `frontend PASS`. The other four jobs passed on the first attempt. Recorded because the failure looks like a code failure in the summary line and is not one.
**At:** `e3ab579d` — the last code-bearing commit — and re-confirmed green on `96297218` (run [31159…](https://github.com/diablo39/Roman.Kartova/actions), all five jobs), which is this ledger entry itself. Anything after that point is documentation only.

## Impact analysis

Per CLAUDE.md the plan carries an `## Impact Analysis (LSP)` section. For this slice it is **`N/A` for C#** — no existing C# symbol changed — and the TypeScript blast radius is **grep-grounded and labelled as such**, because there is no language server for `.ts`/`.tsx` in this repo. The plan's table classifies all 16 `RelationshipKind` hits plus two structural consumers that grep alone under-reports (`EntityGraphNode.tsx` and `graphLayout.ts` reach the type through `GraphNodeData` and never name it). Gate 6 independently swept every `Record<...>` lookup and kind-keyed `switch` in `web/src` and found no site left on the wrong side of the split.
