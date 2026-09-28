# DoD Ledger — E-01b.F-03.S-02 web-admin app shell

**Slice:** `2026-09-28-e01b-s2-web-admin-shell` · **Branch:** `feat/e01b-s2-web-admin-shell` · **HEAD:** `d337205`
**PR:** <#NN / url> · **Last updated:** 2026-09-28
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
| 6 `requesting-code-review` | ⏳ PENDING | — |
| 7 `review-pr` | ⏳ PENDING | — |
| 8 `deep-review` | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| 9 Visual / API verification (ADR-0084) | ⏳ PENDING | — |
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
**Status:** ⏳ PENDING
**Evidence:** <link to requesting-code-review.md / findings>
**At:** <commit / date>

### 7 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING
**Evidence:** <link to review-pr.md / PR review>
**At:** <commit / date>

### 8 — `deep-review`
**Status:** ⏳ PENDING
**Evidence:** <link to deep-review.md>
**At:** <commit / date>

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ⏳ PENDING
**Evidence:** <command + output / CI run URL>
**At:** <commit / date>

### 9 — Visual / API verification (observe the running system)
**Status:** ⏳ PENDING
**Evidence:** <UI: screenshot(s) of the changed surface under verification/<slice>/ + console-clean note; API: live request/response captured against the running stack. Or N/A reason (no runtime surface — docs/pure refactor). Distinct from gate 3 (automated tests).>
**At:** <commit / date>
**Note:** Playwright MCP failed to connect during brainstorming (2026-09-27); reconnect before gate 9.

### 10 — CI green on the PR (terminal; `scripts/ci-local.sh` = required pre-push mirror)
**Status:** ⏳ PENDING
**Evidence:** <PR CI run URL (all jobs green — the runner is the source of truth) + pre-push `ci-local.sh` result. A CI-only failure → fix determinism, don't re-push blindly.>
**At:** <commit / date>
