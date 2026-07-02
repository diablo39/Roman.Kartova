# DoD Ledger — ADR-0073 cleanups (sunset override + successor) + FU-1

**Slice:** `2026-07-01-adr0073-cleanups` · **Branch:** `feat/catalog-adr0073-cleanups` · **HEAD:** `e022858` (sub-slices A/B/C complete)
**PR:** (not opened yet) · **Last updated:** 2026-07-02
**Spec:** `docs/superpowers/specs/2026-07-01-adr0073-cleanups-successor-override-design.md`
**Plan:** `docs/superpowers/plans/2026-07-01-adr0073-cleanups-successor-override.md`
**Per-task tracker (SDD):** `.superpowers/sdd/progress.md` (per-task implement+review status; distinct from this DoD ledger)
**Findings telemetry:** `./gate-findings.yaml`

> Records the Definition of Done from `CLAUDE.md`. Update each row the moment its gate runs.
> Legend: ✅ PASS · ❌ FAIL · ⏳ PENDING · N/A — FAIL and N/A require a one-line reason.
> **Status: all eight blocking gates green at `e022858`.** Gate 8 (`review-pr`) + Playwright surfaced two issues, both fixed: cross-tenant/override/audit test gaps (`b4fcc55`) and an unreachable override UI entry point (`e022858`). Gates 6 + 9 user-waived. Ready for PR pending user go-ahead.

## Summary

| Gate | Status | Updated |
|------|--------|---------|
| 1 Build (`TreatWarningsAsErrors`) | ✅ PASS | 2026-07-02 |
| 2 Per-task subagent reviews | ✅ PASS | 2026-07-02 |
| 3 Full suite (+ real-seam) | ✅ PASS | 2026-07-02 |
| 4 Container build (images CI) | ✅ PASS | 2026-07-02 |
| 5 `/simplify` | ✅ PASS | 2026-07-02 |
| 6 Mutation (conditional) | N/A — user-waived | 2026-07-01 |
| 7 `requesting-code-review` | ✅ PASS | 2026-07-02 |
| 8 `review-pr` | ✅ PASS | 2026-07-02 |
| 9 `deep-review` | N/A — user-waived | 2026-07-02 |
| Manual / Playwright (ADR-0084) | ✅ PASS | 2026-07-02 |
| Terminal re-verify (build + suite) | ✅ PASS | 2026-07-02 |
| Pre-push CI mirror (`ci-local.sh`) | ✅ PASS (flake, see detail) | 2026-07-02 |

## Gate detail

