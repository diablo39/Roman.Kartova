# DoD Ledger — E-01b.F-03.S-02 web-admin app shell

**Slice:** `2026-09-28-e01b-s2-web-admin-shell` · **Branch:** `feat/e01b-s2-web-admin-shell` · **HEAD:** `c3800f6`
**PR:** <#NN / url> · **Last updated:** 2026-09-29
**Spec:** `docs/superpowers/specs/2026-09-28-e01b-s2-web-admin-shell-design.md`
**Plan:** `docs/superpowers/plans/2026-09-28-e01b-s2-web-admin-shell-plan.md`
**Findings telemetry:** `./gate-findings.yaml` — per-gate issues × severity × real/delusion (copy from `templates/gate-findings-template.yaml`)

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.
> This table records each gate's **status**; what each gate **found** (and whether it was real) goes in `gate-findings.yaml`.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-09-28 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-09-28 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS | 2026-09-28 |
| 4 Container build (images CI) | ✅ PASS | 2026-09-28 |
| 5 `/simplify` | ✅ PASS | 2026-09-28 |
| 6 `requesting-code-review` | ✅ PASS | 2026-09-28 |
| 7 `review-pr` | ✅ PASS | 2026-09-28 |
| 8 `deep-review` | ✅ PASS | 2026-09-28 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-09-29 |
| 9 Visual / API verification (ADR-0084) | ✅ PASS | 2026-09-29 |
| 10 CI green on PR (`ci-local.sh` = pre-push mirror) | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS
**Evidence:** `cmd //c "dotnet build Kartova.slnx"` → 0 Warning(s), 0 Error(s); web `npm run typecheck` (tsc -b) exit 0, `npm run build` + `npm run build:admin` OK. See `gate1-3-build-suite.txt`.
**At:** 4fca365 / 2026-09-28

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** subagent-driven-development — fresh implementer + task reviewer (spec ✅ + quality) per task, Tasks 0–10, all Approved. Task 5 went through 1 fix round (admin dev/preview HTML fallback was Accept-based → path-based, 64510a3; scoped re-review: addressed). Deferred minors + rulings are listed in the SDD ledger and handed to gate 6.
**At:** 3e654cb..4fca365 / 2026-09-28

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ✅ PASS
**Evidence:** `dotnet test Kartova.slnx --no-build -m:1` (compose stopped) → 1811 passed / 0 failed across 15 assemblies. Real seam: `CorsTests` (live KeyCloak token, real JwtBearer, 7 tests) + `PlatformRealmLiveTokenTests` (no-role operator → 403). Web `npm test` → 169 files / 1208 tests passed. E2E-impact trigger: full `e2e/` suite 11/11 (incl. 2 new admin specs) — `e2e-run.txt`. See `gate1-3-build-suite.txt`.
**At:** 4fca365 / 2026-09-28

### 4 — Container build (images CI job)
**Status:** ✅ PASS (local; CI `images` job confirms at gate 10)
**Evidence:** applies — `web/Dockerfile` + new `admin.conf.template` + compose service. `docker build` web / `APP=admin` / `APP=bogus` → 0 / 0 / non-zero; admin image contains only `admin.html`; container CSP + deep-link title verified — `gate4-images.txt`. Found + fixed: nginx stock `index.html` survived next to `admin.html` (bec75c1).
**At:** bec75c1 (no web/Docker input changed after) / 2026-09-28

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS (advisory)
**Evidence:** `simplify.md` — 4 angles (reuse/simplification/efficiency/altitude), 13 findings vetted: 5 applied in d337205 (shared `useApiAuthBridge`, `CenteredMessage`, `statusOf` → openapi-fetch-helpers, CORS `AddOriginPolicy`, dedicated nginx root `/usr/share/nginx/app` — no root build step), 8 skipped with reasons. Fix commit reviewed (Approved); re-verified: web 1213 tests + tsc + both builds, backend build 0w, CorsTests 7/7, both images rebuilt + compose smoke (`gate4-images.txt`, re-run section).
**At:** d337205 / 2026-09-28

