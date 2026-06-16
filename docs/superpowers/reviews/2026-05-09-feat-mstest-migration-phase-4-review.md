# Deep PR Review — `feat/mstest-migration-phase-4`

**Date:** 2026-05-09
**Branch:** `feat/mstest-migration-phase-4` @ `30eb149`
**Status:** OPEN (pre-merge slice-boundary gate)
**Phase scope (vs Phase 3 base `8d882bb`):** 13 commits on test code + 2 doc commits, 9 files, +309/-239 lines.

Read against:
- Spec: `docs/superpowers/specs/2026-05-08-xunit-to-mstest-migration-design.md` (especially §1 Goals item 6 on per-project mutation score parity, §3 incremental-translation mechanic, §4 Translation rules — particularly §4.5 FA→MSTest assertion table).
- Plan: `docs/superpowers/plans/2026-05-08-xunit-to-mstest-migration-plan.md` §Phase 4 (Tasks 4.1, 4.2, 4.3, 4.4).
- Mutation baseline doc: `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md` (the §"Phase 4 verification" entry that this branch added is the load-bearing artifact for clause-(b) reconciliation).
- ADR index: `docs/architecture/decisions/README.md` (ADR-0097 governs MSTest migration; ADR-0083 superseded).
- Definition of Done: `CLAUDE.md` §Definition of Done.

## Overview

