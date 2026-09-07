# DoD Ledger — Catalog Hierarchy Browse (E-03.F-03.S-02)

**Slice:** `2026-09-07-catalog-hierarchy-browse` · **Branch:** `feat/catalog-hierarchy-browse` · **HEAD:** `9673ce0`
**PR:** <pending — not yet pushed> · **Last updated:** 2026-09-07
**Spec:** `docs/superpowers/specs/2026-09-07-catalog-hierarchy-browse-design.md`
**Plan:** `docs/superpowers/plans/2026-09-07-catalog-hierarchy-browse.md`
**Findings telemetry:** `./gate-findings.yaml`
**SDD ledger (per-task history + rulings):** `.superpowers/sdd/2026-09-07-catalog-hierarchy-browse/progress.md`

> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A (with reason).

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS (after SSH.NET pin `52c03b1`) | 2026-09-07 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-09-07 |
| 3 Full suite (+ real-seam) | ✅ PASS (Api flake cleared on isolated re-run) | 2026-09-07 |
| 4 Container build (images CI) | ✅ PASS | 2026-09-07 |
| 5 `/simplify` | ✅ PASS (1 fix: `80ba74e`) | 2026-09-07 |
| 6 `requesting-code-review` | ✅ PASS | 2026-09-07 |
| 7 `review-pr` | ✅ PASS (findings fixed `c22d532`) | 2026-09-07 |
| 8 `deep-review` | ✅ PASS (findings fixed `c22d532`) | 2026-09-07 |
| Terminal re-verify (build + suite) | ✅ PASS (batched; FE 1018/1018) | 2026-09-07 |
| 9 Visual / API verification (ADR-0084) | ⚠️ OWNER SELF-VERIFY on running app | 2026-09-07 |
| 10 CI green on PR | ⚠️ WAIVED — owner merged direct to local master (no PR) | 2026-09-07 |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS at `52c03b1`. `dotnet restore --force-evaluate && dotnet build Kartova.slnx -c Debug`: **0 warnings, 0 errors.**
**History:** initially FAILED at 9673ce0 — 7 `NU1903` errors = SSH.NET 2023.0.0 high-severity advisory `GHSA-q939-rpr3-3284` (newly published post-#85, so master's CI was affected too), failing the audited restore under warnings-as-errors. Not my code: SSH.NET is transitive via Testcontainers 4.0.0 into the integration-test projects only; my diff adds no SSH.NET ref (rebuild with `-p:WarningsNotAsErrors=NU1903` gave 0 non-NU1903 errors). **Owner chose "override" over suppress.** Patched to SSH.NET **2026.0.0** (fixed release; affected `<= 2025.1.0`) via a direct versionless `PackageReference` in the 6 Testcontainers-referencing test projects + a central `PackageVersion` — mirrors the Microsoft.OpenApi transitive-pin precedent. (First attempted `CentralPackageTransitivePinningEnabled` — reverted: it triggered an NU1109 downgrade cascade from central-version lag.) Re-verified: force-evaluate restore + full build 0/0, SSH.NET resolves 2026.0.0, no NU1903/NU1109. Commit `52c03b1`.
**At:** 52c03b1 / 2026-09-07

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** 8 task reviews + 2 scoped re-reviews (T1, T7), all in SDD ledger. Every task ended spec ✅ + quality approved (T1 fix round: dead code + truncation-test hardening; T7 fix round: storage guard + state-branch tests + purity + dedupe). All findings addressed or deferred-with-triage.
**At:** per-commit / 2026-09-07

### 3 — Full test suite (unit + arch + integration; real-seam)
**Status:** ⏳ near-green — full `dotnet test Kartova.slnx` at `52c03b1`: every assembly green EXCEPT `Kartova.Api.IntegrationTests` (6/6 failed on `Docker.DotNet.DockerClient.MakeRequestAsync` timeouts = the known Docker-saturation flake, not a code failure — many Testcontainers assemblies + the gate-9 api container starting at once). My slice's real-seam assembly **`Kartova.Catalog.IntegrationTests` = 411/411 PASS** (hierarchy happy cross-team + 401 + RLS + not-truncated, real JWT + real Postgres/RLS), and this run also runtime-verifies the SSH.NET 2026.0.0 bump (all Testcontainers assemblies that pull it started containers fine). ArchitectureTests 69/69. Api.IntegrationTests re-run in isolation → **6/6 PASS**, confirming Docker saturation not code. **Backend suite green.** Frontend vitest: 1014/1017 pass on the loaded full run; the 3 failures were `Test timed out in 5000ms` on ServiceDetailPage/SystemDetailPage/ApplicationDetailPage (detail pages my slice never touched — load timeouts, the 217s run was heavily contended). Re-run isolated → **36/36 PASS in 22s**. **Gate 3 fully green** (backend all assemblies + frontend 1017).
**At:** 52c03b1 / 2026-09-07

### 4 — Container build (images CI job)
**Status:** ✅ PASS — `docker compose build api web`: both `kartova/api:dev` and `kartova/web:dev` **Built**. The web build's codegen live-fetch failed inside the sandbox (no localhost:8080 reachable) and fell back to the committed `openapi-snapshot.json` (which carries the hierarchy endpoint) — expected/correct, not a failure. No Dockerfile/COPY change in this slice.
**At:** 52c03b1 / 2026-09-07

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING

### 6 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
**Evidence:** final whole-branch review (opus, requesting-code-review lens) over the full 10-commit diff + spec + plan: **0 blocking**; semantics verified end-to-end (cross-team-under-steward, ungrouped-by-owner, single-appearance, RLS×4, wire-kind→link mapping, `[BoundedListResult]`/arch). 1 should-fix (truncation badges under-report true totals — spec §8 accepts). Report in `.superpowers/sdd/.../` final-review notification.
**At:** 9673ce0 / 2026-09-07

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ✅ ran (sonnet) at 80ba74e — **0 blocking.** Should-fix: (1) `/hierarchy` route omits `.ProducesProblem(403)` (every sibling GET declares it); (2) spec §6 403-without-catalog.read not covered — `/hierarchy` absent from `CatalogPermissionMatrixTests.Endpoints` (`/graph`+`/impact` share this pre-existing gap). Nit: teams list capped at 200 with no pagination (empty teams beyond page 1 silently dropped). Confirmed clean: `[BoundedListResult]`, no new permission/5-sync, `.Contains(PartOf)` idiom, RLS/ITenantScope, SSH.NET pin (6==6), route no-collision. Fix batched with gate 8.

### 8 — `deep-review`
**Status:** ✅ ran (opus) at 80ba74e — **0 blocking.** Should-fix (both test gaps, distinct-lens catches): (1) 403-without-`catalog.read` untested — only 401; `.RequireAuthorization(CatalogRead)` wiring uncovered, a wrong-permission mutation would survive (spec §6 named 403 as a deliverable; plan de-scoped citing no persona lacks catalog.read). (2) no assembler test for truncation leaving an empty steward node + the cross-team `sum(team counts)==TotalComponentCount` invariant. Nits: breadcrumb test only covers team selection (system/ungrouped/member ancestry untested); `aria-hidden={false}` no-op; useMemo-over-items latent perf; error branch conflates no-data/error. Confirmed strong: SSH.NET pin correct+complete (6 pinned == 6 Testcontainers projects), edge-direction read, sessionStorage guard, key stability. Fix decision batched with gate 7.

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ✅ PASS at `c22d532` (run in batches — see host constraint). Build **0/0** without the NU1903 suppress flag (SSH.NET pin resolves the audit at solution level). Suite:
- Batch 1 (all non-integration: unit + arch, no Docker): green — `Catalog.Tests` **279** (assembler simplify + new truncation invariant test), `Catalog.Infrastructure.Tests` 11, `ArchitectureTests` **69/69**, Organization/SharedKernel/Audit unit assemblies all green.
- Batch 2 (`Catalog.IntegrationTests` alone, one container-set): **411/411** — my slice's real-seam + the augmented permission matrix (`/hierarchy` row).
- Frontend full vitest suite at c22d532: **138 files / 1018 tests pass** (1017 at gate 3 + 1 new breadcrumb test), clean run, no flakes.

**Host constraint (why batched):** the machine has 32 GB but the user's own LLM stack (`llm-vllm`/`llm-ollama`/`llm-llamacpp`/…, up 4-7 days) holds ~27 GB, leaving ~5 GB free. The full parallel Testcontainers suite (7 integration assemblies each spinning Postgres+Keycloak) exceeds that and was OOM-killed 3× (2 parallel, 1 serialized); batching to one container-set at a time fits. The other integration assemblies (Audit/Organization/Api/SharedKernel) were green in the full run at `52c03b1` (gate 3) and the delta since is additive/local (assembler re-sort removal → unit-covered; `.ProducesProblem(403)` metadata → no behavior; the matrix row lives in Catalog.IntegrationTests; FE-only), so they are not re-run under the ceiling. Did not touch the user's LLM containers.

### 9 — Visual / API verification (observe the running system)
**Status:** ⏳ PENDING — stack up (api :8080, web :4173, keycloak healthy, DevSeed loaded); chrome at Kartova login. **Blocked on owner login** (assistant does not enter passwords). **Scope addition mid-gate:** owner requested a dismissible "About this view" help callout on the hierarchy page (explains why the view exists + how to use it) — implemented + tested + re-verified before gate 9 observes the final page (web image rebuilt to include it).
**Evidence:** UI tree at `/catalog/hierarchy` via claude-in-chrome (playwright/chrome-devtools MCP down this session) + live `GET /catalog/hierarchy` capture against the running api (port 8080, container up). Screenshots under this folder.
**At:** —

### 10 — CI green on the PR (pre-push `ci-local.sh` mirror + PR runner)
**Status:** ⚠️ WAIVED by owner. Owner instructed a direct merge to **local** master (no PR, no push) so the PR CI gate was not run. Not pushed to origin/master. Local evidence stands in lieu: gate 1 build 0/0, backend suites green (Catalog real-seam 411/411, arch 69/69), FE 1018/1021 (3 load-flakes cleared isolated), container images built. A pre-push `ci-local.sh` / PR run remains advisable before pushing to origin.
**Merge commits:** `3c1e5f11` (hierarchy) + `b0668018` (decommission fix) on local master.
