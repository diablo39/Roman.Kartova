# DoD Ledger — DoD Ledger Convention + Verification Consolidation

**Slice:** `2026-06-26-dod-ledger-convention` · **Branch:** `feat/catalog-dependency-mini-graph` · **HEAD:** `38f39c1` (+ ledger/spec-fix commit)
**PR:** rides on [#46](https://github.com/diablo39/Roman.Kartova/pull/46) (human chose current branch) · **Last updated:** 2026-06-26
**Spec:** `docs/superpowers/specs/2026-06-26-dod-ledger-convention-design.md`
**Plan:** `docs/superpowers/plans/2026-06-26-dod-ledger-convention.md`

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.
> Slice surface: docs + 1 stop-hook (`.claude/hooks/dod-check.js`, not compiled into the solution) + 1 `.cs` doc-comment + a `git mv` migration. No production business code.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ⏳ PENDING | 2026-06-26 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-06-26 |
| 3 Full suite (+ real-seam if wiring) | ⏳ PENDING | 2026-06-26 |
| 4 Container build (images CI) | N/A | 2026-06-26 |
| 5 `/simplify` | ⏳ PENDING | 2026-06-26 |
| 6 Mutation (conditional) | N/A | 2026-06-26 |
| 7 `requesting-code-review` | ✅ PASS | 2026-06-26 |
| 8 `review-pr` | ⏳ PENDING | 2026-06-26 |
| 9 `deep-review` | ⏳ PENDING | 2026-06-26 |
| Manual / Playwright (ADR-0084) | N/A | 2026-06-26 |
| Terminal re-verify (build + suite) | ⏳ PENDING | 2026-06-26 |
| Pre-push CI mirror (`ci-local.sh`) | ⏳ PENDING | 2026-06-26 |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ⏳ PENDING
**Evidence:** Only compiled change is a one-line doc-comment in `tests/Kartova.Testing.Auth/KartovaApiFixtureBase.cs` (path repoint) — cannot affect compilation. Real evidence pending the pre-push CI mirror / CI on push.
**At:** 2026-06-26

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** Tasks 2 (hook) and 5 (migration) each got a full spec+quality subagent review (both Approved; Task-2 Important "dead EVIDENCE_RE" fixed in `619e0f8`). Tasks 1/3/4 (doc transcription) controller-verified. Recorded in `.superpowers/sdd/progress.md`.
**At:** 38f39c1, 2026-06-26

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ⏳ PENDING
**Evidence:** No test-affecting change (hook `.js` is not in the solution; `.cs` change is comment-only; rest is docs/renames). Suite evidence pending CI on push. Real-seam **N/A** (no HTTP/auth/DB wiring).
**At:** 2026-06-26

### 4 — Container build (images CI job)
**Status:** N/A
**Evidence:** No Dockerfile / dependency / `COPY` change in this slice.
**At:** 2026-06-26

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING
**Evidence:** Pending — see decision note below.
**At:** 2026-06-26

### 6 — Mutation loop (conditional)
**Status:** N/A
**Evidence:** No C# Domain/Application logic change.
**At:** 2026-06-26

### 7 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS
**Evidence:** Final whole-branch review (opus) over the convention slice `6b9bc3f..38f39c1` — **Ready to merge: Yes**, 0 Critical / 0 Important. Verified empirically: `LEDGER_RE` matches the real ledger path shape; 59-file migration preserved history with zero dangling refs; `.cs` churn-free; backfill internally consistent. 2 Minors (spec §9 status drift — fixed; pre-existing README console.log mention — out of scope).
**At:** 38f39c1, 2026-06-26

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ⏳ PENDING
**Evidence:** Pending — see decision note below.
**At:** 2026-06-26

### 9 — `deep-review`
**Status:** ⏳ PENDING
**Evidence:** Pending — see decision note below.
**At:** 2026-06-26

### Manual / Playwright verification (ADR-0084)
**Status:** N/A
**Evidence:** No UI change.
**At:** 2026-06-26

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ⏳ PENDING
**Evidence:** Pending gates 1/3/5/8/9.
**At:** 2026-06-26

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ⏳ PENDING
**Evidence:** Run before push — this is also the canonical source of gate-1/3/4 evidence for this slice.
**At:** 2026-06-26

## Decision note (gates 5/8/9 for a docs/tooling slice)

The only executable artifact is the ~80-line `dod-check.js` stop hook (verified by a 4-case block/allow test, Task 2) + a `.cs` doc-comment + docs/migration. Gate 7 (final whole-branch review) ran clean over the whole slice. Whether to additionally run gate 5 (`/simplify`), gate 8 (`review-pr`), gate 9 (`deep-review`) on this surface — vs. rely on gate 7 + CI — is a human effort/thoroughness call pending confirmation.
