# DoD Ledger — TD-015 / TD-016 / TD-017

**Slice:** `2026-09-29-td-015-016-017` · **Branch:** `chore/tech-debt-td-015-016-017` · **HEAD:** `65d0c10`
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
| 6 `requesting-code-review` | ⏳ PENDING | — |
| 7 `review-pr` | ⏳ PENDING | — |
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

### 10 — CI green on the PR (terminal; `scripts/ci-local.sh` = required pre-push mirror)
**Status:** ⏳ PENDING
**Evidence:** <PR CI run URL (all jobs green — the runner is the source of truth) + pre-push `ci-local.sh` result. A CI-only failure → fix determinism, don't re-push blindly.>
**At:** <commit / date>
