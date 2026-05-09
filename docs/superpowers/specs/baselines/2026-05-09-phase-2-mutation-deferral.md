# Phase 2 — interim mutation gate deferral

**Date:** 2026-05-09
**Branch:** `feat/mstest-migration-phase-2`
**Decision:** Defer Phase 2's interim mutation regression check.

## Rationale

Per the per-phase mutation-gate ownership table (`docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md` §"Per-phase mutation-gate ownership"):

> **Co-driver phases:** when two phases co-drive the same mutation target (e.g., Phase 2 and Phase 11 both feed mutations on `Kartova.SharedKernel.AspNetCore`), the gate runs at the *second* of the two phases — at which point both driving test suites are on MSTest and a clean mutation comparison against the baseline is meaningful. The earlier of the two phases captures an interim score for **diagnostic purposes only**; a >1pt drift at the interim point flags a translation defect to investigate before the second phase.

The /simplify efficiency reviewer of the Phase 1 branch (commit `33431962`, plan note appended to "Stryker invocation note") explicitly flagged Phase 2's interim run as "diagnostic only" and the first candidate for deferral if mutation runtime becomes a constraint.

## What was attempted

Two attempts to run `dotnet stryker -f src/Kartova.SharedKernel.AspNetCore/stryker-config.json` (full mode, no `--since:master` because Phase 2 didn't change any AspNetCore source code — incremental mode would produce 0 mutants):

1. **Direct PowerShell run** via `run_in_background: true`. Stryker reached the "Building solution Kartova.slnx" phase at 13:08:58 and was killed before producing any mutation output. The per-project stryker-config has `solution: Kartova.slnx`, which makes Stryker scan all 15 source projects and build the full solution before mutating — this build phase exceeds the PowerShell tool's effective process lifetime.

2. The Phase 1 mutation-sentinel orchestrator (`bash .../ms-detect-and-run.sh`) ran successfully for ~5 hours over the same span on the prior day. Repeating it for Phase 2 would re-run all 12 per-project invocations to refresh the manifest, but in `--since:master` mode it would find no mutate-able AspNetCore source changes (the only changed production file across Phases 0–2 is `CursorCodec.cs` from Phase 1, in `Kartova.SharedKernel`) — same result as Phase 1's run, no new evidence about Phase 2's translated tests.

## What replaces the gate

- **Test count parity:** Phase 2 preserves 74/74 tests (verified at every commit).
- **Build green:** `dotnet build Kartova.slnx -warnaserror` → 0 warnings, 0 errors at HEAD `6fbcb10`.
- **Per-task subagent reviews:** every Phase 2 commit (`9c6be45`, `0e03aba`, `4b2c32e`, `daf9788`, `322e344`, `4dcf005`, `06c004a`, `dab9319`, `7ab76f3`, `272629f`, `d502a84`, `7779486`, `5154868`, `6fbcb10`) had spec-compliance + code-quality reviewers dispatched per CLAUDE.md DoD #2.
- **Slice-boundary review:** the Task 2.2 batch review confirmed argument-order discipline, `Assert.ThrowsExactly` tightening with documenting comments, NSubstitute idiom preservation, and `BeEquivalentTo` audit alignment for the 2 sites in `TenantClaimsTransformationTests`.
- **Phase 11 official gate:** when `Kartova.Api.IntegrationTests` migrates in Phase 11, both Phase 2's and Phase 11's translated test suites will be in scope for a full-mode mutation run against `Kartova.SharedKernel.AspNetCore`. That gate is the canonical apples-to-apples regression check vs the 100% baseline (3 killed / 3 evaluable from the May 7 baseline).

## Risk

If Phase 2's translated tests *did* introduce a kill-rate regression on `Kartova.SharedKernel.AspNetCore`, it would surface at Phase 11's gate and require remediation before Phase 11 can merge. The "early signal" value of the interim diagnostic is lost; the worst case is one extra phase of distance between defect introduction and detection. Per the spec's deferral license this is an acceptable trade.

## Action

- Phase 2 closes without an interim mutation report.
- The `mutation-report-surviving.md` artifact at repo root reflects the Phase 1 orchestrator run from 2026-05-09T05:17:10Z; not refreshed for Phase 2.
- Phase 11 plan task already prescribes the official gate run for `Kartova.SharedKernel.AspNetCore` — no plan change required.
