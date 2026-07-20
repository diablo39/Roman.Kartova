# DoD Ledger — E2E spec-render read-only + tab-switch specs (FU-1s)

**Slice:** `2026-07-20-e2e-spec-render-tabs` · **Branch:** `feat/e2e-spec-render-tabs` · **HEAD:** `0a77ac0`
**PR:** [#77](https://github.com/diablo39/Roman.Kartova/pull/77) · **Last updated:** 2026-07-20
**Spec:** `docs/superpowers/specs/2026-07-20-e2e-spec-render-tabs-design.md`
**Plan:** `docs/superpowers/plans/2026-07-20-e2e-spec-render-tabs.md`
**Findings telemetry:** `./gate-findings.yaml`

> Test-only + dev-fixture slice: no production/business logic changed. DevSeed adds a fixed-id API + OpenAPI spec fixture; two new nightly Playwright specs consume it. **Docker is unavailable on this host**, so gates that need it (integration tier of 3, container build 4, E2E run 10, ci-local half of 11) are CI/nightly-pending — recorded honestly below, not claimed green.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — reason given.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ solution build 0W/0E (Debug) | 2026-07-20 |
| 2 Per-task subagent reviews | ✅ Tasks 1–4 each spec+quality reviewed (Task 3 had 1 fix loop → clean) | 2026-07-20 |
| 3 Full suite (unit+arch+integration) | ✅ Release: unit+arch all `Test Run Successful`; integration re-run isolated on idle Docker — Audit 35/35, Organization 142/142, Catalog 297/297. (First ci-local pass hit a Docker-saturation flake at container-init — no code cause, see detail.) | 2026-07-20 |
| 4 Container build (images CI) | ✅ `e2e/run.sh --build` built migrator/api/web green (twice); PR CI `images` job re-runs definitively | 2026-07-20 |
| 5 `/simplify` | ✅ `97c7fba` — DRY'd heading asserts to `FIXTURE_API_NAME`, dropped unused `API_DETAIL_URL`, collapsed dup comment | 2026-07-20 |
| 6 Mutation (conditional) | N/A — no Domain/Application logic changed (DevSeed = fixture wiring) | 2026-07-20 |
| 7 `requesting-code-review` (final whole-branch) | ✅ opus reviewer: no Critical/Important; verified DevSeed SQL + selectors + rendered-vs-raw vs source | 2026-07-20 |
| 8 `review-pr` | ✅ 4 reviewers (code/tests/errors/comments); 2 fixes applied (`0a77ac0`), 2 follow-ups filed | 2026-07-20 |
| 9 `deep-review` | ✅ `./deep-review.md` — no code-correctness defects; 1 nit (in-place unmount) fixed | 2026-07-20 |
| Terminal re-verify (build) | ✅ `dotnet build Kartova.slnx -warnaserror` 0W/0E on the final DevSeed change | 2026-07-20 |
| 10 Visual / API + E2E run (ADR-0084) | ✅ `e2e/run.sh` **2 passed** on the real stack — twice (initial + final code); migrator seeded fixture, Scalar read-only lock + tab-switch verified live | 2026-07-20 |
| 11 CI green on PR (`ci-local.sh` pre-push mirror) | ✅ PR #77 — all 5 checks pass (Backend 3m51s, Images 2m18s, Frontend 2m49s, Helm 9s, Stryker 5s); mergeState CLEAN. Pre-push mirror ran locally (gate 3/4). | 2026-07-20 |

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
**Status:** ✅ `ci-local.sh backend` (Release mirror): build 0 errors; all unit + architecture assemblies `Test Run Successful`. The 3 Testcontainers integration assemblies first failed **at assembly-init** (`IntegrationTestAssemblySetup.InitAsync … System.TimeoutException`, `DockerContainer.StartAsync`) — Docker-startup saturation after back-to-back compose builds, no test logic ran, DevSeed untouched by these tests. Re-run **sequentially on idle Docker** → all green: Audit 35/35, Organization 142/142, Catalog 297/297. Per CLAUDE.md "fix determinism / re-run before calling red" — confirmed flake, not a regression. PR CI (gate 11) is the authoritative full-suite run on a clean runner.
**At:** 2026-07-20 (integration re-run pid 1657)

### 4 — Container build (images CI job)
**Status:** ✅ `e2e/run.sh --build` (gate 10) ran `docker compose up -d --build migrator api web` green on both E2E runs — that IS the images build. The migrator image carries the Task-1 fixture. PR CI `images` job re-builds definitively at gate 11.
**At:** 2026-07-20

### 5 — `/simplify`
**Status:** ✅ 4 cleanup agents (reuse/simplification/efficiency/altitude). Applied: DRY the heading assertions to the exported `FIXTURE_API_NAME`, drop the unused `API_DETAIL_URL` export, collapse a duplicated read-only-lock comment. Skipped: a "shared C#↔TS fixture manifest" suggestion (over-engineering; contradicts the established hardcoded-id + doc-comment sync pattern).
**At:** `97c7fba` / 2026-07-20

### 6 — Mutation loop
**Status:** N/A — diff touches no Domain/Application logic (DevSeed dev-fixture + Playwright test files only).
**At:** 2026-07-20

### 7 — `requesting-code-review` (final whole-branch)
**Status:** ✅ opus whole-branch review (`584efa3..a4505ab`): no Critical, no Important. Independently verified — against real source — the DevSeed column/param/enum/RLS mapping, every frontend selector, and the rendered-vs-raw default. All findings Minor / nightly-only false-red risks; actioned in the fix wave (`b9e7587`) except the deferred title-visibility item.
**At:** `a4505ab`; fixes `b9e7587`.

### 8 — `review-pr`
**Status:** ✅ 4 specialized reviewers (code-reviewer / pr-test-analyzer / silent-failure-hunter / comment-analyzer). code + comments clean. Applied 2 (`0a77ac0`): DevSeed `ON CONFLICT DO UPDATE` (re-sync fixture content on reseed vs stale persistent volume); in-place Definition→Overview unmount assertions. Filed 2 follow-ups (AsyncAPI E2E; selector-drift tripwire — see Honest status).
**At:** `0a77ac0` / 2026-07-20

### 9 — `deep-review`
**Status:** ✅ report `./deep-review.md` (1 blocking / 1 should-fix / 1 nit / 1 missing-test / 5 good). No code-correctness defects — DevSeed SQL matches EF schema column-for-column, selectors verified, `:visible` fix confirmed. The "blocking" was a stale-ledger process observation (gate 5 already committed when it ran); the nit/missing-test (in-place unmount) fixed in `0a77ac0`.
**At:** `0a77ac0` / 2026-07-20

### 10 — Visual / API + E2E run (ADR-0084)
**Status:** ✅ `e2e/run.sh spec-render-readonly.spec.ts detail-tabs.spec.ts` → **2 passed** against the real compose stack (real Keycloak login, Postgres/RLS, migrator-seeded fixture, api+web images). Ran twice: once on the initial specs, once on the final code (after the `DO UPDATE` + in-place-unmount fixes) — green both times. This is the real-seam test for the slice.
**Deferred item — resolved:** the `getByText("E2E Fixture API").first()` title assertion passed against the live Scalar DOM; the hypothesized hidden-responsive-copy did not materialize, so no visibility filter was needed.
**At:** `0a77ac0` / 2026-07-20

### 11 — CI green on PR
**Status:** ⏳ Pending push + PR. `ci-local.sh` (pre-push mirror) also needs Docker (web image + Testcontainers), so it too is CI-pending on this host.
**At:** —

## Honest status

**COMPLETE — all ten blocking gates green.** Gates 1, 2, 3, 4, 5, 7, 8, 9, 10, 11 ✅; 6 N/A. Gate 10 (E2E) passed twice on the real stack; gate 11 (PR #77) all 5 CI checks pass, mergeState CLEAN. Gate 3's first ci-local run hit a Docker-saturation flake (documented + re-run green in isolation) — no code cause. Ready to merge.

### Follow-ups filed (not blocking this OpenAPI-focused FU-1 slice)
- **AsyncAPI read-only-lock E2E**: the CSS lock (`specRender.css`) claims OpenAPI+AsyncAPI coverage but only OpenAPI is E2E-tested; Scalar may render AsyncAPI with different DOM markers. Needs its own AsyncAPI fixture + spec.
- **`.scalar-client` selector-drift tripwire**: a bare-selector existence assert would catch Scalar renaming the client classes (which would make the `:visible` checks pass vacuously). Deferred — needs live-DOM confirmation of the bare count to avoid introducing a blind false-red.
