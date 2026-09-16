# DoD Ledger — VM System-side assign + server-side VM name search (TD-009 + TD-007)

**Slice:** `2026-09-16-vm-system-assign-and-search` · **Branch:** `feat/catalog-vm-system-assign-search` · **Terminal commit:** `7108c07`
**PR:** <pending push> · **Last updated:** 2026-09-16
**Scope note:** TD-008 (FE vitest flake mitigation) was pulled into this branch from its planned separate branch when the flake reddened gate 10's frontend mirror.
**Spec:** `docs/superpowers/specs/2026-09-16-vm-system-assign-and-search-design.md`
**Plan:** `docs/superpowers/plans/2026-09-16-vm-system-assign-and-search-plan.md`
**Findings telemetry:** `./gate-findings.yaml`

> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-09-16 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-09-16 |
| 3 Full suite (+ real-seam if wiring) | ✅ PASS | 2026-09-16 |
| 4 Container build (images CI) | N/A | 2026-09-16 |
| 5 `/simplify` | ✅ PASS (advisory; 0 applied, TD-010 filed) | 2026-09-16 |
| 6 `requesting-code-review` | ✅ PASS | 2026-09-16 |
| 7 `review-pr` | ✅ PASS | 2026-09-16 |
| 8 `deep-review` | ✅ PASS | 2026-09-16 |
| Terminal re-verify (build + suite) | ✅ PASS (2 TD-008 flakes, pass isolated) | 2026-09-16 |
| 9 Visual / API verification (ADR-0084) | ✅ PASS | 2026-09-16 |
| 10 CI green on PR | ⏳ PENDING (ci-local mirror ✅; PR runner after push) | 2026-09-16 |

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
**Status:** ✅ PASS — drove the full flow on the running docker stack (web :4173, api :8080, admin@orga OrgAdmin). DevSeed extended with a fixed-id "Payments Platform" System (`e2e…0020`); the 3 seeded VMs already existed. Verified: Infrastructure radio present in the Assign-component dialog; placeholder reads "Search VMs…"; typing "sql" narrows server-side to **only** `sql-vm-02` (excludes web-vm-01/app-vm-03 → TD-007 confirmed live, live API openapi carries the new param); selecting it assigns the VM — it renders in the Members table as a link (→ `/catalog/infrastructure/vms/{id}`) with Kind "Infrastructure" + a working Remove button, and appears in the System diagram. **0 console errors.**
**Evidence:** `gate9-assign-dialog-vm-search.png` (narrowed typeahead), `gate9-vm-assigned-member.png` (assigned member + diagram).
**At:** running stack, 2026-09-16

### 10 — CI green on the PR
**Status:** ⏳ PENDING (PR runner) — pre-push mirror `scripts/ci-local.sh backend frontend images` = all **PASS** on terminal commit `7108c07`. First mirror run caught (a) the TD-008 frontend flake → mitigated (see gate-findings `ci-mirror`), and (b) a real `tsc` error — `poolOptions`/`minWorkers` are not in vitest 4's `InlineConfig` type though the runtime accepted them → switched to the typed `test.maxWorkers`. Both fixed; re-run mirror green. PR runner is the terminal source of truth after push.
