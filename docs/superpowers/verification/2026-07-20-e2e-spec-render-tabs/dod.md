# DoD Ledger — E2E spec-render read-only + tab-switch specs (FU-1s)

**Slice:** `2026-07-20-e2e-spec-render-tabs` · **Branch:** `feat/e2e-spec-render-tabs` · **HEAD:** `b9e7587`
**PR:** _(not yet opened)_ · **Last updated:** 2026-07-20
**Spec:** `docs/superpowers/specs/2026-07-20-e2e-spec-render-tabs-design.md`
**Plan:** `docs/superpowers/plans/2026-07-20-e2e-spec-render-tabs.md`
**Findings telemetry:** `./gate-findings.yaml`

> Test-only + dev-fixture slice: no production/business logic changed. DevSeed adds a fixed-id API + OpenAPI spec fixture; two new nightly Playwright specs consume it. **Docker is unavailable on this host**, so gates that need it (integration tier of 3, container build 4, E2E run 10, ci-local half of 11) are CI/nightly-pending — recorded honestly below, not claimed green.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — reason given.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ solution build 0W/0E | 2026-07-20 |
| 2 Per-task subagent reviews | ✅ Tasks 1–4 each spec+quality reviewed (Task 3 had 1 fix loop → clean) | 2026-07-20 |
| 3 Full suite (unit+arch local; integration = CI) | ⏳ integration tier needs Docker → CI (gate 11); no C# logic touched | — |
| 4 Container build (images CI) | ⏳ migrator image builds the fixture — runs on PR CI | — |
| 5 `/simplify` | ⏳ not yet run | — |
| 6 Mutation (conditional) | N/A — no Domain/Application logic changed (DevSeed = fixture wiring) | 2026-07-20 |
| 7 `requesting-code-review` (final whole-branch) | ✅ opus reviewer: no Critical/Important; verified DevSeed SQL + selectors + rendered-vs-raw vs source | 2026-07-20 |
| 8 `review-pr` | ⏳ not yet run | — |
| 9 `deep-review` | ⏳ not yet run | — |
| Terminal re-verify (build) | ✅ `dotnet build Kartova.slnx -warnaserror` 0W/0E on `b9e7587` | 2026-07-20 |
| 10 Visual / API + E2E run (ADR-0084) | ⏳ **pending — Docker required**; E2E is nightly (not PR-CI), so first real exec is the user's `e2e/run.sh` or the nightly | — |
| 11 CI green on PR (`ci-local.sh` pre-push mirror) | ⏳ pending push/PR; ci-local also needs Docker | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ `cmd //c "dotnet build Kartova.slnx -warnaserror"` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
**At:** `b9e7587` / 2026-07-20 (Task 1 also built migrator + solution clean at `1b9b798`).

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ Every task reviewed by a fresh subagent (spec compliance + code quality):
- Task 1 (DevSeed): Approved, no Critical/Important; ⚠️ RunAsync-reachability resolved by controller (existing sunset/drift fixtures prove DevSeed runs in the compose stack).
- Task 2 (nav): Approved, no issues.
- Task 3 (read-only spec): 1 fix loop — reviewer caught a **real Critical** (read-only assertions used `toHaveCount(0)` on `display:none`-but-DOM-present Scalar elements → non-discriminating / false-red); fixed to `:visible` count-0; re-review Approved.
- Task 4 (tab spec): Approved; minors folded into the final-review fix wave.
**At:** commits `1b9b798`, `ce808f5`, `e08ccca..80848c4`, `a4505ab`.

### 3 — Full test suite
**Status:** ⏳ No C# logic changed (DevSeed fixture is not exercised by unit/arch tests). The integration tier (Testcontainers) and full suite run on PR CI (gate 11), which needs Docker unavailable here. Real-seam: the two E2E specs ARE the real-seam test for this slice (real Keycloak+Postgres+API/web images) — executed at gate 10.
**At:** —

### 4 — Container build (images CI job)
**Status:** ⏳ The migrator image seeds the Task-1 fixture; the `images` CI job builds it on the PR. No Dockerfile/COPY change in this slice.
**At:** —

### 5 — `/simplify`
**Status:** ⏳ Not yet run.
**At:** —

### 6 — Mutation loop
**Status:** N/A — diff touches no Domain/Application logic (DevSeed dev-fixture + Playwright test files only).
**At:** 2026-07-20

### 7 — `requesting-code-review` (final whole-branch)
**Status:** ✅ opus whole-branch review (`584efa3..a4505ab`): no Critical, no Important. Independently verified — against real source — the DevSeed column/param/enum/RLS mapping, every frontend selector, and the rendered-vs-raw default. All findings Minor / nightly-only false-red risks; actioned in the fix wave (`b9e7587`) except the deferred title-visibility item.
**At:** `a4505ab`; fixes `b9e7587`.

### 8 — `review-pr`
**Status:** ⏳ Not yet run.
**At:** —

### 9 — `deep-review`
**Status:** ⏳ Not yet run.
**At:** —

### 10 — Visual / API + E2E run (ADR-0084)
**Status:** ⏳ **Pending — Docker required.** The two specs verify only transpile+discovery locally (`npx playwright test --list` → 2 tests, no errors). The live run — `e2e/run.sh spec-render-readonly.spec.ts detail-tabs.spec.ts` — and the `curl` `hasSpec:true` check need the compose stack. Since E2E is the nightly net (not PR-CI), these specs first execute for real on the user's local Docker run or the nightly.
**Deferred item to resolve here:** `spec-render-readonly.spec.ts` title assertion `getByText("E2E Fixture API").first()` needs a visibility filter iff Scalar's live responsive DOM emits a hidden copy first — confirm/adjust against the real DOM during this run.
**At:** —

### 11 — CI green on PR
**Status:** ⏳ Pending push + PR. `ci-local.sh` (pre-push mirror) also needs Docker (web image + Testcontainers), so it too is CI-pending on this host.
**At:** —

## Honest status

**Implementation staged + reviewed; verification pending on Docker/CI.** Gates 1, 2, 7 green; 6 N/A. Gates 5, 8, 9 not yet run. Gates 3 (integration), 4, 10, 11 are blocked on Docker locally and covered by PR CI / the user's local E2E run / the nightly. **Not "complete"** until those land green.
