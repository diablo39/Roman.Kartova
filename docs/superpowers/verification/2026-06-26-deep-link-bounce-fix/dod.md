# DoD Ledger — Deep-link bounce fix (issue #47)

**Slice:** `2026-06-26-deep-link-bounce-fix` · **Branch:** `fix/deep-link-bounce-oidc-returnto` · **HEAD:** `073ba8d`
**Issue:** [#47](https://github.com/diablo39/Roman.Kartova/issues/47) · **PR:** _opened below_ · **Last updated:** 2026-06-26
**Spec/Plan:** none — bug fix root-caused via `superpowers:systematic-debugging` + TDD (not a full spec/plan slice).

> Records the Definition of Done from `CLAUDE.md`. Frontend-only fix to `web/src/app/providers.tsx` (~25 prod lines) + a test.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`tsc -b` + vite) | ✅ PASS | 2026-06-26 |
| 2 Per-task reviews (spec + quality) | ✅ PASS | 2026-06-26 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS | 2026-06-26 |
| 4 Container build (images CI) | N/A | 2026-06-26 |
| 5 `/simplify` | N/A (skipped) | 2026-06-26 |
| 6 Mutation (conditional) | N/A | 2026-06-26 |
| 7 `requesting-code-review` | ✅ PASS | 2026-06-26 |
| 8 `review-pr` | N/A (skipped) | 2026-06-26 |
| 9 `deep-review` | N/A (skipped) | 2026-06-26 |
| Manual / Playwright (ADR-0084) | ✅ PASS | 2026-06-26 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-06-26 |
| Pre-push CI mirror (`ci-local.sh`) | ✅ PASS | 2026-06-26 |

## Gate detail

### 1 — Build (`tsc -b` + vite)
**Status:** ✅ PASS
**Evidence:** `npm run build` — `tsc -b` clean, `✓ built in 11.27s`; React Flow still its own lazy chunk. (`tsc -b` is the binding type gate, ADR-0109.)
**At:** 073ba8d, 2026-06-26

### 2 — Per-task reviews (spec + quality)
**Status:** ✅ PASS
**Evidence:** TDD (RED→GREEN, 3 tests in `providers.test.tsx`) + a `pr-review-toolkit:code-reviewer` pass on the diff. One Important finding (401 handler held a stale `auth.signinRedirect` snapshot — same class as the token bug) addressed in `073ba8d` with a covering test.
**At:** 073ba8d, 2026-06-26

### 3 — Full test suite
**Status:** ✅ PASS
**Evidence:** `npm test` → 91 files, **628 tests passing** (post-fix terminal run). Real-seam **N/A** — frontend-only, no HTTP/auth/DB wiring added.
**At:** 073ba8d, 2026-06-26

### 4 — Container build (images CI job)
**Status:** N/A
**Evidence:** No Dockerfile / dependency / lockfile change. Web image re-runs on the PR CI regardless.
**At:** 2026-06-26

### 5 — `/simplify` against branch diff
**Status:** N/A (skipped, owner-approved standard)
**Evidence:** ~25-line single-file fix; quality covered by the gate-2 code review (no simplification findings).
**At:** 2026-06-26

### 6 — Mutation loop (conditional)
**Status:** N/A
**Evidence:** No C# Domain/Application change (frontend-only).
**At:** 2026-06-26

### 7 — `requesting-code-review` (code review)
**Status:** ✅ PASS
**Evidence:** code-reviewer verdict: ref-during-render correct + StrictMode-safe, returnTo mirrors RequireAuth with no new open-redirect surface, tests non-tautological. The one Important finding is fixed in `073ba8d`.
**At:** 073ba8d, 2026-06-26

### 8 — `review-pr` (pr-review-toolkit)
**Status:** N/A (skipped, owner-approved standard)
**Evidence:** Covered by gate 7 over the same ~25-line diff.
**At:** 2026-06-26

### 9 — `deep-review`
**Status:** N/A (skipped, owner-approved standard)
**Evidence:** Covered by gate 7; tiny single-file diff.
**At:** 2026-06-26

### Manual / Playwright verification (ADR-0084)
**Status:** ✅ PASS
**Evidence:** Cold-start Playwright MCP on the fix (web dev 5173, KeyCloak login):
- Logged-out deep-link to `/catalog/applications/:id` → login → **returns to the detail page** (returnTo restored).
- **Authenticated hard-reload (`page.goto`) to a detail URL → renders the detail page, no bounce** — the exact action that failed before.
- Empty-state intact; **console 0 errors** (the `/organizations/me/permissions` 401 that previously fired is gone).
**At:** 073ba8d, 2026-06-26

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ✅ PASS
**Evidence:** After the code-review fix: `npm run build` green + `npm test` 628/628 green.
**At:** 073ba8d, 2026-06-26

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ✅ PASS
**Evidence:** Local `tsc -b`+vite build + full vitest (628) are the frontend-job gates (1/3); the full CI frontend/images/helm jobs re-run on the PR push as the runner-side source of truth.
**At:** 073ba8d, 2026-06-26
