# DoD Ledger — System nodes in the graph (FU-A, E-03.F-03.S-01 closeout)

**Slice:** `2026-08-05-catalog-system-graph-nodes` · **Branch:** `feat/catalog-system-graph-nodes` · **HEAD:** `b05cc2a6`
**Merge base (master):** `b9f4eb7f` · **PR:** not opened yet · **Last updated:** 2026-08-05
**Spec:** `docs/superpowers/specs/2026-08-05-catalog-system-graph-nodes-design.md`
**Plan:** `docs/superpowers/plans/2026-08-05-catalog-system-graph-nodes.md` (gitignored scratch)
**Execution:** `superpowers:subagent-driven-development` — 11 tasks, fresh implementer + task review each, then one whole-branch review and one consolidated fix wave. SDD ledger with per-task detail: `.superpowers/sdd/2026-08-05-catalog-system-graph-nodes/progress.md` (gitignored; **kept, not deleted**, until gates 4–10 close — it holds the per-task reports those gates may need to cite).
**Findings telemetry:** `./gate-findings.yaml`

> ⚠️ **Honest status: gates 2, 3, 6 and 9 pass; gates 1, 4, 5, 7, 10 and the terminal re-verify have NOT run.** Gate 9 failed on its first run, forced a spec amendment (§3.1) and passes on the fix. This slice is **not** complete and must not be described as merge-ready until the remaining six gates run.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ⏳ PENDING | — |
| 2 Per-task subagent reviews | ✅ PASS | 2026-08-05 |
| 3 Full suite (+ real-seam) | ✅ PASS | 2026-08-05 |
| 4 Container build (images CI) | ⏳ PENDING | — |
| 5 `/simplify` | ⏳ PENDING | — |
| 6 `requesting-code-review` | ✅ PASS | 2026-08-05 |
| 7 `review-pr` | ⏳ PENDING | — |
| 8 `deep-review` | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| 9 Visual / API verification (ADR-0084) | ✅ PASS (after fix) | 2026-08-06 |
| 10 CI green on PR | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ⏳ PENDING
The full-solution Release build has not been run for this slice. The **frontend** type gate is green on the final commit — `npx tsc -b --noEmit` → exit 0 — and the slice changes no C# production code, but `dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true` is still owed because the branch adds a C# test file.

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
All 11 tasks reviewed individually (spec compliance + code quality) by a fresh reviewer against the task brief, the implementer's report and the task's own diff. **11/11 approved.** Zero Critical, zero Important across the whole run; nine Minor findings recorded and triaged (see `gate-findings.yaml`).
Notable per-task catches: Task 2's three out-of-brief edits were each reviewed on their merits (one — the `GraphExplorerSidebar` record guard — is what stopped a System-node selection throwing once Task 1 widened `parseEntityRef`); Task 8's wholesale `@xyflow/react` mock was verified to match the established sibling pattern and to leave the real `mergeGraphs`/`layoutGraph`/`systemMemberIds` executing; Task 10's brief had guessed two names wrong (`CatalogSystemResponse` → `SystemResponse`, `204` → `200 + SystemMembershipResponse`) and the implementer corrected them against the existing test file rather than adjusting assertions.
**One partial:** Task 5's reviewer report was truncated by output compression after its Strengths section, so its severity sections were never legible. That module (`systemBoundary.ts`) was therefore given a full review inside gate 6 instead, which found the assertion gaps closed by the fix wave.
**At:** `92b5872c` (per task, incrementally)

### 3 — Full test suite (unit + arch + integration; real seam)
**Status:** ✅ PASS
**Frontend:** `npx vitest run` from `web/` → **134 files / 988 tests passed** on the final commit `b05cc2a6` (983 before the fix wave; +5 from the new sidebar and boundary cases). `npx tsc -b --noEmit` → exit 0. `npx eslint --no-ignore` → clean on every touched file (frontend lint is not in CI, so this is the only place it runs).
**Backend real seam:** `Kartova.Catalog.IntegrationTests` → **406/406** against real Postgres + RLS via Testcontainers and real JWT validation through `KartovaApiFixtureBase`, measured at `bb930aa2`. The slice adds **no C# production code**, so the two new tests exist to pin behaviour the diagram depends on and had no coverage: (a) a depth-1 traversal focused on a System returns the `dependsOn` edge **between its members** — matched on edge type *and* both endpoints, so a wrong edge mix cannot satisfy it; (b) a cross-tenant System focus leaks no member and no edge. Commits after `bb930aa2` touch only frontend and docs, so this figure still describes the final commit.
**At:** `b05cc2a6` (frontend) / `bb930aa2` (backend)

### 4 — Container build (images CI job)
**Status:** ⏳ PENDING — `docker compose build` not run for this slice.

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING as a distinct gate. Not folded: the whole-branch review (gate 6) did surface and close reuse/quality items of the kind `/simplify` targets — the duplicated `NODE_W`/`NODE_H` (now exported from `graphLayout.ts`), a no-op cast, a stray top-level test, and a node-component signature diverging from its sibling — but per the no-folding rule that does **not** count as gate 5 having run.

### 6 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
Whole-branch review (13 commits, `b9f4eb7f..92b5872c`) on the most capable model, against the spec's eight locked decisions, with the per-task deferred minors handed over for triage. **1 Blocking, 6 Should-fix, 8 Nits, 5 missing-test gaps.**
The Blocking finding was **this file's absence** — `CHECKLIST.md` had been flipped to `[x]` while the only record of the slice's verification sat in gitignored scratch, so no completion claim was citable. Fixed by writing this ledger and `gate-findings.yaml`.
One consolidated fix wave (`6cd82963`, `b05cc2a6`) closed **F1–F8 and all four nits**; a scoped re-review verdicted every one ADDRESSED with file:line evidence and found no new breakage. Highlights: the explorer sidebar now resolves a System through the existing `useSystem` hook instead of rendering a bare UUID under a comment that falsely claimed no such query existed; `ExplorerEdge.type` is narrowed to the generated wire union and both `"partOf"` comparison sites route through one exported constant (a typo previously compiled and would have rendered "No members yet." for a populated System); and `systemBoundaryBox`'s size assertions are now exact rather than an inequality that let arithmetic mutations survive.
**Two findings were deliberately excluded from the fix wave** and are recorded as open decisions, not silent discards — see `gate-findings.yaml` and the note below.
**At:** `b05cc2a6`

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING — has not run. Not folded into 6 or 8.

### 8 — `deep-review`
**Status:** ⏳ PENDING as a distinct gate. The gate-6 review was run against the spec, the ADRs and the tests and produced the fixed-schema output, but `/deep-review` itself has not been dispatched.

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ⏳ PENDING — owed after gates 5, 7 and 8 run and apply any fixes. The frontend half is already green on `b05cc2a6` (988/988, tsc exit 0); the solution build is the missing piece.

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
**Status:** ⏳ PENDING — not pushed, no PR. `scripts/ci-local.sh` (the required pre-push Release mirror) has not been run either.

## Impact analysis

Per CLAUDE.md the plan carries an `## Impact Analysis (LSP)` section. For this slice it is **`N/A` for C#** — no existing C# symbol changed — and the TypeScript blast radius is **grep-grounded and labelled as such**, because there is no language server for `.ts`/`.tsx` in this repo. The plan's table classifies all 16 `RelationshipKind` hits plus two structural consumers that grep alone under-reports (`EntityGraphNode.tsx` and `graphLayout.ts` reach the type through `GraphNodeData` and never name it). Gate 6 independently swept every `Record<...>` lookup and kind-keyed `switch` in `web/src` and found no site left on the wrong side of the split.
