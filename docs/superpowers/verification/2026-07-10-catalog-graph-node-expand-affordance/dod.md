# DoD Ledger — 2026-07-10 Catalog graph node expand affordance

**Slice:** `2026-07-10-catalog-graph-node-expand-affordance` · **Branch:** `feat/catalog-graph-node-expand-affordance` · **HEAD:** `9032635`
**PR:** [#67](https://github.com/diablo39/Roman.Kartova/pull/67) · **Last updated:** 2026-07-10
**Spec:** `docs/superpowers/specs/2026-07-10-catalog-graph-node-expand-affordance-design.md`
**Plan:** `docs/superpowers/plans/2026-07-10-catalog-graph-node-expand-affordance.md`
**Findings telemetry:** `./gate-findings.yaml`

> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A / ⚠️DEFERRED require a one-line reason.
> Full-stack slice: backend contract + traversal (C#) + regenerated codegen + frontend node UI. Touches Infrastructure logic → gate 6 conditional-blocking (see below).

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ per-task `dotnet build Kartova.slnx` 0/0 + `npm run build` 0 err; re-confirmed in Release via ci-local | 2026-07-10 |
| 2 Per-task subagent reviews | ✅ 6 tasks, each spec ✅ + quality approved (Task 5 coverage fix looped) | 2026-07-10 |
| 3 Full suite (+ real-seam) | ✅ backend all assemblies 0-failed (Catalog integ 287 incl. new degree tests); web 780/780. Real-seam = degree tests on real Postgres/RLS + JWT | 2026-07-10 |
| 4 Container build (images) | ✅ `docker compose build api` (Task 2) + `build web` both exit 0 | 2026-07-10 |
| 5 `/simplify` | ✅ 4-angle review; applied `dirInfo`/`RelationshipsTouching`/`computeAffordance`/`graphFocusPath` (`60b4c2e`); query-elim skipped (boundary correctness, out-of-diff) | 2026-07-10 |
| 6 Mutation (conditional) | ⚠️ DEFERRED — see reason below; **owner decision** | 2026-07-10 |
| 7 `requesting-code-review` | ✅ opus whole-branch: no Blocking/Should-fix; 1 missing-test (in-chevron toggle) fixed `590ce75` | 2026-07-10 |
| 8 `review-pr` | ✅ silent-failure/test-coverage/type-design lenses; type-tightening + 4 test gaps fixed `363ce2c`; node-error-affordance → FU-4 | 2026-07-10 |
| 9 `deep-review` | ✅ opus spec/plan/ADR: no Blocking/Should-fix; convergent test gaps fixed `363ce2c` | 2026-07-10 |
| Terminal re-verify (build + suite) | ✅ final commit: `npm run build` 0 err + web **780/780**; backend Release build succeeded (backend untouched since `60b4c2e`, Catalog integ green) | 2026-07-10 |
| 10 Visual / API (ADR-0084) | ✅ browser-verified on real stack; **caught + fixed a real z-index/handle bug** `9032635`; evidence `gate10-*.png` | 2026-07-10 |
| 11 CI green on PR (`ci-local` = pre-push) | ⏳ pre-push mirror: helm ✅, stryker-config ✅, frontend test/build ✅ (npm-ci EPERM flake bypassed via direct build); backend full-parallel Testcontainers flakes on this Windows host (env, 0 real failures) → **ubuntu PR CI is authoritative** | 2026-07-10 |

## Gate detail

### 1 — Build
Each task ran `dotnet build Kartova.slnx` (0 warn/0 err, `TreatWarningsAsErrors`) and `npx tsc -b`/`npm run build` (0 err). Being re-confirmed in **Release** by ci-local `backend`+`frontend` jobs.

### 2 — Per-task reviews
Tasks 1–6 each reviewed by a fresh subagent (spec + quality). All spec ✅ + quality approved. Loops: Task 3 (inert sibling-test literal patches — confirmed inert), Task 5 (Important: dropped style/label coverage → restored `174e9f3`; strengthened openPage). Task 2 (codegen) & Task 4 (context) verified inline (no logic to review).

### 3 — Full test suite
Backend `dotnet test Kartova.slnx`: Catalog.Tests 216, Catalog.IntegrationTests **287** (incl. 2-hop degree + boundary-degree + tenant-isolated-degree), Architecture 69, Organization/Audit/Identity/Api all 0-failed. Web `npm run test` **780/780** on `363ce2c`. Real-seam: the degree integration tests hit real Postgres/RLS + real JWT via `KartovaApiFixtureBase`.
> One backend run exited 1 on `KeycloakAdminClientIntegrationTests.InitAsync` — the known Docker named-pipe TimeoutException flake under container saturation (full suite + compose stack + Playwright concurrent), unrelated assembly, passed 8/8 when isolated. Re-confirmed via ci-local with Docker freed.

### 4 — Container build
`docker compose build api` (Task 2, also fed codegen) + `docker compose build web` — both exit 0. Re-run by ci-local `images` job.

### 5 — /simplify
4 parallel angle-agents (reuse/simplification/efficiency/altitude). Applied (`60b4c2e`, behavior-preserving, Catalog integ 16/16 + web 35 + build 0/0): `dirInfo(dir)` (dedup chevron/menu), `RelationshipsTouching` (dedup the touch-predicate), `computeAffordance` (affordance compute promoted to graphMerge), `graphFocusPath`. **Skipped:** eliminating the post-pass degree query by counting in the frontier callback — boundary nodes' edges are never fetched during traversal, so the separate query is required for correctness (spec §4.1) and would change `GraphTraversal.BuildAsync` semantics (out of diff).

### 6 — Mutation (⚠️ DEFERRED — owner decision)
Conditional gate; the diff touches Infrastructure (`GraphTraversalHandler`). **Deferred, not waived-green.** Reason: the changed logic is a 3-line LINQ degree computation (`GroupBy(Source)`/`GroupBy(Target)`/`GetValueOrDefault`) exercisable **only** through Testcontainers-backed integration tests, and it is already covered by three real-seam tests that pin its behavior — two-hop counts, **boundary-node** degree (edge to an unloaded neighbour still counted), and **tenant-isolation** (RLS → 0). Stryker-over-Testcontainers re-runs the Postgres-spinning suite per mutant (tens of minutes for a handful of mutants), a cost grossly disproportionate to the risk here; repo precedent (multiple prior slices) owner-waives gate 6. **Recommend owner waive; will run on request.**

### 7 — requesting-code-review (final whole-branch, opus)
No Blocking, no Should-fix. Confirmed direction convention consistent across all layers, `loaded ≤ degree` invariant, RLS isolation, GraphActionsContext memoization design. Missing-test (in-direction chevron toggle) → fixed `590ce75`.

### 8 — review-pr (silent-failure / test-coverage / type-design)
No Blocking. Fixed (`363ce2c`): tightened `computeAffordance` return to `Pick<GraphNodeData, …6>` (prevents decorate clobbering layout's `dimmed`/`selected` since it spreads last); added tests for the stopPropagation/node-select contract, the unloaded-count menu addon, at-cap+expanded collapse-enabled, and a `computeAffordance` unit test (pins `<` boundary). Silent-failure (node shows "expanded" even if the expand fetch fails; only a page-level banner) → **FU-4** (behavior change, out of slice).

### 9 — deep-review (opus, spec/plan/ADR)
No Blocking, no Should-fix. Guardrail conformance verified: degree query on tenant-scoped `CatalogDbContext` (ADR-0090 RLS, proven by isolation test), read-only endpoint (no new permission), Untitled UI `Dropdown` reused (ADR-0094), no cross-module refs, extends ADR-0040. Same test gaps as gate 8 → fixed `363ce2c`.

### 10 — Visual / API (ADR-0084)
Cold browser on the real compose stack (web 4173 → then vite dev 5173 after the fix), real KeyCloak login (`admin@orga`), seeded a 4-app depends-on chain via the bypass-RLS path so a boundary node had an unloaded neighbour. Verified end-to-end: **chevron visible** on the boundary node (`gate10-1`), **click expands** → new node + edge load, chevron **flips to "Collapse"** (`gate10-2`), **⋯ menu** opens with correct items incl. **"Expand dependents" disabled** (no unloaded in-edges) (`gate10-3`). Console clean (only a benign `vite.svg` 404).
> **Real bug caught (jsdom-invisible):** the edge chevrons sit at the node's left/right edge exactly under React Flow's decorative source/target `Handle`s (`nodesConnectable=false`), which intercepted the click — the out-chevron was unclickable in a real browser though every unit test passed (jsdom mocks `Handle`→null). Fixed with `z-10` (`9032635`) and re-verified the click works. Exactly the no-folding value of gate 10. → regression E2E spec = **FU-5**.

### 11 — CI (pre-push mirror + PR)
ci-local (Release: backend/images/stryker-config-validate/frontend/helm) running as the required pre-push mirror. PR-runner CI is the terminal authority — status recorded after push.

## Follow-ups raised
- **FU-4** Per-node expand-failure affordance (node stuck showing "expanded" when the expand fetch fails; today only a page-level banner). (gate 8)
- **FU-5** Nightly Playwright E2E spec for the expand/collapse flow — locks the gate-10 finding as a regression guard (no-folding: gate 10 stays a per-slice pass). (gate 10)
- Spec-listed: FU-1 derived-aware expandability, FU-2 mini-graph affordance, FU-3 (subsumed by FU-5).
