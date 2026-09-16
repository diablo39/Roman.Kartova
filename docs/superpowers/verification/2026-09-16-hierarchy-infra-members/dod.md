# DoD Ledger — Infrastructure in Catalog Hierarchy + System Members (TD-005/006)

**Slice:** `2026-09-16-hierarchy-infra-members` · **Branch:** `chore/tech-debt-td-005-006` · **HEAD:** `bc3495b`
**PR:** <pending> · **Last updated:** 2026-09-16
**Spec:** `docs/superpowers/specs/2026-09-16-hierarchy-infra-members-design.md`
**Plan:** `docs/superpowers/plans/2026-09-16-hierarchy-infra-members.md` (gitignored scratch)
**Findings telemetry:** `./gate-findings.yaml`

> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-09-16 |
| 2 Per-task subagent reviews | ⏳ PENDING | — |
| 3 Full suite (+ real-seam) | ✅ PASS | 2026-09-16 |
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
**Status:** ✅ PASS
**Evidence:** `dotnet build Kartova.slnx -p:TreatWarningsAsErrors=true` → `Build succeeded. 0 Warning(s) 0 Error(s)`.
**At:** bc3495b / 2026-09-16

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ⏳ PENDING
**Evidence:** —

### 3 — Full test suite (unit + arch + integration; real-seam)
**Status:** ✅ PASS
**Evidence:** Backend `dotnet test Kartova.slnx -m:1` → 15 assemblies, 0 failures. Web `npx vitest run` → 1129/1130 passed; sole failure = `SystemDetailPage.test.tsx` "Members tab empty state" **5000ms timeout under full-suite load** — passes 6/6 in 5.75s in isolation → pre-existing TD-008 flake, not slice-caused (see gate-findings.yaml). Real-seam: `GetCatalogHierarchyTests.Hierarchy_includes_infrastructure_member_...` (real Postgres/RLS + JWT) passed.
**At:** bc3495b / 2026-09-16

### 4 — Container build (images CI job)
**Status:** N/A
**Evidence:** Branch diff touches no `Dockerfile`, no `COPY`/`ADD` build input, and no restore surface (`*.csproj`, `Directory.Packages.props`, `nuget.config`). Per gate-4 tuning rule → N/A-with-reason.
**At:** bc3495b / 2026-09-16

### 5 — `/simplify` against branch diff
**Status:** ⏳ PENDING

### 6 — `requesting-code-review` at slice boundary
**Status:** ⏳ PENDING

### 7 — `review-pr` (pr-review-toolkit; standing set type-design + pr-test + code-reviewer)
**Status:** ⏳ PENDING

### 8 — `deep-review`
**Status:** ⏳ PENDING

### Terminal re-verify (build + full suite after gates 5–8)
**Status:** ⏳ PENDING

### 9 — Visual / API verification (observe the running system)
**Status:** ⏳ PENDING
**Evidence:** Plan: cold-start web, register a VM + assign to a System, screenshot Hierarchy page (VM under System) + System detail Members table (VM linked + Remove). DevSeed has no VMs.

### 10 — CI green on the PR (terminal; `scripts/ci-local.sh` = pre-push mirror)
**Status:** ⏳ PENDING
