# DoD Ledger — <Slice / Topic>

**Slice:** `<date>-<topic>` · **Branch:** `<branch>` · **HEAD:** `<short-sha>`
**PR:** <#NN / url> · **Last updated:** <YYYY-MM-DD>
**Spec:** `docs/superpowers/specs/<date>-<topic>-design.md`
**Plan:** `docs/superpowers/plans/<date>-<topic>.md`
**Findings telemetry:** `./gate-findings.yaml` — per-gate issues × severity × real/delusion (copy from `templates/gate-findings-template.yaml`)

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.
> This table records each gate's **status**; what each gate **found** (and whether it was real) goes in `gate-findings.yaml`.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ⏳ PENDING | — |
| 2 Per-task subagent reviews | ⏳ PENDING | — |
| 3 Full suite (+ real-seam if wiring) | ⏳ PENDING | — |
| 4 Container build (images CI) | ⏳ PENDING | — |
| 5 `/simplify` | ⏳ PENDING | — |
| 6 Mutation (conditional) | ⏳ PENDING | — |
| 7 `requesting-code-review` | ⏳ PENDING | — |
| 8 `review-pr` | ⏳ PENDING | — |
| 9 `deep-review` | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| 10 Visual / API verification (ADR-0084) | ⏳ PENDING | — |
| 11 CI green on PR (`ci-local.sh` = pre-push mirror) | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ⏳ PENDING
**Evidence:** <command + output excerpt, or CI run URL>
**At:** <commit / date>

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ⏳ PENDING
**Evidence:** <subagent ids / linked report files>
**At:** <commit / date>

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ⏳ PENDING
**Evidence:** <command + counts, or CI run URL. Note real-seam N/A with reason if frontend-only>
**At:** <commit / date>

### 4 — Container build (images CI job)
**Status:** ⏳ PENDING
**Evidence:** <CI "Container images" check URL>
**At:** <commit / date>

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING
**Evidence:** <link to simplify.md / findings summary>
**At:** <commit / date>

### 6 — Mutation loop (conditional: Domain/Application changes only)
**Status:** ⏳ PENDING
**Evidence:** <score + survivors, or N/A reason (no Domain/Application change)>
**At:** <commit / date>

### 7 — `requesting-code-review` at slice boundary
**Status:** ⏳ PENDING
**Evidence:** <link to requesting-code-review.md / findings>
**At:** <commit / date>

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING
**Evidence:** <link to review-pr.md / PR review>
**At:** <commit / date>

### 9 — `deep-review`
**Status:** ⏳ PENDING
**Evidence:** <link to deep-review.md>
**At:** <commit / date>

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ⏳ PENDING
**Evidence:** <command + output / CI run URL>
**At:** <commit / date>

### 10 — Visual / API verification (observe the running system)
**Status:** ⏳ PENDING
**Evidence:** <UI: screenshot(s) of the changed surface under verification/<slice>/ + console-clean note; API: live request/response captured against the running stack. Or N/A reason (no runtime surface — docs/pure refactor). Distinct from gate 3 (automated tests).>
**At:** <commit / date>

### 11 — CI green on the PR (terminal; `scripts/ci-local.sh` = required pre-push mirror)
**Status:** ⏳ PENDING
**Evidence:** <PR CI run URL (all jobs green — the runner is the source of truth) + pre-push `ci-local.sh` result. A CI-only failure → fix determinism, don't re-push blindly.>
**At:** <commit / date>
