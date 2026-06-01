# CursorCodec filter-generalization — mutation gate deferral

**Date:** 2026-06-01
**Branch:** `feat/slice-9-organization-people-management` (PR #27)
**Scope:** the CursorCodec filter-state generalization refactor (commits `d5668e6..HEAD`).
**Decision:** Defer DoD #7 (mutation ≥80%) for this refactor. User-approved.

## Rationale

The 4 changed files map to three Stryker configs, two of which run Testcontainers-backed integration suites:

| File(s) | Stryker config → test projects | Cost |
|---|---|---|
| `CursorCodec.cs`, `CursorFilterComparer.cs` | `src/Kartova.SharedKernel` → `SharedKernel.Tests` (sqlite/in-mem) | fast (~15–40 min incl. full-solution build) |
| `QueryablePagingExtensions.cs` | `src/Kartova.SharedKernel.Postgres` → Organization + Catalog `IntegrationTests` (PG + Keycloak) | hours |
| `ListApplicationsHandler.cs` | `src/Modules/Catalog` → `Catalog.Tests` + `Catalog.IntegrationTests` | 30–90+ min |

Each per-project invocation also builds the full `Kartova.slnx` first (the configs pin `solution: Kartova.slnx`). The repo's last full mutation-sentinel orchestrator run took **~5 hours** (`2026-05-09-phase-2-mutation-deferral.md` §"What was attempted"). Running all four files' mutation gate is multiple hours, dominated by the two Testcontainers configs — disproportionate for a refactor of this size and consistent with the established deferral precedent for integration-test-backed targets.

## What replaces the gate

- **Build green:** `dotnet build Kartova.slnx -c Debug` → 0 warnings / 0 errors at HEAD (`TreatWarningsAsErrors`).
- **Per-task spec + code-quality subagent reviews:** Tasks 1 & 2 (the source-touching tasks) each had both reviewers dispatched; findings applied (`303b249`, `acadf2c`).
- **Slice-boundary review coverage:** `/simplify` (4-lens, `431814e`), `/pr-review` (silent-failure + type-design, `4c42e6e`), `/deep-review` (no blocking/should-fix).
- **Unit + architecture:** SharedKernel.Tests `116`, AspNetCore.Tests `92`, ArchitectureTests `70` — all green at HEAD.
- **Integration (cursor path):** `Catalog.IntegrationTests/ListApplicationsPaginationTests` `25/25` (incl. the repurposed legacy-cursor → 400 negative path).
- **Docker e2e (production image):** happy page-2 boundary (overlap=0) + negative `400 cursor-filter-mismatch` captured.
- **Logic concentration:** the genuinely new branching logic is `CursorFilterComparer.FindMismatch` and `CursorCodec` encode/decode — both have strong-oracle unit tests (the comparer asserts `Name`/`Expected`/`Actual` incl. the `"(none)"`-vs-literal-value edge case; the codec covers round-trip/omit/empty/malformed-`f`). `QueryablePagingExtensions` and `ListApplicationsHandler` changes are thin wiring over that logic.

## Risk

Surviving-mutant rate on the 4 files is unmeasured. Mitigated by the strong-oracle unit coverage on the new logic plus the integration + docker e2e on the wiring. If a mutation check is later wanted, scope it to the two **SharedKernel** files first (fast, highest-value); the Postgres/Catalog mutation can ride a later integration-heavy mutation pass.

## Action

- Refactor proceeds without a mutation report. No `mutation-report-surviving.md` refreshed for this work.
- Tracked here for DoD honesty; revisit if PR #27's merge bar requires the gate.
