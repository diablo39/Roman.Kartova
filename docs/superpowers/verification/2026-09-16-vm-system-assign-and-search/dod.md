# DoD Ledger — VM System-side assign + server-side VM name search (TD-009 + TD-007)

**Slice:** `2026-09-16-vm-system-assign-and-search` · **Branch:** `feat/catalog-vm-system-assign-search` · **HEAD:** `<pending commit>`
**PR:** <pending> · **Last updated:** 2026-09-16
**Spec:** `docs/superpowers/specs/2026-09-16-vm-system-assign-and-search-design.md`
**Plan:** `docs/superpowers/plans/2026-09-16-vm-system-assign-and-search-plan.md`
**Findings telemetry:** `./gate-findings.yaml`

> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ⏳ PENDING | — |
| 2 Per-task subagent reviews | ⏳ PENDING | — |
| 3 Full suite (+ real-seam if wiring) | ⏳ PENDING | — |
| 4 Container build (images CI) | N/A | 2026-09-16 |
| 5 `/simplify` | ⏳ PENDING | — |
| 6 `requesting-code-review` | ⏳ PENDING | — |
| 7 `review-pr` | ⏳ PENDING | — |
| 8 `deep-review` | ⏳ PENDING | — |
| Terminal re-verify (build + suite) | ⏳ PENDING | — |
| 9 Visual / API verification (ADR-0084) | ⏳ PENDING | — |
| 10 CI green on PR | ⏳ PENDING | — |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ⏳ PENDING — Catalog.Infrastructure built clean (0 warn / 0 err) during dev; full-solution build at terminal re-verify.

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ⏳ PENDING — slice-boundary reviews (gates 6-8) cover the full diff; dedicated per-task review pass to run.

### 3 — Full test suite (real-seam if wiring)
**Status:** ⏳ PENDING — so far: `ListVmsHandlerFilterTests` 13/13 (unit); VM `displayNameContains` integ 10/10 real-Postgres (case-insensitive substring + wildcard-escaping); `AddSystemMemberDialog` 6/6 vitest. Full solution suite at terminal re-verify / CI.
**Real-seam:** ✅ — new `ListVms_filter_displayNameContains_*` tests hit real Postgres/RLS + real JWT via `KartovaApiFixtureBase`.

### 4 — Container build (images CI job)
**Status:** N/A — branch diff touches no `Dockerfile`, no `COPY`/`ADD` build input, and no restore surface (`*.csproj`/`Directory.Packages.props`/`nuget.config` untouched). Changed files: `.cs`, `openapi-snapshot.json`, `.ts`/`.tsx`, docs. Per CLAUDE.md gate-4 N/A rule.

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING

### 6 — `requesting-code-review` at slice boundary
**Status:** ⏳ PENDING

### 7 — `review-pr` (standing set: type-design + pr-test + code-reviewer)
**Status:** ⏳ PENDING — `silent-failure-hunter` N/A candidate (no new error-handling), `comment-analyzer` N/A (not comment-heavy) per gate-7 conditional rule.

### 8 — `deep-review`
**Status:** ⏳ PENDING

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ⏳ PENDING

### 9 — Visual / API verification (observe the running system)
**Status:** ⏳ PENDING — drive System detail → Members → Assign component → Infrastructure radio → pick a VM on the running stack; confirm the picker narrows on typed text + the VM lands in Members. Screenshot under this folder.

### 10 — CI green on the PR
**Status:** ⏳ PENDING — `scripts/ci-local.sh` pre-push mirror, then PR runner.
