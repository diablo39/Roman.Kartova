# DoD Ledger — TD-015 / TD-016 / TD-017

**Slice:** `2026-09-29-td-015-016-017` · **Branch:** `chore/tech-debt-td-015-016-017` · **HEAD:** `e8213be`
**PR:** pending push · **Last updated:** 2026-09-29 · **Terminal commit:** e8213be (terminal re-verify + gate 9; later commits are ledger-only)
**Spec:** `docs/superpowers/specs/2026-09-29-td-015-016-017-design.md`
**Plan:** `docs/superpowers/plans/2026-09-29-td-015-016-017-plan.md`
**Findings telemetry:** `./gate-findings.yaml` — per-gate issues × severity × real/delusion (copy from `templates/gate-findings-template.yaml`)

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.
> This table records each gate's **status**; what each gate **found** (and whether it was real) goes in `gate-findings.yaml`.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-09-29 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-09-29 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS | 2026-09-29 |
| 4 Container build (images CI) | ✅ PASS | 2026-09-29 |
| 5 `/simplify` | ✅ PASS (advisory; 3 applied, 8 skipped) | 2026-09-29 |
| 6 `requesting-code-review` | ✅ PASS (with fixes) | 2026-09-29 |
| 7 `review-pr` | ✅ PASS (with fixes) | 2026-09-29 |
| 8 `deep-review` | ✅ PASS (with fixes) | 2026-09-29 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-09-29 |
| 9 Visual / API verification (ADR-0084) | ✅ PASS | 2026-09-29 |
| 10 CI green on PR (`ci-local.sh` = pre-push mirror) | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
**Evidence:** `cmd //c "dotnet build Kartova.slnx"` → `0 Warning(s) 0 Error(s)` — `gate1-build.txt`. Re-run at terminal re-verify.
**At:** 65d0c10 / 2026-09-29

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** subagent-driven-development — fresh implementer + task reviewer (spec ✅ + quality) per task, Tasks 0–7, all Approved.
- Task 0: 1 Important (stale `HEAD` field) ruled a non-defect (a commit can't record its own SHA; HEAD tracks the verified code commit).
- Task 5: fix round 1 (unrestored `vi.stubGlobal`), re-review ADDRESSED.
- Task 1: post-task fix round (a Task-7 full-suite run caught `RestVerbPolicyRules.No_endpoint_uses_PATCH_verb` failing — the sibling arch host lacked `AddHealthChecks()`/`AddOpenApi()`; fixed 65d0c10, re-review ADDRESSED; controller re-ran the arch project 95/95).
- Deferred minors handed to gate 6.
**At:** 65d0c10 / 2026-09-29

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ✅ PASS
**Evidence (final code, e8213be — `terminal-reverify.txt`):**
- **Backend, 1819/1819 across 14 projects** (run per project, `dotnet test --no-build`):
  - arch 95;
  - Catalog.IntegrationTests 516;
  - Organization.IntegrationTests 154;
  - Api.IntegrationTests 36, incl. `HealthCheckEndpointTests` — real Postgres + real JWT seam; `/health/detailed` 401 for a tenant-realm user; version/OpenAPI/CORS unchanged;
  - the remaining 10 unit/integration projects.
- **Frontend:** `npx tsc -b` exit 0; `npx vitest run` 173 files / 1261 tests; `npm run build` + `npm run build:admin` OK.
- **E2E-impact trigger:** every page load now fetches `/config.js`. Full `e2e/run.sh` re-run on the final code: **11/11 passed** (`e2e-run.txt`). An earlier run at 757980c was also 11/11.
- **Planted-violation proofs** (temporary edits, each FAILed as expected, then reverted):
  1. Removed `RequireAuthorization` on `/health/detailed` → `Every_ops_admin_route_requires_PlatformAdminOnly` FAIL.
  2. Direct `app.MapGet` in Program.cs → `Program_maps_no_routes_directly` FAIL.
  3. `OpsAdminRoutes = []` → `PlatformAdminOnly_is_used_only_under_the_admin_prefix` FAIL (HealthDetailed).
  4. `app.MapAdminModule("x")` → guard FAIL (gate 6).
  5. `app.Map("/x", …)` → guard FAIL (gate 6).
  6. `app.UseHealthChecks("/x")` → guard FAIL (gate 8).
  7. CSP grep pattern broken → `check-runtime-config.sh` FAIL (gate 7).
  Full outputs are in the SDD task/fix reports.
- **Regression caught in this gate:** `RestVerbPolicyRules.No_endpoint_uses_PATCH_verb` failed after Task 1 (sibling arch host lacked AddHealthChecks/AddOpenApi). Fixed in 65d0c10.
**At:** e8213be / 2026-09-29

### 4 — Container build (images CI job)
**Status:** ✅ PASS
**Evidence:** `bash scripts/ci-local.sh images` → `images PASS` incl. new `check-runtime-config(kartova/web:ci): OK` and `check-runtime-config(kartova/web-admin:ci): OK` — `gate4-images.txt`. CI job confirms at gate 10.
**At:** 65d0c10 / 2026-09-29
**Note:** applies — `web/Dockerfile` changes

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS (advisory)
**Evidence:** `simplify.md` — 4 agents, 11 findings, each vetted: 3 applied (dead usings, single `reauthenticate` path, admin panel Try-again test), 8 skipped with reasons.
**At:** 65d0c10 / 2026-09-29

### 6 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS (with fixes)
**Evidence:** `requesting-code-review.md` — whole branch f0a62b1..18c6178, most-capable-model reviewer: With fixes — 0 Critical · 1 Important · 7 Minor.
- **Fixed in 1e954d8:** I1 — the `Program_maps_no_routes_directly` regex missed `.Map(` and `MapAdminModule(`; widened to `\.Map(?!Endpoints\()\w*\(` and proven by planted `MapAdminModule` + `.Map(` violations. Also removed the last dead `using` and documented the unsafe config chars (`'` `"` `$` `\`) plus a `VITE_*` upgrade note.
- **Ruled, not fixed:** retry briefly flashes the app (kept, so the existing "Sign-in unavailable" panels still handle a failed retry redirect); ops rule vacuous when the allowlist is empty (the prefix rule catches that); combined scheme on `/health/detailed` (pinned by the real-seam integration test).
- **Deferred:** T2, T3, T6b–e and T7 minors, each marked can-defer.
- Scoped re-review: all addressed, no new breakage.
**At:** 1e954d8 / 2026-09-29

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS (with fixes)
**Evidence:** `review-pr.md`. Agents: standing set (code-reviewer, pr-test-analyzer, type-design-analyzer) plus silent-failure-hunter, since the diff adds try/catch and auth-error branches. comment-analyzer skipped (code-heavy diff).
- code-reviewer: 0 new findings.
- pr-test-analyzer: all 5 Review Focus items pinned.
- Fixed in e9630cf:
  - R1: production `console.warn` once per key when falling back to the built-in default.
  - R2: container check now asserts the CSP header still carries `script-src 'self'`; the new assert was proven discriminating.
  - R3: container check fails fast when the container dies.
  - R4: no `curl | grep -q` pipelines left (closes gate-6 deferred T3).
- Skipped with reasons: R5–R8.
- Scoped re-review: all addressed, no new breakage.
**At:** e9630cf / 2026-09-29

### 8 — `deep-review`
**Status:** ✅ PASS (with fixes)
**Evidence:** `deep-review.md`: 0 blocking, 2 should-fix, 5 nits, 2 missing tests, 5 good. No mutation report for this slice.
- **SF1 (ADR-0113 and its README row stale; bundled with nit 5, ADR-0060):** owner previewed and approved the amendment wording; applied in e8213be.
- **SF2 (ledger behind):** `gate-findings.yaml` populated (43 entries); gate 3 row now cites the e2e run and the planted-violation proofs; e2e re-run at the terminal re-verify.
- **Fixed in 7eebead:**
  - Nit 1: a tokenless 401 never trips the breaker (`hadToken`).
  - Nit 2: spec synced.
  - Nit 3: the Program.cs guard also catches `UseHealthChecks` / `UseEndpoints`, proven by a planted violation.
  - New test: config reaches the four consumers (`runtimeConfigConsumers.test.ts`).
- **Nit 4:** `tech-debt.md` status gets the PR number and ledger path at gate 10.
- Scoped re-review: all addressed, no new breakage.
**At:** e8213be / 2026-09-29

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ✅ PASS
**Evidence:** `terminal-reverify.txt`, all on e8213be after the last fix wave (gate 8, 7eebead) and the ADR notes:
- `dotnet build Kartova.slnx`: 0 warnings / 0 errors.
- Backend: 1819/1819.
- Frontend: tsc 0 errors; vitest 1261/1261; both builds OK.
- E2E: 11/11.
**At:** e8213be / 2026-09-29

### 9 — Visual / API verification (observe the running system)
**Status:** ✅ PASS
**Evidence:** `gate9-visual.md`, `gate9-api.txt`, and 4 screenshots. Playwright MCP, driven in-SPA.
- **TD-016:** the same images were recreated with a non-default `KARTOVA_API_BASE_URL=http://127.0.0.1:8080` (no rebuild). `/config.js` rendered it with `Cache-Control: no-store`. Every API call from both SPAs went to `127.0.0.1:8080`.
- **TD-017:** a forced persistent authed 401 gave exactly 1 SSO round-trip, then the panel, in both SPAs. The panel is centred. Try again recovers once the 401 is removed.
- **TD-015:**
  - `/health/detailed`: tenant token 401, anonymous 401, operator token 200.
  - live, version and OpenAPI routes answer 200 anonymous.
  - The live OpenAPI doc shows `GetVersion` with tag `SystemEndpoints`.
**At:** e8213be code / 2026-09-29

### 10 — CI green on the PR (terminal; `scripts/ci-local.sh` = required pre-push mirror)
**Status:** ⏳ PENDING — pre-push mirror green; PR CI run pending push
**Evidence:** pre-push `scripts/ci-local.sh` on 481e635 (2026-09-29):
- `gate10-ci-local-a.txt`: stryker PASS; helm PASS (lint 0 failed, `render-check: OK`); images PASS (incl. both `check-runtime-config` OK).
- `gate10-ci-local-b.txt`: frontend PASS (173 files / 1261 tests, typecheck, both builds).
- `gate10-ci-local-c.txt`: backend PASS (Release build 0 warnings / 0 errors; 15 test runs successful).
- e2e job is opt-in; it is covered by the terminal re-verify (11/11).
**At:** 481e635 / 2026-09-29
