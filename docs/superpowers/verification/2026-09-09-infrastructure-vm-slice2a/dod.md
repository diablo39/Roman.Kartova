# DoD Ledger — Infrastructure/VM slice 2a (fields + mutation)

**Slice:** `2026-09-09-infrastructure-vm-slice2a` · **Branch:** `feat/catalog-infrastructure-vm-slice2a` · **HEAD:** `ebf7303`
**PR:** #87 (https://github.com/diablo39/Roman.Kartova/pull/87) · **Last updated:** 2026-09-09
**Spec:** `docs/superpowers/specs/2026-09-09-infrastructure-vm-slice2a-fields-mutation-design.md`
**Plan:** `docs/superpowers/plans/2026-09-09-infrastructure-vm-slice2a-fields-mutation.md`
**Findings telemetry:** `./gate-findings.yaml`

> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A. Status here; what each gate FOUND is in `gate-findings.yaml`.
> Executed subagent-driven (9 tasks + finishing gates). Reviews/reports live in `.superpowers/sdd/2026-09-09-infrastructure-vm-slice2a-fields-mutation/` (gitignored scratch); durable evidence below.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-09-09 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-09-09 |
| 3 Full suite (+ real-seam) | ✅ PASS (per-slice suites; whole-solution at terminal re-verify) | 2026-09-09 |
| 4 Container build (images CI) | ✅ PASS (local `docker compose build`) | 2026-09-09 |
| 5 `/simplify` | ✅ PASS | 2026-09-09 |
| 6 `requesting-code-review` | ✅ PASS | 2026-09-09 |
| 7 `review-pr` | ✅ PASS | 2026-09-09 |
| 8 `deep-review` | ✅ PASS | 2026-09-09 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-09-09 |
| 9 Visual / API verification (ADR-0084) | ✅ PASS (found + fixed VM-create bug, re-verified live) | 2026-09-09 |
| 10 CI green on PR | ✅ PASS | 2026-09-09 |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS — `dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true` → 0 warnings / 0 errors, confirmed at every task commit and the gate-7 fix (`361a710`).
**At:** 361a710 / 2026-09-09

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS — all 9 plan tasks reviewed (spec-compliance + code-quality) by a fresh subagent per task; T7 (JSONB sort) took 1 fix round (2 Criticals fixed 36852d8). Reports: `.superpowers/.../task-N-report.md` + progress ledger.
**At:** per task / 2026-09-09

### 3 — Full test suite (unit + arch + integration; real-seam)
**Status:** ✅ PASS (per-slice) — `Kartova.Catalog.Tests` 356/356, `Kartova.Catalog.Infrastructure.Tests` 11/11, `Kartova.Catalog.IntegrationTests` 460/460 (real Postgres/RLS + real JWT via Testcontainers), `Kartova.ArchitectureTests` 69/69, web vitest catalog green. Whole-solution run at terminal re-verify.
**At:** 361a710 / 2026-09-09

### 4 — Container build (images CI job)
**Status:** ✅ PASS — `docker compose build` built `kartova/web:dev`, `kartova/api:dev`, `kartova/migrator:dev` (migrator carries the provider column + 6 partial-index migration). Web-image codegen live-fetch fell back to the committed `openapi-snapshot.json` (expected — API not up at build time; snapshot already carries the new endpoints).
**At:** a40c668 / 2026-09-09

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS — 4 cleanup agents; 7 accepted cleanups applied (`27cc450`, net -193 lines: VmSortSpecs.IdEquals, shared VmFormFields, alias shared sort specs, test dedup); scoped re-review confirmed all 7 behavior-preserving. 2 out-of-scope items deferred (SharedKernel nullable-keyset general fix; shared ConcurrencyCapture via EF metadata).
**At:** 27cc450 / 2026-09-09

### 6 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS — whole-branch opus review: 0 critical; 1 Important (int-cast indexes not EXPLAIN-verified) + minors fixed (`fa76a97`); scoped re-review clean.
**At:** fa76a97 / 2026-09-09

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS — 5 specialized reviewers (code/tests/errors/types/comments). Found what prior gates missed (validates no-folding): 1 Critical (VM-attribute-400 silent failure), 1 Important (AllInfrastructure provider-sort no-op), Mediums + test/comment gaps — 15-item fix wave applied (`361a710`); scoped re-review all addressed. 1 delusion rejected (ValidateAttributes JSON vs ADR-0115). teamId-select bug deferred to gate 9.
**At:** 361a710 / 2026-09-09

### 8 — `deep-review`
**Status:** ✅ PASS — 0 blocking / 2 should-fix / 2 nits / 2 missing-test / 5 good; conforms to spec §3 + implicated ADRs. Report: `./deep-review.md`. SF1 (JSONB COALESCE) deferred to slice-4 with ruling + documented invariant; SF2 (this ledger) backfilled; nits/missing-tests fixed (gate-8 fix commit).
**At:** 361a710 (+ gate-8 fix) / 2026-09-09

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ✅ PASS — `dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true` 0/0. Whole-solution `dotnet test Kartova.slnx` mass-failed every integration assembly at once (Catalog/Org/Audit/Identity, 100% each, ~40s) = the documented Docker-saturation flake (concurrent Testcontainers exhaust the Docker host), NOT a regression; unit + arch assemblies passed. Re-ran the slice's assembly in isolation: `Kartova.Catalog.IntegrationTests` **465/465 PASS**. CI (ubuntu, gate 10) is the real arbiter for the full-solution parallel run.
**At:** a40c668 / 2026-09-09

### 9 — Visual / API verification (observe the running system)
**Status:** ✅ PASS — live stack (built images) + claude-in-chrome, in-SPA (`gate-9-verification.md`). Confirmed: provider column + JSONB sort headers, isRowHeader invariant (overlay no blank-page), multi-IP render, Register VM dialog + provider field. **Found a real Critical the automated gates missed:** VM create via SPA was broken ("Team is required" even with a team selected — RHF useController on native `<select>`); fixed (`ebf7303`, mirror RegisterApplicationDialog useState pattern + regression test) and **re-verified live** (VM created via SPA, provider AWS shown end-to-end). API PUT/DELETE/GET + EXPLAIN covered by real-seam integration (gate 3/7/8).
**At:** ebf7303 / 2026-09-09

### 10 — CI green on the PR (terminal; `scripts/ci-local.sh` pre-push mirror)
**Status:** ✅ PASS — pre-push Release build 0/0. PR **#87** CI all green (run 34369913055): Backend (arch+unit+integration) SUCCESS 3m30s, Container images SUCCESS 2m1s, Frontend (test+typecheck+build) SUCCESS 3m12s, Stryker config drift SUCCESS, Helm SUCCESS. The ubuntu Backend job passing green confirms the local whole-solution mass-fail was Docker-saturation (concurrent Testcontainers), not a regression.
**At:** 35f575b / 2026-09-09
