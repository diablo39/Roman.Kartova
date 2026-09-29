# DoD Ledger — TD-015 / TD-016 / TD-017

**Slice:** `2026-09-29-td-015-016-017` · **Branch:** `chore/tech-debt-td-015-016-017` · **HEAD:** `e9630cf`
**PR:** <#NN / url> · **Last updated:** 2026-09-29
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
| 3 Full suite (+ real-seam if wiring) | ⏳ PENDING | — |
| 4 Container build (images CI) | ✅ PASS | 2026-09-29 |
| 5 `/simplify` | ✅ PASS (advisory; 3 applied, 8 skipped) | 2026-09-29 |
| 6 `requesting-code-review` | ✅ PASS (with fixes) | 2026-09-29 |
| 7 `review-pr` | ✅ PASS (with fixes) | 2026-09-29 |
| 8 `deep-review` | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| 9 Visual / API verification (ADR-0084) | ⏳ PENDING | — |
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
**Status:** ⏳ PENDING
**Evidence:** <command + counts, or CI run URL. Note real-seam N/A with reason if frontend-only>
**At:** <commit / date>

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

### 10 — CI green on the PR (terminal; `scripts/ci-local.sh` = required pre-push mirror)
**Status:** ⏳ PENDING
**Evidence:** <PR CI run URL (all jobs green — the runner is the source of truth) + pre-push `ci-local.sh` result. A CI-only failure → fix determinism, don't re-push blindly.>
**At:** <commit / date>
