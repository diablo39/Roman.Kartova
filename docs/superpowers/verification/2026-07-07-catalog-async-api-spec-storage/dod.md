# DoD Ledger — 2026-07-07 Catalog AsyncAPI Spec Storage

**Slice:** `2026-07-07-catalog-async-api-spec-storage` · **Branch:** `feat/catalog-api-spec-storage` · **HEAD:** `030b3a9`
**PR:** TBD · **Last updated:** 2026-07-07
**Spec:** `docs/superpowers/specs/2026-07-07-catalog-async-api-spec-storage-design.md`
**Plan:** `docs/superpowers/plans/2026-07-07-catalog-async-api-spec-storage.md`
**Findings telemetry:** `./gate-findings.yaml`
**Reviews:** `./deep-review.md` (gate 9)

> Records the Definition of Done from `CLAUDE.md`. Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A (reason required).

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-07-07 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-07-07 |
| 3 Full suite (+ real-seam) | ✅ PASS | 2026-07-07 |
| 4 Container build (images CI) | ✅ PASS | 2026-07-07 |
| 5 `/simplify` | ✅ PASS | 2026-07-07 |
| 6 Mutation (conditional — Domain changed) | ✅ PASS | 2026-07-07 |
| 7 `requesting-code-review` (whole-branch) | ✅ PASS | 2026-07-07 |
| 8 `review-pr` (silent-failure lens) | ✅ PASS | 2026-07-07 |
| 9 `deep-review` | ✅ PASS (0 blocking) | 2026-07-07 |
| Manual / Playwright (ADR-0084) | N/A | backend-only slice; no UI (spec upload/view UI deferred to a follow-up per spec §7) |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-07-07 |
| Pre-push CI mirror (`ci-local.sh`) | ⏳ PENDING | run before opening the PR |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS — `dotnet build Kartova.slnx -c Debug` → 0 Warning(s), 0 Error(s).
**At:** 030b3a9 / 2026-07-07 (re-verified after each code-mutating gate).

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS — 9 tasks, each with a spec+quality reviewer; fixes looped to green. The Task-7 opus reviewer caught a Critical (charset Content-Type→415) that filtered per-task runs missed.
**At:** commits 8dcb6c3..030b3a9.

### 3 — Full test suite (unit + arch + integration; real-seam)
**Status:** ✅ PASS — wiring slice → real seam (real Postgres/RLS + real JWT via `KartovaApiFixtureBase`). Catalog integration 262/262, Catalog unit 199/199, Organization 142/142 (isolated). Note: `dotnet test Kartova.slnx` transiently flaked the Organization assembly with the known Docker named-pipe container-saturation `TimeoutException`; cleared by re-running that assembly in isolation (142/142).
**At:** 030b3a9 (Catalog) / 2026-07-07.

### 4 — Container build (images CI job)
**Status:** ✅ PASS — `docker compose build` (api + web + migrator, incl. `AddApiSpec` migration) exit 0.
**At:** built on the branch / 2026-07-07. (Pre-push `ci-local.sh` will re-run in Release.)

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS — applied `ca73d3a`: removed a redundant local `catch (ArgumentException)` (now relies on the global `DomainValidationExceptionHandler`, ADR-0091), dropped dead `ApiSpec.Replace` params. 3 findings skipped — one was a false positive that would have reintroduced the EF-translation 500.
**At:** ca73d3a / 2026-07-07.

### 6 — Mutation loop (conditional: Domain/Application changed → blocking)
**Status:** ✅ PASS — changed domain files 100%: `ApiSpec.cs` 13/13 valid mutants killed (initial 76.9% → 3 survivors: empty-creator guard, Replace validation, at-cap boundary → killed by 4 tests in `4caccdf`); `ApiMediaType.cs` 1/1. (Whole `Catalog.Domain` project = 78.57%, driven by pre-existing out-of-slice code.) Report: `StrykerOutput/targeted-apispec/run3/`.
**At:** 4caccdf / 2026-07-07.

### 7 — `requesting-code-review` (whole-branch, opus)
**Status:** ✅ PASS — verdict "ready to merge". One Important (FU-F concurrent double-PUT → 500, rare, no data corruption) consciously **deferred**. Minors triaged.
**At:** reviewed 8f0696e..37195ae / 2026-07-07.

### 8 — `review-pr` (pr-review-toolkit — silent-failure / error-handling lens)
**Status:** ✅ PASS — no findings. Verified fail-closed: `ReadCappedAsync` null→400; `MediaTypeHeaderValue.TryParse` fail→415; `api.spec.updated` audit write cannot commit without its ambient transaction; removed local catch correctly replaced by the global handler (`Program.cs`).
**At:** reviewed 8f0696e..4caccdf / 2026-07-07.

### 9 — `deep-review`
**Status:** ✅ PASS — 0 blocking, 2 should-fix (both process/evidence, now resolved: this ledger backfilled; `api.spec.updated` audit test added `030b3a9`), 5 nits, 2 missing-test (audit test added; other triaged). Report: `./deep-review.md`.
**At:** reviewed 8f0696e..4caccdf / 2026-07-07.

### Manual / Playwright verification (ADR-0084)
**Status:** N/A — backend-only slice; no UI surface (spec upload/view UI deferred to a follow-up per spec §7). The forced frontend touch (Task 8) is a type-level label + snapshot regen, covered by `tsc` + `registerApi` unit tests.

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ✅ PASS — on final commit: build 0/0; Catalog unit 199/199; Catalog integration 262/262 (incl. the new audit test).
**At:** 030b3a9 / 2026-07-07.

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ⏳ PENDING — run (Release build+test + web image + helm/stryker) before `git push` / opening the PR.