### 6 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
**Evidence:** `requesting-code-review.md` — whole branch 2c03a2b..5fdd094, most-capable-model reviewer: With fixes — 0 Critical · 3 Important · 9 Minor. Fixed in 5e7b770 (VITE_* build ARGs + runbook, stale Helm/CSP docs, tenant-`*` guard, `/callback` no-param redirect, top-bar dashes, startup-guard host test, ADR row, favicon); TD-017 (persistent-401 loop, both SPAs); M6 accepted; M9 → PR description; I3 → terminal re-verify incl. full dotnet test + e2e. Scoped re-review: all addressed, no new breakage.
**At:** 5e7b770 / 2026-09-28

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS
**Evidence:** `review-pr.md` — standing set (type-design-analyzer, pr-test-analyzer, code-reviewer) + silent-failure-hunter (error-handling diff); comment-analyzer skipped (code-heavy diff). code-reviewer 0 findings; fixed in 453193d: redirect-promise rejection handling + 401 re-entrancy guard (shared hook, direct test), TopBarFrame blank-identity fallback with raw initials, session-error logging, 5 test gaps (post-logout URIs, fallback branches, boundary positive control, unknown-origin→admin preflight, symmetric Helm disable). Skips with rulings in `review-pr.md`. Scoped re-review: all addressed; 1 Low latent nit (sync-throw guard stick — `signinRedirect` is async, non-blocking). **Correction (gate 8 SF1):** the redirect-rejection `.catch` was dead code (library never rejects); re-fixed at gate 8 (11553e7).
**At:** 453193d / 2026-09-28

### 8 — `deep-review`
**Status:** ✅ PASS
**Evidence:** `deep-review.md` — 0 blocking · 2 should-fix · 5 nits · 4 missing tests. SF1 (controller-verified in node_modules): gate-7's redirect `.catch` was dead code — react-oidc-context 3.3.1 navigators resolve `null` + set `auth.error`, never reject → re-fixed with `auth.error`-driven guard reset + log and a "Sign-in unavailable" panel on the session-401 path. SF2: shared `RequireAuth` no longer loops `signinRedirect` when KeyCloak is unreachable (panel + Try again; `source === "signinRedirect"`). Nits (ADR amendment/phase wording, README row, coverage include, spec Impact heading + notes) and tests (admin-origin 401 carries ACAO — no token + tenant token; arch anti-vacuity) done. Fix 11553e7; scoped re-review: all addressed, no new Critical/Important.
**At:** 11553e7 / 2026-09-28

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ✅ PASS
**Evidence:** `terminal-reverify.txt` — `dotnet build` 0/0; `dotnet test Kartova.slnx -m:1` 1817/1817 (15 assemblies); web `npm test` 1231/1231, `tsc -b` 0, `build` + `build:admin` OK; images re-built (`docker compose build migrator api web web-admin`); full E2E 11/11 on the final stack. First background `e2e/run.sh` attempt was killed by the Claude Code memory-pressure reaper during image build (no test ran) — re-run in foreground stages.
**At:** c3800f6 / 2026-09-29

### 9 — Visual / API verification (observe the running system)
**Status:** ✅ PASS
**Evidence:** `gate9-visual.md` + `gate9-admin-landing.png`, `gate9-admin-no-access.png` — Playwright MCP (reconnected by the user), cold-started `dev:admin` on :5174 against the live stack: PKCE redirect to `kartova-platform`, operator landing in the master shell (session/me 200, console clean), user menu + sign-out, no-role user → No access (403, single call), production container :4174 deep link with enforcing CSP → 0 CSP violations.
**At:** c3800f6 / 2026-09-29

### 10 — CI green on the PR (terminal; `scripts/ci-local.sh` = required pre-push mirror)
**Status:** ⏳ PENDING (pre-push mirror ✅; PR CI not yet run)
**Evidence:** pre-push `scripts/ci-local.sh` on 8a744c8 (2026-09-29): backend PASS (Release build 0 warnings/0 errors, 15 test runs successful, 1817 tests), frontend PASS (170 files / 1231 tests, typecheck, build), helm PASS (lint 0 failed, `render-check: OK`), stryker PASS, images PASS (migrator, api, web, web-admin). e2e job opt-in — covered by the terminal re-verify (11/11). PR CI run URL: pending push.
**At:** 8a744c8 / 2026-09-29
