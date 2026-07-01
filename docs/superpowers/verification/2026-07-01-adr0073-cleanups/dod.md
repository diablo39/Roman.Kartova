# DoD Ledger — ADR-0073 cleanups (sunset override + successor) + FU-1

**Slice:** `2026-07-01-adr0073-cleanups` · **Branch:** `feat/catalog-adr0073-cleanups` · **HEAD:** implementation in progress (B-slice through 87f8f8b; C not started)
**PR:** (not opened yet) · **Last updated:** 2026-07-01
**Spec:** `docs/superpowers/specs/2026-07-01-adr0073-cleanups-successor-override-design.md`
**Plan:** `docs/superpowers/plans/2026-07-01-adr0073-cleanups-successor-override.md`
**Per-task tracker (SDD):** `.superpowers/sdd/progress.md` (per-task implement+review status; distinct from this DoD ledger)
**Findings telemetry:** `./gate-findings.yaml`

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.
> **Status: implementation in progress — NO gate has been run at slice scope yet. All PENDING.**

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ⏳ PENDING | — |
| 2 Per-task subagent reviews | ⏳ IN PROGRESS | 2026-07-01 |
| 3 Full suite (+ real-seam if wiring) | ⏳ PENDING | — |
| 4 Container build (images CI) | ⏳ PENDING | — |
| 5 `/simplify` | ⏳ PENDING | — |
| 6 Mutation (conditional) | N/A — user-waived | 2026-07-01 |
| 7 `requesting-code-review` | ⏳ PENDING | — |
| 8 `review-pr` | ⏳ PENDING | — |
| 9 `deep-review` | ⏳ PENDING | — |
| Manual / Playwright (ADR-0084) | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| Pre-push CI mirror (`ci-local.sh`) | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ⏳ PENDING — per-task builds were green (0W/0E) through each commit, but the authoritative slice-scope build runs at terminal re-verify after all tasks + gates 5–9.
**Evidence:** (pending)

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ⏳ IN PROGRESS — fresh task-reviewer subagent per task (subagent-driven-development). Completed with Spec ✅ Approved: A1, B1, B2, B3, B4. Pending: B5, C1–C7.
**Evidence:** per-task verdicts in `.superpowers/sdd/progress.md`; notable findings in `./gate-findings.yaml`.
**At:** through 87f8f8b

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ⏳ PENDING — per-task suites green in isolation; the full-solution suite run is at terminal re-verify. Real-seam applies (wiring: override authz endpoint, successor endpoint) — real JWT + Postgres/RLS via `KartovaApiFixtureBase`.
**Evidence:** (pending)

### 4 — Container build (images CI job)
**Status:** ⏳ PENDING — runs in CI (`docker compose build`) / `ci-local.sh`. Note: web image codegen for the new decommission/successor DTOs (see gate-findings if codegen deferred locally).
**Evidence:** (pending)

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING
**Evidence:** (pending)

### 6 — Mutation loop (conditional)
**Status:** N/A — **user-waived for this slice** (explicit directive, 2026-07-01: "skip mutating tests in this slice"). The diff does touch Domain/Application logic (`Application.Decommission`/`Deprecate`/`SetSuccessor`, handlers), which would normally make this gate blocking; the slice owner (Roman) has waived it. Domain behavior is instead covered by the TDD unit tests (ApplicationLifecycleTests, ApplicationSuccessorTests) + real-seam integration tests, and by gates 7–9.
**Evidence:** user directive; no mutation run performed.

### 7 — `requesting-code-review` at slice boundary
**Status:** ⏳ PENDING (this is the SDD final whole-branch review, on the most capable model)
**Evidence:** (pending)

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING
**Evidence:** (pending)

### 9 — `deep-review`
**Status:** ⏳ PENDING
**Evidence:** (pending)

### Manual / Playwright verification (ADR-0084)
**Status:** ⏳ PENDING — UI changed (decommission override checkbox; successor picker + set-successor dialog + detail link). Cold-start dev server → verify in browser before claiming done.
**Evidence:** (pending)

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ⏳ PENDING
**Evidence:** (pending)

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ⏳ PENDING
**Evidence:** (pending)