### 1 — Build (`TreatWarningsAsErrors=true`)
**Status:** ✅ PASS — full-solution `dotnet build Kartova.slnx` (Debug) 0W/0E; Release build 0W/0E via `ci-local.sh` backend job; web `tsc -b` + vite build clean. Re-confirmed at terminal re-verify.
**At:** e022858 (web) / b4fcc55 (C#) / 2026-07-02

### 2 — Per-task subagent reviews (spec + quality)
**Status:** ✅ PASS — per-task reviewers (A1, B1–B4) during dev; sub-slice C covered at slice boundary by the whole-branch gate-7 comprehensive review + gate-8 specialized reviewers (tests/errors/types/comments), which superseded per-task C reviews. All surfaced issues triaged (see gate-findings.yaml).
**Evidence:** `.superpowers/sdd/progress.md`; `./gate-findings.yaml`.
**At:** 2026-07-02

### 3 — Full test suite (unit + arch + integration; real-seam if wiring)
**Status:** ✅ PASS — `dotnet test Kartova.slnx` exit 0 across the solution (Release via ci-local). Notable: **Kartova.Catalog.IntegrationTests 211/211** (real-seam: real JwtBearer + Postgres/RLS — override authz 403/200 + override-audit keys before/after sunset, successor 422 incl. **cross-tenant**/400/409, `successor_changed` from/to set→change→clear, FU-1 cursor 400), ArchitectureTests 69/69, Organization.IntegrationTests 142/142, Audit.Infrastructure.IntegrationTests 35/35, Api.IntegrationTests 6/6, SharedKernel.Identity.IntegrationTests 8/8 (+ all unit assemblies). **Web: 690/690 (100 files).**
**At:** e022858 / 2026-07-02

### 4 — Container build (images CI job)
**Status:** ✅ PASS — `docker compose build` → `kartova/api:dev` + `kartova/migrator:dev` built (EXIT 0); web image (`docker build -f web/Dockerfile`) built green via `ci-local.sh` images job. No Dockerfile/`COPY` changes in the diff.
**At:** 2026-07-02

### 5 — `/simplify` against branch diff
**Status:** ✅ PASS — 4 cleanup agents (reuse/simplification/efficiency/altitude) in parallel. **Applied** (commit `2a1b885`): (A) extracted `RejectUnknownSuccessorAsync` in `CatalogEndpointDelegates` — 422 successor guard was byte-duplicated across Deprecate + SetSuccessor (unanimous, 3 agents); (B) extracted shared `SuccessorPicker.tsx` — chip/Clear/combobox block was duplicated in `DeprecateConfirmDialog` + `SetSuccessorDialog` (unanimous, 3 agents). **Skipped w/ reason:** (C) `DecommissionApplicationHandler` sunset-bypass recompute — explicit ternary is defensively self-documenting; domain-returns-flag refactor is a signature change not worth doing with mutation gate waived (drift risk low: same clock+field). (D) `SuccessorChanged` inline-local — cosmetic wash, named local mirrors sibling `LifecycleChanged`. (E) batch audit-append — new `IAuditWriter` API, outside diff, cold path. (F) fold successor-name into GET query — single-item read, negligible, current parallels CreatedBy. (G) setQueryData+invalidate — needed (PUT response unenriched), mirrors existing pattern.
**Verify:** C# 0W/0E (`Kartova.Catalog.Infrastructure` build), web `tsc -b`+vite clean, dialog suites 10/10 (SetSuccessorDialog + DeprecateConfirmDialog).
**At:** 2a1b885 / 2026-07-02

### 6 — Mutation loop (conditional)
**Status:** N/A — **user-waived for this slice** (explicit directive, 2026-07-01: "skip mutating tests in this slice"). The diff does touch Domain/Application logic (`Application.Decommission`/`Deprecate`/`SetSuccessor`, handlers), which would normally make this gate blocking; the slice owner (Roman) has waived it. Domain behavior is instead covered by the TDD unit tests (ApplicationLifecycleTests, ApplicationSuccessorTests) + real-seam integration tests, and by gates 7–9.
**Evidence:** user directive; no mutation run performed.

### 7 — `requesting-code-review` at slice boundary
**Status:** ✅ PASS — whole-branch general reviewer (master…HEAD). Verdict **Ready to merge with fixes**: **0 Critical, 0 code-correctness defects**. Confirmed sound: override authz (imperative check + team gate), RLS-scoped successor existence validation, self-FK + FORCE-RLS migration, audit hash-chain on the two-row deprecate+successor path. Its only blockers were *process* (stale ledger, unrun gates) — now resolved. Minor notes (typed self-ref problem type; write-path null successor name — self-healing via invalidate) triaged/accepted.
**At:** 2a1b885 (reviewed) → resolved by e022858 / 2026-07-02

### 8 — `review-pr` (pr-review-toolkit)
**Status:** ✅ PASS — 4 specialized reviewers (tests / silent-failure / type-design / comments). **0 Critical.** Findings fixed in `b4fcc55`: cross-tenant successor 422 test (was `OrgBUser` declared-unused), override-decommission audit assertions, `successor_changed` from/to test, self-400 type pin, deprecate→`invalidate(detail)` (stale successor name). Comment-rot swept (nonexistent ADR §refs, self→400 doc). Accepted-with-reason: successor typed `Guid?` not `ApplicationId?` (matches `_id` EF-translation decision); batch audit-append (cold path, outside diff).
**At:** b4fcc55 / 2026-07-02

### 9 — `deep-review`
**Status:** N/A — **user-waived for this run** (explicit directive, 2026-07-02: "skip deep review in this run"). Review coverage is provided by Gate 2 (13 per-task reviews), Gate 7 (comprehensive opus whole-branch review — ready-to-merge, 0 blocking), and Gate 8 (`review-pr`). The deep-review agent had completed by the time of the waiver; its output is not gated on per the directive.
**Evidence:** user directive.

### Manual / Playwright verification (ADR-0084)
**Status:** ✅ PASS — cold-started dev server (5173), logged in admin@orga (OrgAdmin), drove in-SPA. Verified: (1) set-successor dialog → successor link renders enriched name **"A App 041 →"**; (2) deprecate-with-successor → detail shows **"A App 093 →"** immediately (confirms the `invalidate(detail)` fix — no stale dash); (3) shared `SuccessorPicker` (chip + Clear) works in both dialogs; (4) react-aria grids intact (no blank page). **0 console errors.**
**Bug found & fixed here:** the sunset-override checkbox was **unreachable** — `LifecycleMenu` disabled Decommission before sunset regardless of the override permission, so an OrgAdmin could never open the dialog to use it. Fixed in `e022858` (thread `canOverride`); re-verified: item enabled → dialog opens → "Override sunset date" checkbox shows.
**At:** e022858 / 2026-07-02

### Terminal re-verify (build + full suite after gates 5–9)
**Status:** ✅ PASS — after all fix-applying gates. C# full solution build 0W/0E + full suite green (Catalog IT 211/211, all assemblies) at `b4fcc55`; web-only `e022858` re-verified with full web suite **690/690 (100 files)** + `tsc -b` clean.
**At:** e022858 / 2026-07-02

### Pre-push CI mirror (`scripts/ci-local.sh`)
**Status:** ✅ PASS (with flake) — images ✅ · stryker (validate) ✅ · frontend ✅ · helm ✅. **backend job reported FAIL, but confirmed Docker-saturation flake, not a regression:** all 14 failures were `Docker.DotNet … MakeRequestAsync` named-pipe `TimeoutException` in `SharedKernel.Identity.IntegrationTests` (8, KeyCloak) + `Api.IntegrationTests` (6, OpenApi/CORS) — container-heavy assemblies my changes don't touch, under saturation from concurrent Testcontainers. **Re-ran both in isolation → Identity 8/8, Api 6/6 green.** Matches the documented full-suite Docker-flake pattern. Log: `scratchpad/ci-local.log`.
**At:** b4fcc55 / 2026-07-02
