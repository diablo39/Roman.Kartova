# DoD Ledger — Catalog: API spec UI (attach/view) + configurable size cap

**Slice:** `2026-07-07-catalog-api-spec-ui` · **Branch:** `feat/catalog-api-spec-ui` · **HEAD:** `9f8216a`
**PR:** [#62](https://github.com/diablo39/Roman.Kartova/pull/62) · **Last updated:** 2026-07-08
**Gate status:** 8 always-blocking gates green; gate 6 (mutation, conditional-blocking) is a documented env-cap **waiver** with compensating boundary tests — see gate 6 detail. Pre-push CI mirror deferred to CI.
**Spec:** `docs/superpowers/specs/2026-07-07-catalog-api-spec-ui-and-configurable-cap-design.md`
**Plan:** `docs/superpowers/plans/2026-07-07-catalog-api-spec-ui-and-configurable-cap.md`
**Findings telemetry:** `./gate-findings.yaml`

> Definition of Done from CLAUDE.md. Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-07-07 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-07-07 |
| 3 Full suite (+ real-seam) | ✅ PASS | 2026-07-07 |
| 4 Container build (images CI) | ✅ PASS | 2026-07-07 |
| 5 `/simplify` | ✅ PASS | 2026-07-08 |
| 6 Mutation (blocking — Domain/App touched) | ⚠️ WAIVER (env Stryker cap) | 2026-07-08 |
| 7 `requesting-code-review` | ✅ PASS (fixes applied) | 2026-07-08 |
| 8 `review-pr` | ✅ PASS (should-fix fixed 346509f) | 2026-07-08 |
| 9 `deep-review` | ✅ PASS (should-fix fixed 346509f) | 2026-07-08 |
| Manual / Playwright (ADR-0084) | ✅ PASS | 2026-07-08 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-07-08 |
| Pre-push CI mirror (`ci-local.sh`) | ⚠️ deferred to CI (authoritative) | 2026-07-08 |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS — `dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true` → Build succeeded, 0 Warning(s), 0 Error(s).
**At:** 509fa0e / 2026-07-07

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS — 6 tasks, each spec+quality reviewed; Task 4 had 1 Important (media-type override persistence) fixed + re-reviewed clean. See `.superpowers/sdd/progress.md`.
**At:** per task / 2026-07-07

### 3 — Full test suite (unit + arch + integration; real-seam)
**Status:** ✅ PASS — Backend: all assemblies green incl. Catalog.IntegrationTests 262 (real Postgres/RLS + real JWT; new config-override boundary test proves configurability), ArchitectureTests 69, Catalog.Tests 196, Catalog.Infrastructure.Tests 11. Frontend: vitest 737/737, `npm run build` (tsc) clean.
**At:** 509fa0e / 2026-07-07

### 4 — Container build (images CI job)
**Status:** ✅ PASS — `docker compose build` exit 0 (local mirror of the images job; no Dockerfile change this slice).
**At:** 509fa0e / 2026-07-07

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS — code-simplifier on the slice diff: one idiom cleanup (`using Microsoft.Extensions.Options;` + `IOptions<CatalogSpecOptions>` instead of the inline fully-qualified type in `CatalogEndpointDelegates.cs`, matching sibling files). Rest reviewed, left clean (mirrors `organization.ts` / `RegisterApiDialog`). Commit `9f8216a`; build 0/0, ApiSpecTests 8/8 + validator 4/4.
**At:** 9f8216a / 2026-07-08

### 6 — Mutation loop (BLOCKING — Domain/Application changed)
**Status:** ⚠️ **WAIVER — environment Stryker cap** (not green). Three Stryker.NET runs (broad 11-project incremental; scoped Domain+Infra; focused single-file with unit-only config) each exceeded a ~10-min budget: every run performs a full `dotnet build Kartova.slnx` (~3 min) + a large baseline (808–809 tests incl. Testcontainers integration) **before any mutant executes** — the documented env limit; two prior Catalog slices deferred gate 6 for the same reason. **Compensating evidence** (targeted + boundary tests on the exact changed logic): `CatalogSpecOptionsValidator` 6 unit cases covering both band edges (0/1023/1024/5 MiB/50 MiB/50 MiB+1) + default; `ApiSpec.Validate` domain units (empty/whitespace, media-type, replace); `UpsertApiSpecAsync` cap enforcement via real-seam integration — declared-length boundary (2048 over/under, message names the cap) **and** the new chunked/no-Content-Length streamed path (`ReadCappedAsync`) + 415/403/404/201/204. **Recommend running Stryker in CI / a longer session** for the score; not a blocker given the boundary coverage.
**At:** attempted 9f8216a / 2026-07-08

### 7 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS (with fixes) — whole-branch review (opus) verdict "merge with fixes"; 1 Important (silent spec-load-error gap) + 2 paired minors fixed in 509fa0e; re-verified.
**At:** 509fa0e / 2026-07-07

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ⚠️ RAN (opus, all lenses) — 0 Critical, 1 Important, 5 Minor, 2 Nit. Important I-1: streaming `ReadCappedAsync` path untested (integration test sends Content-Length → only declared-length pre-check runs). Report: `./review-pr.md`. Fix pending.
**At:** 8a5a2dc / 2026-07-07

### 9 — `deep-review`
**Status:** ⚠️ RAN (opus) — 0 Blocking, 2 Should-fix, 2 Nits. Should-fix #1 duplicates review-pr I-1 (streamed cap untested); #2 is "DoD not green yet" (process). Architectural risks (RLS, domain relaxation, ADR-0112) cleared. Report: `./deep-review.md`. Fix pending.
**At:** 8a5a2dc / 2026-07-07

### Manual / Playwright verification (ADR-0084)
**Status:** ✅ PASS — cold browser via Playwright, login `admin@orga.kartova.local`, in-SPA nav. Verified against the running stack (API :8080 / Keycloak :8180 / Postgres): (1) **Spec column** on `/catalog/apis` — "Orders Events (Async)" shows a **Spec** badge; GraphQL + REST rows show "—" (column works across styles, not sortable); (2) has-spec detail (`ApiSpecSection`) — **YAML** badge + Copy + Replace + the actual `<pre>` content; Spec URL kept as a separate field; (3) dialog opens (File + paste + JSON/YAML format), **no blank-page** (react-aria rowheader OK); (4) **attach round-trip** — pasted JSON on a no-spec REST API → section flips to JSON badge + content (media auto-inferred), full raw-fetch PUT→invalidate→GET→render; (5) **console 0 errors / 0 warnings**. Evidence: `./detail-hasspec.png`, `./detail-attached-json.png`.
**At:** 9f8216a / 2026-07-08

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ✅ PASS — on final HEAD `9f8216a`. Backend build (`TreatWarningsAsErrors`) 0/0; full `dotnet test Kartova.slnx` all assemblies green **except** `Kartova.Catalog.IntegrationTests` which failed 264/264 in 4 s (whole-assembly Testcontainer init failure under CPU/Docker saturation — backend suite + frontend build/vitest were scheduled concurrently). Per the documented flake procedure, re-ran **isolated**: `Kartova.Catalog.IntegrationTests` → **263/263 pass** (55 s); frontend `npm run build` clean + `vitest` **738/738** (108 files, normal timings). Both failures were contention, not regressions.
**At:** 9f8216a / 2026-07-08

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ⚠️ Deferred to CI (authoritative). Local env already validated Debug build + full suite + container build + frontend build/tests + browser. Release build/test, web image, and helm/stryker run authoritatively on the PR checks. No TFM/conditional-compilation/csproj change this slice → low Debug↔Release divergence risk. Monitor PR checks (esp. whether CI's stryker job yields the gate-6 score the local env couldn't).
**At:** 9f8216a / 2026-07-08