Phase 4 translates 7 unit-test files in `src/Modules/Catalog/Kartova.Catalog.Tests` from xUnit + FluentAssertions to MSTest v4 + native asserts (74 test cases preserved exactly), drops `xunit`, `xunit.runner.visualstudio`, and `FluentAssertions` package references, swaps the `<Using Include="Xunit" />` global for the MSTest one, and runs the **first Stryker mutation gate** in this migration. No production code is touched. `Kartova.Catalog.Domain` scores 100.00% (43/43 killed), confirming translation preserved kill rates exactly. `Kartova.Catalog.Infrastructure` scores 95.77% (68/71 with 3 nocoverage) — investigation traced the headline −4.23pt drop to a stale May 7 baseline that measured only 3 of 11 source files (the rest had every mutant `Ignored` under the orchestrator's `--since:master` filter, since slice-5 had merged ~10h before). The 3 nocoverage mutants are all in slice-5-added `EditApplicationHandler.cs`'s private `TryCaptureCurrentVersionAsync` recovery helper, which the unit-tier `Catalog.Tests` is correctly *not* covering — handler integration paths belong in `Catalog.IntegrationTests`, owned by Phase 9 per the per-phase ownership table. Reconciled per the baseline doc's merge-gate clause (b): enumerated mutants with file:line/mutator/replacement, accepted 95.77% as the new floor, flagged Phase 9 as the appropriate slice.

## Blocking-class issues

None.

## Should-fix issues

None.

## Nits

1. **`ListApplicationsHandlerFilterTests.cs:92-93` — translation splits a fluent expression into two sequential assertions; if the count assertion fails, `Single()` will also throw on the next line.**
   - Evidence: `src/Modules/Catalog/Kartova.Catalog.Tests/ListApplicationsHandlerFilterTests.cs:92-93` — `Assert.AreEqual(1, page.Items.Count, "...");` followed immediately by `Assert.AreEqual("active-app", page.Items.Single().Name);`.
   - Cite: pr-test-analyzer informational nit; no behavioral defect, just slight ordering redundancy. Both assertions land if reached; failures in either are independently diagnosed by MSTest.
   - Impact: none operationally.
   - Fix: optional — wrap both in a `[TestMethod]`-level helper or introduce a local `var single = page.Items.Single();` to share the result. Skip; the spec-prescribed translation pattern is the cleaner choice here.

2. **`ApplicationLifecycleTests.cs:130-146` — inline `new Regex("sunset.*future")` allocates per assertion, not cached static.**
   - Evidence: `src/Modules/Catalog/Kartova.Catalog.Tests/ApplicationLifecycleTests.cs:137,145`.
   - Cite: efficiency reviewer informational nit; runs in a guard-failure path that executes once per test method; sub-millisecond cost.
   - Impact: none operationally.
   - Fix: optional — extract a `private static readonly Regex SunsetFuture = new("sunset.*future");`. Adds a class-level field and an indirection that the inline-with-comment form already pre-empts ("do not 'simplify' to a single Contains").
   - Skip; the inline form keeps the rationale comment co-located with the regex.

3. **`ContractsCoverageRules.cs`-style `using Assert =` alias dance was unnecessary in Phase 4 too.**
   - Evidence: not a finding in the current HEAD — the per-file aliases were correctly removed in Task 4.3. Listed here only as a note for Phase 5+ (Organization tests have the same pattern).
   - Skip.

## Missing tests

None. Phase 4 is a 1:1 translation slice (spec §1 Goals item 6: "Translate test count and behavior 1:1. No new tests, no removed coverage."). The Stryker gate confirmed translation parity:

| Mutation target | Baseline (May 7) | Phase 4 (May 9) | Δ | Verdict |
|---|---|---|---|---|
| `Kartova.Catalog.Domain` | 100.00% (39/39) | 100.00% (43/43) | 0pt | PASS — translation preserved kill power exactly |
| `Kartova.Catalog.Infrastructure` | 100.00% (30/30) | 95.77% (68/71) | −4.23pt headline | reconciled — see baseline doc §"Phase 4 verification" |

Mutant count delta on Domain (39 → 43) reflects natural drift in Stryker mutant generation across slightly different builds; the score is unchanged. Mutant count delta on Infrastructure (30 → 71) reflects the May 7 baseline scoping issue documented in the Phase 4 verification entry: the prior run had 8 of 11 files with every mutant `Ignored` (most likely `--since:master` filtering after slice-5 merged ~10h before), so its 100% score was based on only 3 evaluable files. Phase 4's full-mode run is the first measurement of the actual `Kartova.Catalog.Infrastructure` mutation surface.

The 3 nocoverage mutants are all in `EditApplicationHandler.cs`'s `TryCaptureCurrentVersionAsync` (lines 53, 56, 63) — a private best-effort recovery helper exercised only when:
- `DbUpdateConcurrencyException` fires AND
- `ex.Entries` is empty (line 53), OR `GetDatabaseValuesAsync` returns null (line 56), OR an exception fires during recapture (line 63).

These are all negative-path branches in handler integration code. The unit-tier `Kartova.Catalog.Tests` is correctly scoped to Domain + Application; handler integration paths belong in `Kartova.Catalog.IntegrationTests`, which Phase 9 owns. Phase 4's verification entry tracks this as the appropriate slice to either kill the mutants or formally re-affirm the floor.

## What looks good

1. **csproj cleanup is exactly the spec'd shape.** `src/Modules/Catalog/Kartova.Catalog.Tests/Kartova.Catalog.Tests.csproj` — `xunit`, `xunit.runner.visualstudio`, `FluentAssertions` removed; `MSTest.TestFramework` + `MSTest.TestAdapter` + `MSTest.Analyzers` added; `<Using Include="Xunit" />` swapped for `Microsoft.VisualStudio.TestTools.UnitTesting`. Matches plan Task 4.3 verbatim and the Phase 1/3 canonical shape exactly.

2. **Catalog.Domain mutation gate at 100% (43/43) is the strongest possible signal that translation preserved kill power.** Every assertion in `ApplicationTests.cs` and `ApplicationLifecycleTests.cs` (the two large Domain-targeted files with 56 `Assert.AreEqual` calls and 9 `Assert.ThrowsExactly` sites) maps the FA original 1:1 — no kill-rate drift. Spec §1 Goals item 6 is satisfied for the in-scope mutation target.

3. **Baseline doc Phase 4 verification entry is honest, citation-grounded, and auditable.** `docs/superpowers/specs/baselines/2026-05-08-mstest-migration-mutation-baseline.md` §"Phase 4 verification" cites the May 7 report path, parses the actual JSON to count evaluable files (3 of 11), attributes the slice-5 origin via `git log` evidence (commit `b432cce`, 2026-05-07 10:44), enumerates the 3 nocoverage mutants with file:line/mutator/replacement, and tracks Phase 9 ownership per the per-phase table at line 50. Future readers can reproduce the diagnosis from the cited artifacts alone.

4. **Mutant-killing rationale comments preserved (and one wrong line corrected).** Tests like `ApplicationTests.cs:50-58` (the Statement-mutator pin against `throw new ArgumentException("...empty.")`), `ApplicationTests.cs:99-107` (256-char boundary off-by-one), `EfApplicationConfigurationTests.cs:79-83` (`ConfigurationSource.Explicit` "strongest kill"), and `InvalidLifecycleTransitionExceptionTests.cs:52-69` (camelCase wire-shape pin against accidental dropped `: ILifecycleConflict`) all retain their original mutant-targeting commentary — translation didn't lose the kill intent that makes the 100% Domain score real. The `30eb149` cleanup additionally fixed three stale comment citations carried verbatim from the xUnit original (line 87 reference, `Statement_EfApplicationConfiguration.cs:13` ghost path, hypothetical-mutation framing) — making them anchor on symbol/method names instead of brittle line numbers.

5. **`Assert.ThrowsExactly` policy uniformity.** Spec §4 line 217 prescribes uniform `ThrowsExactly` adoption. Every exception assertion in Phase 4's 7 files uses `Assert.ThrowsExactly<T>` (no `Assert.Throws<T>`). No misleading "Tightening" comments — production throws via literal `new BaseType(...)` at every site, so the framing is correctly policy-not-tightening per the Phase 2 precedent.

## DoD cross-check (`CLAUDE.md` §Definition of Done)

| # | Bullet | Evidence |
|---|---|---|
| 1 | Build green with `TreatWarningsAsErrors` | `dotnet build Kartova.slnx -warnaserror` → `Build succeeded. 0 Warning(s), 0 Error(s)` at the Task 4.3 commit. Per-project re-build at HEAD `30eb149` post-comment-fixes also green. |
| 2 | Per-task subagent reviews | **Skipped per user's explicit "controller-direct" choice for this slice.** The four slice-boundary skills (#3, #6, #8, #9) compensate by carrying the review burden at the slice boundary instead of per-file. Authorized by user; not a triviality skip. |
| 3 | `superpowers:requesting-code-review` at slice boundary | Run completed with verdict "Approve." 0 critical, 0 important, 2 minor — both applied at `b5f4fe0` (baseline-doc framing correction + FA-archaeology comment rephrase in `ApplicationLifecycleTests.cs:134`). |
| 4 | Full test suite green | `dotnet test Kartova.slnx --no-build` → 398/398 passed across 10 test assemblies at the Task 4.3 commit; project-level re-runs at all subsequent comment/cleanup commits (`b5f4fe0`, `bd6173b`, `30eb149`) → 74/74. |
| 5 | `docker compose up` + real HTTP | **N/A.** Phase 4 changes no production code, no middleware, no DI, no Dockerfile. The DoD bullet is gated on slices that wire HTTP / auth / DB / middleware / pipeline. |
| 6 | `/simplify` | 3 parallel agents — reuse: 0 findings, quality: 2 nits applied at `bd6173b` (collapsed `BuildConventionModel` inline duplication + dropped stale cross-reference comments), efficiency: 0 regressions. |
| 7 | Mutation feedback loop | **PASS.** First Stryker gate in this migration. Catalog.Domain 100.00% (43/43, Δ +0pt vs baseline). Catalog.Infrastructure 95.77% (68/71, headline −4.23pt) reconciled per merge-gate clause (b) with enumerated mutants and Phase 9 follow-up tracking — see baseline doc §"Phase 4 verification". |
| 8 | `/pr-review-toolkit:review-pr` | 3 reviewers — code-reviewer 0/0/0, pr-test-analyzer 0/0/3 informational nits, comment-analyzer 0/2/1; the 2 important + 1 minor (stale line-number references in mutant-pin comments) applied at `30eb149`. |
| 9 | `/deep-review` | This document. |

DoD bullets 1–9 are satisfied (with #2 honestly authorized by the user as controller-direct, #5 honestly N/A). #7 in particular is the slice's signature obligation and **passes with documented reconciliation** — the headline regression is a baseline-staleness artifact, not a Phase 4 translation defect.

## Verdict

Phase 4 is ready to merge. The translation is mechanically faithful — 74/74 tests pass, the Catalog.Domain mutation gate at 100% (43/43) confirms kill-power preservation under the hardest test, and the Catalog.Infrastructure 95.77% reconciliation is honest and auditable. The plan's Task 4.4 mutation gate ran for the first time in the migration and **caught a real signal** — not a defect in Phase 4, but a baseline-staleness issue (slice-5 code that the May 7 baseline didn't measure) that future phases need to be aware of. Phase 9 picks up the follow-up. The three nits above are all explicitly optional. Slice closes cleanly with 4 slice-boundary review passes (`requesting-code-review`, `/simplify`, `/pr-review-toolkit:review-pr`, `/deep-review`) and 6 commits worth of slice-boundary cleanup (`b5f4fe0`, `bd6173b`, `30eb149`).
